using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1: concept maken, bijwerken, weggooien en lezen. Een factuur uit leveringen (Kind Delivery) neemt prijs, korting en
    // btw van de orderregel over; een directe factuur (Kind Direct) heeft eigen regels. De actor komt altijd uit ICurrentUser.
    public sealed partial class SalesInvoiceService(AppDbContext db, ICurrentUser user = null)
    {
        public const string NothingToInvoiceCode = "NOTHING_TO_INVOICE";
        public const string ReloadCode = "INVOICE_CHANGED";
        public const string QuantityCode = "INVOICE_MORE_THAN_DELIVERED";
        private const int OrderLockClass = 44_045;
        private readonly ICurrentUser _user = user ?? CurrentUser.Default;
        private IBusinessCalendar calendar => db.BusinessCalendar ?? new AmsterdamBusinessCalendar(TimeProvider.System);

        private string Actor() => _user.Actor(SalesInvoiceRules.MaxActorLength);

        public async Task<ServiceResult<SalesInvoiceDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var exists = await db.Invoices.AnyAsync(i => i.Id == id && i.Kind != null, cancellationToken);
            return exists ? ServiceResult<SalesInvoiceDto>.Ok(await DtoAsync(id, cancellationToken)) : ServiceResult<SalesInvoiceDto>.NotFound($"Sales invoice {id} does not exist.");
        }

        // Het hoogste aantal per regel uit een levering of van de order: wat er (zonder deze factuur) nog te factureren is; plus de kg die
        // daarbij horen (voor een evenredig deel bij een deelfactuur).
        private sealed record LineMax(decimal Max, decimal? Kg);

        private async Task<Dictionary<int, LineMax>> MaximaAsync(Invoice invoice, CancellationToken cancellationToken)
        {
            var result = new Dictionary<int, LineMax>();
            if (invoice.Kind != SalesInvoiceRules.KindDelivery)
            {
                return result;
            }
            var deliveryIds = invoice.InvoiceLines.Where(l => l.SalesDeliveryId != null).Select(l => l.SalesDeliveryId.Value).Distinct().ToList();
            var serviceIds = invoice.InvoiceLines.Where(l => l.SalesOrderServiceLineId != null).Select(l => l.SalesOrderServiceLineId.Value).ToList();
            var packagingIds = invoice.InvoiceLines.Where(l => l.SalesOrderPackagingLineId != null).Select(l => l.SalesOrderPackagingLineId.Value).ToList();
            var orderIds = await OrderIdsAsync(deliveryIds, serviceIds, packagingIds, cancellationToken);
            var states = await DeliveryStatesAsync(orderIds, invoice.Id, cancellationToken);
            var extras = await ExtraStatesAsync(orderIds, invoice.Id, cancellationToken);
            foreach (var line in invoice.InvoiceLines.Where(SalesInvoiceRules.Counts))
            {
                if (line.SalesDeliveryId != null)
                {
                    var state = states.FirstOrDefault(s => s.DeliveryId == line.SalesDeliveryId && s.OrderLineId == line.SalesOrderLineId);
                    var rest = state?.Rest ?? 0m;
                    result[line.Id] = new LineMax(rest, state?.DeliveredKg is decimal kg && state.Delivered > 0m ? Math.Round(kg * rest / state.Delivered, 3, MidpointRounding.AwayFromZero) : null);
                }
                else if (line.SalesOrderServiceLineId != null || line.SalesOrderPackagingLineId != null)
                {
                    var extra = extras.FirstOrDefault(e => line.SalesOrderPackagingLineId != null ? e.Packaging && e.LineId == line.SalesOrderPackagingLineId : !e.Packaging && e.LineId == line.SalesOrderServiceLineId);
                    result[line.Id] = new LineMax(extra?.Rest ?? 0m, null);
                }
            }
            return result;
        }

        private async Task<List<int>> OrderIdsAsync(List<int> deliveryIds, List<int> serviceIds, List<int> packagingIds, CancellationToken cancellationToken)
        {
            var orders = await db.SalesDeliveries.AsNoTracking().Where(d => deliveryIds.Contains(d.Id)).Select(d => d.SalesOrderId).ToListAsync(cancellationToken);
            orders.AddRange(await db.SalesOrderServiceLines.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).Select(s => s.SalesOrderId).ToListAsync(cancellationToken));
            orders.AddRange(await db.SalesOrderPackagingLines.AsNoTracking().Where(p => packagingIds.Contains(p.Id)).Select(p => p.SalesOrderId).ToListAsync(cancellationToken));
            return orders.Distinct().OrderBy(o => o).ToList();
        }

        // ---------------- Antwoord ----------------

        internal async Task<SalesInvoiceDto> DtoAsync(int id, CancellationToken cancellationToken)
        {
            var invoice = await db.Invoices.AsNoTracking()
                .Include(i => i.InvoiceLines).ThenInclude(l => l.RevenueType)
                .Include(i => i.InvoiceLines).ThenInclude(l => l.Partij)
                .Include(i => i.Customer)
                .AsSplitQuery()
                .SingleAsync(i => i.Id == id, cancellationToken);
            var lines = invoice.InvoiceLines.Where(SalesInvoiceRules.Counts).OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToList();
            var deliveryIds = lines.Where(l => l.SalesDeliveryId != null).Select(l => l.SalesDeliveryId.Value).Distinct().ToList();
            var deliveries = await db.SalesDeliveries.AsNoTracking().Where(d => deliveryIds.Contains(d.Id))
                .Select(d => new { d.Id, d.SalesOrderId, d.SalesOrder.OrderNumber, d.Sequence, PartijNumber = d.Partij.Number, d.SalesOrder.CustomerReference })
                .ToListAsync(cancellationToken);
            var concept = invoice.Status == SalesInvoiceRules.StatusConcept;
            var maxima = concept ? await MaximaAsync(invoice, cancellationToken) : new Dictionary<int, LineMax>();
            var states = deliveries.Count == 0 ? new List<DeliveryLineState>()
                : await DeliveryStatesAsync(deliveries.Select(d => d.SalesOrderId).Distinct().ToList(), null, cancellationToken);
            var deliveryDtos = deliveries
                .Select(d => new SalesInvoiceDeliveryDto(d.Id, d.SalesOrderId, d.OrderNumber, d.Sequence, d.PartijNumber,
                    states.Where(s => s.DeliveryId == d.Id).Select(s => s.DeliveredAt).Where(x => x != null).DefaultIfEmpty().Max(), d.CustomerReference))
                .OrderBy(d => d.OrderNumber, StringComparer.Ordinal).ThenBy(d => d.Sequence).ToList();
            var summary = SalesInvoiceRules.Summary(lines);
            var totals = new SalesInvoiceTotalsDto(summary.Net, summary.VatRates.Select(r => new SalesInvoiceVatRateDto(r.Percentage, r.Net, r.Vat)).ToList(),
                summary.Vat, summary.Gross, summary.LinesWithoutPrice);
            var customer = invoice.Customer;
            var stale = concept && calendar.Today().DayNumber - calendar.DateOf(invoice.CreatedAt).DayNumber > SalesInvoiceRules.StaleConceptDays;
            return new SalesInvoiceDto(
                invoice.Id, invoice.Kind, invoice.Status, invoice.InvoiceNumber, invoice.InvoiceDate, invoice.DeliveryDate, invoice.DueDate, invoice.PaymentTermDays,
                invoice.CustomerId, concept ? customer?.Name ?? invoice.CustomCustomerName : invoice.CustomCustomerName,
                concept ? customer?.BillingAddress : invoice.CustomerAddress, concept ? customer?.VATNumber : invoice.CustomerVatNumber,
                invoice.ReferenceNumber, invoice.Kind == SalesInvoiceRules.KindDirect ? invoice.BatchNumber : null, deliveryDtos,
                lines.Select(l => new SalesInvoiceLineDto(
                    l.Id, l.LineKind, l.SortOrder, l.SalesDeliveryId, l.SalesOrderLineId, l.SalesOrderServiceLineId, l.SalesOrderPackagingLineId,
                    l.ProductVariantId, l.PartijId, l.Partij?.Number, l.CostArticleId, l.RevenueTypeId, l.RevenueType?.Name,
                    l.Description, l.Quantity, l.Unit, l.UnitPrice, l.DiscountPercentage, l.VATPercentage, l.LineNet, l.LineVat,
                    l.NetWeight > 0m ? l.NetWeight : null, l.OriginCountry, l.LotNumber, l.Ggn,
                    maxima.TryGetValue(l.Id, out var max) ? max.Max : null)).ToList(),
                totals, invoice.CreatedAt, invoice.CreatedBy, invoice.FinalizedAt, invoice.FinalizedBy, invoice.DiscardedAt, invoice.DiscardedBy, invoice.DiscardReason,
                stale, invoice.Version);
        }
    }
}
