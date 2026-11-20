using System.Collections.Concurrent;

namespace AgfAdmin.Services
{
    // Valuta-ronde (november 2026): de ECB-koersen worden één keer per dag opgehaald en daarna uit het geheugen gegeven. De sleutel is
    // valuta + gevraagde dag; een gegeven blijft geldig tot het einde van de kalenderdag (Europe/Amsterdam) waarop het is opgehaald. Ook
    // "de ECB heeft hier niets" (null) wordt bewaard; "nu niet bereikbaar" niet, zodat opnieuw proberen meteen kan.
    public sealed class ExchangeRateDayCache
    {
        private readonly ConcurrentDictionary<(string Currency, DateOnly Day), (DateOnly FetchedOn, ExchangeRateQuote Quote)> _entries = new();

        public bool TryGet(string currency, DateOnly day, DateOnly today, out ExchangeRateQuote quote)
        {
            quote = null;
            if (!_entries.TryGetValue((currency, day), out var entry) || entry.FetchedOn != today) return false;
            quote = entry.Quote;
            return true;
        }

        public void Set(string currency, DateOnly day, DateOnly today, ExchangeRateQuote quote) => _entries[(currency, day)] = (today, quote);
    }

    public sealed class CachedExchangeRateSource : IExchangeRateSource
    {
        private readonly IExchangeRateSource _inner;
        private readonly ExchangeRateDayCache _cache;
        private readonly IBusinessCalendar _calendar;

        public CachedExchangeRateSource(IExchangeRateSource inner, ExchangeRateDayCache cache, IBusinessCalendar calendar)
        {
            _inner = inner;
            _cache = cache;
            _calendar = calendar;
        }

        public async Task<ExchangeRateQuote> LatestAsync(string currency, DateOnly day, CancellationToken cancellationToken)
        {
            var code = currency?.Trim().ToUpperInvariant();
            var today = _calendar.Today();
            if (_cache.TryGet(code, day, today, out var cached)) return cached;
            var quote = await _inner.LatestAsync(code, day, cancellationToken);
            _cache.Set(code, day, today, quote);
            return quote;
        }
    }
}
