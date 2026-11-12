using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Bevestigen van een verkooporder en de laadbon (documenten deel 1). Bevestigen legt vast wie en wanneer (CurrentUser) en maakt de
    // laadbon als versie 1 (snapshot). Verandert de bon daarna (een reservering, een toewijzing, emballage, laden op of kenteken), dan komt
    // er een nieuwe versie; de oude blijft bewaard. Er wordt niets verstuurd: dat doet deel 3 (mailconcept en verzenden).
    public sealed class LoadingSlipService(AppDbContext db, ICurrentUser user = null)
    {
        public const string CancelledCode = "cancelled";
        private readonly ICurrentUser _user = user ?? CurrentUser.Default;
        private string Actor() => _user.Actor(SalesOrderDocumentRules.MaxActorLength);

        public async Task<ServiceResult<SalesOrderDocumentsDto>> ConfirmAsync(int orderId, ConfirmSalesOrderDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var plate = Plate(request?.LicensePlate, errors);
            if (errors.Count > 0) return Validation(errors);
            var order = await LoadAsync(orderId, cancellationToken);
            if (order == null) return ServiceResult<SalesOrderDocumentsDto>.NotFound($"Sales order {orderId} does not exist.");
            if (order.Status == ReservationRules.OrderStatusCancelled)
            {
                return ServiceResult<SalesOrderDocumentsDto>.Conflict($"Sales order {order.OrderNumber} is cancelled and is not confirmed.", new { code = CancelledCode });
            }
            // Al bevestigd: niets nieuws (een tweede klik of een herhaling), de eerste vastlegging blijft.
            if (order.ConfirmedAt != null) return ServiceResult<SalesOrderDocumentsDto>.Ok(await DocumentsDtoAsync(order, cancellationToken));
            if (order.Lines.Count == 0)
            {
                ValidationErrors.Add(errors, "lines", $"Sales order {order.OrderNumber} has no lines: add what the customer ordered before confirming.");
                return Validation(errors);
            }
            var now = DateTime.UtcNow;
            order.ConfirmedAt = now;
            order.ConfirmedBy = Actor();
            order.LoadAt = Utc(request?.LoadAt);
            order.LicensePlate = plate;
            order.UpdatedAt = now;
            AddVersion(order, await SettingsAsync(cancellationToken), 1, SalesOrderDocumentRules.ReasonConfirmed, now);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                // Een gelijktijdige bevestiging was eerder: die blijft staan, wij geven het resultaat daarvan terug.
                db.ChangeTracker.Clear();
                var winner = await LoadAsync(orderId, cancellationToken);
                if (winner?.ConfirmedAt == null) throw;
                if (winner.Status == ReservationRules.OrderStatusCancelled)
                {
                    return ServiceResult<SalesOrderDocumentsDto>.Conflict($"Sales order {winner.OrderNumber} is cancelled and is not confirmed.", new { code = CancelledCode });
                }
                return ServiceResult<SalesOrderDocumentsDto>.Ok(await DocumentsDtoAsync(winner, cancellationToken));
            }
            return ServiceResult<SalesOrderDocumentsDto>.Ok(await DocumentsDtoAsync(order, cancellationToken));
        }

        public Task<ServiceResult<SalesOrderDocumentsDto>> UpdateLoadingAsync(int orderId, UpdateSalesOrderLoadingDto request, CancellationToken cancellationToken = default) =>
            UpdateLoadingAsync(orderId, request, 0, cancellationToken);

        private async Task<ServiceResult<SalesOrderDocumentsDto>> UpdateLoadingAsync(int orderId, UpdateSalesOrderLoadingDto request, int attempt, CancellationToken cancellationToken)
        {
            var errors = new Dictionary<string, List<string>>();
            var plate = Plate(request?.LicensePlate, errors);
            if (errors.Count > 0) return Validation(errors);
            var order = await LoadAsync(orderId, cancellationToken);
            if (order == null) return ServiceResult<SalesOrderDocumentsDto>.NotFound($"Sales order {orderId} does not exist.");
            if (order.ConfirmedAt == null)
            {
                ValidationErrors.Add(errors, "confirmedAt", $"Sales order {order.OrderNumber} is not confirmed yet; loading details come with confirming.");
                return Validation(errors);
            }
            if (order.Status == ReservationRules.OrderStatusCancelled)
            {
                return ServiceResult<SalesOrderDocumentsDto>.Conflict($"Sales order {order.OrderNumber} is cancelled.", new { code = CancelledCode });
            }
            order.LoadAt = Utc(request?.LoadAt);
            order.LicensePlate = plate;
            order.UpdatedAt = DateTime.UtcNow;
            // Laadgegevens en de nieuwe versie van de bon in één save: nooit opgeslagen gegevens zonder de bon die erbij hoort.
            var latest = order.Documents.Where(d => d.Kind == SalesOrderDocumentRules.KindLoadingSlip).OrderByDescending(d => d.Version).FirstOrDefault();
            var snapshot = LoadingSlipBuilder.Build(order, await SettingsAsync(cancellationToken));
            if (latest == null || latest.ContentHash != LoadingSlipBuilder.Hash(snapshot))
            {
                AddVersion(order, snapshot, (latest?.Version ?? 0) + 1, latest == null ? SalesOrderDocumentRules.ReasonConfirmed : SalesOrderDocumentRules.ReasonChanged, DateTime.UtcNow);
            }
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && attempt < 2)
            {
                // Een gelijktijdige verversing nam het versienummer: opnieuw, op de actuele stand (hoogstens twee keer).
                db.ChangeTracker.Clear();
                return await UpdateLoadingAsync(orderId, request, attempt + 1, cancellationToken);
            }
            return ServiceResult<SalesOrderDocumentsDto>.Ok(await DocumentsDtoAsync(await LoadAsync(orderId, cancellationToken), cancellationToken));
        }

        // De documenten van een order. Werkt ook als vangnet: is de bon intussen veranderd zonder dat een opdracht dat meldde, dan komt
        // de nieuwe versie hier alsnog.
        public async Task<ServiceResult<SalesOrderDocumentsDto>> DocumentsAsync(int orderId, CancellationToken cancellationToken = default)
        {
            if (!await db.SalesOrders.AsNoTracking().AnyAsync(o => o.Id == orderId, cancellationToken))
            {
                return ServiceResult<SalesOrderDocumentsDto>.NotFound($"Sales order {orderId} does not exist.");
            }
            await RefreshAsync(orderId, cancellationToken);
            return ServiceResult<SalesOrderDocumentsDto>.Ok(await DocumentsDtoAsync(await LoadAsync(orderId, cancellationToken), cancellationToken));
        }

        // Na elke geslaagde opdracht op de order: een nieuwe versie als de inhoud van de bon veranderd is. Alleen een bevestigde, niet
        // geannuleerde order; dezelfde inhoud geeft nooit een nieuwe versie (vingerafdruk). Twee gelijktijdige verversingen botsen op het
        // unieke versienummer: dan wint de eerste.
        public async Task<bool> RefreshAsync(int orderId, CancellationToken cancellationToken = default)
        {
            // Verloor deze verversing de botsing op het versienummer, dan opnieuw op de actuele stand: zo blijft geen nieuwere inhoud liggen.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var outcome = await RefreshOnceAsync(orderId, cancellationToken);
                if (outcome != null) return outcome.Value;
            }
            return false;
        }

        // true = nieuwe versie, false = niets veranderd, null = versiebotsing (opnieuw proberen).
        private async Task<bool?> RefreshOnceAsync(int orderId, CancellationToken cancellationToken, bool rethrowCollision = false)
        {
            var order = await LoadAsync(orderId, cancellationToken);
            if (order?.ConfirmedAt == null || order.Status == ReservationRules.OrderStatusCancelled) return false;
            var latest = order.Documents.Where(d => d.Kind == SalesOrderDocumentRules.KindLoadingSlip).OrderByDescending(d => d.Version).FirstOrDefault();
            var snapshot = LoadingSlipBuilder.Build(order, await SettingsAsync(cancellationToken));
            if (latest != null && latest.ContentHash == LoadingSlipBuilder.Hash(snapshot)) return false;
            AddVersion(order, snapshot, (latest?.Version ?? 0) + 1, latest == null ? SalesOrderDocumentRules.ReasonConfirmed : SalesOrderDocumentRules.ReasonChanged, DateTime.UtcNow);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && !rethrowCollision)
            {
                // Alleen een versiebotsing met een gelijktijdige verversing; andere databasefouten gaan naar de aanroeper.
                // Binnen een transactie (rethrowCollision) niet: die is na de fout afgebroken, daar herhaalt de aanroeper het geheel.
                db.ChangeTracker.Clear();
                return null;
            }
        }

        // Doorgegeven aan de loods (afgedrukt of overhandigd); deel 3 zet dit bij verzenden. Alleen de nieuwste versie, één keer.
        public async Task<ServiceResult<SalesOrderDocumentsDto>> MarkSentAsync(int orderId, int documentId, CancellationToken cancellationToken = default)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await MarkSentOnceAsync(orderId, documentId, cancellationToken);
                }
                catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && attempt < 2)
                {
                    // Een gelijktijdige verversing nam het versienummer: de transactie is teruggedraaid, opnieuw op de actuele stand.
                    db.ChangeTracker.Clear();
                }
            }
        }

        private async Task<ServiceResult<SalesOrderDocumentsDto>> MarkSentOnceAsync(int orderId, int documentId, CancellationToken cancellationToken)
        {
            // Verversen, controleren en doorgeven in één transactie met een rijlock op de order en haar regels (FOR UPDATE): een
            // gelijktijdige orderwijziging (die de orderrij of een orderregel wijzigt, of er via een FK naar verwijst) wacht tot de
            // doorgifte klaar is, en een al opgeslagen wijziging is bij het verversen hieronder zichtbaar. Zo wordt geen verouderde bon als
            // nieuwste doorgegeven. Alleen PostgreSQL (productie) kent deze lock; SQLite (tests) serialiseert schrijvers zelf.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SalesOrders\" WHERE \"Id\" = {orderId} FOR UPDATE", cancellationToken);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"SalesOrderLines\" WHERE \"SalesOrderId\" = {orderId} FOR UPDATE", cancellationToken);
            }
            // Eerst verversen: een verouderde bon mag niet als nieuwste worden doorgegeven. Een mislukte verversing werpt en blokkeert de doorgifte.
            await RefreshOnceAsync(orderId, cancellationToken, rethrowCollision: true);
            var order = await LoadAsync(orderId, cancellationToken);
            var document = order?.Documents.SingleOrDefault(d => d.Id == documentId);
            if (document == null) return ServiceResult<SalesOrderDocumentsDto>.NotFound($"Document {documentId} of sales order {orderId} does not exist.");
            var latest = order.Documents.Where(d => d.Kind == document.Kind).Max(d => d.Version);
            if (document.Version != latest)
            {
                return ServiceResult<SalesOrderDocumentsDto>.Conflict($"Version {document.Version} is not the latest ({latest}); pass on the latest version.", new { code = "notLatest" });
            }
            if (document.SentAt == null)
            {
                // Voorwaardelijke update in één statement: alleen als er nu nog geen nieuwere versie is en nog niet doorgegeven is.
                // Een gelijktijdige nieuwe versie kan zo niet meer tussen de controle en het opslaan komen.
                var sentAt = DateTime.UtcNow;
                var sentBy = Actor();
                var kind = document.Kind;
                var version = document.Version;
                var updated = await db.SalesOrderDocuments
                    .Where(d => d.Id == documentId && d.SalesOrderId == orderId && d.SentAt == null
                        && !db.SalesOrderDocuments.Any(n => n.SalesOrderId == orderId && n.Kind == kind && n.Version > version))
                    .ExecuteUpdateAsync(u => u.SetProperty(d => d.SentAt, (DateTime?)sentAt).SetProperty(d => d.SentBy, sentBy), cancellationToken);
                if (updated == 0)
                {
                    // Intussen doorgegeven (idempotent) of er is een nieuwere versie bij gekomen.
                    var newest = await db.SalesOrderDocuments.AsNoTracking().Where(d => d.SalesOrderId == orderId && d.Kind == kind).MaxAsync(d => d.Version, cancellationToken);
                    if (version != newest)
                    {
                        return ServiceResult<SalesOrderDocumentsDto>.Conflict($"Version {version} is not the latest ({newest}); pass on the latest version.", new { code = "notLatest" });
                    }
                }
            }
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<SalesOrderDocumentsDto>.Ok(await DocumentsDtoAsync(order, cancellationToken));
        }

        // De snapshot van één versie (voor de PDF), of null.
        public async Task<(SalesOrderDocument Document, LoadingSlipSnapshot Snapshot)?> SnapshotAsync(int orderId, int documentId, CancellationToken cancellationToken = default)
        {
            var document = await db.SalesOrderDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId && d.SalesOrderId == orderId, cancellationToken);
            return document == null ? null : (document, LoadingSlipBuilder.Deserialize(document.SnapshotJson));
        }

        private void AddVersion(SalesOrder order, CompanySettings settings, int version, string reason, DateTime now) =>
            AddVersion(order, LoadingSlipBuilder.Build(order, settings), version, reason, now);

        private void AddVersion(SalesOrder order, LoadingSlipSnapshot snapshot, int version, string reason, DateTime now) =>
            db.SalesOrderDocuments.Add(new SalesOrderDocument
            {
                SalesOrderId = order.Id, Kind = SalesOrderDocumentRules.KindLoadingSlip, Number = SalesOrderDocumentRules.Number(order.OrderNumber), Version = version,
                Reason = reason, SnapshotJson = LoadingSlipBuilder.Serialize(snapshot), ContentHash = LoadingSlipBuilder.Hash(snapshot), CreatedAt = now, CreatedBy = Actor(),
            });

        private async Task<SalesOrderDocumentsDto> DocumentsDtoAsync(SalesOrder order, CancellationToken cancellationToken)
        {
            var documents = await db.SalesOrderDocuments.AsNoTracking().Where(d => d.SalesOrderId == order.Id).OrderByDescending(d => d.Version).ToListAsync(cancellationToken);
            var latest = documents.Count == 0 ? 0 : documents.Max(d => d.Version);
            return new SalesOrderDocumentsDto
            {
                SalesOrderId = order.Id, ConfirmedAt = UtcKind(order.ConfirmedAt), ConfirmedBy = order.ConfirmedBy, LoadAt = UtcKind(order.LoadAt), LicensePlate = order.LicensePlate,
                NeedsResend = SalesOrderDocumentRules.NeedsResend(documents),
                Documents = documents.Select(d => new SalesOrderDocumentDto
                {
                    Id = d.Id, Kind = d.Kind, Number = d.Number, Version = d.Version, Reason = d.Reason, CreatedAt = UtcKind(d.CreatedAt).Value, CreatedBy = d.CreatedBy,
                    SentAt = UtcKind(d.SentAt), SentBy = d.SentBy, Latest = d.Version == latest,
                }).ToList(),
            };
        }

        private Task<SalesOrder> LoadAsync(int orderId, CancellationToken cancellationToken) =>
            db.SalesOrders
                .Include(o => o.Customer).Include(o => o.SalesPartij).Include(o => o.Documents)
                .Include(o => o.PackagingLines).ThenInclude(p => p.CostArticle)
                .Include(o => o.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v.Product)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.PalletContentLine).ThenInclude(c => c.Pallet)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.PalletContentLine).ThenInclude(c => c.Partij)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.SupplyCommitmentLine).ThenInclude(s => s.Partij)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.SupplyCommitmentLine).ThenInclude(s => s.SupplyCommitment).ThenInclude(c => c.Vendor)
                .Include(o => o.Lines).ThenInclude(l => l.Reservations).ThenInclude(r => r.SupplyCommitmentLine).ThenInclude(s => s.SupplyCommitment).ThenInclude(c => c.Partij)
                .AsSplitQuery()
                .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        private async Task<CompanySettings> SettingsAsync(CancellationToken cancellationToken) =>
            await db.CompanySettings.AsNoTracking().OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken) ?? new CompanySettings();

        private static string Plate(string value, Dictionary<string, List<string>> errors)
        {
            var plate = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (plate?.Length > SalesOrderDocumentRules.MaxLicensePlateLength) ValidationErrors.Add(errors, "licensePlate", $"At most {SalesOrderDocumentRules.MaxLicensePlateLength} characters.");
            return plate;
        }

        private static DateTime? Utc(DateTime? value) => value == null ? null : value.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value.Value.ToUniversalTime();
        private static DateTime? UtcKind(DateTime? value) => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

        private static ServiceResult<SalesOrderDocumentsDto> Validation(Dictionary<string, List<string>> errors) =>
            ServiceResult<SalesOrderDocumentsDto>.Validation(ValidationErrors.ToArrays(errors));
    }
}
