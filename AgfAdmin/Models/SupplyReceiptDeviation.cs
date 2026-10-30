namespace AgfAdmin.Models
{
    // Afwijkende ontvangst binnen een bestaande inkooporder: besteld was variant A (bijv. Avocado Hass Peru), ontvangen is variant B
    // van hetzelfde artikel (bijv. Zuid-Afrika). Na bevestiging "Klopt dit?" komt er een vervangende aanvoerregel voor B (NewLine,
    // ReplacesLineId = de oorspronkelijke regel), gaat de oorspronkelijke regel omlaag en hangt de ontvangen inhoud aan de nieuwe regel.
    // Append-only: wie, wanneer, waarom en een momentopname van beide varianten, zodat latere artikelwijzigingen het verleden niet veranderen.
    public sealed class SupplyReceiptDeviation
    {
        public int Id { get; set; }

        // Idempotentiesleutel: dezelfde als die van de ontvangen inhoudsregel, zodat een retry nooit een tweede afwijking maakt.
        public Guid RequestId { get; set; }

        public int SupplyCommitmentId { get; set; }
        public SupplyCommitment SupplyCommitment { get; set; }

        public int OriginalLineId { get; set; }
        public SupplyCommitmentLine OriginalLine { get; set; }

        // Vervangende regel bij een andere variant; null bij een afwijking in alleen het gewicht (zelfde variant, zelfde regel).
        public int? NewLineId { get; set; }
        public SupplyCommitmentLine NewLine { get; set; }

        public int PalletContentLineId { get; set; }
        public PalletContentLine PalletContentLine { get; set; }

        // Afwijkende kenmerken, komma-gescheiden: Origin, Size, Packaging, TradeClass, Brand; Other als de kenmerken gelijk zijn
        // maar het toch een andere variant is (nooit automatisch gelijkgesteld); Weight als het netto kg per verpakking meer dan de
        // tolerantie afwijkt (ReceiptDeviationRules.WeightTolerance).
        public string Kinds { get; set; }

        // Momentopname van de verwachte en de ontvangen variant (id + JSON met artikel, ras, herkomst, maat, verpakking, klasse, merk).
        public int ExpectedProductVariantId { get; set; }
        public string ExpectedVariantSnapshot { get; set; }
        public int ReceivedProductVariantId { get; set; }
        public string ReceivedVariantSnapshot { get; set; }

        // Ontvangen hoeveelheid (= verwacht op de nieuwe regel) en hoeveel de oorspronkelijke regel daardoor omlaag ging. Minder dan
        // de hoeveelheid als er minder dan dat nog verwacht werd: het verschil is meer geleverd dan besteld.
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal ReducedQuantity { get; set; }
        // Netto kg per verpakking: verwacht (bronregel of artikelstandaard) en ontvangen (inhoudsregel); null zonder verwachting.
        public decimal? ExpectedKgPerPackage { get; set; }
        public decimal? ReceivedKgPerPackage { get; set; }
        // De oorspronkelijke regel vóór en na (aantal, kg, palletweergave) als JSON.
        public string OriginalLineBefore { get; set; }
        public string OriginalLineAfter { get; set; }

        public string Reason { get; set; }      // optioneel: SupplierDeliveredDifferently, OtherLotLoaded, Other
        public string ReasonNote { get; set; }  // verplicht bij Other
        public string ActorReference { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
