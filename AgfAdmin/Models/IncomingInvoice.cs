using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AgfAdmin.Models
{
    public class IncomingInvoice
    {
        public int Id { get; set; }

        public int? VendorId { get; set; }
        public Vendor? Vendor { get; set; }

        public string? InvoiceNumber { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public DateTime? DueDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal VATAmount { get; set; }

        public string Status { get; set; } = "Draft"; // Draft, Processing, NeedsReview, Approved, Rejected
        
        // AI Confidence Score (0.0 to 1.0)
        public double ReviewConfidence { get; set; }

        public string? OriginalFilePath { get; set; } // Path to stored PDF/Image

        // JSON string or serialized object containing warnings/issues found by AI
        public string? DetectedIssues { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<IncomingInvoiceLine> InvoiceLines { get; set; }
    }
}
