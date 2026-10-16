namespace AgfAdmin.Models
{
    // Reservering van één orderregel op één bronregel. Source bepaalt of fysieke, verwachte of
    // direct-trade hoeveelheid wordt gereserveerd; een reservering maakt verwachte voorraad nooit fysiek.
    public sealed class StockReservation
    {
        public int Id { get; set; }

        public int OrderLineId { get; set; }
        public OrderLine OrderLine { get; set; }

        public int SupplyCommitmentLineId { get; set; }
        public SupplyCommitmentLine SupplyCommitmentLine { get; set; }

        public int Quantity { get; set; }
        public string Source { get; set; } // "Physical", "Expected" of "DirectTrade"
        public string Status { get; set; } // bijv. "Active", "Released", "Dispatched"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
