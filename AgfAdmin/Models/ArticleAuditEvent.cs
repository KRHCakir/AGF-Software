namespace AgfAdmin.Models
{
    // Append-only historie van een artikel (ProductVariant) of productsoort (Product): aangemaakt (ook vanuit aanvoer of ontvangst),
    // gearchiveerd, teruggezet en verwijderd. Bewust zonder foreign keys: na verwijderen blijft het record met de omschrijving van
    // toen staan (Description is een momentopname, wordt nooit bijgewerkt). Minstens één doel is verplicht (database-check).
    public sealed class ArticleAuditEvent
    {
        public int Id { get; set; }

        public int? ProductVariantId { get; set; }
        public int? ProductId { get; set; }

        public string EventType { get; set; }
        // Volledige naam op het moment van het event: product, ras, herkomst, maat, verpakking, klasse, merk.
        public string Description { get; set; }
        public string Reason { get; set; }
        public string ActorReference { get; set; } // expliciete actor tot authenticatie beschikbaar is

        public DateTime OccurredAt { get; set; }

        // Idempotentiesleutel van het commando dat dit event veroorzaakte; uniek, leeg als het commando er geen had.
        public Guid? RequestId { get; set; }
    }
}
