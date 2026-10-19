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
    //    reservering die in dezelfde save Consumed wordt (gelijke hoeveelheid, eenheid en kg) met een verzendregel, en afgeboekt daalt nooit.
    // Regels 5 t/m 7 gelden niet voor legacy-reserveringen met OrderLineId; vrijgeven wordt nooit geblokkeerd.
    // Append-only (regel 3): PalletAuditEvents, SalesOrderAuditEvents, SalesDispatches, SalesDispatchLines en Dispatch-mutaties
    // (andere mutatietypen blijven ongemoeid).
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

            await CheckSourceLinksAsync(db, async, cancellationToken);
            await CheckNoDirectTradeInWarehouseAsync(db, async, cancellationToken);
            await CheckSalesReservationsAsync(db, async, cancellationToken);
            await CheckReservationsOnOpenOrdersAsync(db, async, cancellationToken);
            CheckDispatchLedger(db);
        }

        // ---- Regel 8: afboekgrootboek in beide richtingen ----

        private static void CheckDispatchLedger(AppDbContext db)
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
            var consumed = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State == EntityState.Modified
                    && e.Entity.Status == ReservationRules.StatusConsumed
                    && e.OriginalValues.GetValue<string>(nameof(StockReservation.Status)) != ReservationRules.StatusConsumed)
                .Select(e => e.Entity)
                .ToList();
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
            var added = db.ChangeTracker.Entries<StockReservation>()
                // Onvolledige rijen (zonder inhoudsregel of eenheid) laten we over aan de database-checks/FK's.
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
