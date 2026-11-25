using AgfAdmin.DTOs;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Verkoopfactuur 1, de lijst Verkoopfacturen: alleen het nieuwe model (oude facturen blijven in hun oude schermen). Zoeken op nummer,
    // klant, referentie, V-nummer (verkoop-partij) en I-nummer (inkoop-partij), ook zonder letter of streepje; filter op status; sorteren
    // op de kolomkoppen (toegestane velden, op de server); bladeren.
    public sealed partial class SalesInvoiceService
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;
        public const string FilterOpen = "open";
        public const string FilterAll = "all";

        public async Task<ServiceResult<SalesInvoiceListDto>> ListAsync(string query, string status, string sort, string direction, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            var filter = string.IsNullOrWhiteSpace(status) ? FilterAll : status.Trim();
            if (filter != FilterAll && filter != FilterOpen && !SalesInvoiceRules.Statuses.Contains(filter))
            {
                errors["status"] = new[] { $"Status must be one of: {FilterAll}, {FilterOpen}, {string.Join(", ", SalesInvoiceRules.Statuses)}." };
            }
            var order = ListSort.Parse(sort, direction, ListSortFields.SalesInvoices.Keys, errors);
            if (page < 1) errors["page"] = new[] { "Page starts at 1." };
            if (pageSize < 1 || pageSize > MaxPageSize) errors["pageSize"] = new[] { $"Page size is 1 to {MaxPageSize}." };
            if (errors.Count > 0)
            {
                return ServiceResult<SalesInvoiceListDto>.Validation(errors);
            }

            var invoices = await db.Invoices.AsNoTracking().Where(i => i.Kind != null)
                .Where(i => filter == FilterAll || (filter == FilterOpen ? i.Status == SalesInvoiceRules.StatusConcept : i.Status == filter))
                .Select(i => new
                {
                    i.Id, i.Kind, i.Status, i.InvoiceNumber, i.InvoiceDate, i.DeliveryDate, i.DueDate, i.CustomerId, i.CustomCustomerName, LiveName = i.Customer.Name,
                    i.BatchNumber, i.ReferenceNumber, i.SubTotal, i.VATAmount, i.TotalAmount, i.CreatedAt, i.Version
                })
                .ToListAsync(cancellationToken);
            var ids = invoices.Select(i => i.Id).ToList();
            var lines = await db.InvoiceLines.AsNoTracking().Where(l => ids.Contains(l.InvoiceId) && l.RemovedAt == null)
                .Select(l => new { l.InvoiceId, l.LotNumber, l.UnitPrice, SalesLot = l.SalesDelivery.Partij.Number, OrderNumber = l.SalesDelivery.SalesOrder.OrderNumber })
                .ToListAsync(cancellationToken);
            var byInvoice = lines.ToLookup(l => l.InvoiceId);
            var today = calendar.Today();

            var items = invoices.Select(i =>
            {
                var own = byInvoice[i.Id].ToList();
                var salesLots = Distinct(own.Select(l => l.SalesLot).Append(i.Kind == SalesInvoiceRules.KindDirect ? i.BatchNumber : null));
                var purchaseLots = Distinct(own.SelectMany(l => (l.LotNumber ?? "").Split(", ", StringSplitOptions.RemoveEmptyEntries)));
                var concept = i.Status == SalesInvoiceRules.StatusConcept;
                return new SalesInvoiceListItemDto(
                    i.Id, i.Kind, i.Status, i.InvoiceNumber, i.InvoiceDate, i.DeliveryDate, i.DueDate, i.CustomerId, concept ? i.LiveName ?? i.CustomCustomerName : i.CustomCustomerName,
                    salesLots, purchaseLots, Distinct(own.Select(l => l.OrderNumber)), i.ReferenceNumber, i.SubTotal, i.VATAmount, i.TotalAmount,
                    own.Count(l => l.UnitPrice == null), concept && today.DayNumber - calendar.DateOf(i.CreatedAt).DayNumber > SalesInvoiceRules.StaleConceptDays,
                    i.CreatedAt, i.Version);
            });
            if (!string.IsNullOrWhiteSpace(query))
            {
                var text = query.Trim().ToLowerInvariant();
                var key = PartijRules.NumberKey(query.Trim());
                bool Matches(string value) => value != null && (value.ToLowerInvariant().Contains(text) || (key != null && (PartijRules.NumberKey(value) ?? "").Contains(key)));
                items = items.Where(i => Matches(i.InvoiceNumber) || Matches(i.CustomerName) || Matches(i.ReferenceNumber)
                    || i.SalesLotNumbers.Any(Matches) || i.PurchaseLotNumbers.Any(Matches) || i.OrderNumbers.Any(Matches));
            }
            // Standaard: concepten eerst (oudste eerst, ze wachten), daarna de nieuwste definitieve facturen.
            var standard = items.OrderBy(i => i.Status == SalesInvoiceRules.StatusConcept ? 0 : i.Status == SalesInvoiceRules.StatusFinal ? 1 : 2)
                .ThenBy(i => i.Status == SalesInvoiceRules.StatusConcept ? i.CreatedAt.Ticks : -i.InvoiceDate.Ticks).ThenByDescending(i => i.Id);
            var sorted = order == null ? standard.ToList() : ListSort.Apply(standard, order, ListSortFields.SalesInvoices);
            var result = sorted.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return ServiceResult<SalesInvoiceListDto>.Ok(new SalesInvoiceListDto(result, sorted.Count, page, pageSize));
        }
    }
}
