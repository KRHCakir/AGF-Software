namespace AgfAdmin.DTOs
{
    public sealed class CreateSalesOrderDto
    {
        public Guid? RequestId { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerReference { get; set; }
        public DateTimeOffset? RequestedDeliveryDate { get; set; }
        public string ActorReference { get; set; }
        public List<CreateSalesOrderLineDto> Lines { get; set; }
    }

    public sealed class CreateSalesOrderLineDto
    {
        public int? ProductVariantId { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
    }

    public sealed class SalesOrderFilter
    {
        public int? CustomerId { get; set; }
        public string Status { get; set; }
    }

    public sealed class SalesOrderDto
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Status { get; set; }
        public string CustomerReference { get; set; }
        public DateTime RequestedDeliveryDate { get; set; }
        public string ActorReference { get; set; }
        public DateTime CreatedAt { get; set; }
        public int Version { get; set; }
        // Alleen gevuld bij Status Cancelled; de actor komt uit het SalesOrderCancelled-event.
        public DateTime? CancelledAt { get; set; }
        public string CancelReason { get; set; }
        public string CancelledBy { get; set; }
        // Afgeleid: elke regel volledig verzonden. De status blijft Open; er is geen aparte vervulde status.
        public bool Fulfilled { get; set; }
        public List<SalesOrderLineDto> Lines { get; set; } = new();
    }

    // Annuleren van een Open order: alle Active reserveringen (ook on hold) worden in dezelfde transactie vrijgegeven.
    public sealed class CancelSalesOrderDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedOrderVersion { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    public sealed class SalesOrderAuditEventDto
    {
        public int Id { get; set; }
        public int SalesOrderId { get; set; }
        public string EventType { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Reason { get; set; }
        public string ActorReference { get; set; }
        public DateTime OccurredAt { get; set; }
        public Guid? RequestId { get; set; }
    }

    public sealed class SalesOrderLineDto
    {
        public int Id { get; set; }
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        // Alleen Active Physical verkoopreserveringen (magazijnvoorraad); nooit omgerekend.
        public decimal ReservedQuantity { get; set; }
        public decimal ReservedNetWeightKg { get; set; }
        // Alleen Active DirectTrade-toewijzingen: verwachte, klantgebonden aanvoer. Geen voorraad, geen kg, nooit "gereed".
        public decimal AllocatedQuantity { get; set; }
        // Verbruikt (Consumed) door verzendingen; telt niet meer als gereserveerd en komt nooit terug als vrije voorraad.
        public decimal DispatchedQuantity { get; set; }
        public decimal DispatchedNetWeightKg { get; set; }
        // Deel van verzonden dat via DirectTrade is verzonden (leverancier -> klant); de kg daarvan zit in DispatchedNetWeightKg.
        public decimal DirectTradeShippedQuantity { get; set; }
        // Open = gevraagd - gereserveerd (Physical) - toegewezen (DirectTrade) - verzonden.
        public decimal OpenQuantity { get; set; }
        // Afgeleid: verzonden == gevraagd.
        public bool Fulfilled { get; set; }
        public int Version { get; set; }
        public List<StockReservationDto> Reservations { get; set; } = new();
    }

    public sealed class StockReservationDto
    {
        public int Id { get; set; }
        public Guid? RequestId { get; set; }
        public int SalesOrderId { get; set; }
        public int SalesOrderLineId { get; set; }
        // Alleen bij Source Physical; een DirectTrade-toewijzing staat nooit op een pallet (null).
        public int? PalletContentLineId { get; set; }
        public int? PalletId { get; set; }
        public string PalletReference { get; set; }
        public string WarehouseLocation { get; set; }
        // Partij: van de inhoudsregel (Physical) of van de bronregel (DirectTrade); kan leeg zijn.
        public string LotNumber { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        // Gemeten netto kg; null bij een DirectTrade-toewijzing (er is geen gemeten kg, en er wordt nooit kg afgeleid).
        public decimal? NetWeightKg { get; set; }
        public string Source { get; set; }
        // Bron van de reservering of toewijzing; bij DirectTrade ook de toezegging met leverancier, herkomst, bestemming en verwachte datum.
        public int SupplyCommitmentLineId { get; set; }
        public int SupplyCommitmentId { get; set; }
        public string VendorName { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public DateTime? ExpectedAt { get; set; }
        // Alleen bij een verzonden (Consumed) DirectTrade-toewijzing: de verzending met werkelijke datum, kg en partij.
        // Verzonden betekent niet afgeleverd: een afleverbevestiging bestaat (nog) niet.
        public int? DirectTradeShipmentId { get; set; }
        public DateOnly? ShippedOn { get; set; }
        public decimal? ShippedNetWeightKg { get; set; }
        public string ShippedLotNumber { get; set; }
        // Alleen bij een teruggenomen DirectTrade-verzending (Corrected): de DirectTrade-correctie. Verzenddatum, kg en partij hierboven
        // blijven als historie zichtbaar; de hoeveelheid telt niet meer als verzonden of als bezette broncapaciteit.
        public int? DirectTradeCorrectionId { get; set; }
        public string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ReleasedAt { get; set; }
        public string ReleaseReason { get; set; }
        public int Version { get; set; }
        // Actuele kwaliteit van de inhoudsregel (nooit afgeleid uit de reserveringsstatus).
        public string ContentQualityStatus { get; set; }
        // Active reservering op inhoud die niet (meer) Approved is: telt niet als beschikbaar en wacht op vrijgave of herkeuring.
        public bool OnHold { get; set; }
        // Alleen bij Consumed: wanneer en met welke verzending.
        public DateTime? ConsumedAt { get; set; }
        public int? DispatchId { get; set; }
        public string DispatchReference { get; set; }
        // Alleen bij Corrected: de verzendcorrectie heeft deze reservering teruggeboekt (eindtoestand).
        public DateTime? CorrectedAt { get; set; }
        public int? CorrectionId { get; set; }
    }

    // Inhoudsregel die voor een orderregel te reserveren is (zelfde variant + eenheid, Approved, Received + Warehouse).
    public sealed class ReservableStockLineDto
    {
        public int PalletContentLineId { get; set; }
        public int PalletId { get; set; }
        public string PalletReference { get; set; }
        public string WarehouseLocation { get; set; }
        public string LotNumber { get; set; }
        public string Unit { get; set; }
        // Historische ontvangst.
        public decimal Quantity { get; set; }
        public decimal NetWeightKg { get; set; }
        // Voorraad = ontvangst - afgeboekt; beschikbaar = voorraad - Active gereserveerd.
        public decimal DispatchedQuantity { get; set; }
        public decimal DispatchedNetWeightKg { get; set; }
        public decimal OnHandQuantity { get; set; }
        public decimal OnHandNetWeightKg { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal ReservedNetWeightKg { get; set; }
        public decimal AvailableQuantity { get; set; }
        public decimal AvailableNetWeightKg { get; set; }
        public string QualityStatus { get; set; }
        public int ContentLineVersion { get; set; }
    }

    // Verzenden: gekozen Active reserveringen in hun geheel verbruiken; hoeveelheid, eenheid en kg komen uit de reservering.
    public sealed class CreateSalesDispatchDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedOrderVersion { get; set; }
        public string ActorReference { get; set; }
        public string Reference { get; set; }
        public List<CreateSalesDispatchLineDto> Lines { get; set; }
    }

    public sealed class CreateSalesDispatchLineDto
    {
        public int? StockReservationId { get; set; }
        public int? ExpectedReservationVersion { get; set; }
    }

    public sealed class SalesDispatchDto
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }
        public int SalesOrderId { get; set; }
        public string Reference { get; set; }
        public string ActorReference { get; set; }
        public DateTime DispatchedAt { get; set; }
        // Gevuld zodra de verzending administratief is gecorrigeerd; de verzending zelf blijft ongewijzigd.
        public SalesDispatchCorrectionDto Correction { get; set; }
        public List<SalesDispatchLineDto> Lines { get; set; } = new();
    }

    // Correctie: alleen voor goederen die het magazijn niet hebben verlaten (door de gebruiker bevestigd).
    public sealed class CorrectSalesDispatchDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedOrderVersion { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
        public bool? GoodsNeverLeftWarehouse { get; set; }
        public List<CreateSalesDispatchLineDto> Lines { get; set; }
    }

    public sealed class SalesDispatchCorrectionDto
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }
        public int SalesDispatchId { get; set; }
        public int SalesOrderId { get; set; }
        public string Reason { get; set; }
        public string ActorReference { get; set; }
        public bool GoodsNeverLeftWarehouseConfirmed { get; set; }
        public DateTime CorrectedAt { get; set; }
        public List<SalesDispatchCorrectionLineDto> Lines { get; set; } = new();
    }

    public sealed class SalesDispatchCorrectionLineDto
    {
        public int Id { get; set; }
        public int SalesDispatchLineId { get; set; }
        public int StockReservationId { get; set; }
        public int RestoredPalletContentLineId { get; set; }
        public int PalletId { get; set; }
        public string PalletReference { get; set; }
        public string LotNumber { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        public string RestoredQualityStatus { get; set; }
        public int StockMovementId { get; set; }
    }

    public sealed class SalesDispatchLineDto
    {
        public int Id { get; set; }
        public int StockReservationId { get; set; }
        public int SalesOrderLineId { get; set; }
        public int PalletContentLineId { get; set; }
        public int PalletId { get; set; }
        public string PalletReference { get; set; }
        public string LotNumber { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        public int StockMovementId { get; set; }
    }

    // Hoeveelheid, eenheid en netto-kg zijn altijd expliciet; er wordt niets afgeleid of omgerekend.
    public sealed class CreateStockReservationDto
    {
        public Guid? RequestId { get; set; }
        public int? PalletContentLineId { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
        public decimal? NetWeightKg { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ReleaseStockReservationDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedReservationVersion { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    // Klantgebonden DirectTrade-bronregel die aan een orderregel kan worden toegewezen: zelfde klant als de order, zelfde variant
    // en expliciete eenheid, toezegging in een toewijsbare status. Verwachte aanvoer: geen voorraad, geen kwaliteit, geen gemeten kg.
    public sealed class DirectTradeSupplyLineDto
    {
        public int SupplyCommitmentLineId { get; set; }
        public int SupplyCommitmentId { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string VendorName { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public string CommitmentStatus { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public string LotNumber { get; set; }
        public string Unit { get; set; }
        public decimal ExpectedQuantity { get; set; }
        // Door de leverancier opgegeven verwacht gewicht van de HELE bronregel; nooit het gewicht van een (deel)toewijzing en nooit opgeteld bij gemeten kg.
        public decimal? DeclaredExpectedNetWeightKg { get; set; }
        // Toegewezen = Active + verzonden (Consumed) DirectTrade-toewijzingen van alle orders; resterend = verwacht - toegewezen.
        public decimal AllocatedQuantity { get; set; }
        public decimal RemainingQuantity { get; set; }
        public decimal AllocatedToThisLineQuantity { get; set; }
        public int SourceLineVersion { get; set; }
    }

    // Toewijzen van verwachte, klantgebonden aanvoer aan een orderregel. Alleen hoeveelheid en eenheid; nooit kg.
    // De gebruiker bevestigt expliciet dat dit verwachte aanvoer is (niet in ons magazijn, niet verzonden); zonder bevestiging 400.
    public sealed class CreateDirectTradeAllocationDto
    {
        public Guid? RequestId { get; set; }
        public int? SupplyCommitmentLineId { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
        public bool? AcknowledgeExpectedSupply { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
    }

    // DirectTrade-verzending: hele Active toewijzingen van één order verbruiken met de werkelijke hoeveelheid, eenheid, netto kg en partij.
    public sealed class CreateDirectTradeShipmentDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedOrderVersion { get; set; }
        public string ActorReference { get; set; }
        // Kalenderdatum (yyyy-MM-dd) zonder tijdstip; niet na vandaag in Europe/Amsterdam.
        public DateOnly? ShippedOn { get; set; }
        public string Reference { get; set; }
        public List<CreateDirectTradeShipmentLineDto> Lines { get; set; }
    }

    public sealed class CreateDirectTradeShipmentLineDto
    {
        public int? StockReservationId { get; set; }
        public int? ExpectedReservationVersion { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
        public decimal? NetWeightKg { get; set; }
        public string LotNumber { get; set; }
        public string LotDeviationReason { get; set; }
    }

    public sealed class DirectTradeShipmentDto
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }
        public int SalesOrderId { get; set; }
        public int CustomerId { get; set; }
        // Snapshot bij verzending; verandert niet als de klantnaam later wijzigt.
        public string CustomerName { get; set; }
        public DateOnly ShippedOn { get; set; }
        public string Reference { get; set; }
        public string ActorReference { get; set; }
        public DateTime RecordedAt { get; set; }
        // Het systeem kent geen afleverbevestiging; dit veld zegt alleen dat er (nog) geen is vastgelegd.
        public bool DeliveryRecorded => false;
        // Gevuld zodra de verzending administratief is teruggenomen; de verzending zelf blijft ongewijzigd.
        public DirectTradeShipmentCorrectionDto Correction { get; set; }
        public List<DirectTradeShipmentLineDto> Lines { get; set; } = new();
    }

    // Terugnemen van een volledige DirectTrade-verzending die niet heeft plaatsgevonden. Alle toewijzingen van de verzending met hun
    // verwachte versies; de verklaring is verplicht (shipmentDidNotTakePlace = true).
    public sealed class CorrectDirectTradeShipmentDto
    {
        public Guid? RequestId { get; set; }
        public int? ExpectedOrderVersion { get; set; }
        public string ActorReference { get; set; }
        public string Reason { get; set; }
        public bool? ShipmentDidNotTakePlace { get; set; }
        public List<CreateSalesDispatchLineDto> Lines { get; set; }
    }

    public sealed class DirectTradeShipmentCorrectionDto
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }
        public int DirectTradeShipmentId { get; set; }
        public int SalesOrderId { get; set; }
        public string Reason { get; set; }
        public string ActorReference { get; set; }
        public bool ShipmentDidNotTakePlaceConfirmed { get; set; }
        public string ConfirmationCode { get; set; }
        public string ConfirmationStatement { get; set; }
        public DateTime CorrectedAt { get; set; }
        public List<DirectTradeShipmentCorrectionLineDto> Lines { get; set; } = new();
    }

    public sealed class DirectTradeShipmentCorrectionLineDto
    {
        public int Id { get; set; }
        public int DirectTradeShipmentLineId { get; set; }
        public int StockReservationId { get; set; }
        public int SupplyCommitmentLineId { get; set; }
        public int SalesOrderLineId { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
    }

    public sealed class DirectTradeShipmentLineDto
    {
        public int Id { get; set; }
        public int StockReservationId { get; set; }
        public int SalesOrderLineId { get; set; }
        public int SupplyCommitmentLineId { get; set; }
        public int SupplyCommitmentId { get; set; }
        public int VendorId { get; set; }
        // Snapshots bij verzending.
        public string VendorName { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        public string LotNumber { get; set; }
        public string SourceLotNumber { get; set; }
        public string LotDeviationReason { get; set; }
    }
}
