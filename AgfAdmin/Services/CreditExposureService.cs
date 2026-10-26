using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Openstaand krediet van een klant (EUR; legacy facturen en orders hebben geen valuta en gelden als EUR). Alleen signalering:
    // niets in de applicatie wordt hierop geblokkeerd.
    //
    // Wat telt (en hoe "open" is gedefinieerd):
    //   1. openstaande facturen: legacy Invoices van deze klant met PaymentStatus <> 'Paid' (Pending, Open, Overdue, …), TotalAmount
    //      (incl. btw);
    //   2. geleverd maar niet gefactureerd: legacy Orders met Status 'Shipped' zonder factuur (geen Invoice met dit OrderId), waarde =
    //      som(Quantity × UnitPrice) van de orderregels (excl. btw);
    //   3. open orders: legacy Orders met elke andere status dan 'Shipped'/'Invoiced' (Pending, Approved) zonder factuur, zelfde waarde;
    //   4. nieuwe verkooporders (SalesOrders) hebben geen prijzen: Open orders van deze klant worden alleen geteld ("N orders zonder
    //      prijs, niet meegeteld") en maken de uitkomst onvolledig (isComplete = false). Er wordt nooit geschat met DefaultPrice.
    // Een legacy Order met Status 'Invoiced' of met een factuur telt alleen via die factuur (nooit dubbel).
    public sealed class CreditExposureService
    {
        public const string LegacyShipped = "Shipped";
        public const string LegacyInvoiced = "Invoiced";
        public const string InvoicePaid = "Paid";

        private readonly AppDbContext _db;

        public CreditExposureService(AppDbContext db) => _db = db;

        public async Task<CreditExposureDto> GetExposureAsync(int customerId, DateOnly today, CancellationToken cancellationToken = default)
        {
            var customer = await _db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
            if (customer == null)
            {
                return null;
            }
            // Bedragen in het geheugen optellen: SQLite (tests) kan geen decimal-sommen in SQL; de aantallen per klant zijn klein.
            var invoices = await _db.Invoices.AsNoTracking().Where(i => i.CustomerId == customerId && i.PaymentStatus != InvoicePaid)
                .Select(i => i.TotalAmount).ToListAsync(cancellationToken);
            var orders = await _db.Orders.AsNoTracking()
                .Where(o => o.CustomerId == customerId && o.Status != LegacyInvoiced && !_db.Invoices.Any(i => i.OrderId == o.Id))
                .Select(o => new { o.Id, o.Status, Lines = o.Lines.Select(l => new { l.Quantity, l.UnitPrice }).ToList() })
                .ToListAsync(cancellationToken);
            var unpriced = await _db.SalesOrders.AsNoTracking().CountAsync(o => o.CustomerId == customerId && o.Status == ReservationRules.OrderStatusOpen, cancellationToken);

            decimal Value(IEnumerable<(int Quantity, decimal UnitPrice)> lines) => lines.Sum(l => l.Quantity * l.UnitPrice);
            var delivered = orders.Where(o => o.Status == LegacyShipped).ToList();
            var open = orders.Where(o => o.Status != LegacyShipped).ToList();
            var dto = new CreditExposureDto
            {
                CustomerId = customerId,
                AsOf = today,
                OpenInvoicesAmount = invoices.Sum(),
                OpenInvoicesCount = invoices.Count,
                DeliveredNotInvoicedAmount = delivered.Sum(o => Value(o.Lines.Select(l => (l.Quantity, l.UnitPrice)))),
                DeliveredNotInvoicedCount = delivered.Count,
                OpenOrdersAmount = open.Sum(o => Value(o.Lines.Select(l => (l.Quantity, l.UnitPrice)))),
                OpenOrdersCount = open.Count,
                UnpricedSalesOrdersCount = unpriced,
                CreditLimit = customer.CreditLimit,
                IsInsured = customer.IsInsured,
                Insurer = customer.Insurer,
                InsuredLimit = customer.InsuredLimit,
                InsuredUntil = customer.InsuredUntil,
                InsuranceExpired = customer.IsInsured && RoleTermsRules.InsuranceExpired(customer.InsuredUntil, today),
                InsuranceExpiresWithin30Days = customer.IsInsured && RoleTermsRules.InsuranceExpiresSoon(customer.InsuredUntil, today),
                InsuranceGap = RoleTermsRules.InsuranceGap(customer.CreditLimit, customer.IsInsured, customer.InsuredLimit)
            };
            dto.TotalOutstanding = dto.OpenInvoicesAmount + dto.DeliveredNotInvoicedAmount + dto.OpenOrdersAmount;
            dto.OverLimitAmount = dto.CreditLimit != null && dto.TotalOutstanding > dto.CreditLimit ? dto.TotalOutstanding - dto.CreditLimit.Value : 0m;
            dto.IsOverLimit = dto.OverLimitAmount > 0m;
            dto.IsComplete = unpriced == 0;
            dto.UnpricedNote = unpriced == 0 ? null : unpriced == 1 ? "1 order zonder prijs, niet meegeteld" : $"{unpriced} orders zonder prijs, niet meegeteld";
            return dto;
        }

        // Bedrag in meldingen: altijd Nederlandse notatie (4.000,00), onafhankelijk van de servercultuur.
        public static string Amount(decimal value) => value.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("nl-NL"));

        // Niet-blokkerende waarschuwing voor een verkooporder: alleen als er een limiet is en het openstaande bedrag erboven ligt.
        public static SalesOrderCreditWarningDto WarningFor(CreditExposureDto exposure) =>
            exposure is not { IsOverLimit: true, CreditLimit: not null } ? null : new SalesOrderCreditWarningDto
            {
                CreditLimit = exposure.CreditLimit.Value,
                Outstanding = exposure.TotalOutstanding,
                Difference = exposure.OverLimitAmount,
                IsComplete = exposure.IsComplete,
                UnpricedSalesOrdersCount = exposure.UnpricedSalesOrdersCount,
                Message = $"Kredietlimiet overschreden: openstaand EUR {Amount(exposure.TotalOutstanding)} bij een limiet van EUR {Amount(exposure.CreditLimit.Value)} (EUR {Amount(exposure.OverLimitAmount)} erboven)."
                    + (exposure.IsComplete ? "" : $" {exposure.UnpricedNote}.")
            };
    }
}
