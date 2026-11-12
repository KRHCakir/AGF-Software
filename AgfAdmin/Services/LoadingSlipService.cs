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
            // De laadbon, en de transportopdracht als er transport is ingevuld: beide versie 1.
            AddChangedVersions(order, await SettingsAsync(cancellationToken), now);
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
            // Laadgegevens en de nieuwe versies van de documenten (laadbon, transportopdracht) in één save: nooit opgeslagen gegevens zonder
            // de documenten die erbij horen.
            AddChangedVersions(order, await SettingsAsync(cancellationToken), DateTime.UtcNow);
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
        private async Task<bool?> RefreshOnceAsync(int orderId, CancellationToken cancellationToken)
        {
            // Altijd de stand in de database: een al gevolgde order (uit de opdracht ervoor, of een eerdere verversing) wordt door een
            // nieuwe query niet bijgewerkt, en dan bleef een gelijktijdige wijziging buiten de bon. Hier staat niets onopgeslagen.
            db.ChangeTracker.Clear();
            var order = await LoadAsync(orderId, cancellationToken);
            if (order?.ConfirmedAt == null || order.Status == ReservationRules.OrderStatusCancelled) return false;
            if (AddChangedVersions(order, await SettingsAsync(cancellationToken), DateTime.UtcNow) == 0) return false;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                // Alleen een versiebotsing met een gelijktijdige verversing; andere databasefouten gaan naar de aanroeper.
                db.ChangeTracker.Clear();
                return null;
            }
        }

        // Doorgegeven aan de loods (afgedrukt of overhandigd); deel 3 zet dit bij verzenden. Alleen de nieuwste versie, één keer.
        //
        // Gelijktijdige wijzigingen worden op inhoud gecontroleerd, niet met locks: een bon kan veranderen door elke opdracht op de order
        // (reservering, toewijzing, emballage, laden op), en die raken verschillende tabellen. "Opnieuw versturen" hangt alleen af van de
        // nieuwste versie, en elke andere inhoud geeft een nieuwe versie zonder doorgifte (de verversing na de opdracht, de verversing
        // hieronder na de doorgifte, en het vangnet bij het ophalen). Komt er een wijziging tussen de controle en de doorgifte, dan is de
        // doorgegeven versie dus meteen niet meer de nieuwste en vraagt de order weer om opnieuw versturen. Dat klopt ook: precies die
        // versie is aan de loods gegeven.
        public async Task<ServiceResult<SalesOrderDocumentsDto>> MarkSentAsync(int orderId, int documentId, CancellationToken cancellationToken = default)
        {
            // Eerst verversen, los opgeslagen: ook als de gevraagde versie daarna niet de nieuwste blijkt, blijft de nieuwe versie bewaard.
            await RefreshAsync(orderId, cancellationToken);
            var order = await LoadAsync(orderId, cancellationToken);
            var document = order?.Documents.SingleOrDefault(d => d.Id == documentId);
            if (document == null) return ServiceResult<SalesOrderDocumentsDto>.NotFound($"Document {documentId} of sales order {orderId} does not exist.");
            var newest = order.Documents.Where(d => d.Kind == document.Kind).Max(d => d.Version);
            if (document.Version != newest) return NotLatest(document.Version, newest);
            if (document.SentAt == null)
            {
                // In één statement, alleen als die versie nog niet doorgegeven is en er geen nieuwere is. Het zet alleen SentAt en SentBy, één
                // keer: de eis van TraceabilityGuard regel 31. Een update in één statement gaat buiten de guard om, daarom staat die eis hier
                // in de voorwaarde.
                var kind = document.Kind;
                var version = document.Version;
                var sentAt = DateTime.UtcNow;
                var sentBy = Actor();
                var updated = await db.SalesOrderDocuments
                    .Where(d => d.Id == documentId && d.SalesOrderId == orderId && d.SentAt == null
                        && !db.SalesOrderDocuments.Any(n => n.SalesOrderId == orderId && n.Kind == kind && n.Version > version))
                    .ExecuteUpdateAsync(u => u.SetProperty(d => d.SentAt, (DateTime?)sentAt).SetProperty(d => d.SentBy, sentBy), cancellationToken);
                if (updated == 0)
                {
                    // Intussen al doorgegeven (dan is dit een herhaling) of er kwam net een nieuwere versie bij.
                    var current = await db.SalesOrderDocuments.AsNoTracking().Where(d => d.SalesOrderId == orderId && d.Kind == kind).MaxAsync(d => d.Version, cancellationToken);
                    if (current != version) return NotLatest(version, current);
                }
            }
            // Daarna opnieuw verversen: een wijziging die na de eerste verversing opgeslagen werd, staat zo meteen als nieuwe versie klaar.
            await RefreshAsync(orderId, cancellationToken);
            return ServiceResult<SalesOrderDocumentsDto>.Ok(await DocumentsDtoAsync(await LoadAsync(orderId, cancellationToken), cancellationToken));
        }

        private static ServiceResult<SalesOrderDocumentsDto> NotLatest(int version, int newest) =>
            ServiceResult<SalesOrderDocumentsDto>.Conflict($"Version {version} is not the latest ({newest}); pass on the latest version.", new { code = "notLatest" });

        public async Task<LoadingSlipSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            var settings = await SettingsAsync(cancellationToken);
            return new LoadingSlipSettingsDto { Language = settings.LoadingSlipLanguage, ShowCustomer = settings.LoadingSlipShowCustomer, ShowSupplier = settings.LoadingSlipShowSupplier, WarehouseAddress = settings.LoadingSlipWarehouseAddress };
        }

        // Geldt voor nieuwe versies; een bestaande versie houdt haar snapshot (en dus haar kolommen).
        public async Task<ServiceResult<LoadingSlipSettingsDto>> UpdateSettingsAsync(LoadingSlipSettingsDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var language = request?.Language?.Trim().ToLowerInvariant();
            if (language == null || !SalesOrderDocumentRules.Languages.Contains(language))
            {
                ValidationErrors.Add(errors, "language", $"Language must be one of: {string.Join(", ", SalesOrderDocumentRules.Languages)}.");
            }
            var warehouse = string.IsNullOrWhiteSpace(request?.WarehouseAddress) ? null : request.WarehouseAddress.Trim();
            if (warehouse?.Length > SalesOrderDocumentRules.MaxWarehouseAddressLength)
            {
                ValidationErrors.Add(errors, "warehouseAddress", $"At most {SalesOrderDocumentRules.MaxWarehouseAddressLength} characters.");
            }
            if (errors.Count > 0) return ServiceResult<LoadingSlipSettingsDto>.Validation(ValidationErrors.ToArrays(errors));
            var settings = await db.CompanySettings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            if (settings == null) db.CompanySettings.Add(settings = new CompanySettings());
            settings.LoadingSlipLanguage = language;
            settings.LoadingSlipShowCustomer = request.ShowCustomer;
            settings.LoadingSlipShowSupplier = request.ShowSupplier;
            settings.LoadingSlipWarehouseAddress = warehouse;
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<LoadingSlipSettingsDto>.Ok(await GetSettingsAsync(cancellationToken));
        }

        // De snapshot van één versie (voor de PDF), of null.
        public async Task<(SalesOrderDocument Document, LoadingSlipSnapshot Snapshot)?> SnapshotAsync(int orderId, int documentId, CancellationToken cancellationToken = default)
        {
            // Alleen de laadbon; de PDF van de transportopdracht volgt.
            var document = await db.SalesOrderDocuments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId && d.SalesOrderId == orderId && d.Kind == SalesOrderDocumentRules.KindLoadingSlip, cancellationToken);
            return document == null ? null : (document, LoadingSlipBuilder.Deserialize(document.SnapshotJson));
        }

        // Een nieuwe versie van elk document waarvan de inhoud veranderd is (vingerafdruk): de laadbon altijd, de transportopdracht alleen als er
        // transport is ingevuld. De eerste versie heet "bevestigd", de volgende "gewijzigd". Geeft het aantal nieuwe versies.
        private int AddChangedVersions(SalesOrder order, CompanySettings settings, DateTime now)
        {
            var added = 0;
            var slip = LoadingSlipBuilder.Build(order, settings);
            if (AddIfChanged(order, SalesOrderDocumentRules.KindLoadingSlip, LoadingSlipBuilder.Serialize(slip), LoadingSlipBuilder.Hash(slip), now)) added++;
            if (order.Transport != null)
            {
                var transport = TransportOrderBuilder.Build(order, settings);
                if (AddIfChanged(order, SalesOrderDocumentRules.KindTransportOrder, TransportOrderBuilder.Serialize(transport), TransportOrderBuilder.Hash(transport), now)) added++;
            }
            return added;
        }

        private bool AddIfChanged(SalesOrder order, string kind, string json, string hash, DateTime now)
        {
            var latest = order.Documents.Where(d => d.Kind == kind).OrderByDescending(d => d.Version).FirstOrDefault();
            if (latest != null && latest.ContentHash == hash) return false;
            db.SalesOrderDocuments.Add(new SalesOrderDocument
            {
                SalesOrderId = order.Id, Kind = kind, Number = SalesOrderDocumentRules.NumberFor(kind, order.OrderNumber), Version = (latest?.Version ?? 0) + 1,
                Reason = latest == null ? SalesOrderDocumentRules.ReasonConfirmed : SalesOrderDocumentRules.ReasonChanged,
                SnapshotJson = json, ContentHash = hash, CreatedAt = now, CreatedBy = Actor(),
            });
            return true;
        }

        private async Task<SalesOrderDocumentsDto> DocumentsDtoAsync(SalesOrder order, CancellationToken cancellationToken)
        {
            var documents = await db.SalesOrderDocuments.AsNoTracking().Where(d => d.SalesOrderId == order.Id).OrderBy(d => d.Kind).ThenByDescending(d => d.Version).ToListAsync(cancellationToken);
            // De nieuwste versie per soort document (laadbon, transportopdracht).
            var latest = documents.GroupBy(d => d.Kind).ToDictionary(g => g.Key, g => g.Max(d => d.Version));
            List<SalesOrderDocument> OfKind(string kind) => documents.Where(d => d.Kind == kind).ToList();
            return new SalesOrderDocumentsDto
            {
                SalesOrderId = order.Id, ConfirmedAt = UtcKind(order.ConfirmedAt), ConfirmedBy = order.ConfirmedBy, LoadAt = UtcKind(order.LoadAt), LicensePlate = order.LicensePlate,
                NeedsResend = SalesOrderDocumentRules.NeedsResend(OfKind(SalesOrderDocumentRules.KindLoadingSlip)),
                TransportOrderNeedsResend = SalesOrderDocumentRules.NeedsResend(OfKind(SalesOrderDocumentRules.KindTransportOrder)),
                HasTransport = order.Transport != null,
                Documents = documents.Select(d => new SalesOrderDocumentDto
                {
                    Id = d.Id, Kind = d.Kind, Number = d.Number, Version = d.Version, Reason = d.Reason, CreatedAt = UtcKind(d.CreatedAt).Value, CreatedBy = d.CreatedBy,
                    SentAt = UtcKind(d.SentAt), SentBy = d.SentBy, Latest = d.Version == latest[d.Kind],
                }).ToList(),
            };
        }

        private Task<SalesOrder> LoadAsync(int orderId, CancellationToken cancellationToken) =>
            db.SalesOrders
                .Include(o => o.Customer).ThenInclude(c => c.Relation).ThenInclude(r => r.Addresses).Include(o => o.SalesPartij).Include(o => o.Documents)
                .Include(o => o.Transport).ThenInclude(t => t.CarrierRelation).ThenInclude(r => r.Addresses)
                .Include(o => o.Transport).ThenInclude(t => t.UnloadingAddress)
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
