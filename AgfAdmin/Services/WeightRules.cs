namespace AgfAdmin.Services
{
    // Gewichtsmethode van een productvariant of inhoudsregel.
    // - Weighed: het totale netto gewicht is werkelijk gewogen en expliciet ingevoerd; nooit afgeleid.
    // - Calculated: het totale netto gewicht volgt uit aantal verpakkingen × netto kg per verpakking (etiket).
    // Bestaande regels van vóór deze keuze zijn Weighed, zodat historische gewichten hun betekenis houden.
    // Een berekend gewicht wordt nooit als gewogen gepresenteerd; de methode reist mee met de regel.
    public static class WeightRules
    {
        public const string Weighed = "Weighed";
        public const string Calculated = "Calculated";
        public static readonly string[] Methods = { Weighed, Calculated };

        public static bool IsWeightMethod(string value) => value == Weighed || value == Calculated;

        // Exact op 3 decimalen, zoals de kolommen; dezelfde afronding als de frontend (lib/weight.ts).
        public static decimal CalculatedTotal(decimal quantity, decimal netKgPerPackage) =>
            decimal.Round(quantity * netKgPerPackage, 3, MidpointRounding.AwayFromZero);
    }
}
