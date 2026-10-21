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
        // Alleen aanroepen als het product gegarandeerd binnen decimal(18,3) valt (zie TryCalculatedTotal).
        public static decimal CalculatedTotal(decimal quantity, decimal netKgPerPackage) =>
            decimal.Round(quantity * netKgPerPackage, 3, MidpointRounding.AwayFromZero);

        // Twee afzonderlijk toegestane waarden kunnen samen buiten decimal (of decimal(18,3)) vallen: dan false, nooit een
        // serverfout.
        public static bool TryCalculatedTotal(decimal quantity, decimal netKgPerPackage, out decimal total)
        {
            try
            {
                total = CalculatedTotal(quantity, netKgPerPackage);
                return SupplyRules.IsPositiveDecimal18_3(total);
            }
            catch (OverflowException)
            {
                total = 0m;
                return false;
            }
        }

        // Afrondingsregel voor reserveringen op een berekende regel: een deelreservering weegt round(aantal × kg per verpakking);
        // wie de volledige rest neemt, neemt exact de resterende kg. Zo tellen deelreserveringen altijd op tot het regeltotaal en
        // raakt er nooit een rest onverkoopbaar door afronding (bijvoorbeeld 3 × 1,001 kg: 1,5 → 1,502 kg, rest 1,5 → 1,501 kg).
        public static bool TryExpectedReservationKg(decimal quantity, decimal netKgPerPackage, decimal remainingQuantity, decimal remainingKg, out decimal expected)
        {
            if (quantity == remainingQuantity)
            {
                expected = remainingKg;
                return true;
            }
            return TryCalculatedTotal(quantity, netKgPerPackage, out expected);
        }
    }
}
