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

        // Kantoorinvoer (PurchaseInvoiceService): idempotentiesleutel, op het document bewaarde betaaltermijn en valuta
        // (leveranciersstandaarden zijn alleen voorstellen), en optionele velden volgens de bedrijfsinstellingen.
        // Valuta is uitsluitend een opgeslagen code: bedragen staan in die valuta, er wordt nooit omgerekend.
        public Guid? RequestId { get; set; }
        // Genormaliseerde oorspronkelijke opdracht (JSON, zie PurchaseInvoiceCommand). Een herhaling met dezelfde RequestId
        // wordt hiermee één-op-één vergeleken, los van intussen gewijzigde leveranciers- of bedrijfsstandaarden.
        // NULL bij facturen van vóór deze vastlegging; die vallen terug op vergelijking van de opgeslagen velden.
        public string? RequestPayload { get; set; }
        public int? PaymentTermsDays { get; set; }
        public string? Currency { get; set; }
        public DateTime? ReceivedDate { get; set; }   // ontvangstdatum is een ander begrip dan factuurdatum
        public string? PaymentReference { get; set; }
        public string? Remarks { get; set; }
        [Column(TypeName = "decimal(18,3)")]
        public decimal? GrossWeightKg { get; set; }
        [Column(TypeName = "decimal(18,3)")]
        public decimal? NetWeightKg { get; set; }
        public string? ContainerNumber { get; set; }
        public string? Transporter { get; set; }
        public string? DriverName { get; set; }
        public string? LicensePlate { get; set; }
        public string? Incoterms { get; set; }
        [Column(TypeName = "decimal(18,3)")]
        public decimal? CustomsValue { get; set; }
        public string? OriginCountry { get; set; }
        public string? ReceiptReference { get; set; }  // verwijzing naar ontvangst, pallet of SSCC; nooit een ontvangstactie

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
