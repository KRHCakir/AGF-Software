namespace AgfAdmin.DTOs
{
    // Voorstel "Reserveren (oudste partij eerst)": alle te reserveren inhoudsregels van deze variant en eenheid in FIFO-volgorde
    // (ontvangstmoment, dan partijnummer, dan inhoudsregel) en de voorgestelde verdeling van wat de regel nog nodig heeft. Alleen lezen;
    // de gebruiker past de verdeling aan en bevestigt met ReserveBatch.
    public sealed class FifoReservationProposalDto
    {
        public int SalesOrderId { get; set; }
        public int SalesOrderLineId { get; set; }
        public string Unit { get; set; }
        // Besteld min vastgelegd (Active, ook on hold, en verzonden): wat er nog gereserveerd kan worden.
        public decimal NeededQuantity { get; set; }
        public decimal ProposedQuantity { get; set; }
        public List<ReservableStockLineDto> Candidates { get; set; } = new();
        public List<FifoReservationItemDto> Items { get; set; } = new();
    }

    public sealed class FifoReservationItemDto
    {
        public int PalletContentLineId { get; set; }
        public decimal Quantity { get; set; }
        // Voorstel: de hele rest neemt exact de resterende kg; berekend gewicht exact aantal × kg per verpakking; gewogen naar verhouding
        // (afgerond op 3 decimalen, aan te passen). De bestaande kg-regels van een reservering gelden ongewijzigd.
        public decimal NetWeightKg { get; set; }
    }

    // Reserveren over meerdere pallets in één keer: elk item is een gewone exacte reservering op één inhoudsregel (traceerbaar tot
    // inkooporder en partij). Alles of niets in één transactie; idempotent via RequestId (dezelfde inhoud geeft hetzelfde antwoord).
    public sealed class CreateStockReservationBatchDto
    {
        public Guid? RequestId { get; set; }
        public string Unit { get; set; }
        public string Reason { get; set; }
        public List<FifoReservationItemDto> Items { get; set; }
    }

    // Werklijst "Verkocht, wacht op voorraad": regel uit verwachte aanvoer waarvoor nu goedgekeurde, vrije voorraad is (oudste verkoop eerst).
    // Het systeem reserveert niet vanzelf; de gebruiker kiest "Reserveren (oudste partij eerst)" en bevestigt.
    public sealed class AwaitingStockLineDto
    {
        public int SalesOrderId { get; set; }
        public string OrderNumber { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string CustomerReference { get; set; }
        public DateTime RequestedDeliveryDate { get; set; }
        public int SalesOrderLineId { get; set; }
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public string Unit { get; set; }
        // Nog niet gereserveerd van deze regel; waarvan nu uit vrije voorraad te reserveren.
        public decimal UnreservedQuantity { get; set; }
        public decimal ReservableNow { get; set; }
        public SalesLineCoverageDto Coverage { get; set; }
    }
}
