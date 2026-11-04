namespace AgfAdmin.Models
{
    // Append-only auditrecord voor wijzigingen aan een pallet of inhoudsregel (status, locatie, hoeveelheid,
    // gewicht, kwaliteit). Minstens één doel is verplicht (database-check).
    public sealed class PalletAuditEvent
    {
        public int Id { get; set; }

        public int? PalletId { get; set; }
        public Pallet Pallet { get; set; }

        public int? PalletContentLineId { get; set; }
        public PalletContentLine PalletContentLine { get; set; }

        public string EventType { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Reason { get; set; }
        // De ingelogde gebruiker (weergavenaam van het account) die het commando gaf.
        public string ActorReference { get; set; }
        // Alleen bij een keuring: wie het product fysiek keurde, per keuring gekozen. Leeg bij andere events en bij keuringen van
        // vóór deze kolom (daar staat de beoordelaar in ActorReference).
        public string InspectedBy { get; set; }

        public DateTime OccurredAt { get; set; }

        // Idempotentiesleutel van het commando dat dit event veroorzaakte; uniek, leeg bij oudere/creatie-events.
        public Guid? RequestId { get; set; }
    }
}
