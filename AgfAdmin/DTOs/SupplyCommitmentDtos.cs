namespace AgfAdmin.DTOs
{
    public sealed class CreateSupplyCommitmentDto
    {
        public Guid? RequestId { get; set; }
        public int VendorId { get; set; }
        public int? CustomerId { get; set; }
        public string Route { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public DateTimeOffset? ExpectedAt { get; set; }
        public List<CreateSupplyCommitmentLineDto> Lines { get; set; }
    }

    public sealed class CreateSupplyCommitmentLineDto
    {
        public int ProductVariantId { get; set; }
        public decimal? ExpectedQuantity { get; set; }
        public string ExpectedUnit { get; set; }
        public decimal? ExpectedNetWeightKg { get; set; }
        public string LotNumber { get; set; }
    }

    public sealed class SupplyCommitmentFilter
    {
        public string Route { get; set; }
        public string Status { get; set; }
        public int? VendorId { get; set; }
        public int? CustomerId { get; set; }
    }

    public sealed class SupplyCommitmentDto
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Route { get; set; }
        public string Status { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<SupplyCommitmentLineDto> Lines { get; set; } = new();
    }

    public sealed class SupplyCommitmentLineDto
    {
        public int Id { get; set; }
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public string LotNumber { get; set; }
        public decimal ExpectedQuantity { get; set; }
        public string ExpectedUnit { get; set; }
        // Opgegeven verwacht gewicht van de hele bronregel; nooit afgeleid en nooit het gewicht van een deeltoewijzing.
        public decimal? ExpectedNetWeightKg { get; set; }
        // Alleen bij route DirectTrade: som van Active toewijzingen aan verkooporders en wat daarvan nog toewijsbaar is.
        // Null bij route Warehouse: magazijnaanvoer heeft geen toewijsbare verwachte capaciteit (die wordt pas voorraad na ontvangst en keuring).
        public decimal? DirectTradeAllocatedQuantity { get; set; }
        public decimal? DirectTradeRemainingQuantity { get; set; }
    }
}
