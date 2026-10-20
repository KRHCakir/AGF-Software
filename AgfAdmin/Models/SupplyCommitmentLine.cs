namespace AgfAdmin.Models
{
    // Regel/partij van een toezegging. Alle nieuwe fysieke voorraad wordt via zo'n regel ontvangen,
    // dus elke voorraadbeweging en reservering is herleidbaar tot deze bron.
    public sealed class SupplyCommitmentLine
    {
        public int Id { get; set; }

        public int SupplyCommitmentId { get; set; }
        public SupplyCommitment SupplyCommitment { get; set; }

        public int ProductVariantId { get; set; }
        public ProductVariant ProductVariant { get; set; }

        public decimal ExpectedQuantity { get; set; }
        public string ExpectedUnit { get; set; }            // box, colli, tray, piece of dozen; optioneel tijdens de overgang
        public decimal? ExpectedNetWeightKg { get; set; }   // werkelijk opgegeven netto kg; nooit afgeleid
        public string LotNumber { get; set; } // optioneel

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token: wordt aangeraakt bij elke DirectTrade-toewijzing of vrijgave op deze regel,
        // zodat twee gelijktijdige toewijzingen nooit samen boven de verwachte hoeveelheid uitkomen. Centraal verhoogd in AppDbContext.
        public int Version { get; set; }
    }
}
