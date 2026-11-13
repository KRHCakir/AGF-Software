using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services.Mail
{
    // Versturen van een mailconcept (documenten deel 3): alleen hier, en alleen als iemand op Versturen klikt (POST .../send). Daarna staat
    // vast wanneer, door wie (CurrentUser), naar wie en met welk bericht-id; het document krijgt "verstuurd". Een mislukte verzending blijft
    // staan met de reden en kan opnieuw. Beperkt tot een aantal verzendingen per minuut per gebruiker; meer dan 10 ontvangers vraagt een
    // uitdrukkelijke bevestiging. Het wachtwoord van de afzender komt nooit in een foutmelding of log.
    public sealed class MailSendService(AppDbContext db, IMailSender sender, MailSettingsService settings, ILogger<MailSendService> logger, ICurrentUser user = null, MailSendLimiter limiter = null)
    {
        private readonly MailSendLimiter _limiter = limiter ?? new MailSendLimiter();
        private readonly ICurrentUser _user = user ?? CurrentUser.Default;
        private static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(5);

        public async Task<ServiceResult<MailDraftDto>> SendAsync(int id, MailDraftActionDto request, CancellationToken cancellationToken = default)
        {
            var draft = await db.MailDrafts.Include(d => d.Document).SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
            if (draft == null) return ServiceResult<MailDraftDto>.NotFound($"Mail {id} does not exist.");
            var now = DateTime.UtcNow;
            // Onzeker: te lang "bezig", of een eerdere poging eindigde met een onbekende uitkomst (dan staat de reden bij een concept in "bezig").
            var stuck = draft.Status == DocumentMailRules.StatusSending && (draft.LastError != null || draft.LastAttemptAt < now - StuckAfter);
            if (draft.Status is not (DocumentMailRules.StatusDraft or DocumentMailRules.StatusFailed) && !stuck)
            {
                return Conflict($"This mail is {draft.Status.ToLowerInvariant()}; it is not sent again.", "notOpen");
            }
            if (request?.ExpectedVersion != draft.Version) return Conflict("This mail was changed by someone else; reload and try again.", "stale");
            if (stuck && request?.ConfirmResend != true)
            {
                return Conflict("It is unknown whether this mail was sent. Check the sent items of the sender first; only then confirm to send it again.", "uncertain");
            }
            var to = DocumentMailRules.SplitAddresses(draft.To);
            var cc = DocumentMailRules.SplitAddresses(draft.Cc);
            if (to.Count == 0)
            {
                return Conflict(draft.MissingRecipient != null ? $"Geen e-mailadres voor {draft.MissingRecipient}: voeg er een toe." : "Fill in at least one address under To.", "noRecipient");
            }
            if (to.Concat(cc).Any(a => !EmailRules.IsStrict(a))) return Conflict("One of the addresses is not valid; check To and Cc.", "invalidAddress");
            var count = to.Concat(cc).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (count > DocumentMailRules.ConfirmAboveRecipients && request?.ConfirmManyRecipients != true)
            {
                return ServiceResult<MailDraftDto>.Conflict($"This mail goes to {count} addresses; confirm to send it.", new { code = "confirmRecipients", count });
            }
            var stored = await db.MailSettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (!settings.Configured(stored)) return Conflict("Sending mail is not set up yet (Beheer › Mail). Download the PDF and copy the text instead.", "notConfigured");
            var actor = _user.Actor(SalesOrderDocumentRules.MaxActorLength);
            // Elke poging telt, ook herhaalde pogingen op hetzelfde concept; reserveren gebeurt atomair per gebruiker.
            if (!_limiter.TryReserve(actor, now, DocumentMailRules.MaxSendsPerMinute)) return Conflict($"At most {DocumentMailRules.MaxSendsPerMinute} mails per minute; try again in a minute.", "rateLimited");

            // Eerst "bezig" vastleggen (met de versie): een tweede klik of een gelijktijdige verzending ziet dat en verstuurt niet nog eens.
            draft.Status = DocumentMailRules.StatusSending;
            draft.Attempts++;
            draft.LastAttemptAt = now;
            draft.LastAttemptBy = actor;
            draft.LastError = null;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict("This mail was changed or sent by someone else; reload.", "stale");
            }

            SmtpOptions smtp = null;
            MailSendResult result;
            try
            {
                // Voorbereiding (wachtwoord ontsleutelen, PDF maken) hoort bij de verzending: een fout hier wordt "mislukt" met een reden, niet "bezig".
                smtp = settings.SmtpOf(stored);
                var (bytes, name) = Pdf(draft.Document);
                var mail = new OutgoingMail(stored?.FromAddress ?? "verkoop@nagebootst.agf.local", stored?.FromName, to, cc, draft.Subject, draft.Body, name, bytes);
                result = await sender.SendAsync(mail, smtp, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = new MailSendResult(false, null, exception.GetType().Name);
            }

            // Vanaf hier niet meer afbreken met het verzoek: de mail is mogelijk al onderweg en de uitkomst moet vastgelegd worden.
            var done = DateTime.UtcNow;
            draft.UpdatedAt = done;
            draft.UpdatedBy = actor;
            if (result.Uncertain)
            {
                // Onbekende uitkomst: blijft "bezig" met de reden erbij; opnieuw versturen kan pas na controle en uitdrukkelijke bevestiging.
                draft.LastError = Mask("Uitkomst onbekend, controleer de verzonden items van de afzender voordat u opnieuw verstuurt. " + result.Error, smtp?.Password);
                await db.SaveChangesAsync(CancellationToken.None);
                logger.LogWarning("Mail {DraftId} via {Sender}: uitkomst onbekend; controle nodig voor opnieuw versturen.", draft.Id, sender.Kind);
            }
            else if (result.Success)
            {
                draft.Status = DocumentMailRules.StatusSent;
                draft.SentAt = done;
                draft.SentBy = actor;
                draft.MessageId = result.MessageId;
                draft.LastError = null;
                // Concept en document samen in één transactie: beide of geen van beide.
                await using (var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None))
                {
                    await db.SaveChangesAsync(CancellationToken.None);
                    // Het document: verstuurd (één keer, zoals Doorgegeven; regel 31 staat alleen dat toe).
                    await db.SalesOrderDocuments.Where(d => d.Id == draft.DocumentId && d.SentAt == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(d => d.SentAt, (DateTime?)done).SetProperty(d => d.SentBy, actor), CancellationToken.None);
                    await transaction.CommitAsync(CancellationToken.None);
                }
                logger.LogInformation("Mail {DraftId} verstuurd naar {Count} adressen via {Sender}.", draft.Id, count, sender.Kind);
            }
            else
            {
                draft.Status = DocumentMailRules.StatusFailed;
                draft.LastError = Mask(result.Error, smtp?.Password);
                await db.SaveChangesAsync(CancellationToken.None);
                logger.LogWarning("Mail {DraftId} niet verstuurd via {Sender}; de reden staat bij het concept.", draft.Id, sender.Kind);
            }
            db.ChangeTracker.Clear();
            return await new MailDraftService(db, _user).GetAsync(draft.Id, cancellationToken);
        }

        // Een foutmelding nooit met het wachtwoord erin, en kort.
        public static string Mask(string error, string password)
        {
            var text = string.IsNullOrWhiteSpace(error) ? "Unknown error" : error;
            if (!string.IsNullOrEmpty(password)) text = text.Replace(password, "***", StringComparison.Ordinal);
            return text.Length > DocumentMailRules.MaxErrorLength ? text[..DocumentMailRules.MaxErrorLength] : text;
        }

        private static (byte[] Bytes, string Name) Pdf(Models.SalesOrderDocument document) =>
            document.Kind == SalesOrderDocumentRules.KindTransportOrder
                ? (TransportOrderPdf.Render(document, TransportOrderBuilder.Deserialize(document.SnapshotJson)), TransportOrderPdf.FileName(document))
                : (LoadingSlipPdf.Render(document, LoadingSlipBuilder.Deserialize(document.SnapshotJson)), LoadingSlipPdf.FileName(document));

        private static ServiceResult<MailDraftDto> Conflict(string message, string code) => ServiceResult<MailDraftDto>.Conflict(message, new { code, message });
    }

    // Maximaal N verzendpogingen per minuut per gebruiker. Elke poging wordt apart geteld en onder een slot gereserveerd, zodat herhaalde
    // pogingen op één concept en gelijktijdige verzoeken de limiet niet omzeilen. Geheugen van dit proces (singleton).
    public sealed class MailSendLimiter
    {
        private readonly Dictionary<string, Queue<DateTime>> _attempts = new();
        private readonly object _lock = new();

        public bool TryReserve(string actor, DateTime now, int max)
        {
            lock (_lock)
            {
                if (!_attempts.TryGetValue(actor, out var queue)) _attempts[actor] = queue = new Queue<DateTime>();
                while (queue.Count > 0 && queue.Peek() < now.AddMinutes(-1)) queue.Dequeue();
                if (queue.Count >= max) return false;
                queue.Enqueue(now);
                return true;
            }
        }
    }
}
