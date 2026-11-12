namespace AgfAdmin.Models
{
    // Auditregel van een inkooporder (stap 4a): een aanpassing (Edit, zolang de order Open is), Definitief maken (Finalize) of een
    // correctie na definitief (Correction, met verplichte reden). Per veld wat het was en wat het werd; bij regels toegevoegd, gewijzigd of
    // verwijderd. Append-only (regel 29): de geschiedenis wordt nooit overschreven. De actor komt uit CurrentUser, nooit van de client.
    public sealed class PurchaseOrderChange
    {
        public int Id { get; set; }

        // Idempotentiesleutel van de client: een herhaald verzoek schrijft nooit een tweede wijziging.
        public Guid RequestId { get; set; }

        public int SupplyCommitmentId { get; set; }
        public SupplyCommitment SupplyCommitment { get; set; }

        public string Kind { get; set; }    // Edit, Finalize of Correction
        public string Reason { get; set; }  // verplicht bij Correction
        public string ActorReference { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // De velden als JSON-lijst van { field, lineId, action, old, new } (PurchaseOrderChangeField).
        public string ChangesJson { get; set; }
    }
}
