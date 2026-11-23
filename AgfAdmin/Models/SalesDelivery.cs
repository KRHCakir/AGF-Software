namespace AgfAdmin.Models
{
    // Een levering van een verkooporder (feedback 22 november): één vracht naar één klant, met een eigen verkoop-partij (V-nummer), een
    // eigen laadbon en transportopdracht. Een order heeft altijd een eerste levering (volgnummer 1, samen met de order aangemaakt, met de
    // verkoop-partij die de order al had); "Deellevering" voegt er een toe met een nieuw V-nummer. Een levering verdwijnt nooit en haar order,
    // volgnummer en partij veranderen nooit (TraceabilityGuard, regel 42).
    //
    // Welke reserveringen erbij horen: StockReservation.SalesDeliveryId; leeg = de eerste levering (zo verandert er bij de migratie geen
    // bestaande reservering). Wat nog niet gereserveerd is, staat op de laatste levering.
    public sealed class SalesDelivery
    {
        public int Id { get; set; }
        // Idempotentiesleutel van "Deellevering" (uniek); null bij de eerste levering en bij de migratie.
        public Guid? RequestId { get; set; }
        public int SalesOrderId { get; set; }
        public SalesOrder SalesOrder { get; set; }
        // 1, 2, ... per order (uniek samen met de order).
        public int Sequence { get; set; }
        // De verkoop-partij van deze levering (uniek): een verkoop-partij van dezelfde order.
        public int PartijId { get; set; }
        public Partij Partij { get; set; }
        // Laden op en kenteken van een deellevering (volgnummer 2 en verder). De eerste levering gebruikt die van de order (SalesOrder.LoadAt,
        // LicensePlate), zoals vóór de leveringen; zo verandert er niets aan bestaande orders en laadbonnen.
        public DateTime? LoadAt { get; set; }
        public string LicensePlate { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        // Weergavenaam van de ingelogde gebruiker (ICurrentUser), nooit van de client.
        public string CreatedBy { get; set; }
        // Optimistisch concurrency-token; centraal verhoogd in AppDbContext.
        public int Version { get; set; }
    }
}
