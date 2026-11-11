namespace AgfAdmin.Services
{
    // Rekenregels van de inkooporderregel (feedback 11 november): het regeltotaal en de pallets per regel en over de order. Gespiegeld in
    // de frontend (lib/purchaseLine.ts); de server blijft leidend. Er wordt niets opgeslagen of afgerond in de database: alles volgt uit
    // de regel (aantal, kg, prijs) en de colli per pallet.
    public static class PurchaseLineMath
    {
        // Aantal × prijs per verpakking, op centen (half van nul af). Zonder prijs geen totaal.
        public static decimal? LineTotal(decimal quantity, decimal? unitPrice) =>
            unitPrice == null ? null : Math.Round(quantity * unitPrice.Value, 2, MidpointRounding.AwayFromZero);

        // Colli per pallet van een regel: de eigen palletinvoer van een oudere regel ("3 pallets × 120"), anders die van het artikel.
        public static decimal? PackagesPerPallet(decimal? linePackagesPerPallet, decimal? articleDefault) =>
            linePackagesPerPallet is > 0m ? linePackagesPerPallet : articleDefault is > 0m ? articleDefault : null;

        // Pallets van een regel, niet afgerond (100 colli bij 252 per pallet = 0,3968…). Zonder colli per pallet: null (telt niet mee).
        public static decimal? Pallets(decimal quantity, decimal? packagesPerPallet) =>
            packagesPerPallet is > 0m ? quantity / packagesPerPallet.Value : null;

        // Twee decimalen voor de weergave, nooit naar hele pallets (2,08 blijft 2,08; afgesproken met de eigenaar).
        public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

        public sealed record Line(decimal Quantity, decimal? NetKg, decimal? GrossKg, decimal? Total, decimal? Pallets);

        public sealed record Totals(decimal Quantity, decimal? NetKg, decimal? GrossKg, decimal? Amount, decimal? Pallets,
            int LinesWithoutNet, int LinesWithoutGross, int LinesWithoutPrice, int LinesWithoutPallets);

        // Totalen onder de regels. Kg, bedrag en pallets tellen alleen de regels die ze hebben; het aantal regels zonder wordt apart
        // genoemd, zodat een totaal nooit stilzwijgend onvolledig is. Pallets: de som van de niet-afgeronde regels, daarna op twee decimalen.
        public static Totals Sum(IReadOnlyCollection<Line> lines)
        {
            decimal? Sum(Func<Line, decimal?> value) => lines.Any(l => value(l) != null) ? lines.Sum(l => value(l) ?? 0m) : null;
            var pallets = Sum(l => l.Pallets);
            return new Totals(lines.Sum(l => l.Quantity), Sum(l => l.NetKg), Sum(l => l.GrossKg), Sum(l => l.Total), pallets == null ? null : Round2(pallets.Value),
                lines.Count(l => l.NetKg == null), lines.Count(l => l.GrossKg == null), lines.Count(l => l.Total == null), lines.Count(l => l.Pallets == null));
        }
    }
}
