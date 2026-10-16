namespace AgfAdmin.Models
{
    // Kleinste fysieke en traceerbare eenheid: precies één bronregel en één partij.
    // De productvariant volgt via de bronregel; een inhoudsregel mengt nooit partijen of bronnen.
    public sealed class PalletContentLine
    {
        public int Id { get; set; }

        // Door de client meegegeven idempotentiesleutel: een retry maakt nooit een tweede fysieke regel.
        public Guid RequestId { get; set; }

        public int PalletId { get; set; }
        public Pallet Pallet { get; set; }

        public int SupplyCommitmentLineId { get; set; }
        public SupplyCommitmentLine SupplyCommitmentLine { get; set; }

        public string LotNumber { get; set; }      // verplicht, niet leeg
        public decimal Quantity { get; set; }      // > 0
        public string Unit { get; set; }           // verplicht, niet leeg: box, colli, tray, piece of dozen
        public decimal NetWeightKg { get; set; }   // > 0, werkelijk netto kg; nooit afgeleid

        public string QualityStatus { get; set; }  // Quarantine, Approved, Blocked, Rejected
        public string QualityReason { get; set; }  // optioneel

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
