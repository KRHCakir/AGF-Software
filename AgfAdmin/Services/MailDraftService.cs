using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Mailconcepten (documenten deel 3). Zodra er een nieuwe documentversie is (laadbon, transportopdracht), staat er een concept klaar: de
    // ontvangers voor die soort (de loods voor de laadbon, de vervoerder voor de transportopdracht), onderwerp en tekst uit het sjabloon met
    // de handtekening, en de PDF als bijlage bij het versturen. Deze service verstuurt nooit: dat gebeurt alleen na een klik (MailSendService).
    public sealed class MailDraftService(AppDbContext db, ICurrentUser user = null)
    {
        private readonly ICurrentUser _user = user ?? CurrentUser.Default;
        public const string WarehouseName = "de loods";

        // Een concept voor de nieuwste versie van elk document dat er nog geen heeft (en nog niet doorgegeven of verstuurd is). Een open concept
        // van een oudere versie wordt "vervangen". Eigen transport of klant haalt op: geen vervoerder, dus geen concept voor de transportopdracht.
        public async Task<int> EnsureDraftsAsync(int orderId, CancellationToken cancellationToken = default)
        {
            var order = await db.SalesOrders.AsNoTracking().Include(o => o.Customer).Include(o => o.SalesPartij)
                .Include(o => o.Transport).ThenInclude(t => t.CarrierRelation)
                .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
            if (order?.ConfirmedAt == null) return 0;
            var documents = await db.SalesOrderDocuments.AsNoTracking().Where(d => d.SalesOrderId == orderId).ToListAsync(cancellationToken);
            var drafts = await db.MailDrafts.Where(d => d.SalesOrderId == orderId).ToListAsync(cancellationToken);
            var settings = await db.MailSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            var company = await db.CompanySettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken) ?? new CompanySettings();
            var recipients = new DocumentRecipientService(db, _user);
            var now = DateTime.UtcNow;
            var actor = _user.Actor(SalesOrderDocumentRules.MaxActorLength);
            var created = 0;
            foreach (var group in documents.GroupBy(d => d.Kind))
            {
                var latest = group.OrderByDescending(d => d.Version).First();
                if (drafts.Any(d => d.DocumentId == latest.Id))
                {
                    // Is het document intussen doorgegeven, dan hoeft een open concept er niet meer te staan.
                    if (latest.SentAt != null)
                        foreach (var open in drafts.Where(d => d.DocumentId == latest.Id && (d.Status == DocumentMailRules.StatusDraft || d.Status == DocumentMailRules.StatusFailed)))
                        {
                            open.Status = DocumentMailRules.StatusSuperseded;
                            open.UpdatedAt = now;
                            open.UpdatedBy = actor;
                        }
                    else
                    {
                        // Is er na het aanmaken een adres bijgekomen, dan komt het in een concept dat nog geen ontvanger heeft; handmatig ingevulde Aan blijft staan.
                        foreach (var open in drafts.Where(d => d.DocumentId == latest.Id && d.MissingRecipient != null && string.IsNullOrWhiteSpace(d.To)
                            && (d.Status == DocumentMailRules.StatusDraft || d.Status == DocumentMailRules.StatusFailed)))
                        {
                            if (latest.Kind == SalesOrderDocumentRules.KindTransportOrder && order.Transport?.Mode != SalesTransportRules.ModeCarrier) continue;
                            var (rTo, rCc, _, _) = latest.Kind == SalesOrderDocumentRules.KindTransportOrder
                                ? await ForRelationAsync(recipients, order.Transport.CarrierRelation, latest.Kind, cancellationToken)
                                : await ForWarehouseAsync(recipients, latest.Kind, cancellationToken);
                            if (rTo.Count == 0) continue;
                            var existingCc = DocumentMailRules.SplitAddresses(open.Cc);
                            open.To = DocumentMailRules.JoinAddresses(rTo);
                            open.Cc = DocumentMailRules.JoinAddresses(existingCc.Concat(rCc).Where(a => !rTo.Contains(a, StringComparer.OrdinalIgnoreCase)));
                            open.MissingRecipient = null;
                            open.MissingRelationId = null;
                            open.UpdatedAt = now;
                            open.UpdatedBy = actor;
                        }
                    }
                    continue;
                }
                // Een open concept van een oudere versie gaat niet meer weg: vervangen, ook als de nieuwe versie zelf geen concept krijgt.
                foreach (var old in drafts.Where(d => d.DocumentKind == latest.Kind && d.DocumentId != latest.Id && (d.Status == DocumentMailRules.StatusDraft || d.Status == DocumentMailRules.StatusFailed)))
                {
                    old.Status = DocumentMailRules.StatusSuperseded;
                    old.UpdatedAt = now;
                    old.UpdatedBy = actor;
                }
                if (latest.SentAt != null) continue;
                if (latest.Kind == SalesOrderDocumentRules.KindTransportOrder && order.Transport?.Mode != SalesTransportRules.ModeCarrier) continue;
                var (to, cc, missing, missingId) = latest.Kind == SalesOrderDocumentRules.KindTransportOrder
                    ? await ForRelationAsync(recipients, order.Transport.CarrierRelation, latest.Kind, cancellationToken)
                    : await ForWarehouseAsync(recipients, latest.Kind, cancellationToken);
                var (subject, body) = await RenderAsync(latest, order, company, settings, cancellationToken);
                db.MailDrafts.Add(new MailDraft
                {
                    SalesOrderId = orderId, DocumentId = latest.Id, DocumentKind = latest.Kind, Status = DocumentMailRules.StatusDraft,
                    To = DocumentMailRules.JoinAddresses(to), Cc = DocumentMailRules.JoinAddresses(cc), Subject = subject, Body = body,
                    MissingRecipient = missing, MissingRelationId = missingId,
                    PreviousSentAt = group.Where(d => d.Version < latest.Version && d.SentAt != null).Select(d => d.SentAt).Max(),
                    CreatedAt = now, CreatedBy = actor, UpdatedAt = now, UpdatedBy = actor,
                });
                created++;
            }
            if (created == 0 && !db.ChangeTracker.HasChanges()) return 0;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                // Een gelijktijdige aanvraag maakte hetzelfde concept al: dat blijft staan.
                db.ChangeTracker.Clear();
                return 0;
            }
            return created;
        }

        public async Task<List<MailDraftDto>> ForOrderAsync(int orderId, CancellationToken cancellationToken = default)
        {
            await EnsureDraftsAsync(orderId, cancellationToken);
            return await Query().Where(d => d.SalesOrderId == orderId && d.Status != DocumentMailRules.StatusDiscarded && d.Status != DocumentMailRules.StatusSuperseded)
                .OrderBy(d => d.DocumentKind).ThenByDescending(d => d.Id).Select(Projection).ToListAsync(cancellationToken);
        }

        // Te versturen, over alle orders: concepten die nog open staan of mislukten, nieuwste eerst.
        // Een concept van een document dat intussen is doorgegeven, hoort er niet meer bij. Pagineren kan met skip/take; OpenCountAsync geeft het totaal.
        public const int MaxPageSize = 200;

        private IQueryable<MailDraft> OpenQuery() =>
            Query().Where(d => ((d.Status == DocumentMailRules.StatusDraft || d.Status == DocumentMailRules.StatusFailed) && d.Document.SentAt == null)
                || d.Status == DocumentMailRules.StatusSending);

        // Bevestigde orders van vóór de conceptfunctie hebben nog geen concept. Voor het tellen en pagineren vullen we die per gerichte batch aan:
        // alleen orders met een nieuwste, niet doorgegeven document zonder enig concept (ook geen verwijderd of vervangen) komen in aanmerking.
        public const int BackfillBatchSize = 50;

        private async Task BackfillAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                var orderIds = await db.SalesOrderDocuments.AsNoTracking()
                    .Where(d => d.SentAt == null && d.SalesOrder.ConfirmedAt != null
                        && (d.Kind != SalesOrderDocumentRules.KindTransportOrder || d.SalesOrder.Transport.Mode == SalesTransportRules.ModeCarrier)
                        && !db.MailDrafts.Any(m => m.DocumentId == d.Id)
                        && !db.SalesOrderDocuments.Any(x => x.SalesOrderId == d.SalesOrderId && x.Kind == d.Kind && x.Version > d.Version))
                    .Select(d => d.SalesOrderId).Distinct().OrderBy(id => id).Take(BackfillBatchSize).ToListAsync(cancellationToken);
                if (orderIds.Count == 0) return;
                var made = 0;
                foreach (var id in orderIds) made += await EnsureDraftsAsync(id, cancellationToken);
                // Niets aangemaakt (bijvoorbeeld door een gelijktijdige aanvraag): stoppen, anders blijven dezelfde orders terugkomen.
                if (made == 0 || orderIds.Count < BackfillBatchSize) return;
            }
        }

        public async Task<int> OpenCountAsync(CancellationToken cancellationToken = default)
        {
            await BackfillAsync(cancellationToken);
            return await OpenQuery().CountAsync(cancellationToken);
        }

        public Task<List<MailDraftDto>> OpenAsync(CancellationToken cancellationToken = default) => OpenAsync(0, MaxPageSize, cancellationToken);

        public async Task<List<MailDraftDto>> OpenAsync(int skip, int take, CancellationToken cancellationToken = default)
        {
            await BackfillAsync(cancellationToken);
            return await OpenQuery().OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
                .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, MaxPageSize)).Select(Projection).ToListAsync(cancellationToken);
        }

        public async Task<ServiceResult<MailDraftDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var draft = await Query().Where(d => d.Id == id).Select(Projection).SingleOrDefaultAsync(cancellationToken);
            return draft == null ? NotFound(id) : ServiceResult<MailDraftDto>.Ok(draft);
        }

        // Aan, Cc, onderwerp en tekst aanpassen, zolang het concept niet verstuurd is.
        public async Task<ServiceResult<MailDraftDto>> UpdateAsync(int id, UpdateMailDraftDto request, CancellationToken cancellationToken = default)
        {
            var draft = await db.MailDrafts.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (draft == null) return NotFound(id);
            if (Editable(draft) is { } refused) return refused;
            if (request?.ExpectedVersion != draft.Version) return Stale(draft);
            var errors = new Dictionary<string, List<string>>();
            var to = Addresses(request.To, "to", errors);
            var toSet = DocumentMailRules.SplitAddresses(to);
            var cc = Addresses(DocumentMailRules.JoinAddresses(DocumentMailRules.SplitAddresses(request.Cc).Where(a => !toSet.Contains(a, StringComparer.OrdinalIgnoreCase))), "cc", errors);
            var subject = ValidationErrors.RequiredText(request.Subject, DocumentMailRules.MaxSubjectLength, "subject", errors);
            var body = (request.Body ?? "").Trim();
            if (body.Length == 0) ValidationErrors.Add(errors, "body", "Write a text.");
            if (body.Length > DocumentMailRules.MaxBodyLength) ValidationErrors.Add(errors, "body", $"At most {DocumentMailRules.MaxBodyLength} characters.");
            if (errors.Count > 0) return ServiceResult<MailDraftDto>.Validation(ValidationErrors.ToArrays(errors));
            draft.To = to;
            draft.Cc = cc;
            if (toSet.Count > 0)
            {
                draft.MissingRecipient = null;
                draft.MissingRelationId = null;
            }
            else if (draft.MissingRecipient == null)
            {
                var isTransport = draft.DocumentKind == SalesOrderDocumentRules.KindTransportOrder;
                var carrier = isTransport
                    ? await db.SalesOrders.AsNoTracking().Where(o => o.Id == draft.SalesOrderId).Select(o => o.Transport.CarrierRelation).FirstOrDefaultAsync(cancellationToken)
                    : null;
                draft.MissingRecipient = carrier?.Name ?? (isTransport ? "de vervoerder" : WarehouseName);
                draft.MissingRelationId = carrier?.Id;
            }
            draft.Subject = subject;
            draft.Body = body;
            draft.UpdatedAt = DateTime.UtcNow;
            draft.UpdatedBy = _user.Actor(SalesOrderDocumentRules.MaxActorLength);
            return await SaveAsync(draft, cancellationToken);
        }

        // Verwijderen haalt het concept weg (status Verwijderd); het document zelf blijft.
        public async Task<ServiceResult<MailDraftDto>> DiscardAsync(int id, MailDraftActionDto request, CancellationToken cancellationToken = default)
        {
            var draft = await db.MailDrafts.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (draft == null) return NotFound(id);
            if (Editable(draft) is { } refused) return refused;
            if (request?.ExpectedVersion != draft.Version) return Stale(draft);
            draft.Status = DocumentMailRules.StatusDiscarded;
            draft.UpdatedAt = DateTime.UtcNow;
            draft.UpdatedBy = _user.Actor(SalesOrderDocumentRules.MaxActorLength);
            return await SaveAsync(draft, cancellationToken);
        }

        private async Task<ServiceResult<MailDraftDto>> SaveAsync(MailDraft draft, CancellationToken cancellationToken)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<MailDraftDto>.Conflict("This mail was changed by someone else; reload and try again.", new { code = "stale" });
            }
            return await GetAsync(draft.Id, cancellationToken);
        }

        private static ServiceResult<MailDraftDto> Editable(MailDraft draft) =>
            draft.Status is DocumentMailRules.StatusDraft or DocumentMailRules.StatusFailed ? null
                : ServiceResult<MailDraftDto>.Conflict($"This mail is {draft.Status.ToLowerInvariant()} and can no longer be changed.", new { code = "notOpen", status = draft.Status });

        private static ServiceResult<MailDraftDto> Stale(MailDraft draft) =>
            ServiceResult<MailDraftDto>.Conflict("This mail was changed by someone else; reload and try again.", new { code = "stale", version = draft.Version });

        private static ServiceResult<MailDraftDto> NotFound(int id) => ServiceResult<MailDraftDto>.NotFound($"Mail {id} does not exist.");

        // Adressen in Aan of Cc: elk strikt geldig; dubbele één keer.
        internal static string Addresses(string value, string field, Dictionary<string, List<string>> errors)
        {
            var list = DocumentMailRules.SplitAddresses(value);
            foreach (var address in list.Where(a => !EmailRules.IsStrict(a))) ValidationErrors.Add(errors, field, $"{address}: {EmailRules.InvalidMessage}");
            var joined = DocumentMailRules.JoinAddresses(list);
            if (joined.Length > DocumentMailRules.MaxAddressListLength) ValidationErrors.Add(errors, field, "Too many addresses.");
            return joined;
        }

        private static async Task<(List<string>, List<string>, string, int?)> ForRelationAsync(DocumentRecipientService recipients, Relation relation, string kind, CancellationToken cancellationToken)
        {
            if (relation == null) return (new(), new(), "de vervoerder", null);
            var (to, cc) = await recipients.ResolveAsync(DocumentMailRules.OwnerRelation, relation.Id, null, kind, cancellationToken);
            return to.Count == 0 ? (to, cc, relation.Name, relation.Id) : (to, cc, null, null);
        }

        private static async Task<(List<string>, List<string>, string, int?)> ForWarehouseAsync(DocumentRecipientService recipients, string kind, CancellationToken cancellationToken)
        {
            var (to, cc) = await recipients.ResolveAsync(DocumentMailRules.OwnerWarehouse, null, null, kind, cancellationToken);
            return to.Count == 0 ? (to, cc, WarehouseName, null) : (to, cc, null, null);
        }

        private async Task<(string Subject, string Body)> RenderAsync(SalesOrderDocument document, SalesOrder order, CompanySettings company, MailSettings settings, CancellationToken cancellationToken)
        {
            var stored = await db.MailTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Kind == document.Kind, cancellationToken);
            var (subject, body) = stored != null ? (stored.Subject, stored.Body) : MailTemplateRules.Default(document.Kind);
            var values = new Dictionary<string, string>
            {
                ["{bedrijf}"] = company.CompanyName, ["{document}"] = MailTemplateRules.DocumentName(document.Kind), ["{nummer}"] = document.Number,
                ["{versie}"] = document.Version.ToString(), ["{order}"] = order.OrderNumber, ["{partij}"] = order.SalesPartij?.Number ?? order.OrderNumber,
                ["{klant}"] = order.Customer?.Name, ["{datum}"] = MailTemplateRules.Date(DateTime.UtcNow, false),
                ["{laaddatum}"] = order.LoadAt != null ? MailTemplateRules.Date(order.LoadAt.Value, true) : "nog niet bekend",
            };
            var text = MailTemplateRules.Render(body, values).Trim();
            if (!string.IsNullOrWhiteSpace(settings?.Signature)) text = $"{text}\n\n{settings.Signature.Trim()}";
            var title = MailTemplateRules.Render(subject, values).Trim();
            return (title.Length > DocumentMailRules.MaxSubjectLength ? title[..DocumentMailRules.MaxSubjectLength] : title,
                text.Length > DocumentMailRules.MaxBodyLength ? text[..DocumentMailRules.MaxBodyLength] : text);
        }

        private IQueryable<MailDraft> Query() => db.MailDrafts.AsNoTracking();

        private static readonly System.Linq.Expressions.Expression<Func<MailDraft, MailDraftDto>> Projection = d => new MailDraftDto
        {
            Id = d.Id, SalesOrderId = d.SalesOrderId, OrderNumber = d.SalesOrder.OrderNumber, CustomerName = d.SalesOrder.Customer.Name, DocumentId = d.DocumentId,
            DocumentKind = d.DocumentKind, DocumentNumber = d.Document.Number, DocumentVersion = d.Document.Version, Status = d.Status, To = d.To, Cc = d.Cc,
            Subject = d.Subject, Body = d.Body, MissingRecipient = d.MissingRecipient, MissingRelationId = d.MissingRelationId,
            PreviousSentAt = d.PreviousSentAt, CreatedAt = d.CreatedAt, SentAt = d.SentAt, SentBy = d.SentBy, MessageId = d.MessageId,
            LastError = d.LastError, Attempts = d.Attempts, Version = d.Version,
        };
    }
}
