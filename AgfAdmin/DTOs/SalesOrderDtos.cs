namespace AgfAdmin.DTOs
{
    public sealed class CreateSalesOrderDto
    {
        public Guid? RequestId { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerReference { get; set; }
        public DateTimeOffset? RequestedDeliveryDate { get; set; }
        public string ActorReference { get; set; }
        public List<CreateSalesOrderLineDto> Lines { get; set; }
    }

    public sealed class CreateSalesOrderLineDto
    {
        public int? ProductVariantId { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
    }

    public sealed class SalesOrderFilter
    {
        public int? CustomerId { get; set; }
        public string Status { get; set; }
    }

    public sealed class SalesOrderDto
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Status { get; set; }
        public string CustomerReference { get; set; }
        public DateTime RequestedDeliveryDate { get; set; }
        public string ActorReference { get; set; }
        public DateTime CreatedAt { get; set; }
        public int Version { get; set; }
        // Alleen gevuld bij Status Cancelled; de actor komt uit het SalesOrderCancelled-event.
        public DateTime? CancelledAt { get; set; }
        public string CancelReason { get; set; }
        public string CancelledBy { get; set; }
        public List<SalesOrderLineDto> Lines { get; set; } = new();
    }

    // Annuleren van een Open order: alle Active reserveringen (ook on hold) worden in dezelfde transactie vrijgegeven.
    public sealed class CancelSalesOrderDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedOrderVersion { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    public sealed class SalesOrderAuditEventDto
    {
        public int Id { get; set; }
        public int SalesOrderId { get; set; }
        public string EventType { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Reason { get; set; }
        public string ActorReference { get; set; }
        public DateTime OccurredAt { get; set; }
        public Guid? RequestId { get; set; }
    }

    public sealed class SalesOrderLineDto
    {
        public int Id { get; set; }
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        // Alleen Active verkoopreserveringen; nooit omgerekend.
        public decimal ReservedQuantity { get; set; }
        public decimal ReservedNetWeightKg { get; set; }
        public decimal OpenQuantity { get; set; }
        public int Version { get; set; }
        public List<StockReservationDto> Reservations { get; set; } = new();
    }

    public sealed class StockReservationDto
    {
        public int Id { get; set; }
        public Guid? RequestId { get; set; }
        public int SalesOrderId { get; set; }
        public int SalesOrderLineId { get; set; }
        public int PalletContentLineId { get; set; }
        public int PalletId { get; set; }
        public string PalletReference { get; set; }
        public string WarehouseLocation { get; set; }
        public string LotNumber { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        public string Source { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReleasedAt { get; set; }
        public string ReleaseReason { get; set; }
        public int Version { get; set; }
        // Actuele kwaliteit van de inhoudsregel (nooit afgeleid uit de reserveringsstatus).
        public string ContentQualityStatus { get; set; }
        // Active reservering op inhoud die niet (meer) Approved is: telt niet als beschikbaar en wacht op vrijgave of herkeuring.
        public bool OnHold { get; set; }
    }

    // Inhoudsregel die voor een orderregel te reserveren is (zelfde variant + eenheid, Approved, Received + Warehouse).
    public sealed class ReservableStockLineDto
    {
        public int PalletContentLineId { get; set; }
        public int PalletId { get; set; }
        public string PalletReference { get; set; }
        public string WarehouseLocation { get; set; }
        public string LotNumber { get; set; }
        public string Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal NetWeightKg { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal ReservedNetWeightKg { get; set; }
        public decimal AvailableQuantity { get; set; }
        public decimal AvailableNetWeightKg { get; set; }
        public string QualityStatus { get; set; }
        public int ContentLineVersion { get; set; }
    }

    // Hoeveelheid, eenheid en netto-kg zijn altijd expliciet; er wordt niets afgeleid of omgerekend.
    public sealed class CreateStockReservationDto
    {
        public Guid? RequestId { get; set; }
        public int? PalletContentLineId { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
        public decimal? NetWeightKg { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ReleaseStockReservationDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedReservationVersion { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }
}
