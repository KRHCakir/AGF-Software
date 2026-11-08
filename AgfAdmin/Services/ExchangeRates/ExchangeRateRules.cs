namespace AgfAdmin.Services
{
    // Koersen voor facturen in een andere valuta dan EUR (feedback 8 november): kosten op partijen staan altijd in euro, dus een
    // factuurregel in USD wordt omgerekend. De koers wordt nooit getypt maar door de server bij de ECB opgehaald (de officiële
    // referentiekoers, ook wat de boekhouding gebruikt), voor de factuurdatum. Zo kan niemand een verkeerde of verouderde koers invullen.
    // Gespiegeld in agf-frontend/src/lib/exchangeRate.ts.
    public static class ExchangeRateRules
    {
        public const string Euro = "EUR";
        public const string SourceEcb = "ECB";
        public const int MaxSourceLength = 20;

        // Een koers mag hoogstens zoveel dagen vóór de factuurdatum liggen: de ECB publiceert niet in het weekend en op feestdagen.
        // Ouder is geen actuele koers; voor sommige valuta's (bijvoorbeeld MAD) geeft de ECB alleen nog een oude indicatieve koers.
        public const int MaxAgeDays = 10;

        public static bool IsEuro(string currency) => string.IsNullOrWhiteSpace(currency) || string.Equals(currency.Trim(), Euro, StringComparison.OrdinalIgnoreCase);

        // Koers in eenheden valuta per 1 euro (zoals de ECB hem geeft): positief, hoogstens zes decimalen, binnen decimal(18,6).
        public static bool IsRate(decimal unitsPerEuro) => unitsPerEuro > 0m && decimal.Round(unitsPerEuro, 6) == unitsPerEuro && unitsPerEuro < 1_000_000_000_000m;

        // Bedrag in euro: bedrag in valuta gedeeld door de koers, afgerond op centen (half van nul af).
        public static decimal ToEuro(decimal amount, decimal unitsPerEuro) => decimal.Round(amount / unitsPerEuro, 2, MidpointRounding.AwayFromZero);

        // Het deel van de regel in de valuta van de factuur, alleen om het oorspronkelijke bedrag naast het eurobedrag te tonen: naar
        // verhouding van de regel (zo is de hele regel precies het regelbedrag, zonder afrondingscent heen en terug).
        public static decimal InCurrency(decimal euro, decimal lineTotal, decimal lineEuro) =>
            lineEuro <= 0m ? 0m : decimal.Round(lineTotal * euro / lineEuro, 2, MidpointRounding.AwayFromZero);

        // Het regelbedrag in euro, de grens voor kosten op partijen (regel 25): bij EUR het bedrag zelf, anders omgerekend met de koers
        // van de factuur; zonder koers null (dan kan er niets op partijen).
        public static decimal? LineAmountInEuro(decimal lineTotal, string currency, decimal? unitsPerEuro) =>
            IsEuro(currency) ? lineTotal : unitsPerEuro is > 0m ? ToEuro(lineTotal, unitsPerEuro.Value) : null;

        // De dag waarvoor de koers geldt: de factuurdatum, nooit later dan vandaag (voor een factuur van morgen is er nog geen koers).
        public static DateOnly RateDay(DateTime? invoiceDate, DateOnly today)
        {
            var day = invoiceDate == null ? today : DateOnly.FromDateTime(invoiceDate.Value);
            return day > today ? today : day;
        }

        public static bool IsCurrent(DateOnly rateDate, DateOnly day) => rateDate <= day && rateDate >= day.AddDays(-MaxAgeDays);
    }
}
