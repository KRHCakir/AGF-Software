using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    // Een factuurregel zou meer factureren dan er geleverd is (of meer dienst of emballage dan op de order). Ontstaat ook als een ander
    // tegelijk hetzelfde factureerde; de service maakt er een 409 van.
    public sealed class InvoiceQuantityConflictException : TraceabilityViolationException
    {
        public InvoiceQuantityConflictException(string message) : base(message) {}
    }

    internal static partial class TraceabilityGuard
    {
        // ---- Regel 45: nooit meer gefactureerd dan geleverd (verkoopfactuur 1) ----
        //
        // 1. per levering en orderregel is het totaal op niet-vervallen facturen (regels niet uit een concept gehaald) nooit meer dan wat er
        //    van die levering geleverd is (verbruikte reserveringen van die levering; zonder levering = de eerste levering van de order);
        // 2. per dienst- of emballageregel van een order is het gefactureerde totaal nooit meer dan het aantal op de order;
        // 3. de orderregel hoort bij de order van de levering, en de klant van die order is de klant van de factuur; zulke regels staan
        //    alleen op een factuur uit leveringen.
        // Gecontroleerd bij elke nieuwe of gewijzigde regel met zo'n herkomst, met de opgeslagen regels van alle andere facturen erbij.
        private sealed record InvoicedRow(int LineId, int? DeliveryId, int? OrderLineId, int? ServiceLineId, int? PackagingLineId, decimal Quantity);

        private static async Task CheckSalesInvoiceQuantitiesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var touched = db.ChangeTracker.Entries<InvoiceLine>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified
                    && (e.Entity.SalesDeliveryId != null || e.Entity.SalesOrderServiceLineId != null || e.Entity.SalesOrderPackagingLineId != null))
                .ToList();
            if (touched.Count == 0)
            {
                return;
            }

            // De factuur van elke geraakte regel (getrackt of opgeslagen): soort, status en klant zoals ze na deze save zijn.
            var trackedInvoices = db.ChangeTracker.Entries<Invoice>().Select(e => e.Entity).ToList();
            var storedIds = touched.Select(e => e.Entity.Invoice?.Id ?? e.Entity.InvoiceId).Where(id => id > 0 && trackedInvoices.All(i => i.Id != id)).Distinct().ToList();
            var stored = storedIds.Count == 0 ? new List<Invoice>()
                : await ToListAsync(db.Invoices.AsNoTracking().Where(i => storedIds.Contains(i.Id)), async, cancellationToken);
            Invoice InvoiceOf(InvoiceLine line) => line.Invoice ?? trackedInvoices.FirstOrDefault(i => i.Id == line.InvoiceId) ?? stored.FirstOrDefault(i => i.Id == line.InvoiceId);
            bool Counts(InvoiceLine line) => line.RemovedAt == null && InvoiceOf(line)?.Status != SalesInvoiceRules.StatusDiscarded;

            foreach (var entry in touched)
            {
                var invoice = InvoiceOf(entry.Entity);
                if (invoice == null || invoice.Kind != SalesInvoiceRules.KindDelivery)
                {
                    throw new TraceabilityViolationException("A line from a delivery or from a sales order only goes on an invoice from deliveries.");
                }
            }

            var deliveryIds = touched.Where(e => e.Entity.SalesDeliveryId != null).Select(e => e.Entity.SalesDeliveryId.Value).Distinct().ToList();
            var deliveries = deliveryIds.Count == 0 ? new Dictionary<int, (int OrderId, int CustomerId, int Sequence)>()
                : (await ToListAsync(db.SalesDeliveries.AsNoTracking().Where(d => deliveryIds.Contains(d.Id))
                    .Select(d => new { d.Id, d.SalesOrderId, d.SalesOrder.CustomerId, d.Sequence }), async, cancellationToken))
                    .ToDictionary(d => d.Id, d => (OrderId: d.SalesOrderId, d.CustomerId, d.Sequence));
            var orderLineIds = touched.Where(e => e.Entity.SalesOrderLineId != null).Select(e => e.Entity.SalesOrderLineId.Value).Distinct().ToList();
            var orderLineOrders = orderLineIds.Count == 0 ? new Dictionary<int, int>()
                : (await ToListAsync(db.SalesOrderLines.AsNoTracking().Where(l => orderLineIds.Contains(l.Id)).Select(l => new { l.Id, l.SalesOrderId }), async, cancellationToken))
                    .ToDictionary(l => l.Id, l => l.SalesOrderId);
            var serviceIds = touched.Where(e => e.Entity.SalesOrderServiceLineId != null).Select(e => e.Entity.SalesOrderServiceLineId.Value).Distinct().ToList();
            var services = serviceIds.Count == 0 ? new Dictionary<int, (decimal Quantity, int CustomerId)>()
                : (await ToListAsync(db.SalesOrderServiceLines.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).Select(s => new { s.Id, s.Quantity, s.SalesOrder.CustomerId }), async, cancellationToken))
                    .ToDictionary(s => s.Id, s => (s.Quantity, s.CustomerId));
            var packagingIds = touched.Where(e => e.Entity.SalesOrderPackagingLineId != null).Select(e => e.Entity.SalesOrderPackagingLineId.Value).Distinct().ToList();
            var packaging = packagingIds.Count == 0 ? new Dictionary<int, (decimal Quantity, int CustomerId)>()
                : (await ToListAsync(db.SalesOrderPackagingLines.AsNoTracking().Where(p => packagingIds.Contains(p.Id)).Select(p => new { p.Id, p.Quantity, p.SalesOrder.CustomerId }), async, cancellationToken))
                    .ToDictionary(p => p.Id, p => (Quantity: (decimal)p.Quantity, p.CustomerId));

            foreach (var entry in touched)
            {
                var line = entry.Entity;
                var customerId = InvoiceOf(line).CustomerId;
                var lineCustomer = line.SalesDeliveryId != null ? deliveries.TryGetValue(line.SalesDeliveryId.Value, out var d) ? d.CustomerId : (int?)null
                    : line.SalesOrderServiceLineId != null ? services.TryGetValue(line.SalesOrderServiceLineId.Value, out var s) ? s.CustomerId : null
                    : packaging.TryGetValue(line.SalesOrderPackagingLineId.Value, out var p) ? p.CustomerId : null;
                if (lineCustomer != null && lineCustomer != customerId)
                {
                    throw new TraceabilityViolationException("A delivery or order line of another customer never goes on this invoice.");
                }
                if (line.SalesDeliveryId != null && deliveries.TryGetValue(line.SalesDeliveryId.Value, out var delivery)
                    && (line.SalesOrderLineId == null || !orderLineOrders.TryGetValue(line.SalesOrderLineId.Value, out var orderOfLine) || orderOfLine != delivery.OrderId))
                {
                    throw new TraceabilityViolationException("The order line of an invoice line belongs to the order of its delivery.");
                }
            }

            // Alle opgeslagen regels met dezelfde herkomst, behalve de getrackte (die tellen met hun nieuwe waarden).
            var trackedIds = db.ChangeTracker.Entries<InvoiceLine>().Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id).ToHashSet();
            // De status van de factuur is die van de opgeslagen database; een getrackte factuur (bv. in dezelfde save vervallen) telt met zijn nieuwe status.
            var othersWithInvoice = await ToListAsync(db.InvoiceLines.AsNoTracking()
                .Where(l => l.RemovedAt == null && l.Invoice.Kind == SalesInvoiceRules.KindDelivery
                    && ((l.SalesDeliveryId != null && deliveryIds.Contains(l.SalesDeliveryId.Value))
                        || (l.SalesOrderServiceLineId != null && serviceIds.Contains(l.SalesOrderServiceLineId.Value))
                        || (l.SalesOrderPackagingLineId != null && packagingIds.Contains(l.SalesOrderPackagingLineId.Value))))
                .Select(l => new { l.InvoiceId, l.Invoice.Status, Row = new InvoicedRow(l.Id, l.SalesDeliveryId, l.SalesOrderLineId, l.SalesOrderServiceLineId, l.SalesOrderPackagingLineId, l.Quantity) }), async, cancellationToken);
            var others = othersWithInvoice
                .Where(o => (trackedInvoices.FirstOrDefault(i => i.Id == o.InvoiceId)?.Status ?? o.Status) != SalesInvoiceRules.StatusDiscarded)
                .Select(o => o.Row).ToList();
            var rows = others.Where(o => !trackedIds.Contains(o.LineId))
                .Concat(db.ChangeTracker.Entries<InvoiceLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged && Counts(e.Entity))
                    .Select(e => new InvoicedRow(e.Entity.Id, e.Entity.SalesDeliveryId, e.Entity.SalesOrderLineId, e.Entity.SalesOrderServiceLineId, e.Entity.SalesOrderPackagingLineId, e.Entity.Quantity)))
                .ToList();

            // Geleverd per (levering, orderregel): verbruikte reserveringen van die regel; zonder levering = de eerste levering.
            var orderIds = deliveries.Values.Select(v => v.OrderId).Distinct().ToList();
            var firstDeliveries = orderIds.Count == 0 ? new Dictionary<int, int>()
                : (await ToListAsync(db.SalesDeliveries.AsNoTracking().Where(x => orderIds.Contains(x.SalesOrderId) && x.Sequence == 1).Select(x => new { x.SalesOrderId, x.Id }), async, cancellationToken))
                    .ToDictionary(x => x.SalesOrderId, x => x.Id);
            var checkedLineIds = touched.Where(e => e.Entity.SalesOrderLineId != null).Select(e => e.Entity.SalesOrderLineId.Value).Distinct().ToList();
            var consumed = checkedLineIds.Count == 0 ? new List<(int LineId, int? DeliveryId, int OrderId, decimal Quantity)>()
                : (await ToListAsync(db.StockReservations.AsNoTracking()
                    .Where(r => r.SalesOrderLineId != null && checkedLineIds.Contains(r.SalesOrderLineId.Value) && r.Status == ReservationRules.StatusConsumed)
                    .Select(r => new { LineId = r.SalesOrderLineId.Value, r.SalesDeliveryId, r.SalesOrderLine.SalesOrderId, r.Quantity }), async, cancellationToken))
                    .Select(r => (r.LineId, DeliveryId: r.SalesDeliveryId, OrderId: r.SalesOrderId, r.Quantity)).ToList();

            // Alleen de geraakte combinaties van levering en orderregel (met alle opgeslagen en gewijzigde bedragen erbij), niet andere regels van dezelfde levering.
            var touchedKeys = touched.Where(e => e.Entity.SalesDeliveryId != null && e.Entity.SalesOrderLineId != null)
                .Select(e => (Delivery: e.Entity.SalesDeliveryId.Value, Line: e.Entity.SalesOrderLineId.Value)).Distinct().ToList();
            foreach (var key in touchedKeys)
            {
                var invoiced = rows.Where(r => r.DeliveryId == key.Delivery && r.OrderLineId == key.Line).Sum(r => r.Quantity);
                var delivered = consumed.Where(c => c.LineId == key.Line
                    && (c.DeliveryId ?? (firstDeliveries.TryGetValue(c.OrderId, out var first) ? first : 0)) == key.Delivery).Sum(c => c.Quantity);
                if (invoiced > delivered)
                {
                    throw new InvoiceQuantityConflictException($"Order line {key.Line} would be invoiced {invoiced} times for delivery {key.Delivery}, but only {delivered} was delivered.");
                }
            }
            foreach (var (id, limit) in services)
            {
                if (rows.Where(r => r.ServiceLineId == id).Sum(r => r.Quantity) > limit.Quantity)
                {
                    throw new InvoiceQuantityConflictException($"Service line {id} would be invoiced more than its quantity on the order.");
                }
            }
            foreach (var (id, limit) in packaging)
            {
                if (rows.Where(r => r.PackagingLineId == id).Sum(r => r.Quantity) > limit.Quantity)
                {
                    throw new InvoiceQuantityConflictException($"Packaging line {id} would be invoiced more than its quantity on the order.");
                }
            }
        }
    }
}
