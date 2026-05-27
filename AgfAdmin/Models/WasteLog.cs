using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgfAdmin.Models
{
    public class WasteLog
    {
        public int Id { get; set; }
        
        public int InventoryId { get; set; }
        public Inventory Inventory { get; set; }

        public int ProductVariantId { get; set; }
        public ProductVariant ProductVariant { get; set; }

        public int QuantityDumped { get; set; }
        public decimal? WeightDumpedKg { get; set; }
        
        public string Reason { get; set; }
        public string LoggedBy { get; set; } = "Admin";
        public DateTime DumpedAt { get; set; } = DateTime.UtcNow;
    }
}
