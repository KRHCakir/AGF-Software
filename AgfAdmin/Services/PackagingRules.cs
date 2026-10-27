namespace AgfAdmin.Services
{
    // Invoer per pallet op aanvoer- en orderregels: "3 pallets × 120 colli + 15 losse colli = 375 colli".
    // De totale hoeveelheid in verpakkingseenheden blijft altijd de bron voor vraag-, voorraad- en reserveringscontroles;
    // het aantal pallets, de verpakkingen per pallet en de losse verpakkingen worden alleen bewaard zodat de regel herkenbaar blijft.
    // Geldt alleen voor pallets met dezelfde inhoud en verhouding; gemengde pallets blijven via inhoudsregels werken.
    // Een bestelling in pallets maakt nooit fysieke palletrecords aan en resterende hoeveelheden worden nooit naar hele
    // pallets afgerond.
    public static class PackagingRules
    {
        public static decimal Total(decimal palletCount, decimal packagesPerPallet, decimal? loosePackages = null) =>
            decimal.Round(palletCount * packagesPerPallet + (loosePackages ?? 0m), 3, MidpointRounding.AwayFromZero);

        // Twee afzonderlijk toegestane waarden kunnen samen buiten decimal (of decimal(18,3)) vallen: dan false, nooit een serverfout.
        public static bool TryTotal(decimal palletCount, decimal packagesPerPallet, decimal? loosePackages, out decimal total)
        {
            try
            {
                total = Total(palletCount, packagesPerPallet, loosePackages);
                return SupplyRules.IsPositiveDecimal18_3(total);
            }
            catch (OverflowException)
            {
                total = 0m;
                return false;
            }
        }

        // Heel getal binnen decimal(18,3): pallets en verpakkingen per pallet (> 0) en losse verpakkingen (>= 0).
        public static bool IsWholeNumber(decimal value) => decimal.Truncate(value) == value;

        // Pallets, verpakkingen per pallet en losse verpakkingen leeg (gewone invoer), of pallets en per pallet beide positief met
        // optioneel losse verpakkingen (heel, >= 0); met een opgegeven totaal moet dat exact pallets × per pallet + los zijn.
        // Geeft het (eventueel berekende) totaal terug, of null bij fouten.
        // Hele pallets worden hier niet afgedwongen: dat geldt alleen voor nieuwe invoer (WholePalletErrors), zodat een herhaling van
        // een eerder opgeslagen opdracht met een gebroken palletaantal nog steeds als dezelfde opdracht wordt herkend.
        public static decimal? Validate(decimal? palletCount, decimal? packagesPerPallet, decimal? loosePackages, decimal? quantity, string key, string quantityField,
            Dictionary<string, List<string>> errors, Action<Dictionary<string, List<string>>, string, string> addError)
        {
            var any = palletCount != null || packagesPerPallet != null;
            if (!any)
            {
                if (loosePackages != null)
                {
                    addError(errors, $"{key}.loosePackages", "Loose packages are only allowed together with a pallet count and packages per pallet.");
                    return null;
                }
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
            if (loosePackages != null && (loosePackages.Value < 0m || !IsWholeNumber(loosePackages.Value) || (loosePackages.Value > 0m && !SupplyRules.IsPositiveDecimal18_3(loosePackages.Value))))
            {
                addError(errors, $"{key}.loosePackages", "Loose packages must be a whole number of 0 or more (e.g. 15), or be omitted.");
                ok = false;
            }
            if (!ok)
            {
                return null;
            }
            if (!TryTotal(palletCount!.Value, packagesPerPallet!.Value, loosePackages, out var total))
            {
                addError(errors, $"{key}.{quantityField}", "Pallet count × packages per pallet + loose packages is out of range (max 999,999,999,999,999.999).");
                return null;
            }
            if (quantity != null && quantity.Value != total)
            {
                var loose = loosePackages == null ? "" : $" + {loosePackages}";
                addError(errors, $"{key}.{quantityField}", $"The total must equal pallet count × packages per pallet + loose packages ({palletCount} × {packagesPerPallet}{loose} = {total}), or be omitted.");
                return null;
            }
            return total;
        }

        // Alleen voor nieuwe invoer: hele pallets en een heel aantal verpakkingen per pallet ("3,5 pallets" bestaat in de praktijk niet;
        // de rest gaat als losse verpakkingen). Bestaande regels met een gebroken palletaantal blijven ongewijzigd leesbaar.
        public static void WholePalletErrors(decimal? palletCount, decimal? packagesPerPallet, string key,
            Dictionary<string, List<string>> errors, Action<Dictionary<string, List<string>>, string, string> addError)
        {
            if (palletCount != null && !IsWholeNumber(palletCount.Value))
            {
                addError(errors, $"{key}.palletCount", "Pallet count must be a whole number (e.g. 3, not 3.5); enter the rest as loose packages.");
            }
            if (packagesPerPallet != null && !IsWholeNumber(packagesPerPallet.Value))
            {
                addError(errors, $"{key}.packagesPerPallet", "Packages per pallet must be a whole number (e.g. 120).");
            }
        }
    }
}
