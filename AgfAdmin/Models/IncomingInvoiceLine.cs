using System.ComponentModel.DataAnnotations.Schema;

namespace AgfAdmin.Models
{
    public class IncomingInvoiceLine
    {
        public int Id { get; set; }
        
        public int IncomingInvoiceId { get; set; }
        public IncomingInvoice IncomingInvoice { get; set; }

        public string? Description { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal Quantity { get; set; }
        
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
