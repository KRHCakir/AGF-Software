namespace AgfAdmin.Services
{
    // Vaste waarden en invoerregels voor het beschikbaarheidsdomein. Hoofdlettergevoelig en zonder normalisatie:
    // "Box" of "kg" is geen geldige eenheid, en er wordt nooit een eenheid omgerekend.
    public static class SupplyRules
    {
        // Herkomst van een order afgeleid van haar regels (de herkomst van de artikelen): één land als alle regels hetzelfde hebben,
        // anders de unieke herkomsten gesorteerd ("Chili, Peru"); null zonder herkomst op de regels. Het kopveld Origin is daarnaast
        // alleen een optioneel bijschrift.
        public static string LinesOrigin(IEnumerable<string> lineOrigins)
        {
            var origins = lineOrigins.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(o => o, StringComparer.Create(new System.Globalization.CultureInfo("nl-NL"), true)).ToList();
            return origins.Count == 0 ? null : string.Join(", ", origins);
        }

        public const string RouteWarehouse = "Warehouse";
        public const string RouteDirectTrade = "DirectTrade";

        public const string StatusExpected = "Expected";
        public const string StatusCancelled = "Cancelled";
        public const string StatusReceived = "Received";
        public const string StatusDelivered = "Delivered";
        // Alle ontvangen inhoud is afgekeurd en niets is goedgekeurd of verkocht: telt niet meer als verwacht (PurchaseOrderService).
        public const string StatusRejected = "Rejected";

        public static readonly IReadOnlyList<string> Routes = new[] { RouteWarehouse, RouteDirectTrade };

        public static readonly IReadOnlyList<string> CommitmentStatuses = new[]
        {
            StatusExpected, "PartiallyReceived", StatusReceived, StatusCancelled, StatusDelivered, StatusRejected
        };

        // Expliciete toelatingslijst: alleen in deze statussen kan DirectTrade-aanvoer aan een verkooporder worden toegewezen.
        // Received, Delivered, Cancelled en elke onbekende waarde zijn nooit toewijsbaar (geen "alles behalve"-regel).
        public const string StatusPartiallyReceived = "PartiallyReceived";
        public static readonly IReadOnlyList<string> AllocatableCommitmentStatuses = new[] { StatusExpected, StatusPartiallyReceived };

        public static readonly IReadOnlyList<string> PackageUnits = new[] { "box", "colli", "tray", "piece", "dozen" };

        // Grootste waarde die in decimal(18,3) past.
        public const decimal MaxDecimal18_3 = 999_999_999_999_999.999m;

        public static bool IsRoute(string value) => value != null && Routes.Contains(value, StringComparer.Ordinal);

        public static bool IsCommitmentStatus(string value) => value != null && CommitmentStatuses.Contains(value, StringComparer.Ordinal);

        public static bool IsAllocatableCommitmentStatus(string value) => value != null && AllocatableCommitmentStatuses.Contains(value, StringComparer.Ordinal);

        public static bool IsPackageUnit(string value) => value != null && PackageUnits.Contains(value, StringComparer.Ordinal);

        // Positief, hooguit 3 decimalen en binnen decimal(18,3); de waarde wordt nooit afgerond.
        public static bool IsPositiveDecimal18_3(decimal value) =>
            value > 0m && value <= MaxDecimal18_3 && decimal.Round(value, 3) == value;
    }
}
