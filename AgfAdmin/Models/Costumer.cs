using System.Text.Json.Serialization;

// Customer.cs
namespace AgfAdmin.Models
{
    // Klantrol van een relatie. Name, VATNumber, BillingAddress, ShippingAddress, ContactPerson, Email en Phone zijn sinds de
    // relatiemigratie een spiegel van Relation (alleen RelationService schrijft ze); rolspecifiek is PaymentTermsDays.
    public class Customer : IRoleTerms
    {
        public int Id { get; set; }
        public int RelationId { get; set; }
        [JsonIgnore]
        public Relation Relation { get; set; }
        public string Name { get; set; }
        public string? VATNumber { get; set; }
        public string? BillingAddress { get; set; }
        public string? ShippingAddress { get; set; }
        public string? ContactPerson { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        // Spiegel van PaymentCondition.Days (alleen RelationService/PaymentConditionResolver schrijven dit; guard regel 16).
        public int PaymentTermsDays { get; set; } = 30;
        public int PaymentConditionId { get; set; }
        [JsonIgnore]
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
        // Valuta van de klantrol (ISO 4217, standaard EUR). Er wordt nooit omgerekend.
        public string Currency { get; set; } = "EUR";

        // Krediet (EUR): interne limiet en kredietverzekering. Alleen signalering (CreditExposureService); nooit blokkerend.
        public decimal? CreditLimit { get; set; }
        public bool IsInsured { get; set; }
        public string? Insurer { get; set; }
        public string? InsurerText { get; set; }
        public decimal? InsuredLimit { get; set; }
        public string? InsurerReference { get; set; }
        public DateOnly? InsuredUntil { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // Idempotentiesleutel van de kantoorflow (POST /api/customers); null voor klanten uit oudere flows.
        public Guid? RequestId { get; set; }
        
        [JsonIgnore]
        public ICollection<Order> Orders { get; set; }
    }
}
