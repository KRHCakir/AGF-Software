namespace AgfAdmin.DTOs
{
    // Filters voor het beschikbaarheidsoverzicht (lot, category, qualityStatus en includeClosed alleen in de drill-down).
    public sealed class AvailabilityQuery
    {
        public int? ProductVariantId { get; set; }
        public int? CustomerId { get; set; }
        public string Route { get; set; }
        public string Unit { get; set; }
        public string Lot { get; set; }
        public string Category { get; set; }
        public string QualityStatus { get; set; }
        public bool IncludeClosed { get; set; }
    }

    // Wat wel en niet in de berekening zit; expliciet zodat een latere UI het niet verkeerd kan lezen.
    public sealed class AvailabilityBasisDto
    {
        public string Source { get; set; } = "SupplyCommitmentLines + PalletContentLines";
        public bool ReservationsApplied { get; set; }
        public bool LegacyInventoryIncluded { get; set; }
        public bool StockLedgerIncluded { get; set; }
    }

    public sealed class AvailabilityOverviewDto
    {
        public DateTime GeneratedAt { get; set; }
        public AvailabilityBasisDto Basis { get; set; } = new();
        public List<VariantAvailabilityDto> Variants { get; set; } = new();
    }

    // Geen totaal per variant: hoeveelheden bestaan alleen per eenheid en worden nooit omgerekend.
    public sealed class VariantAvailabilityDto
    {
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public List<WarehouseSupplyDto> WarehouseSupply { get; set; } = new();
        public List<DirectTradeSupplyDto> DirectTradeSupply { get; set; } = new();
    }

    // Werkelijke hoeveelheid en werkelijk netto-kg, opgeteld uit inhoudsregels.
    public sealed class AvailabilityQuantityDto
    {
        public decimal Quantity { get; set; }
        public decimal NetWeightKg { get; set; }
    }

    public sealed class QualityBreakdownDto
    {
        public AvailabilityQuantityDto Approved { get; set; } = new();
        public AvailabilityQuantityDto Quarantine { get; set; } = new();
        public AvailabilityQuantityDto Blocked { get; set; } = new();
        public AvailabilityQuantityDto Rejected { get; set; } = new();
    }

    // Verwacht maar nog niet op een pallet geregistreerd: geen kwaliteit en nooit een (geschat) gewicht.
    public sealed class ExpectedUnregisteredDto
    {
        public decimal Quantity { get; set; }
        public decimal? NetWeightKg => null;
    }

    // Active Physical verkoopreserveringen op inhoud die nu Approved is en Received + Warehouse.
    // Legacy-reserveringen (OrderLineId) tellen nooit mee.
    public sealed class ReservedDto
    {
        public decimal Quantity { get; set; }
        public decimal NetWeightKg { get; set; }
        public bool Applied => true;
    }

    public sealed class AvailabilityWarningDto
    {
        public string Code { get; set; }
        public int? SupplyCommitmentLineId { get; set; }
        public int? PalletContentLineId { get; set; }
        public decimal? Quantity { get; set; }
    }

    public sealed class WarehouseSupplyDto
    {
        public string Unit { get; set; }
        // Received + Warehouse + Approved + route Warehouse; nog zonder aftrek van reserveringen.
        public AvailabilityQuantityDto AvailableBeforeReservations { get; set; } = new();
        public ReservedDto Reserved { get; set; } = new();
        // Active Physical verkoopreserveringen op inhoud die niet meer Approved is (Blocked, Rejected): wordt nooit afgetrokken.
        public AvailabilityQuantityDto ReservedOnHold { get; set; } = new();
        // availableBeforeReservations - reserved, per eenheid; nooit negatief.
        public AvailabilityQuantityDto AvailableAfterReservations { get; set; } = new();
        public QualityBreakdownDto Warehouse { get; set; } = new();
        public QualityBreakdownDto ReceivedOutsideWarehouse { get; set; } = new();
        public QualityBreakdownDto InTransit { get; set; } = new();
        public QualityBreakdownDto ExpectedOnPallets { get; set; } = new();
        public ExpectedUnregisteredDto ExpectedUnregistered { get; set; } = new();
        public List<AvailabilityWarningDto> Warnings { get; set; } = new();
    }

    // Klantgebonden aanvoer. Bewust zonder "available", "warehouse" of "reserved": nooit algemeen verkoopbaar.
    public sealed class DirectTradeSupplyDto
    {
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Unit { get; set; }
        public bool CustomerBound => true;
        public QualityBreakdownDto ReceivedOutsideWarehouse { get; set; } = new();
        public QualityBreakdownDto InTransit { get; set; } = new();
        public QualityBreakdownDto ExpectedOnPallets { get; set; } = new();
        public ExpectedUnregisteredDto ExpectedUnregistered { get; set; } = new();
        public List<AvailabilityWarningDto> Warnings { get; set; } = new();
    }

    public sealed class AvailabilityDrillDownDto
    {
        public DateTime GeneratedAt { get; set; }
        public AvailabilityBasisDto Basis { get; set; } = new();
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public List<SourceLineAvailabilityDto> SourceLines { get; set; } = new();
        public List<ContentLineAvailabilityDto> ContentLines { get; set; } = new();
    }

    public sealed class SourceLineAvailabilityDto
    {
        public int SupplyCommitmentLineId { get; set; }
        public int SupplyCommitmentId { get; set; }
        public string Route { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string CommitmentStatus { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public string Unit { get; set; }
        public string LotNumber { get; set; }
        public decimal ExpectedQuantity { get; set; }
        // Opgegeven verwacht gewicht van de bron; nooit opgeteld bij gemeten kg.
        public decimal? DeclaredExpectedNetWeightKg { get; set; }
        // Null als de bron geen eenheid heeft (legacy): dan valt niets betrouwbaar af te trekken.
        public decimal? RegisteredQuantity { get; set; }
        public decimal UnregisteredQuantity { get; set; }
        public List<AvailabilityWarningDto> Warnings { get; set; } = new();
    }

    public sealed class ContentLineAvailabilityDto
    {
        public int PalletContentLineId { get; set; }
        public int PalletId { get; set; }
        public string PalletReference { get; set; }
        public string Category { get; set; }
        public string LogisticsStatus { get; set; }
        public string LocationContext { get; set; }
        public string WarehouseLocation { get; set; }
        public string Route { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public int SupplyCommitmentLineId { get; set; }
        public string LotNumber { get; set; }
        public string Unit { get; set; }
        // Historische ontvangst; wordt nooit gewijzigd door verzendingen.
        public decimal Quantity { get; set; }
        public decimal NetWeightKg { get; set; }
        // Afgeboekt door verzendingen en de huidige voorraad (ontvangst - afgeboekt).
        public decimal DispatchedQuantity { get; set; }
        public decimal DispatchedNetWeightKg { get; set; }
        public decimal OnHandQuantity { get; set; }
        public decimal OnHandNetWeightKg { get; set; }
        // Afgeleid: voorraad én kg zijn allebei 0. Zegt niets over de fysieke pallet.
        public bool Depleted { get; set; }
        public string QualityStatus { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal ReservedNetWeightKg { get; set; }
        // Gereserveerd op inhoud die niet meer Approved is.
        public bool OnHold { get; set; }
        // Nog te reserveren: alleen bij Approved inhoud op Received + Warehouse; anders 0.
        public decimal AvailableQuantity { get; set; }
        public decimal AvailableNetWeightKg { get; set; }
        public bool SellableNow { get; set; }
        public bool Closed { get; set; }
    }
}
