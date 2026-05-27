using AgfAdmin.Models;

namespace AgfAdmin.Models
{
    public class Inventory
    {
        public int Id { get; set; }
        public string Location { get; set; }
        public int Quantity { get; set; }
        public DateTime Stored { get; set; } = DateTime.UtcNow;
        public int ProductVariantId { get; set; }
        public ProductVariant ProductVariant { get; set; }
        public int? ShipmentId { get; set; }
        public Shipment? Shipment { get; set; }

        public decimal? TotalWeightKg { get; set; }
        public bool IsFrozen { get; set; } = false;
    }
}
