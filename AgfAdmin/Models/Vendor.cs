using System.ComponentModel.DataAnnotations;

namespace AgfAdmin.Models
{
    public class Vendor
    {
        public int Id { get; set; }
        
        [Required]
        public string Name { get; set; }
        public string? Email { get; set; }
        public string? VATNumber { get; set; }
        public string? BillingAddress { get; set; }
        public string? DefaultCurrency { get; set; } = "USD";
        public int PaymentTermsDays { get; set; } = 30;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<IncomingInvoice> IncomingInvoices { get; set; }
    }
}
