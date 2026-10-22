using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    public class TraceabilityViolationException : InvalidOperationException
    {
        public TraceabilityViolationException(string message) : base(message) {}
    }

    // Regel 13, alleen wanneer de tegenpartij al is opgeslagen: een bevestiging voor een verzending die intussen is gecorrigeerd of
    // teruggenomen of al een actieve bevestiging (of dit volgnummer) heeft, een tweede intrekking van dezelfde bevestiging, of een
    // correctie/terugname terwijl er intussen een actieve bevestiging is. De services controleren dit vooraf; deze fout ontstaat dus alleen
    // als een andere schrijver na die controle en vóór deze save heeft gecommit. Alleen dit type mogen de services als 409 behandelen;
    // elke andere guardfout blijft een fout van de schrijver.
    public sealed class ShipmentDeliveryConflictException : TraceabilityViolationException
    {
        public ShipmentDeliveryConflictException(string message) : base(message) {}
    }

    // Draait vóór elke SaveChanges/SaveChangesAsync, zodat geen enkele writer de regels kan vergeten:
    // 1. een StockMovement/StockReservation met PalletContentLineId heeft dezelfde SupplyCommitmentLineId als die regel;
    // 2. de bronregel van een bestaande PalletContentLine ligt vast, en ook haar partij-GGN: een opgeslagen GGN wordt nooit stilzwijgend
    //    gewijzigd, gezet of gewist (vergeleken met de opgeslagen waarde, ook bij een losgekoppelde Update en ook in dezelfde save als een
    //    verzending of correctie); artikelstandaarden (ProductVariant.DefaultGgn) blijven gewoon wijzigbaar;
    // 3. PalletAuditEvents zijn append-only;
    // 4. inhoud met een DirectTrade-bron staat nooit op een pallet met LocationContext Warehouse;
    // 5. een nieuwe Active verkoopreservering (SalesOrderLineId) staat op Approved inhoud van een Received + Warehouse-pallet,
    //    met route Warehouse en exact dezelfde eenheid;
    // 6. de som van Active verkoopreserveringen per inhoudsregel overschrijdt nooit de hoeveelheid of het netto-kg;
    // 7. een nieuwe Active verkoopreservering hoort bij een orderregel van een Open verkooporder (ook als die order in dezelfde save wordt geannuleerd);
    // 8. het afboekgrootboek is in beide richtingen consistent: een verhoging van DispatchedQuantity/-NetWeightKg op een inhoudsregel
    //    bestaat alleen samen met Dispatch-mutaties van exact hetzelfde totaal in dezelfde save, elke Dispatch-mutatie hoort bij een
    //    reservering die in dezelfde save Active -> Consumed gaat (gelijke hoeveelheid, eenheid en kg) met een verzendregel, afgeboekt
    //    daalt nooit, en elke nieuwe verzendregel is een exacte kopie van precies één zo'n reservering met precies haar nieuwe
    //    Dispatch-mutatie, onder een verzending van dezelfde verkooporder (navigatie of alleen FK).
    // 10. een verkoopreservering heeft een bekende bron (Physical of DirectTrade). Physical staat altijd op een inhoudsregel (regel 5).
    //    Een DirectTrade-toewijzing (Source DirectTrade) staat nooit op een inhoudsregel, heeft nooit kg, hoort bij een bronregel van een
    //    DirectTrade-toezegging in een toewijsbare status (toelatingslijst) van exact dezelfde klant als de verkooporder, met dezelfde
    //    variant en exact dezelfde eenheid; de som van Active toewijzingen per bronregel overschrijdt nooit de verwachte hoeveelheid;
    //    en een toewijzing gaat alleen Active -> Released, nooit terug, en de bron van een bestaande verkoopreservering ligt vast.
    //    Capaciteit per bronregel = Active + Consumed toewijzingen: een verzending maakt nooit capaciteit vrij. Consumed is eindtoestand.
    // 11. een DirectTrade-toewijzing wordt alleen Consumed (vanuit Active, met tijdstip) samen met precies één nieuwe DirectTrade-verzendregel
    //    in dezelfde save, en omgekeerd; de regel kopieert hoeveelheid (hele toewijzing), eenheid, orderregel en bronregel, valt onder een
    //    verzending van dezelfde order en klant, kopieert de echte bronpartij, toezegging en leverancier van de opgeslagen bronregel, en
    //    een afwijkende partij van een ingevulde (echte) bronpartij heeft een toelichting. Hoeveelheid, eenheid, bronregel en orderregel
    //    van een DirectTrade-toewijzing liggen vast (tegen OriginalValues), ook bij en na Consumed. Er ontstaat
    //    geen voorraadmutatie of magazijnverzendregel (regel 8 geldt alleen voor Physical). DirectTrade-verzendingen zijn append-only.
    // 12. een DirectTrade-toewijzing gaat alleen Consumed -> Corrected samen met precies één nieuwe DirectTrade-correctieregel in dezelfde
    //    save, en omgekeerd; de regel hoort bij de (opgeslagen) verzendregel van díe toewijzing en kopieert haar hoeveelheid, eenheid, kg,
    //    bronregel en orderregel; een correctie dekt alle regels van één verzending, hoort bij dezelfde order, draagt de verklaring
    //    (vlag, code, vaste tekst) en een reden. Geen voorraadmutatie, inhoudsregel of magazijncorrectieregel (regel 9 is Physical).
    //    Na Corrected liggen hoeveelheid, eenheid, bronregel, orderregel, ConsumedAt en CorrectedAt vast.
    // 13. een afleverbevestiging verwijst naar precies één bestaande verzending van de juiste soort en dezelfde order, draagt de verklaring
    //    (vlag, code, vaste tekst), heeft een afleverdatum tussen de verzenddatum en vandaag (Europe/Amsterdam, gedeelde kalender) en
    //    hoort nooit bij een gecorrigeerde of teruggenomen verzending; omgekeerd krijgt een verzending met een ACTIEVE bevestiging
    //    (zonder opgeslagen intrekking, DeliveryState) geen (magazijn- of DirectTrade-)correctie. Per verzending hooguit één actieve
    //    bevestiging: volgnummer = hoogste + 1 en alle eerdere ingetrokken. Een intrekking hoort bij een opgeslagen, nog niet ingetrokken
    //    bevestiging, kopieert soort/verzending/order, draagt verklaring en reden. Intrekking samen met bevestiging of correctie van dezelfde
    //    verzending in één save is verboden. Bevestigingen en intrekkingen zijn append-only. Een nieuwe DirectTrade-verzending heeft
    //    ShippedOn <= vandaag.
    // Regels 5 t/m 7 en 10 t/m 12 gelden niet voor legacy-reserveringen met OrderLineId; vrijgeven wordt nooit geblokkeerd.
    // 9. een verzendcorrectie is in beide richtingen consistent: een reservering gaat alleen Consumed -> Corrected samen met precies één
    //    correctieregel (exacte kopie van de verzendregel), één nieuwe inhoudsregel in quarantaine op dezelfde pallet met dezelfde partij en
    //    bronregel en exact dezelfde hoeveelheid/kg (herkomst = die correctieregel), en één compenserende Correction-mutatie; de correctie
    //    hoort bij dezelfde order als de verzending. Correctie-inhoud en zulke mutaties bestaan nooit buiten een correctie. De herkomst, de
    //    bronregel en de partij van correctie-inhoud zijn onveranderlijk; Corrected is een eindtoestand.
    // Append-only (regel 3): PalletAuditEvents, SalesOrderAuditEvents, SalesDispatches, SalesDispatchLines, SalesDispatchCorrections,
    // SalesDispatchCorrectionLines, Dispatch-mutaties en compenserende Correction-mutaties (andere mutatietypen blijven ongemoeid).
    // Er wordt nooit automatisch omgekoppeld. Een niet-bestaande inhoudsregel laten we over aan de FK in de database.
    // Let op: ExecuteUpdate/ExecuteDelete en ruwe SQL gaan buiten de change tracker om en dus ook buiten deze guard.
    internal static class TraceabilityGuard
    {
        public static void Validate(AppDbContext db) =>
            // Met async = false voert de kern alleen synchrone EF-aanroepen uit en is de taak al voltooid.
            ValidateCoreAsync(db, async: false, CancellationToken.None).GetAwaiter().GetResult();

        public static Task ValidateAsync(AppDbContext db, CancellationToken cancellationToken) =>
            ValidateCoreAsync(db, async: true, cancellationToken);

        private sealed record SourceLink(string Entity, int ContentLineId, int SupplyCommitmentLineId);

        private static async Task ValidateCoreAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.AutoDetectChangesEnabled)
            {
                db.ChangeTracker.DetectChanges();
            }

            // Regel 3: een auditrecord mag nooit worden gewijzigd of verwijderd.
            if (db.ChangeTracker.Entries<PalletAuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Pallet audit events are append-only and cannot be modified or deleted.");
            }
            if (db.ChangeTracker.Entries<SalesOrderAuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Sales order audit events are append-only and cannot be modified or deleted.");
            }
            if (db.ChangeTracker.Entries<SalesDispatch>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
                || db.ChangeTracker.Entries<SalesDispatchLine>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Sales dispatches are append-only and cannot be modified or deleted.");
            }
            if (db.ChangeTracker.Entries<StockMovement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted
                    && (e.Entity.Type == ReservationRules.MovementTypeDispatch || e.OriginalValues.GetValue<string>(nameof(StockMovement.Type)) == ReservationRules.MovementTypeDispatch)))
            {
                throw new TraceabilityViolationException("Dispatch stock movements are append-only and cannot be modified or deleted.");
            }
            if (db.ChangeTracker.Entries<DirectTradeShipment>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
                || db.ChangeTracker.Entries<DirectTradeShipmentLine>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("DirectTrade shipments are append-only and cannot be modified or deleted.");
            }
            if (db.ChangeTracker.Entries<ShipmentDeliveryRetraction>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Shipment delivery retractions are append-only and cannot be modified or deleted.");
            }
            if (db.ChangeTracker.Entries<ShipmentDeliveryConfirmation>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Shipment delivery confirmations are append-only and cannot be modified or deleted.");
            }
            if (db.ChangeTracker.Entries<DirectTradeShipmentCorrection>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
                || db.ChangeTracker.Entries<DirectTradeShipmentCorrectionLine>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("DirectTrade shipment corrections are append-only and cannot be modified or deleted.");
            }
            if (db.ChangeTracker.Entries<SalesDispatchCorrection>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
                || db.ChangeTracker.Entries<SalesDispatchCorrectionLine>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Sales dispatch corrections are append-only and cannot be modified or deleted.");
            }
            if (db.ChangeTracker.Entries<StockMovement>().Any(e => e.State is EntityState.Modified or EntityState.Deleted
                    && (e.Entity.SalesDispatchCorrectionLineId != null || e.OriginalValues.GetValue<int?>(nameof(StockMovement.SalesDispatchCorrectionLineId)) != null)))
            {
                throw new TraceabilityViolationException("Compensating correction stock movements are append-only and cannot be modified or deleted.");
            }
            // Corrected is een eindtoestand: nooit meer Active, Consumed of Released.
            if (db.ChangeTracker.Entries<StockReservation>().Any(e => e.State == EntityState.Modified
                    && e.OriginalValues.GetValue<string>(nameof(StockReservation.Status)) == ReservationRules.StatusCorrected
                    && e.Entity.Status != ReservationRules.StatusCorrected))
            {
                throw new TraceabilityViolationException("A corrected reservation is terminal and can never become Active, Consumed or Released again.");
            }
            // Herkomst, bronregel en partij van correctie-inhoud zijn onveranderlijk (de bronregel van elke inhoud al via regel 2).
            foreach (var entry in db.ChangeTracker.Entries<PalletContentLine>().Where(e => e.State == EntityState.Modified))
            {
                var originBefore = entry.OriginalValues.GetValue<int?>(nameof(PalletContentLine.OriginCorrectionLineId));
                if (originBefore != entry.Property(c => c.OriginCorrectionLineId).CurrentValue)
                {
                    throw new TraceabilityViolationException($"The correction origin of pallet content line {entry.Entity.Id} cannot be set, removed or reassigned.");
                }
                if (originBefore != null && entry.OriginalValues.GetValue<string>(nameof(PalletContentLine.LotNumber)) != entry.Entity.LotNumber)
                {
                    throw new TraceabilityViolationException($"The lot of correction content line {entry.Entity.Id} cannot be changed.");
                }
            }

            await CheckContentLineGgnAsync(db, async, cancellationToken);
            await CheckRelationMirrorsAsync(db, async, cancellationToken);
            await CheckSourceLinksAsync(db, async, cancellationToken);
            await CheckNoDirectTradeInWarehouseAsync(db, async, cancellationToken);
            await CheckDirectTradeAllocationsAsync(db, async, cancellationToken);
            await CheckSalesReservationsAsync(db, async, cancellationToken);
            await CheckReservationsOnOpenOrdersAsync(db, async, cancellationToken);
            await CheckDispatchLedgerAsync(db, async, cancellationToken);
            await CheckDirectTradeShipmentsAsync(db, async, cancellationToken);
            await CheckDirectTradeShipmentCorrectionsAsync(db, async, cancellationToken);
            await CheckDeliveryConfirmationsAsync(db, async, cancellationToken);
            await CheckCorrectionLedgerAsync(db, async, cancellationToken);
        }

        // ---- Regel 15: de gedeelde kolommen op Customer/Vendor zijn een spiegel van de relatie ----
        //
        // Naam, btw-nummer, adressen en contactgegevens hebben één wijzigbare bron (Relation). Een gewijzigde spiegelkolom op een
        // bestaande rol moet overeenkomen met wat RelationService.SyncMirror uit de (gevolgde of opgeslagen) relatie afleidt; anders
        // probeert een schrijver de centrale gegevens te omzeilen. Rolspecifieke velden (betaaltermijn, valuta) blijven vrij.
        private static readonly string[] CustomerMirror = { nameof(Customer.Name), nameof(Customer.VATNumber), nameof(Customer.BillingAddress), nameof(Customer.ShippingAddress), nameof(Customer.ContactPerson), nameof(Customer.Email), nameof(Customer.Phone) };
        private static readonly string[] VendorMirror = { nameof(Vendor.Name), nameof(Vendor.VATNumber), nameof(Vendor.BillingAddress), nameof(Vendor.Email) };

        private static async Task CheckRelationMirrorsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var customers = db.ChangeTracker.Entries<Customer>().Where(e => e.State == EntityState.Modified && CustomerMirror.Any(p => e.Property(p).IsModified)).ToList();
            var vendors = db.ChangeTracker.Entries<Vendor>().Where(e => e.State == EntityState.Modified && VendorMirror.Any(p => e.Property(p).IsModified)).ToList();
            if (customers.Count == 0 && vendors.Count == 0)
            {
                return;
            }
            foreach (var entry in customers)
            {
                var relation = await RelationForMirrorAsync(db, entry.Entity.RelationId, async, cancellationToken);
                var expected = new Customer();
                var probe = Shallow(relation);
                probe.Customer = expected;
                Services.RelationService.SyncMirror(probe);
                var c = entry.Entity;
                if (!Same(c.Name, expected.Name) || !Same(c.VATNumber, expected.VATNumber) || !Same(c.BillingAddress, expected.BillingAddress) || !Same(c.ShippingAddress, expected.ShippingAddress)
                    || !Same(c.ContactPerson, expected.ContactPerson) || !Same(c.Email, expected.Email) || !Same(c.Phone, expected.Phone))
                {
                    throw new TraceabilityViolationException($"Customer {c.Id} mirrors relation {c.RelationId}: name, VAT number, addresses and contact details can only change through the relation.");
                }
            }
            foreach (var entry in vendors)
            {
                var relation = await RelationForMirrorAsync(db, entry.Entity.RelationId, async, cancellationToken);
                var expected = new Vendor();
                var probe = Shallow(relation);
                probe.Vendor = expected;
                Services.RelationService.SyncMirror(probe);
                var v = entry.Entity;
                if (!Same(v.Name, expected.Name) || !Same(v.VATNumber, expected.VATNumber) || !Same(v.BillingAddress, expected.BillingAddress) || !Same(v.Email, expected.Email))
                {
                    throw new TraceabilityViolationException($"Vendor {v.Id} mirrors relation {v.RelationId}: name, VAT number, address and e-mail can only change through the relation.");
                }
            }
        }

        // De relatie zoals zij in deze save wordt opgeslagen: gevolgd (met haar gevolgde adressen/contacten) of anders uit de database.
        private static async Task<Relation> RelationForMirrorAsync(AppDbContext db, int relationId, bool async, CancellationToken cancellationToken)
        {
            var tracked = db.ChangeTracker.Entries<Relation>().FirstOrDefault(e => e.State is not (EntityState.Deleted or EntityState.Detached) && e.Property(r => r.Id).CurrentValue == relationId)?.Entity;
            if (tracked != null)
            {
                return tracked;
            }
            var stored = await FirstOrDefaultAsync(db.Relations.AsNoTracking().Include(r => r.Addresses).Include(r => r.Contacts).ThenInclude(c => c.Channels).Where(r => r.Id == relationId), async, cancellationToken);
            return stored ?? throw new TraceabilityViolationException($"Relation {relationId} does not exist.");
        }

        private static Relation Shallow(Relation source) => new()
        {
            Id = source.Id, Name = source.Name, VatNumber = source.VatNumber, Email = source.Email, Phone = source.Phone,
            Addresses = source.Addresses.ToList(), Contacts = source.Contacts.ToList()
        };

        private static bool Same(string a, string b) => string.Equals(string.IsNullOrWhiteSpace(a) ? null : a, string.IsNullOrWhiteSpace(b) ? null : b, StringComparison.Ordinal);

        // ---- Regel 2 (vervolg): de partij-GGN van een bestaande inhoudsregel is historisch ----
        //
        // Vergeleken met de opgeslagen waarde, niet met OriginalValues: zo wordt ook een losgekoppelde Update (OriginalValues =
        // CurrentValues) herkend. Geldt voor elke wijziging, dus ook wissen (naar null) en alsnog zetten (vanaf null), en ongeacht wat er
        // verder in dezelfde save gebeurt (verzending, correctie, keuring). Nieuwe regels zijn vrij; correctie-inhoud valt onder regel 9.
        private static async Task CheckContentLineGgnAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var modified = db.ChangeTracker.Entries<PalletContentLine>()
                .Where(e => e.State == EntityState.Modified && e.Property(c => c.Ggn).IsModified)
                .ToList();
            if (modified.Count == 0)
            {
                return;
            }
            var ids = modified.Select(e => e.Entity.Id).ToList();
            var stored = (await ToListAsync(db.PalletContentLines.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Ggn }), async, cancellationToken))
                .ToDictionary(c => c.Id, c => c.Ggn);
            foreach (var entry in modified)
            {
                var before = stored.GetValueOrDefault(entry.Entity.Id);
                if (!string.Equals(before, entry.Entity.Ggn, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"The GGN of pallet content line {entry.Entity.Id} is historical and cannot be changed, set or cleared (stored: {before ?? "none"}, attempted: {entry.Entity.Ggn ?? "none"}).");
                }
            }
        }

        // ---- Regel 9: verzendcorrecties in beide richtingen ----

        private static async Task CheckCorrectionLedgerAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var correctedEntries = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State == EntityState.Modified
                    && e.Entity.Status == ReservationRules.StatusCorrected
                    && e.OriginalValues.GetValue<string>(nameof(StockReservation.Status)) != ReservationRules.StatusCorrected)
                .ToList();
            if (correctedEntries.Any(e => e.OriginalValues.GetValue<string>(nameof(StockReservation.Status)) != ReservationRules.StatusConsumed))
            {
                throw new TraceabilityViolationException("Only a Consumed reservation can become Corrected.");
            }
            if (db.ChangeTracker.Entries<StockReservation>().Any(e => e.State == EntityState.Added && e.Entity.Status == ReservationRules.StatusCorrected))
            {
                throw new TraceabilityViolationException("A reservation can only become Corrected through a dispatch correction, never be created as Corrected.");
            }
            // Magazijncorrectie: alleen fysieke reserveringen. Een DirectTrade-toewijzing wordt teruggenomen via regel 12 en kan hier nooit
            // aan een magazijncorrectieregel, herstelde inhoud of Correction-mutatie hangen (de aantallen kloppen dan niet en worden geweigerd).
            var corrected = correctedEntries.Where(e => e.Entity.Source != ReservationRules.SourceDirectTrade).Select(e => e.Entity).ToList();
            var correctionLines = db.ChangeTracker.Entries<SalesDispatchCorrectionLine>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            var restoredLines = db.ChangeTracker.Entries<PalletContentLine>()
                .Where(e => e.State == EntityState.Added && (e.Entity.OriginCorrectionLine != null || e.Entity.OriginCorrectionLineId != null))
                .Select(e => e.Entity)
                .ToList();
            var compensations = db.ChangeTracker.Entries<StockMovement>()
                .Where(e => e.State == EntityState.Added && e.Entity.Type == ReservationRules.MovementTypeCorrection
                    && (e.Entity.SalesDispatchCorrectionLine != null || e.Entity.SalesDispatchCorrectionLineId != null || e.Entity.StockReservation != null || e.Entity.StockReservationId != null))
                .Select(e => e.Entity)
                .ToList();
            if (corrected.Count == 0 && correctionLines.Count == 0 && restoredLines.Count == 0 && compensations.Count == 0)
            {
                return;
            }
            if (corrected.Count != correctionLines.Count || restoredLines.Count != correctionLines.Count || compensations.Count != correctionLines.Count)
            {
                throw new TraceabilityViolationException("A dispatch correction needs, per reservation, exactly one corrected reservation, one correction line, one restored content line and one compensating correction movement in the same save.");
            }

            // Verzendregels en oorspronkelijke inhoud (navigatie of opgeslagen).
            var dispatchLineIds = correctionLines.Select(l => l.SalesDispatchLine != null ? db.Entry(l.SalesDispatchLine).Property(x => x.Id).CurrentValue : l.SalesDispatchLineId).Distinct().ToList();
            var dispatchLines = correctionLines.Where(l => l.SalesDispatchLine != null).Select(l => l.SalesDispatchLine).ToDictionary(l => l.Id);
            var missing = dispatchLineIds.Where(id => !dispatchLines.ContainsKey(id)).ToList();
            if (missing.Count > 0)
            {
                foreach (var stored in await ToListAsync(db.SalesDispatchLines.AsNoTracking().Include(l => l.SalesDispatch).Include(l => l.PalletContentLine).Where(l => missing.Contains(l.Id)), async, cancellationToken))
                {
                    dispatchLines[stored.Id] = stored;
                }
            }
            var originalContentIds = dispatchLines.Values.Select(l => l.PalletContentLineId).Distinct().ToList();
            var originals = (await ToListAsync(db.PalletContentLines.AsNoTracking().Where(c => originalContentIds.Contains(c.Id)), async, cancellationToken)).ToDictionary(c => c.Id);
            // De opgeslagen partij-GGN van de oorspronkelijke inhoud (nooit wat er in dezelfde save van gemaakt is; dat weigert regel 2 al).
            var storedGgns = originals.ToDictionary(o => o.Key, o => o.Value.Ggn);
            foreach (var entry in db.ChangeTracker.Entries<PalletContentLine>().Where(e => e.State is not (EntityState.Added or EntityState.Deleted or EntityState.Detached) && originalContentIds.Contains(e.Entity.Id)))
            {
                originals[entry.Entity.Id] = entry.Entity;
            }
            var dispatchIds = dispatchLines.Values.Select(l => l.SalesDispatch != null ? db.Entry(l.SalesDispatch).Property(d => d.Id).CurrentValue : l.SalesDispatchId).Distinct().ToList();
            var orderOfDispatch = (await ToListAsync(db.SalesDispatches.AsNoTracking().Where(d => dispatchIds.Contains(d.Id)).Select(d => new { d.Id, d.SalesOrderId }), async, cancellationToken))
                .ToDictionary(d => d.Id, d => d.SalesOrderId);
            foreach (var entry in db.ChangeTracker.Entries<SalesDispatch>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached)))
            {
                orderOfDispatch[entry.Property(d => d.Id).CurrentValue] = entry.Entity.SalesOrder?.Id ?? entry.Property(d => d.SalesOrderId).CurrentValue;
            }

            var usedReservations = new HashSet<StockReservation>();
            foreach (var correctionLine in correctionLines)
            {
                var reservation = correctionLine.StockReservation ?? corrected.FirstOrDefault(r => r.Id == correctionLine.StockReservationId);
                if (reservation == null || !corrected.Contains(reservation) || !usedReservations.Add(reservation))
                {
                    throw new TraceabilityViolationException("A dispatch correction line requires exactly one reservation that goes from Consumed to Corrected in the same save.");
                }
                if (reservation.CorrectedAt == null)
                {
                    throw new TraceabilityViolationException($"Reservation {reservation.Id} becomes Corrected without a correction time.");
                }
                var dispatchLineId = correctionLine.SalesDispatchLine != null ? db.Entry(correctionLine.SalesDispatchLine).Property(x => x.Id).CurrentValue : correctionLine.SalesDispatchLineId;
                if (!dispatchLines.TryGetValue(dispatchLineId, out var dispatchLine) || dispatchLine.StockReservationId != reservation.Id)
                {
                    throw new TraceabilityViolationException($"Correction line for reservation {reservation.Id} must reference the dispatch line that consumed that reservation.");
                }
                if (correctionLine.Quantity != dispatchLine.Quantity || !string.Equals(correctionLine.Unit, dispatchLine.Unit, StringComparison.Ordinal) || correctionLine.NetWeightKg != dispatchLine.NetWeightKg)
                {
                    throw new TraceabilityViolationException($"Correction line for reservation {reservation.Id} must copy quantity, unit and net weight of the dispatch line exactly.");
                }
                if (!originals.TryGetValue(dispatchLine.PalletContentLineId, out var original))
                {
                    throw new TraceabilityViolationException($"The original content line of dispatch line {dispatchLineId} does not exist.");
                }

                var restored = restoredLines.FirstOrDefault(c => ReferenceEquals(c.OriginCorrectionLine, correctionLine)
                    || (c.OriginCorrectionLine == null && c.OriginCorrectionLineId == db.Entry(correctionLine).Property(x => x.Id).CurrentValue));
                if (restored == null)
                {
                    throw new TraceabilityViolationException($"Correction line for reservation {reservation.Id} has no restored content line.");
                }
                var restoredPalletId = restored.Pallet != null ? db.Entry(restored.Pallet).Property(p => p.Id).CurrentValue : restored.PalletId;
                if (restored.Quantity != dispatchLine.Quantity || restored.NetWeightKg != dispatchLine.NetWeightKg || !string.Equals(restored.Unit, dispatchLine.Unit, StringComparison.Ordinal)
                    || restored.QualityStatus != PalletRules.QualityQuarantine
                    || restored.DispatchedQuantity != 0m || restored.DispatchedNetWeightKg != 0m
                    || restoredPalletId != original.PalletId
                    || restored.SupplyCommitmentLineId != original.SupplyCommitmentLineId
                    || !string.Equals(restored.LotNumber, original.LotNumber, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"Restored content for reservation {reservation.Id} must be Quarantine on the original pallet, same lot and source line, with exactly the dispatched quantity and net weight and nothing dispatched.");
                }
                // Dezelfde partij, dus exact de opgeslagen GGN van de oorspronkelijke inhoud, inclusief null (nooit uit het artikel afgeleid).
                var originalGgn = storedGgns.GetValueOrDefault(original.Id, original.Ggn);
                if (!string.Equals(restored.Ggn, originalGgn, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"Restored content for reservation {reservation.Id} must copy the stored GGN of the original content line exactly ({originalGgn ?? "none"}), not {restored.Ggn ?? "none"}.");
                }

                var movement = compensations.FirstOrDefault(m => ReferenceEquals(m.SalesDispatchCorrectionLine, correctionLine)
                    || (m.SalesDispatchCorrectionLine == null && m.SalesDispatchCorrectionLineId == db.Entry(correctionLine).Property(x => x.Id).CurrentValue));
                if (movement == null)
                {
                    throw new TraceabilityViolationException($"Correction line for reservation {reservation.Id} has no compensating correction movement.");
                }
                var movementReservation = movement.StockReservation ?? corrected.FirstOrDefault(r => r.Id == movement.StockReservationId);
                var movementContent = movement.PalletContentLine;
                if (!ReferenceEquals(movementReservation, reservation) || !ReferenceEquals(movementContent, restored)
                    || movement.Quantity != dispatchLine.Quantity || !string.Equals(movement.Unit, dispatchLine.Unit, StringComparison.Ordinal) || movement.NetWeightKg != dispatchLine.NetWeightKg
                    || movement.SupplyCommitmentLineId != original.SupplyCommitmentLineId)
                {
                    throw new TraceabilityViolationException($"The compensating movement for reservation {reservation.Id} must reference its reservation and restored content line and copy quantity, unit, net weight and source line exactly.");
                }

                var correction = correctionLine.SalesDispatchCorrection;
                var correctionOrderId = correction?.SalesOrder?.Id ?? correction?.SalesOrderId;
                var correctionDispatchId = correction?.SalesDispatch != null ? db.Entry(correction.SalesDispatch).Property(d => d.Id).CurrentValue : correction?.SalesDispatchId;
                var dispatchId = dispatchLine.SalesDispatch != null ? db.Entry(dispatchLine.SalesDispatch).Property(d => d.Id).CurrentValue : dispatchLine.SalesDispatchId;
                if (correction == null || correctionDispatchId != dispatchId || !orderOfDispatch.TryGetValue(dispatchId, out var dispatchOrderId) || correctionOrderId != dispatchOrderId)
                {
                    throw new TraceabilityViolationException($"Correction line for reservation {reservation.Id} must belong to a correction of the dispatch and sales order that consumed it.");
                }
            }
        }

        // ---- Regel 8: afboekgrootboek in beide richtingen ----

        private static async Task CheckDispatchLedgerAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var contentDeltas = new Dictionary<int, (decimal Quantity, decimal Kg)>();
            foreach (var entry in db.ChangeTracker.Entries<PalletContentLine>().Where(e => e.State == EntityState.Modified))
            {
                var dq = entry.Property(c => c.DispatchedQuantity).CurrentValue - entry.Property(c => c.DispatchedQuantity).OriginalValue;
                var dk = entry.Property(c => c.DispatchedNetWeightKg).CurrentValue - entry.Property(c => c.DispatchedNetWeightKg).OriginalValue;
                if (dq < 0m || dk < 0m)
                {
                    throw new TraceabilityViolationException($"Dispatched totals of pallet content line {entry.Entity.Id} can never decrease.");
                }
                if (dq != 0m || dk != 0m)
                {
                    contentDeltas[entry.Entity.Id] = (dq, dk);
                }
            }
            foreach (var entry in db.ChangeTracker.Entries<PalletContentLine>().Where(e => e.State == EntityState.Added))
            {
                if (entry.Entity.DispatchedQuantity != 0m || entry.Entity.DispatchedNetWeightKg != 0m)
                {
                    throw new TraceabilityViolationException("A new pallet content line starts with nothing dispatched.");
                }
            }

            var movements = db.ChangeTracker.Entries<StockMovement>()
                .Where(e => e.State == EntityState.Added && e.Entity.Type == ReservationRules.MovementTypeDispatch)
                .Select(e => e.Entity)
                .ToList();
            // Alleen Active -> Consumed telt als verbruik in deze save; elke andere overgang naar Consumed is een fout.
            var consumedEntries = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State == EntityState.Modified
                    && e.Entity.Status == ReservationRules.StatusConsumed
                    && e.OriginalValues.GetValue<string>(nameof(StockReservation.Status)) != ReservationRules.StatusConsumed)
                .ToList();
            if (consumedEntries.Any(e => e.OriginalValues.GetValue<string>(nameof(StockReservation.Status)) != ReservationRules.StatusActive))
            {
                throw new TraceabilityViolationException("Only an Active reservation can become Consumed.");
            }
            // Magazijnverbruik: alleen fysieke reserveringen. Een DirectTrade-toewijzing wordt verbruikt via regel 11 en kan hier nooit
            // aan een Dispatch-mutatie of magazijnverzendregel hangen (die vinden haar dan niet in `consumed` en worden geweigerd).
            var consumed = consumedEntries.Where(e => e.Entity.Source != ReservationRules.SourceDirectTrade).Select(e => e.Entity).ToList();
            var dispatchLines = db.ChangeTracker.Entries<SalesDispatchLine>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            if (db.ChangeTracker.Entries<StockReservation>().Any(e => e.State == EntityState.Added && e.Entity.Status == ReservationRules.StatusConsumed))
            {
                throw new TraceabilityViolationException("A reservation can only become Consumed through a dispatch, never be created as Consumed.");
            }
            if (contentDeltas.Count == 0 && movements.Count == 0 && consumed.Count == 0 && dispatchLines.Count == 0)
            {
                return;
            }

            // Elke Dispatch-mutatie: één reservering die nu Consumed wordt, met gelijke hoeveelheid/eenheid/kg en inhoudsregel, en één verzendregel.
            var consumedById = consumed.ToDictionary(r => r.Id);
            foreach (var movement in movements)
            {
                if (movement.StockReservationId == null && movement.StockReservation == null)
                {
                    throw new TraceabilityViolationException("A dispatch stock movement must reference the consumed reservation.");
                }
                var reservation = movement.StockReservation ?? (movement.StockReservationId is int id && consumedById.TryGetValue(id, out var found) ? found : null);
                if (reservation == null || !consumed.Contains(reservation))
                {
                    throw new TraceabilityViolationException("A dispatch stock movement requires its reservation to become Consumed in the same save.");
                }
                if (movement.Quantity != reservation.Quantity
                    || !string.Equals(movement.Unit, reservation.Unit, StringComparison.Ordinal)
                    || movement.NetWeightKg != reservation.NetWeightKg
                    || (movement.PalletContentLineId ?? movement.PalletContentLine?.Id) != reservation.PalletContentLineId)
                {
                    throw new TraceabilityViolationException("A dispatch stock movement must copy quantity, unit, net weight and content line of its reservation exactly.");
                }
            }
            if (movements.Count != consumed.Count)
            {
                throw new TraceabilityViolationException("Every consumed reservation needs exactly one dispatch stock movement in the same save.");
            }
            var movementReservations = movements.Select(m => m.StockReservation ?? consumedById[m.StockReservationId!.Value]).ToHashSet();
            if (movementReservations.Count != consumed.Count || consumed.Any(r => !movementReservations.Contains(r)))
            {
                throw new TraceabilityViolationException("Every consumed reservation needs exactly one dispatch stock movement in the same save.");
            }
            foreach (var reservation in consumed)
            {
                if (reservation.ConsumedAt == null)
                {
                    throw new TraceabilityViolationException($"Reservation {reservation.Id} becomes Consumed without a consumption time.");
                }
                if (!dispatchLines.Any(l => l.StockReservation == reservation || l.StockReservationId == reservation.Id))
                {
                    throw new TraceabilityViolationException($"Reservation {reservation.Id} is consumed without a sales dispatch line.");
                }
            }

            // Beide richtingen: de som van de nieuwe Dispatch-mutaties per inhoudsregel is exact de verhoging van afgeboekt, en omgekeerd.
            var movementTotals = movements
                .GroupBy(m => m.PalletContentLineId ?? m.PalletContentLine?.Id ?? 0)
                .ToDictionary(g => g.Key, g => (Quantity: g.Sum(m => m.Quantity), Kg: g.Sum(m => m.NetWeightKg ?? 0m)));
            foreach (var (contentId, delta) in contentDeltas)
            {
                if (!movementTotals.TryGetValue(contentId, out var total) || total.Quantity != delta.Quantity || total.Kg != delta.Kg)
                {
                    throw new TraceabilityViolationException($"Dispatched totals of pallet content line {contentId} increase without matching dispatch stock movements.");
                }
            }
            foreach (var (contentId, total) in movementTotals)
            {
                if (!contentDeltas.TryGetValue(contentId, out var delta) || delta.Quantity != total.Quantity || delta.Kg != total.Kg)
                {
                    throw new TraceabilityViolationException($"Dispatch stock movements on pallet content line {contentId} without a matching increase of its dispatched totals.");
                }
            }

            await CheckDispatchLinesAsync(db, dispatchLines, consumed, movements, async, cancellationToken);
        }

        // Verzendregels: elke nieuwe regel is een exacte kopie van precies één reservering die nu Active -> Consumed gaat, verwijst naar
        // precies haar nieuwe Dispatch-mutatie en hangt onder een verzending van dezelfde verkooporder. Verwijzingen mogen via navigatie
        // of alleen via FK lopen; een FK naar een nieuwe (nog ongesleutelde) entiteit wordt via de tijdelijke sleutel van de tracker gevonden.
        private static async Task CheckDispatchLinesAsync(AppDbContext db, List<SalesDispatchLine> dispatchLines, List<StockReservation> consumed, List<StockMovement> movements, bool async, CancellationToken cancellationToken)
        {
            if (dispatchLines.Count == 0)
            {
                return;
            }

            StockReservation ReservationOf(SalesDispatchLine line)
            {
                if (line.StockReservation != null) return line.StockReservation;
                return consumed.FirstOrDefault(r => r.Id == line.StockReservationId);
            }

            StockMovement MovementOf(SalesDispatchLine line)
            {
                if (line.StockMovement != null) return line.StockMovement;
                // Een nieuwe mutatie heeft een tijdelijke sleutel; vergelijk met de huidige sleutelwaarde in de tracker.
                return movements.FirstOrDefault(m => db.Entry(m).Property(x => x.Id).CurrentValue == line.StockMovementId);
            }

            // Verkooporder per verzendregel-reservering (via de orderregel) en per verzending (nieuw via navigatie, bestaand via de database).
            var reservationLineIds = dispatchLines.Select(l => ReservationOf(l)?.SalesOrderLineId).Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
            var orderOfLine = (await ToListAsync(
                    db.SalesOrderLines.AsNoTracking().Where(l => reservationLineIds.Contains(l.Id)).Select(l => new { l.Id, l.SalesOrderId }),
                    async, cancellationToken))
                .ToDictionary(l => l.Id, l => l.SalesOrderId);
            foreach (var entry in db.ChangeTracker.Entries<SalesOrderLine>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached) && reservationLineIds.Contains(e.Entity.Id)))
            {
                orderOfLine[entry.Entity.Id] = entry.Entity.SalesOrder?.Id ?? entry.Property(l => l.SalesOrderId).CurrentValue;
            }

            var storedDispatchIds = dispatchLines
                .Where(l => l.SalesDispatch == null && l.SalesDispatchId != 0)
                .Select(l => l.SalesDispatchId)
                .Distinct()
                .ToList();
            var orderOfDispatch = storedDispatchIds.Count == 0
                ? new Dictionary<int, int>()
                : (await ToListAsync(
                        db.SalesDispatches.AsNoTracking().Where(d => storedDispatchIds.Contains(d.Id)).Select(d => new { d.Id, d.SalesOrderId }),
                        async, cancellationToken))
                    .ToDictionary(d => d.Id, d => d.SalesOrderId);
            foreach (var entry in db.ChangeTracker.Entries<SalesDispatch>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached)))
            {
                orderOfDispatch[entry.Property(d => d.Id).CurrentValue] = entry.Entity.SalesOrder?.Id ?? entry.Property(d => d.SalesOrderId).CurrentValue;
            }

            var usedReservations = new HashSet<StockReservation>();
            var usedMovements = new HashSet<StockMovement>();
            foreach (var line in dispatchLines)
            {
                var reservation = ReservationOf(line);
                if (reservation == null || !consumed.Contains(reservation))
                {
                    throw new TraceabilityViolationException("A sales dispatch line requires its reservation to go from Active to Consumed in the same save.");
                }
                if (!usedReservations.Add(reservation))
                {
                    throw new TraceabilityViolationException($"Reservation {reservation.Id} has more than one sales dispatch line.");
                }
                var movement = MovementOf(line);
                if (movement == null || !movements.Contains(movement))
                {
                    throw new TraceabilityViolationException("A sales dispatch line requires a new dispatch stock movement in the same save.");
                }
                if (!usedMovements.Add(movement))
                {
                    throw new TraceabilityViolationException("A dispatch stock movement can belong to only one sales dispatch line.");
                }
                var movementReservation = movement.StockReservation ?? consumed.FirstOrDefault(r => r.Id == movement.StockReservationId);
                if (!ReferenceEquals(movementReservation, reservation))
                {
                    throw new TraceabilityViolationException("A sales dispatch line must reference the dispatch stock movement of its own reservation.");
                }
                var lineContentId = line.PalletContentLine != null ? db.Entry(line.PalletContentLine).Property(c => c.Id).CurrentValue : line.PalletContentLineId;
                var lineOrderLineId = line.SalesOrderLine != null ? db.Entry(line.SalesOrderLine).Property(l => l.Id).CurrentValue : line.SalesOrderLineId;
                if (line.Quantity != reservation.Quantity
                    || !string.Equals(line.Unit, reservation.Unit, StringComparison.Ordinal)
                    || line.NetWeightKg != (reservation.NetWeightKg ?? 0m)
                    || lineContentId != reservation.PalletContentLineId
                    || lineOrderLineId != reservation.SalesOrderLineId)
                {
                    throw new TraceabilityViolationException($"Sales dispatch line for reservation {reservation.Id} must copy quantity, unit, net weight, content line and sales order line exactly.");
                }
                var dispatchId = line.SalesDispatch != null ? db.Entry(line.SalesDispatch).Property(d => d.Id).CurrentValue : line.SalesDispatchId;
                if (!orderOfDispatch.TryGetValue(dispatchId, out var dispatchOrderId)
                    || reservation.SalesOrderLineId == null
                    || !orderOfLine.TryGetValue(reservation.SalesOrderLineId.Value, out var reservationOrderId)
                    || dispatchOrderId != reservationOrderId)
                {
                    throw new TraceabilityViolationException($"Sales dispatch line for reservation {reservation.Id} belongs to a dispatch of another sales order.");
                }
            }
        }

        // ---- Regel 7 ----

        private static async Task CheckReservationsOnOpenOrdersAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var lineIds = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State == EntityState.Added && IsActiveSalesReservation(e.Entity))
                .Select(e => e.Entity.SalesOrderLineId!.Value)
                .Distinct()
                .ToList();
            if (lineIds.Count == 0)
            {
                return;
            }

            // Orderregel -> order, met voorrang voor gevolgde (in dezelfde save gewijzigde) regels en orders.
            var lineOrders = (await ToListAsync(
                    db.SalesOrderLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, l.SalesOrderId }),
                    async, cancellationToken))
                .ToDictionary(l => l.Id, l => l.SalesOrderId);
            foreach (var entry in db.ChangeTracker.Entries<SalesOrderLine>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached) && lineIds.Contains(e.Entity.Id)))
            {
                lineOrders[entry.Entity.Id] = entry.Property(l => l.SalesOrderId).CurrentValue;
            }

            var orderIds = lineOrders.Values.Distinct().ToList();
            var orderStatuses = (await ToListAsync(
                    db.SalesOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Status }),
                    async, cancellationToken))
                .ToDictionary(o => o.Id, o => o.Status);
            foreach (var entry in db.ChangeTracker.Entries<SalesOrder>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached) && orderIds.Contains(e.Entity.Id)))
            {
                orderStatuses[entry.Entity.Id] = entry.Property(o => o.Status).CurrentValue;
            }

            foreach (var lineId in lineIds)
            {
                if (!lineOrders.TryGetValue(lineId, out var orderId))
                {
                    continue; // bestaat niet: de FK-constraint weigert dit bij het opslaan
                }
                if (orderStatuses.TryGetValue(orderId, out var status) && status != ReservationRules.OrderStatusOpen)
                {
                    throw new TraceabilityViolationException(
                        $"Sales order {orderId} is {status}; an active reservation requires an open sales order.");
                }
            }
        }

        // ---- Regels 5 en 6 ----

        // Quantity/NetWeightKg zijn hier de voorraad (ontvangst minus afgeboekt), niet de historische ontvangst.
        private sealed record ContentState(string QualityStatus, string Unit, decimal Quantity, decimal NetWeightKg, int PalletId, string Route);

        private static bool IsActiveSalesReservation(StockReservation reservation) =>
            reservation.SalesOrderLineId != null && reservation.Status == ReservationRules.StatusActive;

        private static async Task CheckSalesReservationsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            foreach (var entry in db.ChangeTracker.Entries<StockReservation>().Where(e => e.State == EntityState.Added && e.Entity.SalesOrderLineId != null))
            {
                if (!ReservationRules.IsSalesSource(entry.Entity.Source))
                {
                    throw new TraceabilityViolationException($"A sales reservation needs a known source ({string.Join(" or ", ReservationRules.SalesSources)}), not '{entry.Entity.Source ?? "null"}'.");
                }
                if (entry.Entity.Source == ReservationRules.SourcePhysical && entry.Entity.PalletContentLineId == null && entry.Entity.PalletContentLine == null)
                {
                    throw new TraceabilityViolationException("A Physical sales reservation must be placed on exactly one pallet content line.");
                }
            }
            var added = db.ChangeTracker.Entries<StockReservation>()
                // Onvolledige rijen (zonder eenheid) laten we over aan de database-checks/FK's; toewijzingen (zonder inhoudsregel) volgen regel 10.
                .Where(e => e.State == EntityState.Added && IsActiveSalesReservation(e.Entity) && e.Entity.PalletContentLineId != null && e.Entity.Unit != null)
                .Select(e => e.Entity)
                .ToList();
            if (added.Count == 0)
            {
                return;
            }

            var contentIds = added.Select(r => r.PalletContentLineId!.Value).Distinct().ToList();
            var contents = (await ToListAsync(
                    db.PalletContentLines.AsNoTracking()
                        .Where(c => contentIds.Contains(c.Id))
                        .Select(c => new { c.Id, c.QualityStatus, c.Unit, c.Quantity, c.NetWeightKg, c.DispatchedQuantity, c.DispatchedNetWeightKg, c.PalletId, c.SupplyCommitmentLine.SupplyCommitment.Route }),
                    async, cancellationToken))
                .ToDictionary(c => c.Id, c => new ContentState(c.QualityStatus, c.Unit,
                    ReservationRules.OnHandQuantity(c.Quantity, c.DispatchedQuantity), ReservationRules.OnHandQuantity(c.NetWeightKg, c.DispatchedNetWeightKg), c.PalletId, c.Route));

            // Een in dezelfde save gewijzigde inhoudsregel telt met zijn actuele waarden.
            foreach (var entry in db.ChangeTracker.Entries<PalletContentLine>().Where(e => contentIds.Contains(e.Entity.Id)))
            {
                if (contents.TryGetValue(entry.Entity.Id, out var state))
                {
                    contents[entry.Entity.Id] = state with
                    {
                        QualityStatus = entry.Entity.QualityStatus,
                        Unit = entry.Entity.Unit,
                        Quantity = ReservationRules.OnHandQuantity(entry.Entity.Quantity, entry.Entity.DispatchedQuantity),
                        NetWeightKg = ReservationRules.OnHandQuantity(entry.Entity.NetWeightKg, entry.Entity.DispatchedNetWeightKg),
                    };
                }
            }

            var palletIds = contents.Values.Select(c => c.PalletId).Distinct().ToList();
            var pallets = (await ToListAsync(
                    db.Pallets.AsNoTracking().Where(p => palletIds.Contains(p.Id)).Select(p => new { p.Id, p.LogisticsStatus, p.LocationContext }),
                    async, cancellationToken))
                .ToDictionary(p => p.Id, p => (p.LogisticsStatus, p.LocationContext));
            foreach (var entry in db.ChangeTracker.Entries<Pallet>().Where(e => palletIds.Contains(e.Entity.Id)))
            {
                pallets[entry.Entity.Id] = (entry.Entity.LogisticsStatus, entry.Entity.LocationContext);
            }

            // Regel 5.
            foreach (var reservation in added)
            {
                if (!contents.TryGetValue(reservation.PalletContentLineId!.Value, out var content))
                {
                    continue; // bestaat niet: de FK-constraint weigert dit bij het opslaan
                }
                var (status, location) = pallets.TryGetValue(content.PalletId, out var pallet) ? pallet : (null, null);
                if (content.QualityStatus != PalletRules.QualityApproved
                    || status != PalletRules.StatusReceived
                    || location != PalletRules.LocationWarehouse
                    || content.Route != SupplyRules.RouteWarehouse
                    || !string.Equals(content.Unit, reservation.Unit, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException(
                        $"Pallet content line {reservation.PalletContentLineId} is not reservable: it must be Approved Warehouse-route content on a Received pallet in the Warehouse, in the same unit.");
                }
            }

            // Regel 6: opgeslagen Active verkoopreserveringen (minus de gevolgde) plus alle gevolgde Active verkoopreserveringen.
            var tracked = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State is not (EntityState.Detached) && e.Entity.PalletContentLineId != null && contentIds.Contains(e.Entity.PalletContentLineId.Value))
                .ToList();
            var trackedIds = tracked.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id).ToHashSet();
            var stored = await ToListAsync(
                db.StockReservations.AsNoTracking()
                    .Where(r => r.PalletContentLineId != null && contentIds.Contains(r.PalletContentLineId.Value)
                        && r.SalesOrderLineId != null && r.Status == ReservationRules.StatusActive)
                    .Select(r => new { r.Id, ContentId = r.PalletContentLineId!.Value, r.Quantity, r.NetWeightKg }),
                async, cancellationToken);

            var totals = stored
                .Where(r => !trackedIds.Contains(r.Id))
                .Select(r => (r.ContentId, r.Quantity, Kg: r.NetWeightKg ?? 0m))
                .Concat(tracked
                    .Where(e => e.State != EntityState.Deleted && IsActiveSalesReservation(e.Entity))
                    .Select(e => (ContentId: e.Entity.PalletContentLineId!.Value, e.Entity.Quantity, Kg: e.Entity.NetWeightKg ?? 0m)))
                .GroupBy(r => r.ContentId);

            foreach (var group in totals)
            {
                if (!contents.TryGetValue(group.Key, out var content))
                {
                    continue;
                }
                if (group.Sum(r => r.Quantity) > content.Quantity || group.Sum(r => r.Kg) > content.NetWeightKg)
                {
                    throw new TraceabilityViolationException(
                        $"Active reservations on pallet content line {group.Key} would exceed its on-hand quantity or net weight.");
                }
            }
        }

        // ---- Regel 13: afleverbevestigingen, intrekkingen, datumgrenzen en uitsluiting met correcties ----
        // "Actief" komt uitsluitend uit DeliveryState.IsActive (geen opgeslagen intrekking). Conflicten met een al opgeslagen tegenpartij
        // gooien ShipmentDeliveryConflictException (services: 409); fouten binnen één save en alle andere schendingen het basistype.

        private sealed record StoredShipment(int SalesOrderId, DateTime? DispatchedAt, DateOnly? ShippedOn, bool Corrected, bool HasActive, int MaxSequence);

        private static async Task<StoredShipment> StoredShipmentAsync(AppDbContext db, string kind, int id, bool async, CancellationToken cancellationToken)
        {
            if (kind == ReservationRules.ShipmentKindWarehouse)
            {
                return await FirstOrDefaultAsync(db.SalesDispatches.AsNoTracking().Where(d => d.Id == id).Select(d => new StoredShipment(
                    d.SalesOrderId, d.DispatchedAt, null,
                    db.SalesDispatchCorrections.Any(c => c.SalesDispatchId == d.Id),
                    db.ShipmentDeliveryConfirmations.Where(c => c.SalesDispatchId == d.Id).Any(DeliveryState.IsActive),
                    db.ShipmentDeliveryConfirmations.Where(c => c.SalesDispatchId == d.Id).Max(c => (int?)c.Sequence) ?? 0)), async, cancellationToken);
            }
            return await FirstOrDefaultAsync(db.DirectTradeShipments.AsNoTracking().Where(s => s.Id == id).Select(s => new StoredShipment(
                s.SalesOrderId, null, s.ShippedOn,
                db.DirectTradeShipmentCorrections.Any(c => c.DirectTradeShipmentId == s.Id),
                db.ShipmentDeliveryConfirmations.Where(c => c.DirectTradeShipmentId == s.Id).Any(DeliveryState.IsActive),
                db.ShipmentDeliveryConfirmations.Where(c => c.DirectTradeShipmentId == s.Id).Max(c => (int?)c.Sequence) ?? 0)), async, cancellationToken);
        }

        private static async Task CheckDeliveryConfirmationsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var calendar = db.BusinessCalendar;
            var today = calendar.Today();

            // Nieuwe DirectTrade-verzendingen: ShippedOn nooit na vandaag (Europe/Amsterdam), ook voor rechtstreekse schrijvers.
            foreach (var shipment in db.ChangeTracker.Entries<DirectTradeShipment>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            {
                if (shipment.ShippedOn > today)
                {
                    throw new TraceabilityViolationException($"A DirectTrade shipment cannot be shipped on {shipment.ShippedOn:yyyy-MM-dd}, after today ({today:yyyy-MM-dd}, Europe/Amsterdam).");
                }
            }

            int DispatchIdOf(ShipmentDeliveryConfirmation c) => c.SalesDispatch != null ? db.Entry(c.SalesDispatch).Property(x => x.Id).CurrentValue : c.SalesDispatchId ?? 0;
            int ShipmentIdOf(ShipmentDeliveryConfirmation c) => c.DirectTradeShipment != null ? db.Entry(c.DirectTradeShipment).Property(x => x.Id).CurrentValue : c.DirectTradeShipmentId ?? 0;
            (string Kind, int Id) KeyOf(ShipmentDeliveryConfirmation c) => c.ShipmentKind == ReservationRules.ShipmentKindWarehouse ? (c.ShipmentKind, DispatchIdOf(c)) : (c.ShipmentKind, ShipmentIdOf(c));
            (string Kind, int Id) RetractionKeyOf(ShipmentDeliveryRetraction r) => (r.ShipmentKind, r.ShipmentKind == ReservationRules.ShipmentKindWarehouse ? r.SalesDispatchId ?? 0 : r.DirectTradeShipmentId ?? 0);
            int WarehouseCorrectionIdOf(SalesDispatchCorrection c) => c.SalesDispatch != null ? db.Entry(c.SalesDispatch).Property(x => x.Id).CurrentValue : c.SalesDispatchId;
            int DirectTradeCorrectionIdOf(DirectTradeShipmentCorrection c) => c.DirectTradeShipment != null ? db.Entry(c.DirectTradeShipment).Property(x => x.Id).CurrentValue : c.DirectTradeShipmentId;

            var deliveries = db.ChangeTracker.Entries<ShipmentDeliveryConfirmation>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            var retractions = db.ChangeTracker.Entries<ShipmentDeliveryRetraction>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            var newWarehouseCorrections = db.ChangeTracker.Entries<SalesDispatchCorrection>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            var newDirectTradeCorrections = db.ChangeTracker.Entries<DirectTradeShipmentCorrection>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            if (deliveries.Count == 0 && retractions.Count == 0 && newWarehouseCorrections.Count == 0 && newDirectTradeCorrections.Count == 0)
            {
                return;
            }

            // ---- Intrekkingen ----
            var retractedShipments = new HashSet<(string, int)>();
            var retractedConfirmations = new HashSet<int>();
            foreach (var retraction in retractions)
            {
                if (retraction.Confirmation != null && db.Entry(retraction.Confirmation).State == EntityState.Added)
                {
                    throw new TraceabilityViolationException("A delivery confirmation can only be retracted after it has been saved, never in the same save.");
                }
                var confirmationId = retraction.Confirmation != null ? db.Entry(retraction.Confirmation).Property(x => x.Id).CurrentValue : retraction.ShipmentDeliveryConfirmationId;
                if (!retractedConfirmations.Add(confirmationId))
                {
                    throw new TraceabilityViolationException($"Delivery confirmation {confirmationId} can be retracted at most once.");
                }
                if (!retraction.RecordedInErrorConfirmed
                    || !string.Equals(retraction.ConfirmationCode, ReservationRules.RecordedInErrorCode, StringComparison.Ordinal)
                    || !string.Equals(retraction.ConfirmationStatement, ReservationRules.RecordedInErrorStatement, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException("A retraction needs the explicit statement that the delivery confirmation was recorded in error.");
                }
                if (string.IsNullOrWhiteSpace(retraction.Reason))
                {
                    throw new TraceabilityViolationException("A retraction needs a reason.");
                }
                var stored = await FirstOrDefaultAsync(db.ShipmentDeliveryConfirmations.AsNoTracking().Where(c => c.Id == confirmationId)
                    .Select(c => new { c.ShipmentKind, c.SalesDispatchId, c.DirectTradeShipmentId, c.SalesOrderId, Retracted = c.Retraction != null }), async, cancellationToken);
                if (stored == null)
                {
                    throw new TraceabilityViolationException($"Delivery confirmation {confirmationId} does not exist.");
                }
                if (!string.Equals(stored.ShipmentKind, retraction.ShipmentKind, StringComparison.Ordinal) || stored.SalesDispatchId != retraction.SalesDispatchId
                    || stored.DirectTradeShipmentId != retraction.DirectTradeShipmentId || stored.SalesOrderId != retraction.SalesOrderId)
                {
                    throw new TraceabilityViolationException($"A retraction must copy kind, shipment and sales order of delivery confirmation {confirmationId} exactly.");
                }
                if (stored.Retracted)
                {
                    throw new ShipmentDeliveryConflictException($"Delivery confirmation {confirmationId} has already been retracted.");
                }
                retractedShipments.Add(RetractionKeyOf(retraction));
            }

            // ---- Nieuwe bevestigingen ----
            var confirmedShipments = new HashSet<(string, int)>();
            foreach (var delivery in deliveries)
            {
                var isWarehouse = delivery.ShipmentKind == ReservationRules.ShipmentKindWarehouse;
                var isDirectTrade = delivery.ShipmentKind == ReservationRules.ShipmentKindDirectTrade;
                var hasDispatch = delivery.SalesDispatch != null || delivery.SalesDispatchId != null;
                var hasShipment = delivery.DirectTradeShipment != null || delivery.DirectTradeShipmentId != null;
                if (!(isWarehouse && hasDispatch && !hasShipment) && !(isDirectTrade && hasShipment && !hasDispatch))
                {
                    throw new TraceabilityViolationException("A delivery confirmation refers to exactly one shipment of its own kind (Warehouse dispatch or DirectTrade shipment).");
                }
                if (!delivery.DeliveredInFullConfirmed
                    || !string.Equals(delivery.ConfirmationCode, ReservationRules.DeliveredInFullCode, StringComparison.Ordinal)
                    || !string.Equals(delivery.ConfirmationStatement, ReservationRules.DeliveredInFullStatement, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException("A delivery confirmation needs the explicit statement that the shipment was delivered in full without deviations.");
                }
                if (delivery.DeliveredOn > today)
                {
                    throw new TraceabilityViolationException($"A delivery cannot be confirmed for {delivery.DeliveredOn:yyyy-MM-dd}, after today ({today:yyyy-MM-dd}, Europe/Amsterdam).");
                }
                var key = KeyOf(delivery);
                var label = isWarehouse ? $"Dispatch {key.Id}" : $"DirectTrade shipment {key.Id}";
                if (!confirmedShipments.Add(key))
                {
                    throw new TraceabilityViolationException($"{label} can get at most one delivery confirmation per save.");
                }
                if (retractedShipments.Contains(key))
                {
                    throw new TraceabilityViolationException($"{label}: a retraction must be saved before a new delivery confirmation, never in the same save.");
                }
                var orderId = delivery.SalesOrder != null ? db.Entry(delivery.SalesOrder).Property(o => o.Id).CurrentValue : delivery.SalesOrderId;
                var stored = await StoredShipmentAsync(db, delivery.ShipmentKind, key.Id, async, cancellationToken);
                if (stored == null)
                {
                    throw new TraceabilityViolationException($"{label} does not exist.");
                }
                if (stored.SalesOrderId != orderId)
                {
                    throw new TraceabilityViolationException($"A delivery confirmation for {label.ToLowerInvariant()} must belong to its sales order {stored.SalesOrderId}.");
                }
                if (stored.Corrected)
                {
                    throw new ShipmentDeliveryConflictException(isWarehouse
                        ? $"Dispatch {key.Id} has been corrected and can never be confirmed as delivered."
                        : $"DirectTrade shipment {key.Id} has been taken back and can never be confirmed as delivered.");
                }
                if (isWarehouse ? newWarehouseCorrections.Any(c => WarehouseCorrectionIdOf(c) == key.Id) : newDirectTradeCorrections.Any(c => DirectTradeCorrectionIdOf(c) == key.Id))
                {
                    throw new TraceabilityViolationException(isWarehouse
                        ? $"Dispatch {key.Id} has been corrected and can never be confirmed as delivered."
                        : $"DirectTrade shipment {key.Id} has been taken back and can never be confirmed as delivered.");
                }
                if (stored.HasActive)
                {
                    throw new ShipmentDeliveryConflictException($"{label} already has an active delivery confirmation; retract it before confirming again.");
                }
                if (delivery.Sequence < 1 || delivery.Sequence > stored.MaxSequence + 1)
                {
                    throw new TraceabilityViolationException($"{label}: delivery confirmation number {delivery.Sequence} must be {stored.MaxSequence + 1}.");
                }
                if (delivery.Sequence <= stored.MaxSequence)
                {
                    throw new ShipmentDeliveryConflictException($"{label}: delivery confirmation number {delivery.Sequence} was already recorded concurrently.");
                }
                var shippedOn = isWarehouse ? calendar.DateOf(stored.DispatchedAt!.Value) : stored.ShippedOn!.Value;
                if (delivery.DeliveredOn < shippedOn)
                {
                    throw new TraceabilityViolationException(isWarehouse
                        ? $"Dispatch {key.Id} was dispatched on {shippedOn:yyyy-MM-dd} (Europe/Amsterdam); it cannot be delivered on {delivery.DeliveredOn:yyyy-MM-dd}."
                        : $"DirectTrade shipment {key.Id} was shipped on {shippedOn:yyyy-MM-dd}; it cannot be delivered on {delivery.DeliveredOn:yyyy-MM-dd}.");
                }
            }

            // ---- Correcties: alleen geblokkeerd door een ACTIEVE bevestiging ----
            foreach (var correction in newWarehouseCorrections)
            {
                var dispatchId = WarehouseCorrectionIdOf(correction);
                var message = $"Dispatch {dispatchId} has an active delivery confirmation; it cannot be corrected as goods that never left the warehouse.";
                if (confirmedShipments.Contains((ReservationRules.ShipmentKindWarehouse, dispatchId)))
                {
                    throw new TraceabilityViolationException(message);
                }
                if (retractedShipments.Contains((ReservationRules.ShipmentKindWarehouse, dispatchId)))
                {
                    throw new TraceabilityViolationException($"Dispatch {dispatchId}: a retraction must be saved before a correction, never in the same save.");
                }
                if (await AnyAsync(db.ShipmentDeliveryConfirmations.AsNoTracking().Where(d => d.SalesDispatchId == dispatchId).Where(DeliveryState.IsActive), async, cancellationToken))
                {
                    throw new ShipmentDeliveryConflictException(message);
                }
            }
            foreach (var correction in newDirectTradeCorrections)
            {
                var shipmentId = DirectTradeCorrectionIdOf(correction);
                var message = $"DirectTrade shipment {shipmentId} has an active delivery confirmation; it cannot be taken back as a shipment that did not take place.";
                if (confirmedShipments.Contains((ReservationRules.ShipmentKindDirectTrade, shipmentId)))
                {
                    throw new TraceabilityViolationException(message);
                }
                if (retractedShipments.Contains((ReservationRules.ShipmentKindDirectTrade, shipmentId)))
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment {shipmentId}: a retraction must be saved before a take-back, never in the same save.");
                }
                if (await AnyAsync(db.ShipmentDeliveryConfirmations.AsNoTracking().Where(d => d.DirectTradeShipmentId == shipmentId).Where(DeliveryState.IsActive), async, cancellationToken))
                {
                    throw new ShipmentDeliveryConflictException(message);
                }
            }
        }

        private static async Task<T> FirstOrDefaultAsync<T>(IQueryable<T> query, bool async, CancellationToken cancellationToken) =>
            async ? await query.FirstOrDefaultAsync(cancellationToken) : query.FirstOrDefault();

        private static async Task<bool> AnyAsync<T>(IQueryable<T> query, bool async, CancellationToken cancellationToken) =>
            async ? await query.AnyAsync(cancellationToken) : query.Any();

        // ---- Regel 12: DirectTrade-verzendcorrecties in beide richtingen ----

        private static async Task CheckDirectTradeShipmentCorrectionsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var correctedEntries = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State == EntityState.Modified && e.Entity.SalesOrderLineId != null
                    && e.Entity.Source == ReservationRules.SourceDirectTrade
                    && e.Entity.Status == ReservationRules.StatusCorrected
                    && e.OriginalValues.GetValue<string>(nameof(StockReservation.Status)) != ReservationRules.StatusCorrected)
                .ToList();
            if (correctedEntries.Any(e => e.OriginalValues.GetValue<string>(nameof(StockReservation.Status)) != ReservationRules.StatusConsumed))
            {
                throw new TraceabilityViolationException("Only a shipped (Consumed) DirectTrade allocation can become Corrected.");
            }
            var corrected = correctedEntries.Select(e => e.Entity).ToList();
            var lines = db.ChangeTracker.Entries<DirectTradeShipmentCorrectionLine>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            var corrections = db.ChangeTracker.Entries<DirectTradeShipmentCorrection>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            if (corrected.Count == 0 && lines.Count == 0 && corrections.Count == 0)
            {
                return;
            }

            StockReservation ReservationOf(DirectTradeShipmentCorrectionLine line) =>
                line.StockReservation ?? corrected.FirstOrDefault(r => r.Id == line.StockReservationId);

            foreach (var reservation in corrected)
            {
                if (reservation.CorrectedAt == null || reservation.ConsumedAt == null)
                {
                    throw new TraceabilityViolationException($"DirectTrade allocation {reservation.Id} becomes Corrected without a correction time.");
                }
                if (lines.Count(l => ReferenceEquals(ReservationOf(l), reservation)) != 1)
                {
                    throw new TraceabilityViolationException($"DirectTrade allocation {reservation.Id} must be taken back by exactly one new DirectTrade shipment correction line in the same save.");
                }
            }

            // De opgeslagen verzendregels (append-only) en per verzending het aantal regels en de order.
            var shipmentLineIds = lines.Select(l => l.DirectTradeShipmentLine != null ? db.Entry(l.DirectTradeShipmentLine).Property(x => x.Id).CurrentValue : l.DirectTradeShipmentLineId).Distinct().ToList();
            var shipmentLines = (await ToListAsync(db.DirectTradeShipmentLines.AsNoTracking().Where(l => shipmentLineIds.Contains(l.Id)), async, cancellationToken)).ToDictionary(l => l.Id);
            var shipmentIds = shipmentLines.Values.Select(l => l.DirectTradeShipmentId)
                .Concat(corrections.Select(c => c.DirectTradeShipment != null ? db.Entry(c.DirectTradeShipment).Property(x => x.Id).CurrentValue : c.DirectTradeShipmentId))
                .Distinct().ToList();
            var shipments = (await ToListAsync(db.DirectTradeShipments.AsNoTracking().Where(s => shipmentIds.Contains(s.Id)).Select(s => new { s.Id, s.SalesOrderId, LineCount = s.Lines.Count() }), async, cancellationToken))
                .ToDictionary(s => s.Id, s => (s.SalesOrderId, s.LineCount));

            foreach (var line in lines)
            {
                var reservation = ReservationOf(line);
                if (reservation == null || !corrected.Contains(reservation))
                {
                    throw new TraceabilityViolationException("A DirectTrade shipment correction line requires its DirectTrade allocation to become Corrected in the same save.");
                }
                var shipmentLineId = line.DirectTradeShipmentLine != null ? db.Entry(line.DirectTradeShipmentLine).Property(x => x.Id).CurrentValue : line.DirectTradeShipmentLineId;
                if (!shipmentLines.TryGetValue(shipmentLineId, out var shipmentLine) || shipmentLine.StockReservationId != reservation.Id)
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment correction line for allocation {reservation.Id} must refer to the DirectTrade shipment line of that allocation.");
                }
                if (line.Quantity != shipmentLine.Quantity || !string.Equals(line.Unit, shipmentLine.Unit, StringComparison.Ordinal) || line.NetWeightKg != shipmentLine.NetWeightKg
                    || line.SupplyCommitmentLineId != shipmentLine.SupplyCommitmentLineId || line.SalesOrderLineId != shipmentLine.SalesOrderLineId)
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment correction line for allocation {reservation.Id} must copy quantity, unit, net weight, supply line and order line of the shipment line exactly.");
                }
                var correction = line.DirectTradeShipmentCorrection;
                if (correction == null || !corrections.Contains(correction))
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment correction line for allocation {reservation.Id} must belong to a new correction in the same save.");
                }
                var correctionShipmentId = correction.DirectTradeShipment != null ? db.Entry(correction.DirectTradeShipment).Property(x => x.Id).CurrentValue : correction.DirectTradeShipmentId;
                if (correctionShipmentId != shipmentLine.DirectTradeShipmentId)
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment correction for shipment {correctionShipmentId} cannot take back a line of shipment {shipmentLine.DirectTradeShipmentId}.");
                }
            }

            foreach (var correction in corrections)
            {
                if (!correction.ShipmentDidNotTakePlaceConfirmed
                    || !string.Equals(correction.ConfirmationCode, ReservationRules.ShipmentDidNotTakePlaceCode, StringComparison.Ordinal)
                    || !string.Equals(correction.ConfirmationStatement, ReservationRules.ShipmentDidNotTakePlaceStatement, StringComparison.Ordinal)
                    || string.IsNullOrWhiteSpace(correction.Reason))
                {
                    throw new TraceabilityViolationException("A DirectTrade shipment correction needs the explicit statement that the shipment did not take place and a reason.");
                }
                var shipmentId = correction.DirectTradeShipment != null ? db.Entry(correction.DirectTradeShipment).Property(x => x.Id).CurrentValue : correction.DirectTradeShipmentId;
                if (!shipments.TryGetValue(shipmentId, out var shipment))
                {
                    continue; // bestaat niet: de FK-constraint weigert dit bij het opslaan
                }
                var orderId = correction.SalesOrder != null ? db.Entry(correction.SalesOrder).Property(x => x.Id).CurrentValue : correction.SalesOrderId;
                if (orderId != shipment.SalesOrderId)
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment correction for shipment {shipmentId} must belong to its sales order {shipment.SalesOrderId}.");
                }
                var correctionLines = lines.Count(l => ReferenceEquals(l.DirectTradeShipmentCorrection, correction));
                if (correctionLines != shipment.LineCount)
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment correction for shipment {shipmentId} must take back all {shipment.LineCount} lines, not {correctionLines}.");
                }
            }
        }

        // ---- Regel 11: DirectTrade-verzendingen in beide richtingen ----

        private static async Task CheckDirectTradeShipmentsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<StockReservation>().Any(e => e.State == EntityState.Added && e.Entity.Source == ReservationRules.SourceDirectTrade && e.Entity.Status == ReservationRules.StatusConsumed))
            {
                throw new TraceabilityViolationException("A DirectTrade allocation can only become Consumed through a DirectTrade shipment, never be created as Consumed.");
            }
            var shipped = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State == EntityState.Modified && e.Entity.SalesOrderLineId != null
                    && e.Entity.Source == ReservationRules.SourceDirectTrade
                    && e.Entity.Status == ReservationRules.StatusConsumed
                    && e.OriginalValues.GetValue<string>(nameof(StockReservation.Status)) != ReservationRules.StatusConsumed)
                .Select(e => e.Entity)
                .ToList();
            var lines = db.ChangeTracker.Entries<DirectTradeShipmentLine>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            if (shipped.Count == 0 && lines.Count == 0)
            {
                return;
            }

            StockReservation ReservationOf(DirectTradeShipmentLine line) =>
                line.StockReservation ?? shipped.FirstOrDefault(r => r.Id == line.StockReservationId);

            foreach (var reservation in shipped)
            {
                if (reservation.ConsumedAt == null)
                {
                    throw new TraceabilityViolationException($"DirectTrade allocation {reservation.Id} becomes Consumed without a consumption time.");
                }
                var count = lines.Count(l => ReferenceEquals(ReservationOf(l), reservation));
                if (count != 1)
                {
                    throw new TraceabilityViolationException($"DirectTrade allocation {reservation.Id} must be shipped by exactly one new DirectTrade shipment line in the same save.");
                }
            }

            // Orderregel -> order en order -> klant, met voorrang voor gevolgde entiteiten.
            var lineIds = lines.Select(l => l.SalesOrderLine != null ? db.Entry(l.SalesOrderLine).Property(x => x.Id).CurrentValue : l.SalesOrderLineId).Distinct().ToList();
            var lineOrders = (await ToListAsync(db.SalesOrderLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, l.SalesOrderId }), async, cancellationToken))
                .ToDictionary(l => l.Id, l => l.SalesOrderId);
            foreach (var entry in db.ChangeTracker.Entries<SalesOrderLine>().Where(e => lineIds.Contains(e.Entity.Id)))
            {
                lineOrders[entry.Entity.Id] = entry.Property(l => l.SalesOrderId).CurrentValue;
            }
            var orderIds = lineOrders.Values.Distinct().ToList();
            var orderCustomers = (await ToListAsync(db.SalesOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.CustomerId }), async, cancellationToken))
                .ToDictionary(o => o.Id, o => o.CustomerId);
            // De echte bron (opgeslagen waarden, niet wat in dezelfde save is aangepast): partij van de bronregel, toezegging en leverancier.
            // De gekopieerde bronpartij, toezeggings- en leverancier-id in de verzendregel worden daartegen gecontroleerd.
            var sourceIds = lines.Select(l => l.SupplyCommitmentLine != null ? db.Entry(l.SupplyCommitmentLine).Property(x => x.Id).CurrentValue : l.SupplyCommitmentLineId).Distinct().ToList();
            var realSources = (await ToListAsync(
                    db.SupplyCommitmentLines.AsNoTracking().Where(l => sourceIds.Contains(l.Id))
                        .Select(l => new { l.Id, l.LotNumber, l.SupplyCommitmentId, l.SupplyCommitment.VendorId }),
                    async, cancellationToken))
                .ToDictionary(l => l.Id, l => (l.LotNumber, l.SupplyCommitmentId, l.VendorId));
            var storedShipmentIds = lines.Where(l => l.DirectTradeShipment == null).Select(l => l.DirectTradeShipmentId).Distinct().ToList();
            var storedShipments = (await ToListAsync(db.DirectTradeShipments.AsNoTracking().Where(d => storedShipmentIds.Contains(d.Id)).Select(d => new { d.Id, d.SalesOrderId, d.CustomerId }), async, cancellationToken))
                .ToDictionary(d => d.Id, d => (d.SalesOrderId, d.CustomerId));

            foreach (var line in lines)
            {
                var reservation = ReservationOf(line);
                if (reservation == null || !shipped.Contains(reservation))
                {
                    throw new TraceabilityViolationException("A DirectTrade shipment line requires its allocation to become Consumed in the same save.");
                }
                var lineId = line.SalesOrderLine != null ? db.Entry(line.SalesOrderLine).Property(x => x.Id).CurrentValue : line.SalesOrderLineId;
                var sourceId = line.SupplyCommitmentLine != null ? db.Entry(line.SupplyCommitmentLine).Property(x => x.Id).CurrentValue : line.SupplyCommitmentLineId;
                if (line.Quantity != reservation.Quantity || line.AllocatedQuantity != reservation.Quantity
                    || !string.Equals(line.Unit, reservation.Unit, StringComparison.Ordinal)
                    || lineId != reservation.SalesOrderLineId
                    || sourceId != reservation.SupplyCommitmentLineId)
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment line for allocation {reservation.Id} must copy the whole quantity, unit, order line and supply line of the allocation.");
                }
                if (line.NetWeightKg <= 0m || string.IsNullOrWhiteSpace(line.LotNumber))
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment line for allocation {reservation.Id} needs an explicit net weight and lot.");
                }
                if (!realSources.TryGetValue(sourceId, out var real))
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment line for allocation {reservation.Id} refers to a supply line that does not exist.");
                }
                if (!string.Equals(line.SourceLotNumber, real.LotNumber, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment line for allocation {reservation.Id} must copy the real source lot ({real.LotNumber ?? "none"}), not {line.SourceLotNumber ?? "none"}.");
                }
                if (line.SupplyCommitmentId != real.SupplyCommitmentId || line.VendorId != real.VendorId)
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment line for allocation {reservation.Id} must copy the supply commitment ({real.SupplyCommitmentId}) and vendor ({real.VendorId}) of its supply line.");
                }
                // De afwijkingsregel tegen de echte bronpartij, niet tegen een (mogelijk vervalste) kopie.
                if (real.LotNumber != null && !string.Equals(line.LotNumber, real.LotNumber, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(line.LotDeviationReason))
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment line for allocation {reservation.Id} deviates from source lot {real.LotNumber} without a reason.");
                }
                var (shipmentOrderId, shipmentCustomerId) = line.DirectTradeShipment != null
                    ? (db.Entry(line.DirectTradeShipment).Property(d => d.SalesOrderId).CurrentValue, line.DirectTradeShipment.CustomerId)
                    : storedShipments.GetValueOrDefault(line.DirectTradeShipmentId);
                if (!lineOrders.TryGetValue(lineId, out var orderId) || orderId != shipmentOrderId)
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment line for allocation {reservation.Id} must belong to a shipment of the same sales order.");
                }
                if (!orderCustomers.TryGetValue(orderId, out var customerId) || customerId != shipmentCustomerId)
                {
                    throw new TraceabilityViolationException($"DirectTrade shipment for sales order {orderId} must be recorded for the customer of that order.");
                }
            }
        }

        // ---- Regel 10: DirectTrade-toewijzingen ----

        private sealed record SourceState(int SupplyCommitmentId, string Route, int? CustomerId, string Status, string ExpectedUnit, decimal ExpectedQuantity, int ProductVariantId);

        private static async Task CheckDirectTradeAllocationsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            // Overgangen, niet alleen toegestane waarden: een toewijzing gaat alleen Active -> Released en nooit terug.
            // Reactiveren zou de capaciteitscontrole (alleen voor nieuwe rijen) omzeilen; de bron van een bestaande
            // verkoopreservering ligt vast, zodat een rij ook niet via een bronwissel een toewijzing kan worden of ophouden te zijn.
            foreach (var entry in db.ChangeTracker.Entries<StockReservation>().Where(e => e.State == EntityState.Modified && e.Entity.SalesOrderLineId != null))
            {
                var originalSource = entry.OriginalValues.GetValue<string>(nameof(StockReservation.Source));
                var isAllocation = originalSource == ReservationRules.SourceDirectTrade || entry.Entity.Source == ReservationRules.SourceDirectTrade;
                if (!isAllocation)
                {
                    continue;
                }
                if (!string.Equals(originalSource, entry.Entity.Source, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"The source of sales reservation {entry.Entity.Id} cannot change from {originalSource} to {entry.Entity.Source}.");
                }
                var originalStatus = entry.OriginalValues.GetValue<string>(nameof(StockReservation.Status));
                if (originalStatus == ReservationRules.StatusReleased && entry.Entity.Status != ReservationRules.StatusReleased)
                {
                    throw new TraceabilityViolationException($"DirectTrade allocation {entry.Entity.Id} is Released and can never become {entry.Entity.Status} again; allocate anew instead.");
                }
                if (originalStatus == ReservationRules.StatusConsumed && entry.Entity.Status != ReservationRules.StatusConsumed && entry.Entity.Status != ReservationRules.StatusCorrected)
                {
                    throw new TraceabilityViolationException($"DirectTrade allocation {entry.Entity.Id} is shipped (Consumed) and can never become {entry.Entity.Status}; only a DirectTrade shipment correction can take it back (Corrected).");
                }
                if (originalStatus == ReservationRules.StatusCorrected && entry.Entity.Status != ReservationRules.StatusCorrected)
                {
                    throw new TraceabilityViolationException($"DirectTrade allocation {entry.Entity.Id} is Corrected (terminal) and can never become {entry.Entity.Status}.");
                }
                // De toewijzing zelf ligt vast: hoeveelheid, eenheid, bronregel en orderregel worden vergeleken met de opgeslagen waarden
                // (OriginalValues), niet met wat er in dezelfde save van gemaakt is. Zo kan niemand vlak vóór het verbruiken de toegewezen
                // hoeveelheid wijzigen en die verzenden, en na Consumed blijven broncapaciteit en ordertotalen gelijk aan de verzending.
                // Dit geldt ook voor een Active toewijzing: een in-place wijziging zou de capaciteitscontrole (alleen nieuwe rijen) omzeilen.
                var changed = new List<string>();
                if (entry.OriginalValues.GetValue<decimal>(nameof(StockReservation.Quantity)) != entry.Entity.Quantity) changed.Add("quantity");
                if (!string.Equals(entry.OriginalValues.GetValue<string>(nameof(StockReservation.Unit)), entry.Entity.Unit, StringComparison.Ordinal)) changed.Add("unit");
                if (entry.OriginalValues.GetValue<int>(nameof(StockReservation.SupplyCommitmentLineId)) != entry.Property(r => r.SupplyCommitmentLineId).CurrentValue) changed.Add("supply line");
                if (entry.OriginalValues.GetValue<int?>(nameof(StockReservation.SalesOrderLineId)) != entry.Property(r => r.SalesOrderLineId).CurrentValue) changed.Add("order line");
                if ((originalStatus == ReservationRules.StatusConsumed || originalStatus == ReservationRules.StatusCorrected)
                    && entry.OriginalValues.GetValue<DateTime?>(nameof(StockReservation.ConsumedAt)) != entry.Entity.ConsumedAt) changed.Add("consumption time");
                if (originalStatus == ReservationRules.StatusCorrected && entry.OriginalValues.GetValue<DateTime?>(nameof(StockReservation.CorrectedAt)) != entry.Entity.CorrectedAt) changed.Add("correction time");
                if (changed.Count > 0)
                {
                    throw new TraceabilityViolationException($"DirectTrade allocation {entry.Entity.Id} ({originalStatus}) cannot change its {string.Join(", ", changed)}; release it and allocate anew, or use a DirectTrade shipment correction once it is shipped.");
                }
            }

            // Een toewijzing kent alleen Active, Released en Consumed (Corrected bestaat nog niet voor DirectTrade).
            foreach (var entry in db.ChangeTracker.Entries<StockReservation>().Where(e => e.State is EntityState.Added or EntityState.Modified
                         && e.Entity.SalesOrderLineId != null && e.Entity.Source == ReservationRules.SourceDirectTrade))
            {
                if (!ReservationRules.DirectTradeStatuses.Contains(entry.Entity.Status, StringComparer.Ordinal))
                {
                    throw new TraceabilityViolationException($"A DirectTrade allocation can only be Active, Released or Consumed, not {entry.Entity.Status}; a DirectTrade shipment correction is a separate flow.");
                }
                if (entry.Entity.PalletContentLineId != null || entry.Entity.PalletContentLine != null)
                {
                    throw new TraceabilityViolationException("A DirectTrade allocation is expected, customer-bound supply and can never be placed on a pallet content line.");
                }
                if (entry.Entity.NetWeightKg != null)
                {
                    throw new TraceabilityViolationException("A DirectTrade allocation has no measured net weight; kg is never derived from expected supply.");
                }
            }

            var added = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State == EntityState.Added && IsActiveSalesReservation(e.Entity) && e.Entity.Source == ReservationRules.SourceDirectTrade)
                .Select(e => e.Entity)
                .ToList();
            if (added.Count == 0)
            {
                return;
            }

            // Bronregels: opgeslagen, met voorrang voor gevolgde regels en toezeggingen in dezelfde save.
            var sourceIds = added.Select(r => r.SupplyCommitmentLineId).Distinct().ToList();
            var sources = (await ToListAsync(
                    db.SupplyCommitmentLines.AsNoTracking().Where(l => sourceIds.Contains(l.Id))
                        .Select(l => new { l.Id, l.SupplyCommitmentId, l.SupplyCommitment.Route, l.SupplyCommitment.CustomerId, l.SupplyCommitment.Status, l.ExpectedUnit, l.ExpectedQuantity, l.ProductVariantId }),
                    async, cancellationToken))
                .ToDictionary(l => l.Id, l => new SourceState(l.SupplyCommitmentId, l.Route, l.CustomerId, l.Status, l.ExpectedUnit, l.ExpectedQuantity, l.ProductVariantId));
            var trackedCommitments = db.ChangeTracker.Entries<SupplyCommitment>()
                .Where(e => e.State is not (EntityState.Deleted or EntityState.Detached))
                .ToDictionary(e => e.Property(c => c.Id).CurrentValue, e => (e.Entity.Route, e.Entity.CustomerId, e.Entity.Status));
            foreach (var entry in db.ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached) && sourceIds.Contains(e.Entity.Id)))
            {
                var commitmentId = entry.Property(l => l.SupplyCommitmentId).CurrentValue;
                var storedSource = sources.GetValueOrDefault(entry.Entity.Id);
                var commitment = trackedCommitments.TryGetValue(commitmentId, out var trackedCommitment) ? trackedCommitment
                    : storedSource != null && storedSource.SupplyCommitmentId == commitmentId ? (storedSource.Route, storedSource.CustomerId, storedSource.Status)
                    : (null, null, null);
                sources[entry.Entity.Id] = new SourceState(commitmentId, commitment.Route, commitment.CustomerId, commitment.Status, entry.Entity.ExpectedUnit, entry.Entity.ExpectedQuantity, entry.Entity.ProductVariantId);
            }

            // Orderregel -> order (klant), met voorrang voor gevolgde entiteiten.
            var lineIds = added.Select(r => r.SalesOrderLineId!.Value).Distinct().ToList();
            var lines = (await ToListAsync(
                    db.SalesOrderLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, l.SalesOrderId, l.ProductVariantId, l.Unit }),
                    async, cancellationToken))
                .ToDictionary(l => l.Id, l => (l.SalesOrderId, l.ProductVariantId, l.Unit));
            foreach (var entry in db.ChangeTracker.Entries<SalesOrderLine>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached) && lineIds.Contains(e.Entity.Id)))
            {
                lines[entry.Entity.Id] = (entry.Property(l => l.SalesOrderId).CurrentValue, entry.Entity.ProductVariantId, entry.Entity.Unit);
            }
            var orderIds = lines.Values.Select(l => l.SalesOrderId).Distinct().ToList();
            var orderCustomers = (await ToListAsync(
                    db.SalesOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.CustomerId }),
                    async, cancellationToken))
                .ToDictionary(o => o.Id, o => o.CustomerId);
            foreach (var entry in db.ChangeTracker.Entries<SalesOrder>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached) && orderIds.Contains(e.Entity.Id)))
            {
                orderCustomers[entry.Entity.Id] = entry.Property(o => o.CustomerId).CurrentValue;
            }

            foreach (var reservation in added)
            {
                if (!sources.TryGetValue(reservation.SupplyCommitmentLineId, out var source) || !lines.TryGetValue(reservation.SalesOrderLineId!.Value, out var line))
                {
                    continue; // bestaat niet: de FK-constraint weigert dit bij het opslaan
                }
                if (source.Route != SupplyRules.RouteDirectTrade)
                {
                    throw new TraceabilityViolationException($"Supply commitment line {reservation.SupplyCommitmentLineId} is not DirectTrade supply and can never be allocated to a customer.");
                }
                if (!SupplyRules.IsAllocatableCommitmentStatus(source.Status))
                {
                    throw new TraceabilityViolationException($"Supply commitment {source.SupplyCommitmentId} is {source.Status ?? "unknown"}; only {string.Join(" or ", SupplyRules.AllocatableCommitmentStatuses)} supply can be allocated.");
                }
                if (source.CustomerId == null || !orderCustomers.TryGetValue(line.SalesOrderId, out var orderCustomer) || orderCustomer != source.CustomerId)
                {
                    throw new TraceabilityViolationException($"Supply commitment {source.SupplyCommitmentId} is bound to customer {source.CustomerId}, which is not the customer of sales order {line.SalesOrderId}.");
                }
                if (source.ProductVariantId != line.ProductVariantId)
                {
                    throw new TraceabilityViolationException($"Supply commitment line {reservation.SupplyCommitmentLineId} is a different product variant than sales order line {line.Unit}.");
                }
                if (source.ExpectedUnit == null || !string.Equals(source.ExpectedUnit, reservation.Unit, StringComparison.Ordinal) || !string.Equals(line.Unit, reservation.Unit, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"A DirectTrade allocation must use exactly the unit of its supply line and order line; units are never converted or inferred.");
                }
            }

            // Bezette capaciteit per bronregel = Active + Consumed toewijzingen: opgeslagen (minus de gevolgde) plus alle gevolgde.
            var tracked = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State is not EntityState.Detached && sourceIds.Contains(e.Entity.SupplyCommitmentLineId))
                .ToList();
            var trackedIds = tracked.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id).ToHashSet();
            var stored = await ToListAsync(
                db.StockReservations.AsNoTracking()
                    .Where(r => sourceIds.Contains(r.SupplyCommitmentLineId) && r.SalesOrderLineId != null
                        && r.Source == ReservationRules.SourceDirectTrade && (r.Status == ReservationRules.StatusActive || r.Status == ReservationRules.StatusConsumed))
                    .Select(r => new { r.Id, r.SupplyCommitmentLineId, r.Quantity }),
                async, cancellationToken);
            var totals = stored
                .Where(r => !trackedIds.Contains(r.Id))
                .Select(r => (r.SupplyCommitmentLineId, r.Quantity))
                .Concat(tracked
                    .Where(e => e.State != EntityState.Deleted && e.Entity.SalesOrderLineId != null && e.Entity.Source == ReservationRules.SourceDirectTrade
                        && (e.Entity.Status == ReservationRules.StatusActive || e.Entity.Status == ReservationRules.StatusConsumed))
                    .Select(e => (e.Entity.SupplyCommitmentLineId, e.Entity.Quantity)))
                .GroupBy(r => r.SupplyCommitmentLineId);
            foreach (var group in totals)
            {
                if (sources.TryGetValue(group.Key, out var source) && group.Sum(r => r.Quantity) > source.ExpectedQuantity)
                {
                    throw new TraceabilityViolationException($"Active and shipped DirectTrade allocations on supply commitment line {group.Key} would exceed its expected quantity {source.ExpectedQuantity}.");
                }
            }
        }

        // ---- Regels 1 en 2 ----

        private static async Task CheckSourceLinksAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var trackedContentSources = new Dictionary<int, int>();
            var modifiedContentSources = new Dictionary<int, int>();

            // Waarden via de entry, zodat ook tijdelijke sleutels van nieuwe entiteiten in dezelfde save meetellen.
            foreach (var entry in db.ChangeTracker.Entries<PalletContentLine>())
            {
                if (entry.State is EntityState.Deleted or EntityState.Detached)
                {
                    continue;
                }

                var id = entry.Property(c => c.Id).CurrentValue;
                var sourceLineId = entry.Property(c => c.SupplyCommitmentLineId).CurrentValue;
                trackedContentSources[id] = sourceLineId;

                if (entry.State == EntityState.Modified && entry.Property(c => c.SupplyCommitmentLineId).IsModified)
                {
                    modifiedContentSources[id] = sourceLineId;
                }
            }

            var links = new List<SourceLink>();
            foreach (var entry in db.ChangeTracker.Entries<StockMovement>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                var contentLineId = entry.Property(m => m.PalletContentLineId).CurrentValue;
                if (contentLineId != null)
                {
                    links.Add(new SourceLink(nameof(StockMovement), contentLineId.Value, entry.Property(m => m.SupplyCommitmentLineId).CurrentValue));
                }
            }
            foreach (var entry in db.ChangeTracker.Entries<StockReservation>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                var contentLineId = entry.Property(r => r.PalletContentLineId).CurrentValue;
                if (contentLineId != null)
                {
                    links.Add(new SourceLink(nameof(StockReservation), contentLineId.Value, entry.Property(r => r.SupplyCommitmentLineId).CurrentValue));
                }
            }

            var idsToLoad = modifiedContentSources.Keys
                .Concat(links.Select(l => l.ContentLineId).Where(id => !trackedContentSources.ContainsKey(id)))
                .Distinct()
                .ToList();
            var stored = idsToLoad.Count == 0
                ? new Dictionary<int, int>()
                : (await ToListAsync(
                        db.PalletContentLines.AsNoTracking()
                            .Where(c => idsToLoad.Contains(c.Id))
                            .Select(c => new { c.Id, c.SupplyCommitmentLineId }),
                        async, cancellationToken))
                    .ToDictionary(c => c.Id, c => c.SupplyCommitmentLineId);

            foreach (var (contentLineId, newSourceLineId) in modifiedContentSources)
            {
                if (stored.TryGetValue(contentLineId, out var storedSourceLineId) && storedSourceLineId != newSourceLineId)
                {
                    throw new TraceabilityViolationException(
                        $"The source supply commitment line of pallet content line {contentLineId} cannot be changed.");
                }
            }

            foreach (var link in links)
            {
                var known = trackedContentSources.TryGetValue(link.ContentLineId, out var contentSourceLineId)
                    || stored.TryGetValue(link.ContentLineId, out contentSourceLineId);
                if (!known)
                {
                    continue; // bestaat niet: de FK-constraint weigert dit bij het opslaan
                }

                if (contentSourceLineId != link.SupplyCommitmentLineId)
                {
                    throw new TraceabilityViolationException(
                        $"{link.Entity} references pallet content line {link.ContentLineId}, which belongs to supply commitment line " +
                        $"{contentSourceLineId}, not {link.SupplyCommitmentLineId}.");
                }
            }
        }

        // ---- Regel 4 ----

        private static async Task CheckNoDirectTradeInWarehouseAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var trackedPalletLocations = db.ChangeTracker.Entries<Pallet>()
                .Where(e => e.State is not (EntityState.Deleted or EntityState.Detached))
                .ToDictionary(e => e.Property(p => p.Id).CurrentValue, e => e.Property(p => p.LocationContext).CurrentValue);

            var warehousePalletIds = db.ChangeTracker.Entries<Pallet>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified
                    && e.Property(p => p.LocationContext).CurrentValue == PalletRules.LocationWarehouse)
                .Select(e => e.Property(p => p.Id).CurrentValue)
                .ToHashSet();

            // Te controleren paren (pallet, bronregel): nieuwe inhoud op elke pallet, en gevolgde inhoud op een pallet die
            // (opnieuw) in het magazijn staat.
            var pairs = db.ChangeTracker.Entries<PalletContentLine>()
                .Where(e => e.State is not (EntityState.Deleted or EntityState.Detached))
                .Select(e => (
                    State: e.State,
                    PalletId: e.Property(c => c.PalletId).CurrentValue,
                    SourceLineId: e.Property(c => c.SupplyCommitmentLineId).CurrentValue))
                .Where(c => c.State == EntityState.Added || warehousePalletIds.Contains(c.PalletId))
                .Select(c => (c.PalletId, c.SourceLineId))
                .ToList();

            if (warehousePalletIds.Count == 0 && pairs.Count == 0)
            {
                return;
            }

            // Opgeslagen inhoud van pallets die naar het magazijn gaan.
            if (warehousePalletIds.Count > 0)
            {
                var ids = warehousePalletIds.ToList();
                var storedDirectTrade = await ToListAsync(
                    db.PalletContentLines.AsNoTracking()
                        .Where(c => ids.Contains(c.PalletId) && c.SupplyCommitmentLine.SupplyCommitment.Route == SupplyRules.RouteDirectTrade)
                        .Select(c => c.PalletId),
                    async, cancellationToken);
                if (storedDirectTrade.Count > 0)
                {
                    throw DirectTradeInWarehouse(storedDirectTrade[0]);
                }
            }

            if (pairs.Count == 0)
            {
                return;
            }

            var missingPalletIds = pairs.Select(p => p.PalletId).Where(id => !trackedPalletLocations.ContainsKey(id)).Distinct().ToList();
            var palletLocations = new Dictionary<int, string>(trackedPalletLocations);
            if (missingPalletIds.Count > 0)
            {
                foreach (var pallet in await ToListAsync(
                    db.Pallets.AsNoTracking().Where(p => missingPalletIds.Contains(p.Id)).Select(p => new { p.Id, p.LocationContext }),
                    async, cancellationToken))
                {
                    palletLocations[pallet.Id] = pallet.LocationContext;
                }
            }

            var warehousePairs = pairs.Where(p => palletLocations.TryGetValue(p.PalletId, out var location) && location == PalletRules.LocationWarehouse).ToList();
            if (warehousePairs.Count == 0)
            {
                return;
            }

            var routes = TrackedSourceRoutes(db);
            var missingSourceIds = warehousePairs.Select(p => p.SourceLineId).Where(id => !routes.ContainsKey(id)).Distinct().ToList();
            if (missingSourceIds.Count > 0)
            {
                foreach (var source in await ToListAsync(
                    db.SupplyCommitmentLines.AsNoTracking()
                        .Where(l => missingSourceIds.Contains(l.Id))
                        .Select(l => new { l.Id, l.SupplyCommitment.Route }),
                    async, cancellationToken))
                {
                    routes[source.Id] = source.Route;
                }
            }

            foreach (var (palletId, sourceLineId) in warehousePairs)
            {
                if (routes.TryGetValue(sourceLineId, out var route) && route == SupplyRules.RouteDirectTrade)
                {
                    throw DirectTradeInWarehouse(palletId);
                }
            }
        }

        // Route per gevolgde bronregel, via de gevolgde toezegging (ook met tijdelijke sleutels in dezelfde save).
        private static Dictionary<int, string> TrackedSourceRoutes(AppDbContext db)
        {
            var commitmentRoutes = db.ChangeTracker.Entries<SupplyCommitment>()
                .Where(e => e.State is not (EntityState.Deleted or EntityState.Detached))
                .ToDictionary(e => e.Property(c => c.Id).CurrentValue, e => e.Property(c => c.Route).CurrentValue);

            var routes = new Dictionary<int, string>();
            foreach (var entry in db.ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached)))
            {
                if (commitmentRoutes.TryGetValue(entry.Property(l => l.SupplyCommitmentId).CurrentValue, out var route))
                {
                    routes[entry.Property(l => l.Id).CurrentValue] = route;
                }
            }
            return routes;
        }

        private static TraceabilityViolationException DirectTradeInWarehouse(int palletId) =>
            new($"Pallet {palletId} cannot hold DirectTrade content while its location context is {PalletRules.LocationWarehouse}.");

        private static async Task<List<T>> ToListAsync<T>(IQueryable<T> query, bool async, CancellationToken cancellationToken) =>
            async ? await query.ToListAsync(cancellationToken) : query.ToList();
    }
}
