namespace AgfAdmin.Services
{
    // Definities van het beschikbaarheidsoverzicht. Elke inhoudsregel valt in precies één categorie op basis van
    // palletstatus en locatie; alleen goedgekeurde Warehouse-voorraad in het eigen magazijn is "nu beschikbaar".
    public static class AvailabilityRules
    {
        public const string CategoryWarehouse = "warehouse";
        public const string CategoryReceivedOutsideWarehouse = "receivedOutsideWarehouse";
        public const string CategoryInTransit = "inTransit";
        public const string CategoryExpectedOnPallets = "expectedOnPallets";
        // Dispatched, Delivered of Cancelled: alleen zichtbaar in de drill-down met includeClosed, telt nergens mee.
        public const string CategoryClosed = "closed";

        public const string WarningOverRegistered = "OVER_REGISTERED";
        public const string WarningSourceUnitUnknown = "SOURCE_UNIT_UNKNOWN";
        public const string WarningDirectTradeInWarehouse = "DIRECT_TRADE_IN_WAREHOUSE";

        public static readonly IReadOnlyList<string> ActiveCategories = new[]
        {
            CategoryWarehouse, CategoryReceivedOutsideWarehouse, CategoryInTransit, CategoryExpectedOnPallets
        };

        public static readonly IReadOnlyList<string> AllCategories = ActiveCategories.Append(CategoryClosed).ToArray();

        private static readonly IReadOnlyList<string> OpenCommitmentStatuses = new[] { SupplyRules.StatusExpected, "PartiallyReceived" };

        public static string Classify(string logisticsStatus, string locationContext) => logisticsStatus switch
        {
            PalletRules.StatusExpected => CategoryExpectedOnPallets,
            PalletRules.StatusInTransit => CategoryInTransit,
            PalletRules.StatusReceived => locationContext == PalletRules.LocationWarehouse ? CategoryWarehouse : CategoryReceivedOutsideWarehouse,
            PalletRules.StatusDispatched or PalletRules.StatusDelivered or PalletRules.StatusCancelled => CategoryClosed,
            _ => null
        };

        public static bool IsAvailableNow(string category, string qualityStatus, string route) =>
            category == CategoryWarehouse && PalletRules.IsSellableQuality(qualityStatus) && route == SupplyRules.RouteWarehouse;

        public static bool IsOpenCommitmentStatus(string status) => status != null && OpenCommitmentStatuses.Contains(status, StringComparer.Ordinal);

        // Vaste volgorde van eenheden in antwoorden; een onbekende (legacy) eenheid komt als laatste.
        public static int UnitOrder(string unit)
        {
            var index = SupplyRules.PackageUnits.ToList().IndexOf(unit);
            return index < 0 ? int.MaxValue : index;
        }
    }
}
