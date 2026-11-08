namespace AgfAdmin.Models
{
    // Kosten op een partij (P2): een deel van een inkoopfactuurregel, excl. btw, op een inkoop-partij (zeevracht op de hele container) of
    // een verkoop-partij (handling per klantlading). Een factuurregel kan over meerdere partijen worden verdeeld; wat niet verdeeld is,
    // telt als algemene kosten. Eén regel per factuurregel en partij. Wijzigen of herverdelen mag, maar altijd met een auditregel
    // (TraceabilityGuard, regel 25); de historie verdwijnt nooit.
    public sealed class PartijCost
    {
        public int Id { get; set; }

        public int IncomingInvoiceLineId { get; set; }
        public IncomingInvoiceLine IncomingInvoiceLine { get; set; }

        public int PartijId { get; set; }
        public Partij Partij { get; set; }

        // Bedrag excl. btw in euro (aanname: kosten verdelen alleen bij facturen in EUR).
        public decimal AmountExclVat { get; set; }

        // Soort, afgeleid uit de factuurregel bij het verdelen: de naam van het kostenartikel (Handling, Zeevracht, ...), "Goederen" bij
        // een artikel en "Overig" bij alleen een omschrijving. Verandert daarna niet.
        public string Kind { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // Weergavenaam van de ingelogde gebruiker (ICurrentUser), nooit van de client.
        public string CreatedBy { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // Historie van de verdeling: verdeeld, gewijzigd (oud en nieuw bedrag) of weggehaald. Append-only. Zonder foreign key naar de partij of
    // de kostenregel, zodat de historie blijft als een verdeling wordt weggehaald.
    public sealed class PartijCostAuditEvent
    {
        public int Id { get; set; }
        public int IncomingInvoiceId { get; set; }
        public int IncomingInvoiceLineId { get; set; }
        public int PartijId { get; set; }
        public string EventType { get; set; }
        public decimal? OldAmount { get; set; }
        public decimal? NewAmount { get; set; }
        // Idempotentiesleutel van de opdracht "verdeling opslaan" (alle events van één opdracht delen haar).
        public Guid RequestId { get; set; }
        public string ActorReference { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
