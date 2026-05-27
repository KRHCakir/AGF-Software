using System;

namespace AgfAdmin.Models
{
    // Junction table mathematically tracing exactly how many boxes of a specific OrderLine
    // were deducted from which exact batch/shipment inside the overarching Inventory.
    public class OrderLineAllocation
    {
        public int Id { get; set; }
        
        public int OrderLineId { get; set; }
        public OrderLine OrderLine { get; set; }

        public int InventoryId { get; set; }
        public Inventory Inventory { get; set; }

        public int AllocatedQuantity { get; set; }
        
        public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
    }
}
