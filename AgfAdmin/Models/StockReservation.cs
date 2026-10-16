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

        // Optioneel: de fysieke inhoudsregel (partij op een pallet), zodra die bestaat.
        public int? PalletContentLineId { get; set; }
        public PalletContentLine PalletContentLine { get; set; }

        public decimal Quantity { get; set; }
        public string Unit { get; set; }            // expliciete eenheid; optioneel tijdens de overgang
        public decimal? NetWeightKg { get; set; }   // werkelijk netto kg; nooit afgeleid
        public string Source { get; set; } // "Physical", "Expected" of "DirectTrade"
        public string Status { get; set; } // bijv. "Active", "Released", "Dispatched"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
