using System.Text.Json.Serialization;

// Customer.cs
namespace AgfAdmin.Models
{
    // Klantrol van een relatie. Name, VATNumber, BillingAddress, ShippingAddress, ContactPerson, Email en Phone zijn sinds de
    // relatiemigratie een spiegel van Relation (alleen RelationService schrijft ze); rolspecifiek is PaymentTermsDays.
    public class Customer
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
        public int PaymentTermsDays { get; set; } = 30;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // Idempotentiesleutel van de kantoorflow (POST /api/customers); null voor klanten uit oudere flows.
        public Guid? RequestId { get; set; }
        
        [JsonIgnore]
        public ICollection<Order> Orders { get; set; }
    }
}
