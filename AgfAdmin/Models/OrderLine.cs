using System;

namespace AgfAdmin.Models
{
    public class OrderLine
    {
        public int Id { get; set; }
        
        public int OrderId { get; set; }
        public Order Order { get; set; } = null!;

        public int ProductVariantId { get; set; }
        public ProductVariant ProductVariant { get; set; } = null!;

        public int Quantity { get; set; }
        
        public decimal UnitPrice { get; set; }
        
        // Optional tracking
        public decimal LineTotal => Quantity * UnitPrice;

        public List<OrderLineAllocation> Allocations { get; set; } = new List<OrderLineAllocation>();
    }
}
