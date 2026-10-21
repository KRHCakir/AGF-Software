namespace AgfAdmin.Services
{
    // Invoer per pallet op aanvoer- en orderregels: "9 pallets × 120 dozen = 1.080 dozen".
    // De totale hoeveelheid in verpakkingseenheden blijft altijd de bron voor vraag-, voorraad- en reserveringscontroles;
    // het aantal pallets en de verpakkingen per pallet worden alleen bewaard zodat de regel herkenbaar blijft.
    // Geldt alleen voor pallets met dezelfde inhoud en verhouding; gemengde pallets blijven via inhoudsregels werken.
    // Een bestelling in pallets maakt nooit fysieke palletrecords aan en resterende hoeveelheden worden nooit naar hele
    // pallets afgerond.
    public static class PackagingRules
    {
        public static decimal Total(decimal palletCount, decimal packagesPerPallet) =>
            decimal.Round(palletCount * packagesPerPallet, 3, MidpointRounding.AwayFromZero);

        // Twee afzonderlijk toegestane waarden kunnen samen buiten decimal (of decimal(18,3)) vallen: dan false, nooit een serverfout.
        public static bool TryTotal(decimal palletCount, decimal packagesPerPallet, out decimal total)
        {
            try
            {
                total = Total(palletCount, packagesPerPallet);
                return SupplyRules.IsPositiveDecimal18_3(total);
            }
            catch (OverflowException)
            {
                total = 0m;
                return false;
            }
        }

        // Beide velden leeg (gewone invoer) of beide positief; met een opgegeven totaal moet dat exact kloppen.
        // Geeft het (eventueel berekende) totaal terug, of null bij fouten.
        public static decimal? Validate(decimal? palletCount, decimal? packagesPerPallet, decimal? quantity, string key, string quantityField,
            Dictionary<string, List<string>> errors, Action<Dictionary<string, List<string>>, string, string> addError)
        {
            var any = palletCount != null || packagesPerPallet != null;
            if (!any)
            {
                return quantity;
            }
            var ok = true;
            if (palletCount == null || !SupplyRules.IsPositiveDecimal18_3(palletCount.Value))
            {
                addError(errors, $"{key}.palletCount", "Pallet count must be positive and may have at most 3 decimals when packages per pallet are given.");
                ok = false;
            }
            if (packagesPerPallet == null || !SupplyRules.IsPositiveDecimal18_3(packagesPerPallet.Value))
            {
                addError(errors, $"{key}.packagesPerPallet", "Packages per pallet must be positive and may have at most 3 decimals when a pallet count is given.");
                ok = false;
            }
            if (!ok)
            {
                return null;
            }
            if (!TryTotal(palletCount!.Value, packagesPerPallet!.Value, out var total))
            {
                addError(errors, $"{key}.{quantityField}", "Pallet count × packages per pallet is out of range (max 999,999,999,999,999.999).");
                return null;
            }
            if (quantity != null && quantity.Value != total)
            {
                addError(errors, $"{key}.{quantityField}", $"The total must equal pallet count × packages per pallet ({palletCount} × {packagesPerPallet} = {total}), or be omitted.");
                return null;
            }
            return total;
        }
    }
}
