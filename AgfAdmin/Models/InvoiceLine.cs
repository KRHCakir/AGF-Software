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
        // Omschrijving: bij het nieuwe model product, ras, verpakking, maat en klasse (momentopname bij Definitief), of de vrije tekst.
        public string Description { get; set; }
        public decimal Quantity { get; set; }
        // Prijs per eenheid excl. btw; leeg = nog geen prijs (alleen in een concept; Definitief vraagt een prijs op elke regel).
        public decimal? UnitPrice { get; set; }
        public decimal VATPercentage { get; set; } = 21.0m; // Default VAT
        public decimal LineTotal => Quantity * (UnitPrice ?? 0m);
        public decimal VATAmount => LineTotal * (VATPercentage / 100);
        public decimal Total => LineTotal + VATAmount;

        // --- Nieuwe AGF Specifieke Velden ---
        public string Unit { get; set; } = "kg"; // kg, doos, pallet, etc.
        public decimal TareWeight { get; set; }  // Taragewicht
        public decimal NetWeight { get; set; }   // Nettogewicht; nieuw model: netto kg van de geleverde goederen (0 = onbekend)
        public string? OriginCountry { get; set; } // Land van herkomst; nieuw model: de landcode (PE, ZA)
        public string? QualityClass { get; set; }  // Extra, Klasse I, II
        public decimal DiscountPercentage { get; set; }
        public string? Remarks { get; set; }     // Sortering, opmerkingen

        // Feedback 22 november: de inkoop-partij waar deze regel uit komt (herleidbaarheid van de directe factuur); optioneel, vast na aanmaken.
        // Verkoopfactuur 1: verplicht op een productregel van een directe factuur (de reserveringen komen uit deze partij).
        public int? PartijId { get; set; }
        public Partij? Partij { get; set; }

        // --- Verkoopfactuur 1 (nieuw model; leeg bij oude regels) ---
        // Product (goederen), Service (dienst of vrije regel) of Packaging (emballage van de order).
        public string? LineKind { get; set; }
        // Factuur uit een levering: de levering en de orderregel (Product), of de dienst- of emballageregel van de order. Vast na aanmaken.
        public int? SalesDeliveryId { get; set; }
        public SalesDelivery? SalesDelivery { get; set; }
        public int? SalesOrderLineId { get; set; }
        public SalesOrderLine? SalesOrderLine { get; set; }
        public int? SalesOrderServiceLineId { get; set; }
        public SalesOrderServiceLine? SalesOrderServiceLine { get; set; }
        public int? SalesOrderPackagingLineId { get; set; }
        public SalesOrderPackagingLine? SalesOrderPackagingLine { get; set; }
        // Het dienst- of emballage-artikel (kostenartikel), of leeg bij een vrije omschrijving.
        public int? CostArticleId { get; set; }
        public CostArticle? CostArticle { get; set; }
        // Omzetsoort van een dienst- of vrije regel (voor de latere koppeling met de grootboekrekening).
        public int? RevenueTypeId { get; set; }
        public RevenueType? RevenueType { get; set; }
        // Regelbedrag excl. btw en btw-bedrag, op centen (InvoiceMath); leeg zonder prijs.
        public decimal? LineNet { get; set; }
        public decimal? LineVat { get; set; }
        // Momentopname bij Definitief: partijnummer(s) en GGN('s) van de goederen.
        public string? LotNumber { get; set; }
        public string? Ggn { get; set; }
        public int SortOrder { get; set; }
        // Uit een concept gehaald: de regel blijft bestaan (reserveringen wijzen ernaar), maar telt nergens meer mee. Nooit terug.
        public DateTime? RemovedAt { get; set; }
    }
}
