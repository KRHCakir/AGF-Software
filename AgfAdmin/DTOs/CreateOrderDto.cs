namespace AgfAdmin.DTOs
{
    public class CreateOrderDto
    {
        public int? CustomerId { get; set; } // Nu optioneel (let op het vraagteken!)
        public string? CustomerName { get; set; } // Nieuw veld!
        
        public string? ContainerCode { get; set; }
        public List<CreateOrderLineDto> Lines { get; set; } = new();
    }

    public class CreateOrderLineDto
    {
        public int ProductVariantId { get; set; }
        public int Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
    }
}