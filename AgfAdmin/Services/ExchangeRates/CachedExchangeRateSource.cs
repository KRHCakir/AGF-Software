using System.Collections.Concurrent;

namespace AgfAdmin.Services
{
    // Valuta-ronde (november 2026): de ECB-koersen worden één keer per dag opgehaald en daarna uit het geheugen gegeven. De sleutel is
    // valuta + gevraagde dag; een gegeven blijft geldig tot het einde van de kalenderdag (Europe/Amsterdam) waarop het is opgehaald. Ook
    // "de ECB heeft hier niets" (null) wordt bewaard; "nu niet bereikbaar" niet, zodat opnieuw proberen meteen kan.
    // Uitzondering: een koers die achterloopt op de gevraagde dag wordt maar kort bewaard (zie CachedExchangeRateSource).
    public sealed class ExchangeRateDayCache
    {
        private readonly ConcurrentDictionary<(string Currency, DateOnly Day), (DateOnly FetchedOn, DateTimeOffset? ExpiresAt, ExchangeRateQuote Quote)> _entries = new();

        // Lopende ophaalacties per sleutel, zodat gelijktijdige missers dezelfde aanvraag delen.
        internal ConcurrentDictionary<(string Currency, DateOnly Day), Task<ExchangeRateQuote>> InFlight { get; } = new();

        public bool TryGet(string currency, DateOnly day, DateOnly today, DateTimeOffset now, out ExchangeRateQuote quote)
        {
            quote = null;
            if (!_entries.TryGetValue((currency, day), out var entry) || entry.FetchedOn != today) return false;
            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value <= now) return false;
            quote = entry.Quote;
            return true;
        }

        // expiresAt = null: geldig tot het einde van de kalenderdag. Items van een eerdere ophaaldag gelden niet meer en gaan er dan uit,
        // zodat de cache niet blijft groeien.
        public void Set(string currency, DateOnly day, DateOnly today, DateTimeOffset? expiresAt, ExchangeRateQuote quote)
        {
            foreach (var stale in _entries.Where(entry => entry.Value.FetchedOn != today).Select(entry => entry.Key).ToList()) _entries.TryRemove(stale, out _);
            _entries[(currency, day)] = (today, expiresAt, quote);
        }

        public int Count => _entries.Count;
    }

    public sealed class CachedExchangeRateSource : IExchangeRateSource
    {
        // De ECB publiceert werkdagen rond 16:00 uur. Een koers die achterloopt op de gevraagde dag (vandaag of later) wordt daarom maar
        // kort bewaard, zodat een ochtendkoers na publicatie wordt ververst. In het weekend kost dat hooguit één aanvraag per kwartier.
        public static readonly TimeSpan LaggingRateLifetime = TimeSpan.FromMinutes(15);

        private readonly IExchangeRateSource _inner;
        private readonly ExchangeRateDayCache _cache;
        private readonly IBusinessCalendar _calendar;
        private readonly TimeProvider _clock;

        public CachedExchangeRateSource(IExchangeRateSource inner, ExchangeRateDayCache cache, IBusinessCalendar calendar, TimeProvider clock = null)
        {
            _inner = inner;
            _cache = cache;
            _calendar = calendar;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<ExchangeRateQuote> LatestAsync(string currency, DateOnly day, CancellationToken cancellationToken)
        {
            var code = currency?.Trim().ToUpperInvariant();
            var today = _calendar.Today();
            if (_cache.TryGet(code, day, today, _clock.GetUtcNow(), out var cached)) return cached;

            // Gelijktijdige missers delen één aanvraag; die loopt los van de annulering van één aanroeper en wordt na afloop (ook bij een
            // fout) weggehaald, zodat opnieuw proberen kan.
            var key = (code, day);
            var completion = new TaskCompletionSource<ExchangeRateQuote>(TaskCreationOptions.RunContinuationsAsynchronously);
            var fetch = _cache.InFlight.GetOrAdd(key, completion.Task);
            if (ReferenceEquals(fetch, completion.Task))
            {
                // Tussen de eerste controle en GetOrAdd kan een andere aanvraag net klaar zijn: dan staat de koers al in de cache.
                if (_cache.TryGet(code, day, today, _clock.GetUtcNow(), out var justCached))
                {
                    _cache.InFlight.TryRemove(key, out _);
                    completion.SetResult(justCached);
                }
                else
                {
                    _ = FetchAsync(code, day, today, key, completion);
                }
            }
            return await fetch.WaitAsync(cancellationToken);
        }

        private async Task FetchAsync(string code, DateOnly day, DateOnly today, (string, DateOnly) key, TaskCompletionSource<ExchangeRateQuote> completion)
        {
            try
            {
                var quote = await _inner.LatestAsync(code, day, CancellationToken.None);
                var lagging = quote != null && day >= today && quote.RateDate < day;
                _cache.Set(code, day, today, lagging ? _clock.GetUtcNow() + LaggingRateLifetime : null, quote);
                _cache.InFlight.TryRemove(key, out _);
                completion.SetResult(quote);
            }
            catch (Exception exception)
            {
                _cache.InFlight.TryRemove(key, out _);
                completion.SetException(exception);
            }
        }
    }
}
