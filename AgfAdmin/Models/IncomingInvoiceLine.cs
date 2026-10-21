using System.ComponentModel.DataAnnotations.Schema;

namespace AgfAdmin.Models
{
    public class IncomingInvoiceLine
    {
        public int Id { get; set; }
        
        public int IncomingInvoiceId { get; set; }
        public IncomingInvoice IncomingInvoice { get; set; }

        // Optionele koppeling aan een artikel (variant); de omschrijving blijft altijd op de regel bewaard.
        public int? ProductVariantId { get; set; }
        public ProductVariant? ProductVariant { get; set; }

        public string? Description { get; set; }
        public string? Unit { get; set; }           // box, colli, tray, piece, dozen of leeg; nooit omgerekend

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountPercentage { get; set; } // standaard 0
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal LineTotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal VATPercentage { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal VATAmount { get; set; }
    }
}
