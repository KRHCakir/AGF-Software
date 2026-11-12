namespace AgfAdmin.Services
{
    // Transport van een verkooporder (documenten deel 2): constanten en grenzen, zonder database.
    public static class SalesTransportRules
    {
        public const string ModeCarrier = "Carrier";      // een vervoerder (relatie met de rol vervoerder)
        public const string ModeOwn = "Own";              // eigen transport of de klant haalt op
        public static readonly IReadOnlyList<string> Modes = new[] { ModeCarrier, ModeOwn };

        public const string DefaultPalletKind = "Europallet";
        public const int MaxPlaceLength = 300;
        public const int MaxReferenceLength = 64;
        public const int MaxRemarksLength = 1000;
        public const int MaxPalletKindLength = 100;
        public const decimal MinTemperatureC = -30m;
        public const decimal MaxTemperatureC = 30m;
    }
}
