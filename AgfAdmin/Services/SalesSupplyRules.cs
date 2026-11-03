namespace AgfAdmin.Services
{
    // Verkopen vóór aankomst: vaste waarden en het voorstel per orderregel. Gespiegeld in agf-frontend/src/lib/salesSupply.ts; beide
    // kanten hebben tests op dezelfde gevallen.
    // - Stock: uit voorraad. Bij aanmaken nooit meer dan er vrij op voorraad is (anders 409); de regel wordt daarna gereserveerd.
    // - Expected: uit verwachte aanvoer. Verkocht vóór de goederen er zijn: een orderregel zonder reservering totdat er goedgekeurde
    //   voorraad is (een exacte pallet reserveren kan alleen op goederen die er zijn). Een tekort is een waarschuwing, geen blokkade.
    public static class SalesSupplyRules
    {
        public const string SourceStock = "Stock";
        public const string SourceExpected = "Expected";

        public static readonly IReadOnlyList<string> Sources = new[] { SourceStock, SourceExpected };

        public static bool IsSource(string value) => value != null && Sources.Contains(value, StringComparer.Ordinal);

        // Eigen verkoopordernummer "VO-2026-0001", zelfde patroon als het inkoopordernummer (PurchaseOrderRules.FormatNumber).
        public const string NumberPrefix = "VO";

        public static string FormatNumber(int year, int sequence) => $"{NumberPrefix}-{year}-{sequence:D4}";

        // Nummering van bestaande verkooporders bij de migratie (AddSalesBeforeArrival): op CreatedAt en dan Id, per kalenderjaar van
        // CreatedAt in Europe/Amsterdam, vanaf 1 zonder gaten. C#-spiegel van de SQL in die migratie.
        public static IEnumerable<(int Id, int Year, int Sequence, string OrderNumber)> InitialNumbers(IEnumerable<(int Id, DateTime CreatedAt)> existing) =>
            existing
                .Select(o => (o.Id, o.CreatedAt, Year: AmsterdamBusinessCalendar.ToAmsterdamDate(o.CreatedAt).Year))
                .GroupBy(o => o.Year)
                .SelectMany(year => year.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id)
                    .Select((o, index) => (o.Id, year.Key, index + 1, FormatNumber(year.Key, index + 1))));

        // Voorstel voor een nieuwe regel: past het aantal in de vrije voorraad, dan helemaal uit voorraad; anders het vrije deel uit voorraad
        // en de rest uit verwachte aanvoer (splitsen). Zonder vrije voorraad alles uit verwachte aanvoer. Nooit een negatief deel.
        public static (decimal Stock, decimal Expected) Propose(decimal quantity, decimal freeStock)
        {
            if (quantity <= 0m)
            {
                return (0m, 0m);
            }
            var stock = Math.Min(quantity, Math.Max(0m, freeStock));
            return (stock, quantity - stock);
        }

        // Bron als de aanvrager er geen opgaf (oudere clients): uit voorraad als het past, anders uit verwachte aanvoer. Nooit stil splitsen:
        // het aantal regels van de order blijft wat de aanvrager stuurde.
        public static string Resolve(string requested, decimal quantity, decimal freeStock) =>
            requested ?? (quantity <= Math.Max(0m, freeStock) ? SourceStock : SourceExpected);
    }
}
