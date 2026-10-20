using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    public sealed class TraceabilityViolationException : InvalidOperationException
    {
        public TraceabilityViolationException(string message) : base(message) {}
    }

    // Draait vóór elke SaveChanges/SaveChangesAsync, zodat geen enkele writer de regels kan vergeten:
    // 1. een StockMovement/StockReservation met PalletContentLineId heeft dezelfde SupplyCommitmentLineId als die regel;
    // 2. de bronregel van een bestaande PalletContentLine ligt vast;
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
    //    en een toewijzing gaat alleen Active -> Released (verzenden van DirectTrade bestaat nog niet).
    // Regels 5 t/m 7 en 10 gelden niet voor legacy-reserveringen met OrderLineId; vrijgeven wordt nooit geblokkeerd.
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

            await CheckSourceLinksAsync(db, async, cancellationToken);
            await CheckNoDirectTradeInWarehouseAsync(db, async, cancellationToken);
            await CheckDirectTradeAllocationsAsync(db, async, cancellationToken);
            await CheckSalesReservationsAsync(db, async, cancellationToken);
            await CheckReservationsOnOpenOrdersAsync(db, async, cancellationToken);
            await CheckDispatchLedgerAsync(db, async, cancellationToken);
            await CheckCorrectionLedgerAsync(db, async, cancellationToken);
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
            var corrected = correctedEntries.Select(e => e.Entity).ToList();
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
            var consumed = consumedEntries.Select(e => e.Entity).ToList();
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

        // ---- Regel 10: DirectTrade-toewijzingen ----

        private sealed record SourceState(int SupplyCommitmentId, string Route, int? CustomerId, string Status, string ExpectedUnit, decimal ExpectedQuantity, int ProductVariantId);

        private static async Task CheckDirectTradeAllocationsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            // Een toewijzing kent alleen Active en Released; elke andere overgang (Consumed, Corrected) is in deze fase een fout.
            foreach (var entry in db.ChangeTracker.Entries<StockReservation>().Where(e => e.State is EntityState.Added or EntityState.Modified
                         && e.Entity.SalesOrderLineId != null && e.Entity.Source == ReservationRules.SourceDirectTrade))
            {
                if (!ReservationRules.DirectTradeStatuses.Contains(entry.Entity.Status, StringComparer.Ordinal))
                {
                    throw new TraceabilityViolationException($"A DirectTrade allocation can only be Active or Released, not {entry.Entity.Status}; DirectTrade shipment is a separate flow.");
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

            // Som van Active toewijzingen per bronregel: opgeslagen (minus de gevolgde) plus alle gevolgde Active toewijzingen.
            var tracked = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State is not EntityState.Detached && sourceIds.Contains(e.Entity.SupplyCommitmentLineId))
                .ToList();
            var trackedIds = tracked.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id).ToHashSet();
            var stored = await ToListAsync(
                db.StockReservations.AsNoTracking()
                    .Where(r => sourceIds.Contains(r.SupplyCommitmentLineId) && r.SalesOrderLineId != null
                        && r.Source == ReservationRules.SourceDirectTrade && r.Status == ReservationRules.StatusActive)
                    .Select(r => new { r.Id, r.SupplyCommitmentLineId, r.Quantity }),
                async, cancellationToken);
            var totals = stored
                .Where(r => !trackedIds.Contains(r.Id))
                .Select(r => (r.SupplyCommitmentLineId, r.Quantity))
                .Concat(tracked
                    .Where(e => e.State != EntityState.Deleted && IsActiveSalesReservation(e.Entity) && e.Entity.Source == ReservationRules.SourceDirectTrade)
                    .Select(e => (e.Entity.SupplyCommitmentLineId, e.Entity.Quantity)))
                .GroupBy(r => r.SupplyCommitmentLineId);
            foreach (var group in totals)
            {
                if (sources.TryGetValue(group.Key, out var source) && group.Sum(r => r.Quantity) > source.ExpectedQuantity)
                {
                    throw new TraceabilityViolationException($"Active DirectTrade allocations on supply commitment line {group.Key} would exceed its expected quantity {source.ExpectedQuantity}.");
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
