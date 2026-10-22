using System.ComponentModel.DataAnnotations;

namespace AgfAdmin.Models
{
    // Leveranciersrol van een relatie. Name, Email, VATNumber en BillingAddress zijn sinds de relatiemigratie een spiegel van
    // Relation (alleen RelationService schrijft ze); rolspecifiek zijn DefaultCurrency, PaymentTermsDays en de GGN's.
    public class Vendor
    {
        public int Id { get; set; }
        public int RelationId { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public Relation Relation { get; set; }
        
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
        [System.Text.Json.Serialization.JsonIgnore]
        public ICollection<VendorGgn> Ggns { get; set; } = new List<VendorGgn>();
    }
}
