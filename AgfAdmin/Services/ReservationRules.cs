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
        public const string StatusCorrected = "Corrected";

        public const string SourcePhysical = "Physical";
        // Toewijzing van klantgebonden, verwachte aanvoer (DirectTrade): nooit fysieke voorraad, nooit op een inhoudsregel, nooit kg.
        public const string SourceDirectTrade = "DirectTrade";

        // Enige bronnen die de verkoopflow kent; een onbekende of lege bron is nooit geldig (ook als database-check).
        public static readonly IReadOnlyList<string> SalesSources = new[] { SourcePhysical, SourceDirectTrade };

        public static bool IsSalesSource(string value) => value != null && SalesSources.Contains(value, StringComparer.Ordinal);

        // Een toewijzing kent Active, Released en Consumed (verzonden via een DirectTrade-verzending). Corrected volgt met de correctieflow.
        // Corrected: de verzending is administratief teruggenomen (eindtoestand; telt niet als verzonden en niet als bezette capaciteit).
        public static readonly IReadOnlyList<string> DirectTradeStatuses = new[] { StatusActive, StatusReleased, StatusConsumed, StatusCorrected };

        public const string ReservationCreatedEvent = "ReservationCreated";
        public const string DirectTradeAllocatedEvent = "DirectTradeAllocated";
        public const string DirectTradeReleasedEvent = "DirectTradeReleased";
        public const string DirectTradeShippedEvent = "DirectTradeShipped";
        public const string DirectTradeShipmentCorrectedEvent = "DirectTradeShipmentCorrected";
        // Verklaring bij het terugnemen: taalonafhankelijke code en vaste tekst (beide database-check).
        public const string ShipmentDidNotTakePlaceCode = "SHIPMENT_DID_NOT_TAKE_PLACE";
        public const string ShipmentDidNotTakePlaceStatement = "Deze geregistreerde verzending heeft niet plaatsgevonden.";
        public const int MaxLotNumberLength = 100;
        public const string ReservationReleasedEvent = "ReservationReleased";
        public const string OrderCancelledEvent = "SalesOrderCancelled";
        public const string OrderDispatchedEvent = "SalesOrderDispatched";
        public const string ReservationConsumedEvent = "ReservationConsumed";

        public const string MovementTypeDispatch = "Dispatch";
        public const string MovementTypeCorrection = "Correction";
        public const string OrderDispatchCorrectedEvent = "SalesOrderDispatchCorrected";
        public const string ReservationCorrectedEvent = "ReservationCorrected";
        public const int MaxDispatchReferenceLength = 100;
        public const int MaxDispatchLines = 200;

        // Voorraad = ontvangst - afgeboekt; nooit negatief.
        public static decimal OnHandQuantity(decimal receipt, decimal dispatched) => Math.Max(0m, receipt - dispatched);
        public static bool IsDepleted(decimal quantity, decimal netWeightKg, decimal dispatchedQuantity, decimal dispatchedNetWeightKg) =>
            receiptMinus(quantity, dispatchedQuantity) == 0m && receiptMinus(netWeightKg, dispatchedNetWeightKg) == 0m;

        private static decimal receiptMinus(decimal receipt, decimal dispatched) => Math.Max(0m, receipt - dispatched);

        public static readonly IReadOnlyList<string> OrderStatuses = new[] { OrderStatusOpen, OrderStatusCancelled };

        public const int MaxOrderLines = 200;
        public const int MaxListResults = 200;
        public const int MaxCustomerReferenceLength = 100;
        public const int MaxActorLength = 100;
        public const int MaxReasonLength = 500;

        public static bool IsOrderStatus(string value) => value != null && OrderStatuses.Contains(value, StringComparer.Ordinal);
    }
}
