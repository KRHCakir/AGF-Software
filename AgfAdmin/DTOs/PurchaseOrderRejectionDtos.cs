namespace AgfAdmin.DTOs
{
    // Afkeuren van ontvangen inkoop: POST /api/purchase-orders/{id}/rejections (PurchaseOrderService.RejectAsync).
    public sealed class RejectPurchaseOrderDto
    {
        public Guid? RequestId { get; set; }
        public string Reason { get; set; }
        public string Explanation { get; set; }
        public string Decision { get; set; }
        public string ActorReference { get; set; }
        public List<RejectPurchaseOrderLineDto> Lines { get; set; }
    }

    public sealed class RejectPurchaseOrderLineDto
    {
        public int? PalletContentLineId { get; set; }
        public int? ExpectedContentLineVersion { get; set; }
        public decimal? Quantity { get; set; }
        // Optioneel: weggelaten = standaard (hele voorraad: exact de resterende kg; deel: berekend of evenredig).
        public decimal? NetWeightKg { get; set; }
    }
}
