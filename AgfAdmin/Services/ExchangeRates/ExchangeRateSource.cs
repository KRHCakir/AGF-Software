using System.Globalization;
using System.Net;

namespace AgfAdmin.Services
{
    // Een referentiekoers: eenheden valuta per 1 euro op een publicatiedag, en van welke bron.
    public sealed record ExchangeRateQuote(string Currency, decimal UnitsPerEuro, DateOnly RateDate, string Source);

    public interface IExchangeRateSource
    {
        // De laatste referentiekoers op of vóór `day`; null als de bron voor deze valuta niets heeft. Gooit
        // ExchangeRateUnavailableException als de bron nu niet bereikbaar is (opnieuw proberen kan).
        Task<ExchangeRateQuote> LatestAsync(string currency, DateOnly day, CancellationToken cancellationToken);
    }

    public sealed class ExchangeRateUnavailableException : Exception
    {
        public ExchangeRateUnavailableException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    // De ECB-referentiekoersen via de openbare data-API (geen sleutel nodig): https://data-api.ecb.europa.eu, reeks EXR.D.{valuta}.EUR.SP00.A,
    // de laatste waarneming tot en met de gevraagde dag. Werkdagen rond 16:00 uur gepubliceerd. Een onbekende reeks geeft 404 (null).
    public sealed class EcbExchangeRateSource : IExchangeRateSource
    {
        public const string BaseUrl = "https://data-api.ecb.europa.eu/service/data/EXR/";
        private readonly HttpClient _http;

        public EcbExchangeRateSource(HttpClient http)
        {
            _http = http;
        }

        public async Task<ExchangeRateQuote> LatestAsync(string currency, DateOnly day, CancellationToken cancellationToken)
        {
            if (!PurchaseInvoiceRules.IsCurrencyCode(currency) || ExchangeRateRules.IsEuro(currency))
            {
                return null;
            }
            var url = $"{BaseUrl}D.{currency}.EUR.SP00.A?endPeriod={day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&lastNObservations=1&format=csvdata";
            HttpResponseMessage response;
            try
            {
                response = await _http.GetAsync(url, cancellationToken);
            }
            catch (HttpRequestException exception)
            {
                throw new ExchangeRateUnavailableException("The ECB exchange rates could not be reached.", exception);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ExchangeRateUnavailableException("The ECB exchange rates did not answer in time.", exception);
            }
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
                if (!response.IsSuccessStatusCode)
                {
                    throw new ExchangeRateUnavailableException($"The ECB exchange rates answered {(int)response.StatusCode}.");
                }
                return Parse(await response.Content.ReadAsStringAsync(cancellationToken), currency);
            }
        }

        // csvdata van de ECB: een kopregel met o.a. TIME_PERIOD en OBS_VALUE, daarna één regel per waarneming (velden met komma's
        // staan tussen aanhalingstekens). De laatste geldige waarneming telt; geen geldige regel = null.
        public static ExchangeRateQuote Parse(string csv, string currency)
        {
            var lines = (csv ?? "").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
            if (lines.Count < 2)
            {
                return null;
            }
            var header = SplitCsv(lines[0]);
            var dateColumn = header.IndexOf("TIME_PERIOD");
            var valueColumn = header.IndexOf("OBS_VALUE");
            if (dateColumn < 0 || valueColumn < 0)
            {
                return null;
            }
            ExchangeRateQuote quote = null;
            foreach (var fields in lines.Skip(1).Select(SplitCsv))
            {
                if (fields.Count <= Math.Max(dateColumn, valueColumn)
                    || !DateOnly.TryParseExact(fields[dateColumn], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    || !decimal.TryParse(fields[valueColumn], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                    || !ExchangeRateRules.IsRate(value))
                {
                    continue;
                }
                if (quote == null || date >= quote.RateDate)
                {
                    quote = new ExchangeRateQuote(currency, value, date, ExchangeRateRules.SourceEcb);
                }
            }
            return quote;
        }

        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var current = new System.Text.StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else if (c == '"')
                    {
                        quoted = false;
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            fields.Add(current.ToString());
            return fields;
        }
    }

    // Koersen ophalen staat uit (ExchangeRates:Source = Off, bijvoorbeeld een afgeschermde testomgeving): altijd "niet bereikbaar".
    public sealed class DisabledExchangeRateSource : IExchangeRateSource
    {
        public Task<ExchangeRateQuote> LatestAsync(string currency, DateOnly day, CancellationToken cancellationToken) =>
            throw new ExchangeRateUnavailableException("Fetching exchange rates is switched off (ExchangeRates:Source = Off).");
    }
}
