using System.ComponentModel.DataAnnotations;

namespace AgfAdmin.Models
{
    // Leveranciersrol van een relatie. Name, Email, VATNumber en BillingAddress zijn sinds de relatiemigratie een spiegel van
    // Relation (alleen RelationService schrijft ze); rolspecifiek zijn DefaultCurrency, PaymentTermsDays en de GGN's.
    public class Vendor : IRoleTerms
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
        public string? DefaultCurrency { get; set; } = "EUR";
        // Spiegel van PaymentCondition.Days (alleen RelationService/PaymentConditionResolver schrijven dit; guard regel 16).
        public int PaymentTermsDays { get; set; } = 30;
        public int PaymentConditionId { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public PaymentCondition PaymentCondition { get; set; }
        
        // Rolafspraken (IRoleTerms): betaalwijze (incasso vraagt mandaatkenmerk + ondertekeningsdatum), levering, handelsvoorwaarden.
        public string PaymentMethod { get; set; } = PaymentMethods.BankTransfer;
        public string? MandateReference { get; set; }
        public DateOnly? MandateSignedOn { get; set; }
        public string? DeliveryTermKind { get; set; }
        public string? Incoterm { get; set; }
        public string? IncotermPlace { get; set; }
        public string? DeliveryTermText { get; set; }
        public string? TradeConditions { get; set; }
        public string? TradeConditionsText { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<IncomingInvoice> IncomingInvoices { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public ICollection<VendorGgn> Ggns { get; set; } = new List<VendorGgn>();
    }
}
