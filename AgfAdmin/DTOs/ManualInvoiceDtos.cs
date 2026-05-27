using System.ComponentModel.DataAnnotations;

namespace AgfAdmin.DTOs
{
    // Manual INCOMING Invoice (Purchase)
    public class CreateIncomingInvoiceDto
    {
        [Required]
        public int VendorId { get; set; }
        
        [Required]
        public string InvoiceNumber { get; set; }
        
        [Required]
        public DateTime InvoiceDate { get; set; }
        
        public DateTime? DueDate { get; set; }
        
        public List<CreateIncomingInvoiceLineDto> Lines { get; set; } = new();
    }

    public class CreateIncomingInvoiceLineDto
    {
        public string Description { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal VATPercentage { get; set; } = 21.0m;
        
        // Optional: Link to ProductVariant if we want to auto-update stock
        public int? ProductVariantId { get; set; } 
    }

    // Manual OUTGOING Invoice (Sale)
    public class CreateManualInvoiceDto
    {
        [Required]
        public int CustomerId { get; set; }
        
        [Required]
        public DateTime InvoiceDate { get; set; }
        
        public DateTime? DueDate { get; set; }
        
        public List<CreateManualInvoiceLineDto> Lines { get; set; } = new();
    }

    public class CreateManualInvoiceLineDto
    {
        public int? ProductVariantId { get; set; }
        public string Description { get; set; } // Fallback if no product ID
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal VATPercentage { get; set; } = 21.0m;
    }
}
