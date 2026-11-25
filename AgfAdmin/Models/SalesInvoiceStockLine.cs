namespace AgfAdmin.Models
{
    // Afboeking van een productregel van een directe verkoopfactuur bij Definitief (verkoopfactuur 1): verbruikt één actieve reservering
    // in haar geheel, zoals een SalesDispatchLine bij een levering. Hoeveelheid, eenheid en netto kg zijn een kopie van de reservering;
    // de bijbehorende Dispatch-mutatie is het grootboek. Append-only (regel 3 en 44).
    public sealed class SalesInvoiceStockLine
    {
        public int Id { get; set; }
        public int InvoiceId { get; set; }
        public Invoice Invoice { get; set; }
        public int InvoiceLineId { get; set; }
        public InvoiceLine InvoiceLine { get; set; }
        // Een reservering wordt hooguit één keer verbruikt (unieke index).
        public int StockReservationId { get; set; }
        public StockReservation StockReservation { get; set; }
        public int PalletContentLineId { get; set; }
        public PalletContentLine PalletContentLine { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        public int StockMovementId { get; set; }
        public StockMovement StockMovement { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
