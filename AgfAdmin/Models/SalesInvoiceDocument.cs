namespace AgfAdmin.Models
{
    // De PDF van een definitieve verkoopfactuur (verkoopfactuur 1), zoals de laadbon: een versie met een snapshot (JSON) waaruit de PDF elke
    // keer opnieuw wordt gemaakt, nooit een bestand. Versie 1 ontstaat bij Definitief; de inhoud verandert daarna nooit (regel 44), alleen
    // het moment van versturen wordt één keer vastgelegd. Een concept heeft geen document: zijn PDF wordt live gemaakt, met "CONCEPT".
    public sealed class SalesInvoiceDocument
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; }
        public int Version { get; set; }
        // Het factuurnummer (VF-jjjj-nnnnn).
        public string Number { get; set; }
        public string SnapshotJson { get; set; }
        public string ContentHash { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; }
        public DateTime? SentAt { get; set; }
        public string SentBy { get; set; }
    }
}
