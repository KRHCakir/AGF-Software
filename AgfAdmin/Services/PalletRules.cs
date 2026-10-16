namespace AgfAdmin.Services
{
    // Vaste waarden voor pallets en inhoudsregels; hoofdlettergevoelig en zonder normalisatie.
    public static class PalletRules
    {
        public const string StatusReceived = "Received";
        public const string LocationWarehouse = "Warehouse";
        public const string QualityQuarantine = "Quarantine";

        public static readonly IReadOnlyList<string> LogisticsStatuses = new[]
        {
            "Expected", "InTransit", StatusReceived, "Dispatched", "Delivered", "Cancelled"
        };

        // Een nieuwe pallet start altijd in een van deze statussen; overgangen volgen in een aparte API.
        public static readonly IReadOnlyList<string> InitialLogisticsStatuses = new[] { "Expected", "InTransit", StatusReceived };

        public static readonly IReadOnlyList<string> TerminalLogisticsStatuses = new[] { "Dispatched", "Delivered", "Cancelled" };

        public static readonly IReadOnlyList<string> LocationContexts = new[]
        {
            "Supplier", LocationWarehouse, "Crossdock", "InTransit", "Customer", "External"
        };

        public static readonly IReadOnlyList<string> QualityStatuses = new[] { QualityQuarantine, "Approved", "Blocked", "Rejected" };

        public static bool IsInitialLogisticsStatus(string value) => value != null && InitialLogisticsStatuses.Contains(value, StringComparer.Ordinal);

        public static bool IsTerminalLogisticsStatus(string value) => value != null && TerminalLogisticsStatuses.Contains(value, StringComparer.Ordinal);

        public static bool IsLocationContext(string value) => value != null && LocationContexts.Contains(value, StringComparer.Ordinal);
    }
}
