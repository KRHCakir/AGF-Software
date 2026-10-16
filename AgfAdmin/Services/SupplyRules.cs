namespace AgfAdmin.Services
{
    // Vaste waarden en invoerregels voor het beschikbaarheidsdomein. Hoofdlettergevoelig en zonder normalisatie:
    // "Box" of "kg" is geen geldige eenheid, en er wordt nooit een eenheid omgerekend.
    public static class SupplyRules
    {
        public const string RouteWarehouse = "Warehouse";
        public const string RouteDirectTrade = "DirectTrade";

        public const string StatusExpected = "Expected";

        public static readonly IReadOnlyList<string> Routes = new[] { RouteWarehouse, RouteDirectTrade };

        public static readonly IReadOnlyList<string> CommitmentStatuses = new[]
        {
            StatusExpected, "PartiallyReceived", "Received", "Cancelled", "Delivered"
        };

        public static readonly IReadOnlyList<string> PackageUnits = new[] { "box", "colli", "tray", "piece", "dozen" };

        // Grootste waarde die in decimal(18,3) past.
        public const decimal MaxDecimal18_3 = 999_999_999_999_999.999m;

        public static bool IsRoute(string value) => value != null && Routes.Contains(value, StringComparer.Ordinal);

        public static bool IsCommitmentStatus(string value) => value != null && CommitmentStatuses.Contains(value, StringComparer.Ordinal);

        public static bool IsPackageUnit(string value) => value != null && PackageUnits.Contains(value, StringComparer.Ordinal);

        // Positief, hooguit 3 decimalen en binnen decimal(18,3); de waarde wordt nooit afgerond.
        public static bool IsPositiveDecimal18_3(decimal value) =>
            value > 0m && value <= MaxDecimal18_3 && decimal.Round(value, 3) == value;
    }
}
