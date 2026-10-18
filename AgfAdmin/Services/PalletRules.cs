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

        public const string StatusExpected = "Expected";
        public const string StatusInTransit = "InTransit";
        public const string StatusDispatched = "Dispatched";
        public const string StatusDelivered = "Delivered";
        public const string StatusCancelled = "Cancelled";
        public const string QualityApproved = "Approved";
        public const string QualityBlocked = "Blocked";
        public const string QualityRejected = "Rejected";

        // Alleen deze overgangen bestaan; er is (nog) geen terugweg of correctie-flow. Delivered en Cancelled zijn eindstatussen.
        private static readonly IReadOnlyDictionary<string, string[]> LogisticsTransitions = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [StatusExpected] = new[] { StatusInTransit, StatusReceived, StatusCancelled },
            [StatusInTransit] = new[] { StatusReceived, StatusDelivered },
            [StatusReceived] = new[] { StatusDispatched },
            [StatusDispatched] = new[] { StatusDelivered },
            [StatusDelivered] = Array.Empty<string>(),
            [StatusCancelled] = Array.Empty<string>()
        };

        // Quarantine is alleen de beginstatus; Blocked wordt alleen expliciet opgeheven; Rejected is definitief.
        // Gedeeltelijke goedkeuring bestaat niet als status: dat wordt een splitsing in aparte inhoudsregels.
        private static readonly IReadOnlyDictionary<string, string[]> QualityTransitions = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [QualityQuarantine] = new[] { QualityApproved, QualityBlocked, QualityRejected },
            [QualityApproved] = new[] { QualityBlocked },
            [QualityBlocked] = new[] { QualityApproved, QualityRejected },
            [QualityRejected] = Array.Empty<string>()
        };

        public static readonly IReadOnlyList<string> DeliveredLocations = new[] { "Customer", "External" };

        // Statussen waarin de kwaliteit niet meer mag wijzigen.
        public static readonly IReadOnlyList<string> QualityLockedLogisticsStatuses = new[] { StatusDelivered, StatusCancelled };

        // Statussen waarin de locatie niet meer mag wijzigen.
        public static readonly IReadOnlyList<string> LocationLockedLogisticsStatuses = new[] { StatusDelivered, StatusCancelled };

        public static bool IsLogisticsStatus(string value) => value != null && LogisticsStatuses.Contains(value, StringComparer.Ordinal);

        public static bool IsQualityStatus(string value) => value != null && QualityStatuses.Contains(value, StringComparer.Ordinal);

        public static bool IsAllowedLogisticsTransition(string from, string to) =>
            from != null && to != null && LogisticsTransitions.TryGetValue(from, out var targets) && targets.Contains(to, StringComparer.Ordinal);

        public static bool IsAllowedQualityTransition(string from, string to) =>
            from != null && to != null && QualityTransitions.TryGetValue(from, out var targets) && targets.Contains(to, StringComparer.Ordinal);

        public static bool LogisticsTransitionRequiresReason(string from, string to) => to == StatusCancelled;

        public static bool QualityTransitionRequiresReason(string from, string to) =>
            to == QualityBlocked || to == QualityRejected || (from == QualityBlocked && to == QualityApproved);

        // Enige bron van waarheid voor latere beschikbaarheid: alleen goedgekeurde inhoud is ooit verkoopbaar.
        public static bool IsSellableQuality(string value) => value == QualityApproved;

        public static bool IsInitialLogisticsStatus(string value) => value != null && InitialLogisticsStatuses.Contains(value, StringComparer.Ordinal);

        public static bool IsTerminalLogisticsStatus(string value) => value != null && TerminalLogisticsStatuses.Contains(value, StringComparer.Ordinal);

        public static bool IsLocationContext(string value) => value != null && LocationContexts.Contains(value, StringComparer.Ordinal);
    }
}
