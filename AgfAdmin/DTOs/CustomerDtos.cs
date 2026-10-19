// DTOs/CustomerDtos.cs
namespace AgfAdmin.DTOs
{
    public class CreateCustomerDto
    {
        public string Name { get; set; }
        public string? VATNumber { get; set; }
        public string? BillingAddress { get; set; }
        public string? ShippingAddress { get; set; }
        public string? ContactPerson { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public int PaymentTermsDays { get; set; } = 30;
    }

    // Klant aanmaken vanuit de kantoorflow (Verkoop): alleen operationele velden. Betaaltermijn en boekhoudvelden
    // blijven op hun bestaande standaard; namen zijn bewust niet uniek, gelijkende namen vragen om een expliciete bevestiging.
    public class CreateOperationalCustomerDto
    {
        public Guid? RequestId { get; set; }
        public string Name { get; set; }
        public string? ContactPerson { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? ShippingAddress { get; set; }
        public bool AcknowledgeSimilarNames { get; set; }
    }

    public class SimilarCustomerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? ContactPerson { get; set; }
        public string? Phone { get; set; }
    }

    public class CustomerDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? VATNumber { get; set; }
        public string? BillingAddress { get; set; }
        public string? ShippingAddress { get; set; }
        public string? ContactPerson { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public int PaymentTermsDays { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}