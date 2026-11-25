using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1, factureren vanuit de levering: wat er per levering geleverd is (verbruikte reserveringen van die levering), wat al
    // op een niet-vervallen factuur staat en wat er dus nog te factureren is. Dienst- en emballageregels van de order gaan mee met de eerste
    // factuur die ze nog niet volledig heeft.
    public sealed partial class SalesInvoiceService
    {
        // Eén orderregel binnen één levering.
        internal sealed record DeliveryLineState(
            int DeliveryId, int OrderId, int OrderLineId, decimal Delivered, decimal? DeliveredKg, DateTime? DeliveredAt, decimal Invoiced,
            IReadOnlyList<string> Lots, IReadOnlyList<string> Ggns)
        {
            public decimal Rest => Math.Max(0m, Delivered - Invoiced);
        }

        internal sealed record OrderExtraState(int OrderId, int LineId, bool Packaging, decimal Quantity, decimal Invoiced)
        {
            public decimal Rest => Math.Max(0m, Quantity - Invoiced);
        }

        private sealed record ConsumedFact(int OrderId, int OrderLineId, int? DeliveryId, decimal Quantity, decimal? Kg, DateTime? At, string Lot, string Ggn);

        // Geleverd en gefactureerd per (levering, orderregel) van deze orders; regels van exceptInvoiceId tellen niet als "al gefactureerd"
        // (zo is het maximum van een conceptregel de rest plus wat er al op staat).
        internal async Task<List<DeliveryLineState>> DeliveryStatesAsync(IReadOnlyCollection<int> orderIds, int? exceptInvoiceId, CancellationToken cancellationToken)
        {
            var deliveries = await db.SalesDeliveries.AsNoTracking().Where(d => orderIds.Contains(d.SalesOrderId))
                .Select(d => new { d.Id, d.SalesOrderId, d.Sequence }).ToListAsync(cancellationToken);
            var first = deliveries.GroupBy(d => d.SalesOrderId).ToDictionary(g => g.Key, g => g.OrderBy(d => d.Sequence).First().Id);
            var consumed = await db.StockReservations.AsNoTracking()
                .Where(r => r.SalesOrderLineId != null && orderIds.Contains(r.SalesOrderLine.SalesOrderId) && r.Status == ReservationRules.StatusConsumed)
                .Select(r => new ConsumedFact(
                    r.SalesOrderLine.SalesOrderId, r.SalesOrderLineId.Value, r.SalesDeliveryId, r.Quantity, r.NetWeightKg, r.ConsumedAt,
                    r.PalletContentLine != null
                        ? (r.PalletContentLine.Partij != null ? r.PalletContentLine.Partij.Number : r.PalletContentLine.LotNumber)
                        : (r.SupplyCommitmentLine.Partij != null ? r.SupplyCommitmentLine.Partij.Number
                            : r.SupplyCommitmentLine.SupplyCommitment.Partij != null ? r.SupplyCommitmentLine.SupplyCommitment.Partij.Number : null),
                    r.PalletContentLine != null ? r.PalletContentLine.Ggn : r.SupplyCommitmentLine.Ggn))
                .ToListAsync(cancellationToken);
            var deliveryIds = deliveries.Select(d => d.Id).ToList();
            var invoiced = await InvoicedQuery(exceptInvoiceId).Where(l => l.SalesDeliveryId != null && deliveryIds.Contains(l.SalesDeliveryId.Value))
                .Select(l => new { DeliveryId = l.SalesDeliveryId.Value, OrderLineId = l.SalesOrderLineId.Value, l.Quantity })
                .ToListAsync(cancellationToken);
            var invoicedBy = invoiced.GroupBy(l => (l.DeliveryId, l.OrderLineId)).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

            return consumed
                .Where(c => c.DeliveryId != null || first.ContainsKey(c.OrderId))
                .GroupBy(c => (DeliveryId: c.DeliveryId ?? first[c.OrderId], c.OrderId, c.OrderLineId))
                .Select(g => new DeliveryLineState(
                    g.Key.DeliveryId, g.Key.OrderId, g.Key.OrderLineId, g.Sum(c => c.Quantity),
                    g.All(c => c.Kg != null) ? g.Sum(c => c.Kg.Value) : null, g.Max(c => c.At),
                    invoicedBy.TryGetValue((g.Key.DeliveryId, g.Key.OrderLineId), out var done) ? done : 0m,
                    Distinct(g.Select(c => c.Lot)), Distinct(g.Select(c => c.Ggn))))
                .OrderBy(s => s.OrderId).ThenBy(s => s.DeliveryId).ThenBy(s => s.OrderLineId)
                .ToList();
        }

        // Dienst- en emballageregels van deze orders met wat er al van gefactureerd is.
        internal async Task<List<OrderExtraState>> ExtraStatesAsync(IReadOnlyCollection<int> orderIds, int? exceptInvoiceId, CancellationToken cancellationToken)
        {
            var services = await db.SalesOrderServiceLines.AsNoTracking().Where(s => orderIds.Contains(s.SalesOrderId))
                .Select(s => new { s.Id, s.SalesOrderId, s.Quantity }).ToListAsync(cancellationToken);
            var packaging = await db.SalesOrderPackagingLines.AsNoTracking().Where(p => orderIds.Contains(p.SalesOrderId))
                .Select(p => new { p.Id, p.SalesOrderId, p.Quantity }).ToListAsync(cancellationToken);
            var serviceIds = services.Select(s => s.Id).ToList();
            var packagingIds = packaging.Select(p => p.Id).ToList();
            var invoiced = await InvoicedQuery(exceptInvoiceId)
                .Where(l => (l.SalesOrderServiceLineId != null && serviceIds.Contains(l.SalesOrderServiceLineId.Value))
                    || (l.SalesOrderPackagingLineId != null && packagingIds.Contains(l.SalesOrderPackagingLineId.Value)))
                .Select(l => new { l.SalesOrderServiceLineId, l.SalesOrderPackagingLineId, l.Quantity }).ToListAsync(cancellationToken);
            return services.Select(s => new OrderExtraState(s.SalesOrderId, s.Id, false, s.Quantity, invoiced.Where(l => l.SalesOrderServiceLineId == s.Id).Sum(l => l.Quantity)))
                .Concat(packaging.Select(p => new OrderExtraState(p.SalesOrderId, p.Id, true, p.Quantity, invoiced.Where(l => l.SalesOrderPackagingLineId == p.Id).Sum(l => l.Quantity))))
                .OrderBy(s => s.OrderId).ThenBy(s => s.Packaging).ThenBy(s => s.LineId)
                .ToList();
        }

        // Regels die meetellen als gefactureerd: niet uit een concept gehaald, op een factuur die niet vervallen is.
        private IQueryable<InvoiceLine> InvoicedQuery(int? exceptInvoiceId) =>
            db.InvoiceLines.AsNoTracking().Where(l => l.RemovedAt == null && l.Invoice.Kind == SalesInvoiceRules.KindDelivery
                && l.Invoice.Status != SalesInvoiceRules.StatusDiscarded && (exceptInvoiceId == null || l.InvoiceId != exceptInvoiceId));

        private static List<string> Distinct(IEnumerable<string> values) =>
            values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToList();

        // ---------------- Te factureren (verkooporder en werklijst) ----------------

        public async Task<ServiceResult<List<DeliveryInvoicingDto>>> OrderInvoicingAsync(int orderId, CancellationToken cancellationToken = default)
        {
            if (!await db.SalesOrders.AnyAsync(o => o.Id == orderId, cancellationToken))
            {
                return ServiceResult<List<DeliveryInvoicingDto>>.NotFound($"Sales order {orderId} does not exist.");
            }
            return ServiceResult<List<DeliveryInvoicingDto>>.Ok(await InvoicingAsync(new[] { orderId }, onlyRest: false, cancellationToken));
        }

        // De werklijst Nog te factureren: leveringen met een rest, per klant, oudste levering eerst.
        public async Task<List<InvoicingCustomerDto>> ToInvoiceAsync(CancellationToken cancellationToken = default)
        {
            var delivered = await db.StockReservations.AsNoTracking()
                .Where(r => r.SalesOrderLineId != null && r.Status == ReservationRules.StatusConsumed)
                .Select(r => r.SalesOrderLine.SalesOrderId).Distinct().ToListAsync(cancellationToken);
            var deliveries = await InvoicingAsync(delivered, onlyRest: true, cancellationToken);
            return deliveries.GroupBy(d => (d.CustomerId, d.CustomerName))
                .Select(g => new InvoicingCustomerDto(g.Key.CustomerId, g.Key.CustomerName, g.OrderBy(d => d.DeliveredAt).ThenBy(d => d.OrderNumber, StringComparer.Ordinal).ThenBy(d => d.Sequence).ToList()))
                .OrderBy(c => c.Deliveries.Min(d => d.DeliveredAt)).ThenBy(c => c.CustomerName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private async Task<List<DeliveryInvoicingDto>> InvoicingAsync(IReadOnlyCollection<int> orderIds, bool onlyRest, CancellationToken cancellationToken)
        {
            if (orderIds.Count == 0)
            {
                return new List<DeliveryInvoicingDto>();
            }
            var deliveries = await db.SalesDeliveries.AsNoTracking().Where(d => orderIds.Contains(d.SalesOrderId))
                .Select(d => new { d.Id, d.SalesOrderId, d.Sequence, PartijNumber = d.Partij.Number, d.SalesOrder.OrderNumber, d.SalesOrder.CustomerId, CustomerName = d.SalesOrder.Customer.Name })
                .ToListAsync(cancellationToken);
            var states = await DeliveryStatesAsync(orderIds, null, cancellationToken);
            var extras = await ExtraStatesAsync(orderIds, null, cancellationToken);
            var lineIds = states.Select(s => s.OrderLineId).Distinct().ToList();
            var lines = await db.SalesOrderLines.AsNoTracking().Where(l => lineIds.Contains(l.Id))
                .Select(l => new { l.Id, l.Unit, l.ProductVariant, ProductName = l.ProductVariant.Product.Name }).ToListAsync(cancellationToken);
            var lineInfo = lines.ToDictionary(l => l.Id, l => (Description: SalesInvoiceRules.Describe(l.ProductVariant, l.ProductName), l.Unit));
            var deliveryIds = deliveries.Select(d => d.Id).ToList();
            var invoices = await db.InvoiceLines.AsNoTracking()
                .Where(l => l.SalesDeliveryId != null && deliveryIds.Contains(l.SalesDeliveryId.Value) && l.RemovedAt == null && l.Invoice.Kind == SalesInvoiceRules.KindDelivery)
                .Select(l => new { DeliveryId = l.SalesDeliveryId.Value, l.InvoiceId, l.Invoice.Status, l.Invoice.InvoiceNumber })
                .Distinct().ToListAsync(cancellationToken);
            var today = calendar.Today();

            var result = new List<DeliveryInvoicingDto>();
            foreach (var delivery in deliveries.OrderBy(d => d.OrderNumber, StringComparer.Ordinal).ThenBy(d => d.Sequence))
            {
                var own = states.Where(s => s.DeliveryId == delivery.Id).ToList();
                var orderExtras = extras.Where(e => e.OrderId == delivery.SalesOrderId && e.Rest > 0m).ToList();
                var hasRest = own.Any(s => s.Rest > 0m) || (own.Count > 0 && orderExtras.Count > 0);
                if (onlyRest && !hasRest)
                {
                    continue;
                }
                var deliveredAt = own.Select(s => s.DeliveredAt).Where(d => d != null).DefaultIfEmpty().Max();
                var mine = invoices.Where(i => i.DeliveryId == delivery.Id && i.Status != SalesInvoiceRules.StatusDiscarded)
                    .Select(i => new DeliveryInvoicingInvoiceDto(i.InvoiceId, i.Status, i.InvoiceNumber)).DistinctBy(i => i.Id).OrderBy(i => i.Id).ToList();
                result.Add(new DeliveryInvoicingDto(
                    delivery.Id, delivery.SalesOrderId, delivery.OrderNumber, delivery.Sequence, delivery.PartijNumber, delivery.CustomerId, delivery.CustomerName,
                    deliveredAt, deliveredAt == null ? null : today.DayNumber - calendar.DateOf(deliveredAt.Value).DayNumber,
                    own.Select(s => new DeliveryInvoicingLineDto(s.OrderLineId, lineInfo[s.OrderLineId].Description, lineInfo[s.OrderLineId].Unit, s.Delivered, s.Invoiced, s.Rest)).ToList(),
                    hasRest, mine.FirstOrDefault(i => i.Status == SalesInvoiceRules.StatusConcept)?.Id, mine));
            }
            return result;
        }
    }
}
