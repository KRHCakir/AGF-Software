// Invoice.cs (updated)
namespace AgfAdmin.Models
{
    public class Invoice
    {
        public int Id { get; set; }
        public int? OrderId { get; set; }
        public Order? Order { get; set; }
        
        public int? CustomerId { get; set; }
        public Customer? Customer { get; set; }
        public string? CustomCustomerName { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime InvoiceDate { get; set; }
        public decimal SubTotal { get; set; }
        public decimal VATAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = "Draft"; // Lifecycle status (Draft, Finalized)
        public string PaymentStatus { get; set; } = "Pending"; // Financial status (Pending, Paid, Overdue)
        public DateTime? DueDate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation property for lines
        public ICollection<InvoiceLine> InvoiceLines { get; set; }
    }
}