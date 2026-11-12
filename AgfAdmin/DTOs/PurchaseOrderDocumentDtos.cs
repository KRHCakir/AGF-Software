namespace AgfAdmin.DTOs
{
    public sealed class FinalizePurchaseOrderDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedVersion { get; set; }
    }

    // Correctie van een definitieve order: reden verplicht; alleen prijs per regel, ordernummer van de leverancier en notitie.
    public sealed class CorrectPurchaseOrderDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedVersion { get; set; }
        public string Reason { get; set; }
        public string SupplierReference { get; set; }
        public string Note { get; set; }
        public List<CorrectPurchaseOrderLineDto> Lines { get; set; } = new();
    }

    public sealed class CorrectPurchaseOrderLineDto
    {
        public int Id { get; set; }
        public decimal? UnitPrice { get; set; }
    }
}
