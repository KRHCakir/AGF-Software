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
        // Bedragen staan in de valuta van de factuur; alleen voor kosten op partijen wordt met de ECB-koers naar euro omgerekend.
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
        // Interne opmerking bij dit document ("Klant komt morgen langs"): alleen voor interne werkplekken, nooit in klantfacturen,
        // leverbonnen, mails of exports; niet overgenomen naar volgende documenten. Los van Remarks (algemeen opmerkingenveld).
        public string? InternalNote { get; set; }

        // Koers voor een factuur in een andere valuta dan EUR (zie InvoiceExchangeRateService): de ECB-referentiekoers in eenheden
        // valuta per 1 euro (zoals de ECB hem publiceert, 1 EUR = 1,1165 USD), de datum van die publicatie (de laatste op of vóór de
        // factuurdatum), de bron en wanneer hij is opgehaald. Nooit getypt, altijd door de server opgehaald; ligt vast zodra er kosten op
        // partijen staan (regel 26). Alle vier leeg bij een factuur in euro of zolang er nog geen koers is.
        public decimal? ExchangeRate { get; set; }
        public DateTime? ExchangeRateDate { get; set; }
        public string? ExchangeRateSource { get; set; }
        public DateTime? ExchangeRateFetchedAt { get; set; }

        // Wat er gebeurde met een prijsverschil tussen deze factuur en de gekoppelde bon(nen) (stap 4b): AdjustOrder (de prijs op de
        // orderregel werd de factuurprijs, met auditregel) of RecordDifference (de bon bleef staan, het verschil blijft zichtbaar). Leeg
        // zonder verschil of zonder bon. Vast na opslaan (TraceabilityGuard regel 30).
        public string? OrderPriceDecision { get; set; }

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
