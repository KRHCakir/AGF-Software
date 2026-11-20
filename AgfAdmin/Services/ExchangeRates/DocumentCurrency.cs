namespace AgfAdmin.Services
{
    // Valuta en koers van een inkoopdocument (valuta-ronde, november 2026). De aanvraag geeft een valuta (weggelaten = EUR) en voor een andere
    // valuta een bron: ECB (de server haalt de koers zelf op en legt hem vast, ook als de aanvraag een getal meestuurt) of Manual (de koers
    // die de gebruiker invulde of aanpaste). Euro is altijd koers 1 met bron NotApplicable.
    public sealed record DocumentCurrency(string Currency, decimal ExchangeRate, DateOnly? ExchangeRateDate, string ExchangeRateSource)
    {
        public static readonly DocumentCurrency Euro = new(ExchangeRateRules.Euro, 1m, null, ExchangeRateRules.SourceNotApplicable);

        public bool IsEuro => Currency == ExchangeRateRules.Euro;

        // Valuta, bron en (bij Manual) de koers uit de aanvraag, zonder te fetchen. Bij ECB is de koers hier nog 0: FixAsync legt hem vast.
        public static DocumentCurrency FromRequest(string currency, decimal? exchangeRate, string source, Action<string, string> addError)
        {
            var code = string.IsNullOrWhiteSpace(currency) ? ExchangeRateRules.Euro : currency.Trim().ToUpperInvariant();
            if (!PurchaseInvoiceRules.IsCurrencyCode(code))
            {
                addError("currency", "Valuta is een ISO-code van drie letters (bijvoorbeeld USD).");
                return Euro;
            }
            if (code == ExchangeRateRules.Euro) return Euro;
            var kind = string.IsNullOrWhiteSpace(source) ? ExchangeRateRules.SourceEcb : source.Trim();
            if (kind == ExchangeRateRules.SourceManual)
            {
                if (exchangeRate is not decimal rate || !ExchangeRateRules.IsRate(rate))
                {
                    addError("exchangeRate", "Vul een koers in: hoeveel " + code + " is 1 euro (positief, hoogstens zes decimalen).");
                    return new DocumentCurrency(code, 1m, null, ExchangeRateRules.SourceManual);
                }
                return new DocumentCurrency(code, rate, null, ExchangeRateRules.SourceManual);
            }
            if (kind != ExchangeRateRules.SourceEcb)
            {
                addError("exchangeRateSource", "De bron van de koers is ECB of Manual.");
            }
            return new DocumentCurrency(code, 0m, null, ExchangeRateRules.SourceEcb);
        }

        // Dezelfde keuze als het opgeslagen document: dan blijft de vastgelegde koers staan (een latere ECB-koers verandert niets).
        public bool SameChoiceAs(string currency, decimal rate, string source) =>
            Currency == currency && ExchangeRateSource == source && (ExchangeRateSource != ExchangeRateRules.SourceManual || ExchangeRate == rate);
    }

    public sealed record FixedRate(DocumentCurrency Value, string Problem, bool Unavailable);

    public static class DocumentCurrencyFixer
    {
        // Legt de ECB-koers vast voor de dag van het document (nooit later dan vandaag; in het weekend of op een feestdag de laatste
        // werkdag, hoogstens ExchangeRateRules.MaxAgeDays terug). Geen ECB-koers of de ECB onbereikbaar: een melding die vraagt om de koers
        // zelf in te vullen.
        public static async Task<FixedRate> FixAsync(DocumentCurrency choice, DateOnly documentDay, DateOnly today, IExchangeRateSource source, CancellationToken cancellationToken)
        {
            if (choice.IsEuro || choice.ExchangeRateSource != ExchangeRateRules.SourceEcb) return new FixedRate(choice, null, false);
            var day = documentDay > today ? today : documentDay;
            if (source == null)
            {
                return new FixedRate(choice, "De ECB-koers kan nu niet worden opgehaald. Vul de koers zelf in.", true);
            }
            try
            {
                var quote = await source.LatestAsync(choice.Currency, day, cancellationToken);
                if (quote == null || !ExchangeRateRules.IsCurrent(quote.RateDate, day) || !ExchangeRateRules.IsRate(quote.UnitsPerEuro))
                {
                    return new FixedRate(choice, $"De ECB publiceert geen actuele koers voor {choice.Currency}. Vul de koers zelf in.", false);
                }
                return new FixedRate(choice with { ExchangeRate = quote.UnitsPerEuro, ExchangeRateDate = quote.RateDate }, null, false);
            }
            catch (ExchangeRateUnavailableException)
            {
                return new FixedRate(choice, "De ECB is nu niet bereikbaar. Vul de koers zelf in, of probeer het zo opnieuw.", true);
            }
        }
    }
}
