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
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal VATPercentage { get; set; } = 21.0m; // Default VAT
        public decimal LineTotal => Quantity * UnitPrice;
        public decimal VATAmount => LineTotal * (VATPercentage / 100);
        public decimal Total => LineTotal + VATAmount;

        // --- Nieuwe AGF Specifieke Velden ---
        public string Unit { get; set; } = "kg"; // kg, doos, pallet, etc.
        public decimal TareWeight { get; set; }  // Taragewicht
        public decimal NetWeight { get; set; }   // Nettogewicht
        public string? OriginCountry { get; set; } // Land van herkomst
        public string? QualityClass { get; set; }  // Extra, Klasse I, II
        public decimal DiscountPercentage { get; set; }
        public string? Remarks { get; set; }     // Sortering, opmerkingen
    }
}