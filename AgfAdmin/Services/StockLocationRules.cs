namespace AgfAdmin.Services
{
    // Soorten voorraadlocatie (feedback 22 november). Een pallet of inkooporder zonder locatie ligt in de eigen loods.
    public static class StockLocationRules
    {
        public const string KindOwnWarehouse = "OwnWarehouse";
        public const string KindSupplier = "Supplier";
        public const string KindCrossdock = "Crossdock";
        public const string KindExternalStorage = "ExternalStorage";
        // Onderweg / direct naar klant: ontvangen heet daar Afgeleverd; de voorraad telt mee in Beschikbaarheid met de locatie erbij.
        public const string KindInTransit = "InTransit";

        public static readonly IReadOnlyList<string> Kinds = new[] { KindOwnWarehouse, KindSupplier, KindCrossdock, KindExternalStorage, KindInTransit };

        public const int MaxNameLength = 100;
        public const int MaxAddressLength = 500;

        public static bool IsKind(string value) => value != null && Kinds.Contains(value, StringComparer.Ordinal);
    }
}
