namespace AgfAdmin.Services
{
    // Vaste waarden van de verkoop- en reserveringsflow (hoofdlettergevoelig, gelijk aan de database-checks).
    public static class ReservationRules
    {
        public const string OrderStatusOpen = "Open";
        public const string OrderStatusCancelled = "Cancelled";

        public const string StatusActive = "Active";
        public const string StatusReleased = "Released";
        public const string StatusConsumed = "Consumed";

        public const string SourcePhysical = "Physical";

        public const string ReservationCreatedEvent = "ReservationCreated";
        public const string ReservationReleasedEvent = "ReservationReleased";

        public static readonly IReadOnlyList<string> OrderStatuses = new[] { OrderStatusOpen, OrderStatusCancelled };

        public const int MaxOrderLines = 200;
        public const int MaxListResults = 200;
        public const int MaxCustomerReferenceLength = 100;
        public const int MaxActorLength = 100;
        public const int MaxReasonLength = 500;

        public static bool IsOrderStatus(string value) => value != null && OrderStatuses.Contains(value, StringComparer.Ordinal);
    }
}
