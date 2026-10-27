namespace AgfAdmin.Models
{
    // Afkeuring van (een deel van) ontvangen inkoop: reden, besluit, actor en tijdstip; de regels zeggen welke inhoud.
    // Append-only. De kwaliteit van de afgekeurde inhoud wordt in dezelfde save Rejected (nooit verkoopbaar).
    public sealed class SupplyRejection
    {
        public int Id { get; set; }

        // Idempotentiesleutel van de client: een retry keurt nooit twee keer af.
        public Guid RequestId { get; set; }

        public int SupplyCommitmentId { get; set; }
        public SupplyCommitment SupplyCommitment { get; set; }

        public string Reason { get; set; }       // Quality, Ripeness, Temperature, Damaged, WrongArticle, Other
        public string Explanation { get; set; }  // verplicht bij Other
        public string Decision { get; set; }     // ReturnToSupplier, ClaimDiscount, Dispose
        public string ActorReference { get; set; }
        public DateTime RejectedAt { get; set; } = DateTime.UtcNow;

        public ICollection<SupplyRejectionLine> Lines { get; set; } = new List<SupplyRejectionLine>();
    }

    // Afgekeurde hoeveelheid van één ontvangen inhoudsregel. Bij een deel van de regel ontstaat een afgesplitste inhoudsregel
    // (OriginRejectionLineId) met precies deze hoeveelheid en kg; bij de hele voorraad van de regel wordt de regel zelf Rejected.
    public sealed class SupplyRejectionLine
    {
        public int Id { get; set; }

        public int SupplyRejectionId { get; set; }
        public SupplyRejection SupplyRejection { get; set; }

        public int PalletContentLineId { get; set; }
        public PalletContentLine PalletContentLine { get; set; }

        // Kopie van de bronregel van die inhoud (guard regel 1-stijl): traceerbaar zonder via de inhoud te gaan.
        public int SupplyCommitmentLineId { get; set; }
        public SupplyCommitmentLine SupplyCommitmentLine { get; set; }

        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        public bool SplitOff { get; set; }       // true = deel van de regel afgesplitst; false = hele voorraad van de regel afgekeurd

        // Bij SplitOff: de afgesplitste inhoudsregel (die verwijst hierheen via OriginRejectionLineId).
        public PalletContentLine SplitContentLine { get; set; }
    }
}
