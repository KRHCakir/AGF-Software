// InvoiceLine.cs
namespace AgfAdmin.Models
{
    public class InvoiceLine
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; }
        public int? ProductVariantId { get; set; }
        public ProductVariant? ProductVariant { get; set; }
        public string Description { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal VATPercentage { get; set; } = 21.0m; // Default VAT
        public decimal LineTotal => Quantity * UnitPrice;
        public decimal VATAmount => LineTotal * (VATPercentage / 100);
        public decimal Total => LineTotal + VATAmount;
    }
}