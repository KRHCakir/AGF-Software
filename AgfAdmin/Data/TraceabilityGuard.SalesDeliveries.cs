using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 42: leveringen (feedback 22 november: een verkoop-partij is één levering aan één klant; zie SalesDeliveryService) ----
        //
        // 1. een levering wordt nooit verwijderd; haar order, volgnummer en verkoop-partij veranderen nooit;
        // 2. een nieuwe levering krijgt een nieuwe verkoop-partij van dezelfde order (in dezelfde save aangemaakt); de eerste levering
        //    (volgnummer 1) ontstaat met de order, een volgende alleen bij een open order en met het volgende volgnummer;
        // 3. een nieuwe verkoop-partij van een order hoort altijd bij een nieuwe levering (geen losse partijen naast de leveringen);
        // 4. een reservering wijst alleen naar een levering van haar eigen order; ze verhuist alleen zolang ze actief is, en alleen samen met
        //    de auditregel "levering aangemaakt" van die order (de historie zegt wanneer en door wie);
        // 5. een document hoort bij een levering van zijn eigen order en verandert daarna nooit van levering.
        // Vergeleken met de opgeslagen waarden in de database, niet met OriginalValues.
        private static async Task CheckSalesDeliveriesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var deliveryEntries = db.ChangeTracker.Entries<SalesDelivery>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            if (deliveryEntries.Any(e => e.State == EntityState.Deleted))
            {
                throw new TraceabilityViolationException("A delivery is never deleted.");
            }
            var modified = deliveryEntries.Where(e => e.State == EntityState.Modified).ToList();
            if (modified.Count > 0)
            {
                var ids = modified.Select(e => e.Entity.Id).ToList();
                var stored = (await ToListAsync(db.SalesDeliveries.AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.SalesOrderId, d.Sequence, d.PartijId }), async, cancellationToken))
                    .ToDictionary(d => d.Id);
                foreach (var entry in modified)
                {
                    if (stored.TryGetValue(entry.Entity.Id, out var before)
                        && (before.SalesOrderId != entry.Property(d => d.SalesOrderId).CurrentValue || before.Sequence != entry.Entity.Sequence || before.PartijId != entry.Property(d => d.PartijId).CurrentValue))
                    {
                        throw new TraceabilityViolationException("The order, sequence and sales lot of a delivery never change.");
                    }
                }
            }

            foreach (var entry in deliveryEntries.Where(e => e.State == EntityState.Added))
            {
                var delivery = entry.Entity;
                var orderEntry = delivery.SalesOrder != null ? db.Entry(delivery.SalesOrder) : null;
                var orderId = entry.Property(d => d.SalesOrderId).CurrentValue;
                var partijEntry = delivery.Partij != null ? db.Entry(delivery.Partij) : null;
                if (partijEntry == null || partijEntry.State != EntityState.Added || delivery.Partij.Kind != PartijRules.KindSales
                    || (delivery.Partij.SalesOrder != null ? delivery.Partij.SalesOrder != delivery.SalesOrder && db.Entry(delivery.Partij.SalesOrder).Property(o => o.Id).CurrentValue != orderId
                        : partijEntry.Property(p => p.SalesOrderId).CurrentValue != orderId))
                {
                    throw new TraceabilityViolationException("A new delivery gets its own new sales lot of the same sales order.");
                }
                var newOrder = orderEntry != null && orderEntry.State == EntityState.Added;
                if (newOrder != (delivery.Sequence == 1))
                {
                    throw new TraceabilityViolationException("The first delivery comes with its sales order; a partial delivery is added to an existing order.");
                }
                if (!newOrder)
                {
                    var order = await FirstOrDefaultAsync(db.SalesOrders.AsNoTracking().Where(o => o.Id == orderId)
                        .Select(o => new { o.Status, Last = o.Deliveries.Max(d => (int?)d.Sequence) }), async, cancellationToken);
                    var pendingLast = deliveryEntries.Where(e => e.State == EntityState.Added && e != entry && e.Property(d => d.SalesOrderId).CurrentValue == orderId && e.Entity.Sequence < delivery.Sequence)
                        .Select(e => e.Entity.Sequence).DefaultIfEmpty(0).Max();
                    if (order == null || order.Status != ReservationRules.OrderStatusOpen || delivery.Sequence != Math.Max(order.Last ?? 0, pendingLast) + 1)
                    {
                        throw new TraceabilityViolationException("A partial delivery is only added to an open sales order, with the next sequence number.");
                    }
                }
            }

            var newDeliveryLots = deliveryEntries.Where(e => e.State == EntityState.Added).Select(e => e.Entity.Partij).Where(p => p != null).ToHashSet();
            if (db.ChangeTracker.Entries<Partij>().Any(e => e.State == EntityState.Added && e.Entity.Kind == PartijRules.KindSales && e.Entity.Invoice == null
                    && e.Property(p => p.InvoiceId).CurrentValue == null && !newDeliveryLots.Contains(e.Entity)))
            {
                throw new TraceabilityViolationException("A new sales lot of a sales order always belongs to a new delivery.");
            }

            await CheckReservationDeliveriesAsync(db, async, cancellationToken);
            await CheckDocumentDeliveriesAsync(db, async, cancellationToken);
        }

        private static async Task CheckReservationDeliveriesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var entries = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => (e.State == EntityState.Added && (e.Entity.SalesDelivery != null || e.Entity.SalesDeliveryId != null))
                    || (e.State == EntityState.Modified && e.Property(r => r.SalesDeliveryId).IsModified))
                .ToList();
            if (entries.Count == 0)
            {
                return;
            }
            var modifiedIds = entries.Where(e => e.State == EntityState.Modified).Select(e => e.Entity.Id).ToList();
            var stored = modifiedIds.Count == 0 ? new Dictionary<int, (int? DeliveryId, string Status)>()
                : (await ToListAsync(db.StockReservations.AsNoTracking().Where(r => modifiedIds.Contains(r.Id)).Select(r => new { r.Id, r.SalesDeliveryId, r.Status }), async, cancellationToken))
                    .ToDictionary(r => r.Id, r => (DeliveryId: r.SalesDeliveryId, r.Status));
            var lineIds = entries.Select(e => e.Property(r => r.SalesOrderLineId).CurrentValue).Where(id => id != null).Select(id => id.Value).Distinct().ToList();
            var orderOfLine = (await ToListAsync(db.SalesOrderLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, l.SalesOrderId }), async, cancellationToken))
                .ToDictionary(l => l.Id, l => l.SalesOrderId);
            var deliveryIds = entries.Select(e => e.Property(r => r.SalesDeliveryId).CurrentValue).Where(id => id != null && id > 0).Select(id => id.Value).Distinct().ToList();
            var orderOfDelivery = (await ToListAsync(db.SalesDeliveries.AsNoTracking().Where(d => deliveryIds.Contains(d.Id)).Select(d => new { d.Id, d.SalesOrderId }), async, cancellationToken))
                .ToDictionary(d => d.Id, d => d.SalesOrderId);
            var createdFor = db.ChangeTracker.Entries<SalesOrderAuditEvent>()
                .Where(e => e.State == EntityState.Added && e.Entity.EventType == SalesDeliveryRules.DeliveryCreatedEvent)
                .Select(e => e.Property(a => a.SalesOrderId).CurrentValue).ToHashSet();

            foreach (var entry in entries)
            {
                var reservation = entry.Entity;
                var lineId = entry.Property(r => r.SalesOrderLineId).CurrentValue;
                var reservationOrder = lineId != null && orderOfLine.TryGetValue(lineId.Value, out var o) ? o : (int?)null;
                int? targetOrder = reservation.SalesDelivery != null && db.Entry(reservation.SalesDelivery).State == EntityState.Added
                    ? db.Entry(reservation.SalesDelivery).Property(d => d.SalesOrderId).CurrentValue
                    : entry.Property(r => r.SalesDeliveryId).CurrentValue is int target && orderOfDelivery.TryGetValue(target, out var t) ? t : null;
                if (entry.Property(r => r.SalesDeliveryId).CurrentValue != null || reservation.SalesDelivery != null)
                {
                    if (reservationOrder == null || targetOrder != reservationOrder)
                    {
                        throw new TraceabilityViolationException("A reservation only goes with a delivery of its own sales order.");
                    }
                }
                if (entry.State != EntityState.Modified || !stored.TryGetValue(reservation.Id, out var before) || before.DeliveryId == entry.Property(r => r.SalesDeliveryId).CurrentValue)
                {
                    continue;
                }
                if (before.Status != ReservationRules.StatusActive || reservation.Status != ReservationRules.StatusActive)
                {
                    throw new TraceabilityViolationException("Only an active reservation moves to another delivery; what has been shipped stays with its delivery.");
                }
                if (reservationOrder == null || !createdFor.Contains(reservationOrder.Value))
                {
                    throw new TraceabilityViolationException("A reservation moves to another delivery only together with the audit line of that delivery.");
                }
            }
        }

        private static async Task CheckDocumentDeliveriesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var added = db.ChangeTracker.Entries<SalesOrderDocument>().Where(e => e.State == EntityState.Added).ToList();
            var modified = db.ChangeTracker.Entries<SalesOrderDocument>().Where(e => e.State == EntityState.Modified && e.Property(d => d.SalesDeliveryId).IsModified).ToList();
            if (modified.Count > 0)
            {
                var ids = modified.Select(e => e.Entity.Id).ToList();
                var stored = (await ToListAsync(db.SalesOrderDocuments.AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.SalesDeliveryId }), async, cancellationToken))
                    .ToDictionary(d => d.Id, d => d.SalesDeliveryId);
                if (modified.Any(e => stored.TryGetValue(e.Entity.Id, out var before) && before != e.Property(d => d.SalesDeliveryId).CurrentValue))
                {
                    throw new TraceabilityViolationException("A document stays with its delivery.");
                }
            }
            if (added.Count == 0)
            {
                return;
            }
            var deliveryIds = added.Select(e => e.Property(d => d.SalesDeliveryId).CurrentValue).Where(id => id > 0).Distinct().ToList();
            var orderOfDelivery = (await ToListAsync(db.SalesDeliveries.AsNoTracking().Where(d => deliveryIds.Contains(d.Id)).Select(d => new { d.Id, d.SalesOrderId }), async, cancellationToken))
                .ToDictionary(d => d.Id, d => d.SalesOrderId);
            foreach (var entry in added)
            {
                var document = entry.Entity;
                var deliveryOrder = document.SalesDelivery != null ? db.Entry(document.SalesDelivery).Property(d => d.SalesOrderId).CurrentValue
                    : orderOfDelivery.TryGetValue(entry.Property(d => d.SalesDeliveryId).CurrentValue, out var o) ? o : (int?)null;
                if (deliveryOrder != entry.Property(d => d.SalesOrderId).CurrentValue)
                {
                    throw new TraceabilityViolationException("A document belongs to a delivery of its own sales order.");
                }
            }
        }
    }
}
