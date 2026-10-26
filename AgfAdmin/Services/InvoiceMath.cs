namespace AgfAdmin.Services
{
    // Eén geldberekening voor inkoopfacturen, gespiegeld in agf-frontend/src/lib/invoiceMath.ts, zodat scherm, server en
    // opgeslagen totalen nooit verschillen. Alle bedragen op 2 decimalen (half omhoog), per regel afgerond en daarna opgeteld.
    public static class InvoiceMath
    {
        public static decimal Round2(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

        // Grootste bedrag dat in de kolommen decimal(18,2) past (regelbedrag, btw-bedrag, documenttotalen). Een bedrag
        // daarbuiten is een validatiefout, nooit een databasefout; gespiegeld als MAX_AMOUNT in invoiceMath.ts.
        public const decimal MaxAmount = 9_999_999_999_999_999.99m;

        public static bool FitsAmount(decimal value) => value >= -MaxAmount && value <= MaxAmount;

        // Regelbedrag exclusief btw: aantal × prijs per eenheid, minus korting in procenten.
        public static decimal LineNet(decimal quantity, decimal unitPrice, decimal discountPercentage) =>
            Round2(quantity * unitPrice * (1m - discountPercentage / 100m));

        public static decimal LineVat(decimal lineNet, decimal vatPercentage) => Round2(lineNet * vatPercentage / 100m);

        // Documenttotalen: som van de afgeronde regelbedragen; nooit opnieuw afgerond vanuit een ruw totaal.
        public static (decimal Net, decimal Vat, decimal Gross) Totals(IEnumerable<(decimal Net, decimal Vat)> lines)
        {
            decimal net = 0m, vat = 0m;
            foreach (var line in lines)
            {
                net += line.Net;
                vat += line.Vat;
            }
            return (net, vat, net + vat);
        }

        // Gemiddeld bedrag over een reeks bedragen, op 2 decimalen. Een lege reeks heeft geen gemiddelde.
        public static decimal AverageAmount(IReadOnlyCollection<decimal> amounts)
        {
            if (amounts.Count == 0)
                throw new ArgumentException("Kan geen gemiddelde berekenen over een lege reeks bedragen.", nameof(amounts));
            return Round2(amounts.Sum() / amounts.Count);
        }
    }
}
