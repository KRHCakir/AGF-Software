using System.ComponentModel.DataAnnotations.Schema;

namespace AgfAdmin.Models
{
    public class IncomingInvoiceLine
    {
        public int Id { get; set; }
        
        public int IncomingInvoiceId { get; set; }
        public IncomingInvoice IncomingInvoice { get; set; }

        // Optionele koppeling aan een artikel (variant); de omschrijving blijft altijd op de regel bewaard.
        public int? ProductVariantId { get; set; }
        public ProductVariant? ProductVariant { get; set; }

        // Optioneel kostenartikel (type Dienst: Handling, Transport, ...) in plaats van een artikel; nooit allebei. Geeft de soort van
        // de kosten op een partij (P2).
        public int? CostArticleId { get; set; }
        public CostArticle? CostArticle { get; set; }

        public string? Description { get; set; }
        public string? Unit { get; set; }           // box, colli, tray, piece, dozen of leeg; nooit omgerekend

        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountPercentage { get; set; } // standaard 0
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal LineTotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal VATPercentage { get; set; }
        
        [Column(TypeName = "decimal(18,2)")]
        public decimal VATAmount { get; set; }

        // Koppeling aan de orderregel (stap 4b): welke regel van welke inkooporder deze factuurregel factureert, met de prijs die op de
        // bon stond toen de factuur werd ingevoerd (het verschil met de factuur blijft zo zichtbaar, ook als de order later wordt
        // aangepast). Leeg bij een factuurregel zonder bon (kosten, P2). Vast na opslaan (TraceabilityGuard regel 30).
        public int? SupplyCommitmentLineId { get; set; }
        public SupplyCommitmentLine? SupplyCommitmentLine { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal? OrderUnitPrice { get; set; }

        // Versie van de verdeling over partijen (PartijCost): bij elke wijziging +1, concurrency-token, zodat twee gelijktijdige
        // herverdelingen nooit stil over elkaar heen gaan.
        public int AllocationVersion { get; set; }
    }
}
