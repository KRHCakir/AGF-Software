namespace AgfAdmin.DTOs
{
    public sealed class CreatePalletDto
    {
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
        public decimal? NetWeightKg { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
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
        public decimal ContentNetKg { get; set; }
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
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
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
