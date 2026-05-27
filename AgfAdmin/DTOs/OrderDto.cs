namespace AgfAdmin.DTOs
{
    public class OrderDto
    {
        public int Id { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public decimal TotalAmount { get; set; }
        public string? ContainerCode { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public InspectionDto? Inspection { get; set; }
        
        public List<OrderLineDto> Lines { get; set; } = new();
    }

    public class OrderLineDto
    {
        public int Id { get; set; }
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string Origin { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }
}
