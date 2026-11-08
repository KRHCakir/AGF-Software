using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // De ECB-koers bij een inkoopfactuur in een andere valuta (feedback 8 november). De server haalt hem zelf op, voor de factuurdatum
    // (de laatste publicatie op of vóór die dag, hoogstens ExchangeRateRules.MaxAgeDays oud); niemand typt een koers. Zolang er niets
    // op partijen staat, wordt hij bij elk openen van "Kosten verdelen" opnieuw opgehaald (RefreshAsync); daarna ligt hij vast (regel 26),
    // zodat de kosten op een partij nooit vanzelf veranderen. Een nieuwe koers verhoogt de versie van elke regel: wie intussen op de oude
    // koers verdeelde, krijgt een conflict (409) in plaats van een stille omrekening.
    public sealed class InvoiceExchangeRateService
    {
        private readonly AppDbContext _db;
        private readonly IExchangeRateSource _source;
        private readonly IBusinessCalendar _calendar;
        private readonly ICurrentUser _user;

        public InvoiceExchangeRateService(AppDbContext db, IExchangeRateSource source, IBusinessCalendar calendar, ICurrentUser currentUser = null)
        {
            _db = db;
            _source = source;
            _calendar = calendar;
            _user = currentUser ?? CurrentUser.Default;
        }

        // Alleen tonen (bijvoorbeeld in het invoerscherm, vóór het opslaan): de koers voor deze valuta en factuurdatum. Slaat niets op.
        public async Task<ServiceResult<ExchangeRateDto>> QuoteAsync(string currency, DateTime? invoiceDate, CancellationToken cancellationToken = default)
        {
            var code = currency?.Trim().ToUpperInvariant();
            if (!PurchaseInvoiceRules.IsCurrencyCode(code))
            {
                return Invalid<ExchangeRateDto>("Currency must be a three-letter code (for example USD).");
            }
            if (ExchangeRateRules.IsEuro(code))
            {
                return Invalid<ExchangeRateDto>("An invoice in euro needs no exchange rate.");
            }
            var day = ExchangeRateRules.RateDay(PurchaseInvoiceRules.AsUtcDate(invoiceDate), _calendar.Today());
            var fetched = await FetchAsync(code, day, cancellationToken);
            if (fetched.Problem != null)
            {
                return fetched.Unavailable ? ServiceResult<ExchangeRateDto>.Unavailable(fetched.Problem) : Invalid<ExchangeRateDto>(fetched.Problem);
            }
            return ServiceResult<ExchangeRateDto>.Ok(new ExchangeRateDto
            {
                Currency = code, UnitsPerEuro = fetched.Quote.UnitsPerEuro, RateDate = fetched.Quote.RateDate, Source = fetched.Quote.Source, ForDate = day
            });
        }

        // Koers van de factuur bijwerken (alleen een factuur in een andere valuta, zolang er niets op partijen staat) en de verdeling teruggeven.
        public async Task<ServiceResult<InvoiceAllocationsDto>> RefreshAsync(int invoiceId, CancellationToken cancellationToken = default)
        {
            var costs = new PartijCostService(_db, _user);
            var invoice = await _db.IncomingInvoices.Include(i => i.InvoiceLines).SingleOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
            if (invoice == null)
            {
                return ServiceResult<InvoiceAllocationsDto>.NotFound($"Purchase invoice {invoiceId} does not exist.");
            }
            var lineIds = invoice.InvoiceLines.Select(l => l.Id).ToList();
            if (ExchangeRateRules.IsEuro(invoice.Currency) || await _db.PartijCosts.AnyAsync(c => lineIds.Contains(c.IncomingInvoiceLineId), cancellationToken))
            {
                return await costs.GetInvoiceAsync(invoiceId, cancellationToken);
            }
            var code = invoice.Currency.Trim().ToUpperInvariant();
            var day = ExchangeRateRules.RateDay(invoice.InvoiceDate, _calendar.Today());
            var fetched = await FetchAsync(code, day, cancellationToken);
            if (fetched.Problem != null)
            {
                return fetched.Unavailable ? ServiceResult<InvoiceAllocationsDto>.Unavailable(fetched.Problem) : Invalid<InvoiceAllocationsDto>(fetched.Problem);
            }
            var quote = fetched.Quote;
            var rateDate = DateTime.SpecifyKind(quote.RateDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            if (invoice.ExchangeRate == quote.UnitsPerEuro && invoice.ExchangeRateDate == rateDate && invoice.ExchangeRateSource == quote.Source)
            {
                return await costs.GetInvoiceAsync(invoiceId, cancellationToken);
            }
            invoice.ExchangeRate = quote.UnitsPerEuro;
            invoice.ExchangeRateDate = rateDate;
            invoice.ExchangeRateSource = quote.Source;
            invoice.ExchangeRateFetchedAt = DateTime.UtcNow;
            foreach (var line in invoice.InvoiceLines)
            {
                line.AllocationVersion++;
            }
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<InvoiceAllocationsDto>.Conflict("The allocation of this invoice was changed at the same time; reload and try again.");
            }
            catch (TraceabilityViolationException)
            {
                // Regel 26: intussen zijn er kosten op partijen gezet; de koers blijft dan zoals hij was.
                return ServiceResult<InvoiceAllocationsDto>.Conflict("Costs were allocated at the same time; the exchange rate now stays as it is. Reload.");
            }
            return await costs.GetInvoiceAsync(invoiceId, cancellationToken);
        }

        private sealed record Fetched(ExchangeRateQuote Quote, string Problem, bool Unavailable);

        private async Task<Fetched> FetchAsync(string currency, DateOnly day, CancellationToken cancellationToken)
        {
            ExchangeRateQuote quote;
            try
            {
                quote = await _source.LatestAsync(currency, day, cancellationToken);
            }
            catch (ExchangeRateUnavailableException exception)
            {
                return new Fetched(null, $"{exception.Message} Try again in a moment.", true);
            }
            if (quote == null || !ExchangeRateRules.IsCurrent(quote.RateDate, day))
            {
                return new Fetched(null, $"The ECB publishes no current exchange rate for {currency} on {day:yyyy-MM-dd}.", false);
            }
            return new Fetched(quote, null, false);
        }

        private static ServiceResult<T> Invalid<T>(string message) =>
            ServiceResult<T>.Validation(new Dictionary<string, string[]> { ["currency"] = new[] { message } });
    }
}
