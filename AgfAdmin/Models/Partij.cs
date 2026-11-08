namespace AgfAdmin.Models
{
    // Partij: één lading (bijvoorbeeld 2610251) waar inkoop, ontvangst en verkoop naar verwijzen. Eén partij kan meerdere inkooporders
    // hebben (goederen van meerdere leveranciers); een inkooporder hoort bij één partij. De verkoop volgt de partij via de palletregel.
    // Het nummer is vrij te typen of voorgesteld volgens de bedrijfsinstelling (PartijRules); de sleutel (hoofdletters, zonder spaties
    // en streepjes) is uniek, zodat 2610251 en "2610 251" nooit twee partijen worden. Na de eerste koppeling verandert het nummer alleen
    // via "Nummer corrigeren" met reden en auditregel (TraceabilityGuard, regel 23).
    public sealed class Partij
    {
        public int Id { get; set; }

        // Door de client meegegeven idempotentiesleutel bij los aanmaken; null bij aanmaken samen met een inkooporder en bij de migratie.
        public Guid? RequestId { get; set; }

        public string Number { get; set; }
        public string NumberKey { get; set; }

        // Inkoop (P1: leverancier, inkooporder, palletregels) of Verkoop (P2: precies één verkooporder). Het nummer is uniek per soort,
        // en elke soort heeft een eigen formaat en teller. Soort en order veranderen nooit (TraceabilityGuard, regel 24).
        public string Kind { get; set; } = Services.PartijRules.KindPurchase;
        public int? SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }

        // Opgeslagen status: nu altijd Open. Uitverkocht wordt afgeleid (PartijRules.DisplayStatus), Afgesloten komt met het
        // partijrapport (P3) als tweede opgeslagen waarde.
        public string Status { get; set; } = Services.PartijRules.StatusOpen;

        // Voorgesteld nummer: de periode waarin het volgnummer telt ("" = nooit opnieuw, "2026" = per jaar, "2026-11" = per maand) en
        // het volgnummer. Beide null bij een eigen nummer: dat verschuift de teller niet.
        public string SequencePeriod { get; set; }
        public int? Sequence { get; set; }

        public string Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // Weergavenaam van de ingelogde gebruiker (ICurrentUser), nooit van de client; "Migratie" bij partijen uit bestaande regels.
        public string CreatedBy { get; set; }
    }

    // Historie van een partij: nummer gecorrigeerd (oud, nieuw, reden), inkooporder gekoppeld en verwijderd. Append-only; het
    // aanmaken staat op de partij zelf.
    // Bewust zonder foreign key naar de partij: de historie blijft bestaan nadat een ongebruikte partij is verwijderd.
    public sealed class PartijAuditEvent
    {
        public int Id { get; set; }
        public int PartijId { get; set; }
        public string EventType { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Reason { get; set; }
        public int? SupplyCommitmentId { get; set; }
        public string ActorReference { get; set; }
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
