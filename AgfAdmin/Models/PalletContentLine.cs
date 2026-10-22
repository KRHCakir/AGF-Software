using AgfAdmin.Services;

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
        public decimal NetWeightKg { get; set; }   // > 0, totaal netto kg; gewogen (Weighed) of aantal × kg per verpakking (Calculated)

        // Hoe NetWeightKg tot stand kwam: "Weighed" (werkelijk gewogen) of "Calculated" (Quantity × NetKgPerPackage, etiket).
        // Bestaande regels zijn Weighed. De database bewaakt dat een berekend totaal exact bij aantal en kg per verpakking past.
        public string WeightMethod { get; set; } = WeightRules.Weighed;
        public decimal? NetKgPerPackage { get; set; } // alleen bij Calculated, > 0

        // GGN van deze partij zoals bij ontvangst gekozen (voorstel uit het artikel, per regel aangepast of leeggemaakt); 13 cijfers
        // als tekst, null = niet vastgelegd. Historisch: een latere artikelwijziging verandert dit nooit. Zoals opgegeven;
        // certificeringsstatus niet gecontroleerd.
        public string Ggn { get; set; }

        // Lopende afboeking door verzendingen (som van Dispatch-mutaties; alleen stijgend, nooit boven de ontvangst).
        // Quantity/NetWeightKg blijven de historische ontvangst; voorraad = ontvangst - afgeboekt.
        public decimal DispatchedQuantity { get; set; }
        public decimal DispatchedNetWeightKg { get; set; }

        // Herkomst: null = fysieke ontvangst; anders de correctieregel waaruit deze inhoud is teruggeboekt (onveranderlijk, uniek).
        // Zulke inhoud telt niet mee als registratie tegen de bronregel: die goederen zijn al bij de oorspronkelijke regel geteld.
        public int? OriginCorrectionLineId { get; set; }
        public SalesDispatchCorrectionLine OriginCorrectionLine { get; set; }

        public string QualityStatus { get; set; }  // Quarantine, Approved, Blocked, Rejected
        public string QualityReason { get; set; }  // optioneel

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Optimistisch concurrency-token: start op 0 en wordt centraal in AppDbContext verhoogd bij elke wijziging.
        public int Version { get; set; }
    }
}
