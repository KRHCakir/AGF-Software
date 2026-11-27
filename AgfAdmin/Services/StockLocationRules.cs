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

        // Onderweg is er altijd (november 2026): bij het opstarten komt er een actieve InTransit-locatie met deze naam als er geen is, en de
        // laatste actieve InTransit-locatie blijft actief en Onderweg (guard-regel 47).
        public const string StandardInTransitName = "Onderweg / direct naar klant";
        public const string SystemActor = "systeem";

        public static bool IsKind(string value) => value != null && Kinds.Contains(value, StringComparer.Ordinal);
    }
}
