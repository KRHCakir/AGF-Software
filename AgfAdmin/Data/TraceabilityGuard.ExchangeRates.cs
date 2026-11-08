using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private sealed record StoredInvoiceRate(int Id, string Currency, decimal? ExchangeRate, DateTime? ExchangeRateDate, string ExchangeRateSource);

        // ---- Regel 26: koers van een factuur in een andere valuta (zie InvoiceExchangeRateService) ----
        //
        // Kosten op partijen staan in euro (regel 25). Bij een factuur in een andere valuta is de ECB-koers van de factuur daarom de basis:
        // 1. een nieuwe of gewijzigde kostenregel op een regel van een factuur in een andere valuta kan alleen als die factuur een koers heeft;
        // 2. valuta en koers (koers, datum, bron) van een factuur met kosten op partijen veranderen nooit, zodat het eurobedrag op een
        //    partij nooit vanzelf verschuift. Vergeleken met de opgeslagen waarden in de database, niet met OriginalValues.
        // Regel 25 vergelijkt de som per regel met het regelbedrag in euro (ExchangeRateRules.LineAmountInEuro): bij een factuur in euro
        // is dat precies het regelbedrag, zoals voorheen.
        private static async Task CheckExchangeRatesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var invoices = db.ChangeTracker.Entries<IncomingInvoice>().Where(e => e.State == EntityState.Modified).ToList();
            if (invoices.Count > 0)
            {
                var ids = invoices.Select(e => e.Entity.Id).ToList();
                var stored = (await ToListAsync(db.IncomingInvoices.AsNoTracking().Where(i => ids.Contains(i.Id))
                        .Select(i => new StoredInvoiceRate(i.Id, i.Currency, i.ExchangeRate, i.ExchangeRateDate, i.ExchangeRateSource)), async, cancellationToken))
                    .ToDictionary(i => i.Id);
                var changed = invoices.Where(e => stored.TryGetValue(e.Entity.Id, out var before)
                        && (!string.Equals(Normalized(before.Currency), Normalized(e.Entity.Currency), StringComparison.Ordinal)
                            || before.ExchangeRate != e.Entity.ExchangeRate || before.ExchangeRateDate != e.Entity.ExchangeRateDate
                            || !string.Equals(before.ExchangeRateSource, e.Entity.ExchangeRateSource, StringComparison.Ordinal)))
                    .Select(e => e.Entity.Id).ToList();
                if (changed.Count > 0 && await AnyAsync(db.PartijCosts.AsNoTracking().Where(c => changed.Contains(c.IncomingInvoiceLine.IncomingInvoiceId)), async, cancellationToken))
                {
                    throw new TraceabilityViolationException("The currency and exchange rate of an invoice with costs on lots never change.");
                }
            }

            var costs = db.ChangeTracker.Entries<PartijCost>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList();
            if (costs.Count == 0)
            {
                return;
            }
            var lineIds = costs.Select(e => e.Property(c => c.IncomingInvoiceLineId).CurrentValue).Distinct().ToList();
            var lines = await ToListAsync(db.IncomingInvoiceLines.AsNoTracking().Where(l => lineIds.Contains(l.Id))
                .Select(l => new { l.Id, l.IncomingInvoiceId, l.IncomingInvoice.Currency, l.IncomingInvoice.ExchangeRate }), async, cancellationToken);
            var tracked = db.ChangeTracker.Entries<IncomingInvoice>().Where(e => e.State != EntityState.Detached).ToDictionary(e => e.Entity.Id, e => e.Entity);
            foreach (var line in lines)
            {
                var currency = tracked.TryGetValue(line.IncomingInvoiceId, out var invoice) ? invoice.Currency : line.Currency;
                var rate = invoice != null ? invoice.ExchangeRate : line.ExchangeRate;
                if (!ExchangeRateRules.IsEuro(currency) && rate is not > 0m)
                {
                    throw new TraceabilityViolationException($"A cost on a lot from invoice line {line.Id} in {currency} needs the exchange rate of its invoice.");
                }
            }
        }

        private static string Normalized(string currency) => ExchangeRateRules.IsEuro(currency) ? ExchangeRateRules.Euro : currency.Trim().ToUpperInvariant();
    }
}
