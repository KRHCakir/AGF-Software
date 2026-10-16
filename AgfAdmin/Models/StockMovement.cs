namespace AgfAdmin.Models
{
    // Append-only voorraadmutatie. Type: Receipt, Dispatch, Waste, Correction, Reservation, ReservationRelease
    // (afgedwongen in servicecode). Alleen Receipt/Dispatch/Waste/Correction tellen als fysieke voorraad.
    public sealed class StockMovement
    {
        public int Id { get; set; }
        public string Type { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }            // expliciete eenheid; optioneel tijdens de overgang
        public decimal? NetWeightKg { get; set; }   // werkelijk netto kg; nooit afgeleid

        // Bron/partij van de beweging; altijd verplicht.
        public int SupplyCommitmentLineId { get; set; }
        public SupplyCommitmentLine SupplyCommitmentLine { get; set; }

        // Optioneel: de fysieke inhoudsregel (partij op een pallet), zodra die bestaat.
        public int? PalletContentLineId { get; set; }
        public PalletContentLine PalletContentLine { get; set; }

        // Gezet bij Receipt.
        public int? StockReceiptId { get; set; }
        public StockReceipt StockReceipt { get; set; }

        // Gezet bij Reservation, ReservationRelease en Dispatch.
        public int? StockReservationId { get; set; }
        public StockReservation StockReservation { get; set; }

        public string Reason { get; set; } // optioneel; verplicht voor Waste/Correction (servicecode)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
