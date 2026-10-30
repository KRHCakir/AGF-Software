namespace AgfAdmin.DTOs
{
    public sealed class CreatePalletDto
    {
        // Idempotentiesleutel (aanbevolen). Verplicht als er geen eigen palletnummer wordt opgegeven, zodat een herhaling
        // zonder nummer dezelfde pallet teruggeeft in plaats van een tweede met een nieuw gegenereerd nummer.
        public Guid? RequestId { get; set; }
        // Eigen palletnummer (optioneel). Leeg: de server genereert een uniek, herkenbaar nummer (P-000123).
        public string Reference { get; set; }
        public string Sscc { get; set; }
        public string PalletType { get; set; }
        public decimal? TareKg { get; set; }
        public decimal? GrossKg { get; set; }
        public decimal? DeclaredNetKg { get; set; }
        public string LogisticsStatus { get; set; }
        public string LocationContext { get; set; }
        public string WarehouseLocation { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    // Geen kwaliteitsstatus: een nieuwe inhoudsregel staat altijd in Quarantine.
    public sealed class AddPalletContentLineDto
    {
        public Guid? RequestId { get; set; }
        public int? SupplyCommitmentLineId { get; set; }
        public string LotNumber { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
        // Weighed (standaard, ook voor oudere clients): NetWeightKg verplicht, gewogen. Calculated: NetKgPerPackage verplicht;
        // NetWeightKg mag weg (server berekent) of moet exact aantal × kg per verpakking zijn.
        public string WeightMethod { get; set; }
        public decimal? NetKgPerPackage { get; set; }
        public decimal? NetWeightKg { get; set; }
        // GGN van de partij (optioneel, 13 cijfers als tekst): het voorstel uit het artikel is al door de client overgenomen of
        // bewust aangepast/leeggemaakt; de server bewaart wat hier staat.
        public string Ggn { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
        // Afwijkende ontvangst: de werkelijk ontvangen variant als die anders is dan die van de bronregel. Leeg of gelijk = gewone
        // ontvangst. Anders weigert de server met 409 DEVIATION_CONFIRMATION_REQUIRED (met het verschil) tot dezelfde aanvraag
        // terugkomt met ConfirmDeviation = true; de reden is optioneel (toelichting verplicht bij Other).
        public int? ReceivedProductVariantId { get; set; }
        public bool? ConfirmDeviation { get; set; }
        public string DeviationReason { get; set; }
        public string DeviationReasonNote { get; set; }
    }

    public sealed class PalletDto
    {
        public int Id { get; set; }
        public string Reference { get; set; }
        public string Sscc { get; set; }
        public string PalletType { get; set; }
        public decimal? TareKg { get; set; }
        public decimal? GrossKg { get; set; }
        public decimal? DeclaredNetKg { get; set; }
        // Som van de werkelijk ingevoerde netto-kg van de inhoudsregels; niet opgeslagen en nooit geschat.
        // Som van de voorraad-kg van de inhoudsregels (ontvangst minus afgeboekt): het fysieke gewicht op de pallet.
        public decimal ContentNetKg { get; set; }
        // Som van de historische ontvangst-kg van fysiek ontvangen inhoud (zonder teruggeboekte correctie-inhoud).
        public decimal ReceivedNetKg { get; set; }
        // Afgeleid: minstens één inhoudsregel en elke regel is uitgeput (voorraad en kg 0). Zegt niets over de fysieke pallet.
        public bool Depleted { get; set; }
        public string LogisticsStatus { get; set; }
        public string LocationContext { get; set; }
        public string WarehouseLocation { get; set; }
        public DateTime CreatedAt { get; set; }
        public int Version { get; set; }
        public List<PalletContentLineDto> ContentLines { get; set; } = new();
    }

    public sealed class PalletContentLineDto
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }
        public int PalletId { get; set; }
        public int SupplyCommitmentLineId { get; set; }
        public int SupplyCommitmentId { get; set; }
        public string Route { get; set; }
        public int? CustomerId { get; set; }
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public string LotNumber { get; set; }
        // Historische ontvangst.
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        // Weighed of Calculated; bij Calculated is NetWeightKg = Quantity × NetKgPerPackage (etiket), nooit gewogen.
        public string WeightMethod { get; set; }
        public decimal? NetKgPerPackage { get; set; }
        // GGN zoals bij ontvangst vastgelegd (null = niet vastgelegd); zoals opgegeven, certificeringsstatus niet gecontroleerd.
        public string Ggn { get; set; }
        // Afgeboekt door verzendingen en de huidige voorraad; uitgeput als voorraad en kg allebei 0 zijn.
        public decimal DispatchedQuantity { get; set; }
        public decimal DispatchedNetWeightKg { get; set; }
        public decimal OnHandQuantity { get; set; }
        public decimal OnHandNetWeightKg { get; set; }
        public bool Depleted { get; set; }
        // Receipt (fysieke ontvangst) of DispatchCorrection (teruggeboekt uit een gecorrigeerde verzending; telt niet als registratie).
        public string Origin { get; set; }
        public int? OriginCorrectionLineId { get; set; }
        // RejectionSplit: afgesplitst door een deelafkeuring (OriginRejectionLineId); Split-off = wat van deze regel is afgesplitst.
        public int? OriginRejectionLineId { get; set; }
        public decimal SplitOffQuantity { get; set; }
        public decimal SplitOffNetWeightKg { get; set; }
        public string QualityStatus { get; set; }
        public string QualityReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public int Version { get; set; }
        // Huidige versie van de pallet: elke wijziging van een regel raakt ook de pallet.
        public int PalletVersion { get; set; }
    }

    // Expliciete commando's: elk met een idempotentiesleutel, de verwachte versie en een actor.
    public sealed class ChangeLogisticsStatusDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedPalletVersion { get; set; }
        public string ToStatus { get; set; }
        // Optioneel: een nieuwe locatie die tegelijk met de status ingaat (bijvoorbeeld bij dispatch uit het magazijn).
        public string LocationContext { get; set; }
        public string WarehouseLocation { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ChangePalletLocationDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedPalletVersion { get; set; }
        public string LocationContext { get; set; }
        public string WarehouseLocation { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ChangeQualityStatusDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedContentLineVersion { get; set; }
        public string ToStatus { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    public sealed class PalletAuditEventDto
    {
        public int Id { get; set; }
        public int? PalletId { get; set; }
        public int? PalletContentLineId { get; set; }
        public string EventType { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Reason { get; set; }
        public string ActorReference { get; set; }
        public DateTime OccurredAt { get; set; }
        public Guid? RequestId { get; set; }
    }
}
