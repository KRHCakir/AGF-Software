namespace AgfAdmin.Services
{
    // Rekenregels van een orderregel met prijs, korting en btw (feedback 22 november), gedeeld door de inkooporder en de verkooporder en
    // gespiegeld in agf-frontend/src/lib/orderLineMath.ts. Het bedrag is InvoiceMath.LineNet en de btw InvoiceMath.LineVat (per regel op
    // centen, daarna opgeteld), zodat een factuur die haar regels van de order overneemt op de cent hetzelfde uitkomt.
    public static class OrderLineMath
    {
        public const decimal MaxDiscountPercentage = 100m;

        // Korting in procenten: 0 tot en met 100, hooguit 2 decimalen. Leeg (null) is geen korting.
        public static bool IsDiscountPercentage(decimal value) =>
            value >= 0m && value <= MaxDiscountPercentage && decimal.Round(value, 2) == value;

        // Bedrag excl. btw: aantal × prijs per eenheid min de korting, op centen. Zonder prijs geen bedrag.
        public static decimal? LineAmount(decimal quantity, decimal? unitPrice, decimal? discountPercentage) =>
            unitPrice == null ? null : InvoiceMath.LineNet(quantity, unitPrice.Value, discountPercentage ?? 0m);

        // Prijs, korting en btw van een regel controleren; de sleutels zijn die van het formulier (lines[i].unitPrice enz.).
        public static void Validate(decimal? unitPrice, decimal? discountPercentage, decimal? vatPercentage, string key,
            Dictionary<string, List<string>> errors, Action<Dictionary<string, List<string>>, string, string> addError)
        {
            if (unitPrice != null && !CostArticleRules.IsUnitPrice(unitPrice.Value))
            {
                addError(errors, $"{key}.unitPrice", "Unit price, when given, must be 0 or more with at most 2 decimals.");
            }
            if (discountPercentage != null && !IsDiscountPercentage(discountPercentage.Value))
            {
                addError(errors, $"{key}.discountPercentage", "Discount, when given, must be from 0 to 100 percent with at most 2 decimals.");
            }
            if (vatPercentage != null && !ArticleRules.IsVatRate(vatPercentage.Value))
            {
                addError(errors, $"{key}.vatPercentage", $"VAT must be one of: {string.Join(", ", ArticleRules.VatRates)} percent.");
            }
        }

        public sealed record Line(decimal? Amount, decimal? VatPercentage);

        public sealed record VatRate(decimal Percentage, decimal Net, decimal Vat);

        // Totaal excl. btw (alle regels met een bedrag), btw per tarief, totaal btw en totaal incl. btw. Regels zonder prijs en regels met
        // een bedrag maar zonder btw-tarief (opgeslagen vóór deze ronde) worden apart geteld; zulke regels tellen niet mee in de btw.
        public sealed record Summary(decimal? Net, IReadOnlyList<VatRate> VatRates, decimal? Vat, decimal? Gross, int LinesWithoutPrice, int LinesWithoutVat);

        public static Summary Summarize(IEnumerable<Line> lines)
        {
            var all = lines.ToList();
            var priced = all.Where(l => l.Amount != null).ToList();
            var rates = priced.Where(l => l.VatPercentage != null)
                .GroupBy(l => l.VatPercentage!.Value)
                .OrderBy(g => g.Key)
                .Select(g => new VatRate(g.Key, g.Sum(l => l.Amount!.Value), g.Sum(l => InvoiceMath.LineVat(l.Amount!.Value, g.Key))))
                .ToList();
            decimal? net = priced.Count == 0 ? null : priced.Sum(l => l.Amount!.Value);
            decimal? vat = priced.Count == 0 ? null : rates.Sum(r => r.Vat);
            return new Summary(net, rates, vat, net == null ? null : net + vat, all.Count(l => l.Amount == null), priced.Count(l => l.VatPercentage == null));
        }
    }
}
