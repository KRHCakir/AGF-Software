namespace AgfAdmin.DTOs
{
    public sealed class CreateSupplyCommitmentDto
    {
        public Guid? RequestId { get; set; }
        public int VendorId { get; set; }
        public int? CustomerId { get; set; }
        public string Route { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public DateTimeOffset? ExpectedAt { get; set; }
        public List<CreateSupplyCommitmentLineDto> Lines { get; set; }
    }

    public sealed class CreateSupplyCommitmentLineDto
    {
        public int ProductVariantId { get; set; }
        public decimal? ExpectedQuantity { get; set; }
        public string ExpectedUnit { get; set; }
        public decimal? ExpectedNetWeightKg { get; set; }
        // Optioneel: verwachte bruto kg (bruto >= netto) en de bron van de kg (Calculated of Entered; weggelaten = Entered, zoals oudere opdrachten met alleen netto kg).
        public decimal? ExpectedGrossWeightKg { get; set; }
        public string WeightSource { get; set; }
        public string LotNumber { get; set; }
        // Optioneel, beide of geen: ExpectedQuantity mag dan weg (server berekent) of moet exact PalletCount × PackagesPerPallet + LoosePackages zijn.
        // Nieuwe invoer: hele pallets en een heel aantal per pallet; LoosePackages (heel, >= 0) alleen samen met palletinvoer.
        public decimal? PalletCount { get; set; }
        public decimal? PackagesPerPallet { get; set; }
        public decimal? LoosePackages { get; set; }
    }

    public sealed class SupplyCommitmentFilter
    {
        public string Route { get; set; }
        public string Status { get; set; }
        public int? VendorId { get; set; }
        public int? CustomerId { get; set; }
    }

    public sealed class SupplyCommitmentDto
    {
        public int Id { get; set; }
        public Guid RequestId { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Route { get; set; }
        public string Status { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        // Actieve GGN's van de leverancier (met producentnaam): invoerhulp bij ontvangst; de gekozen waarde staat op de inhoudsregel.
        public List<VendorGgnOptionDto> VendorGgns { get; set; } = new();
        public List<SupplyCommitmentLineDto> Lines { get; set; } = new();
    }

    public sealed class VendorGgnOptionDto
    {
        public string Ggn { get; set; }
        public string ProducerName { get; set; }
        // Laatste handmatige controle in de GLOBALG.A.P.-portal en geldigheid van het certificaat (zoals ingevoerd): de ontvangst
        // waarschuwt bij een verlopen of nooit gecontroleerd certificaat. Nooit blokkerend.
        public DateOnly? CheckedOn { get; set; }
        public DateOnly? CertificateValidUntil { get; set; }
        public bool HasGgnLabel { get; set; }
    }

    public sealed class SupplyCommitmentLineDto
    {
        public int Id { get; set; }
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public string LotNumber { get; set; }
        public decimal ExpectedQuantity { get; set; }
        public string ExpectedUnit { get; set; }
        // Verwacht gewicht van de hele bronregel (nooit het gewicht van een deeltoewijzing); WeightSource zegt of het berekend
        // (aantal × artikelstandaard) of opgegeven is. Null bron = geen kg.
        public decimal? ExpectedNetWeightKg { get; set; }
        public decimal? ExpectedGrossWeightKg { get; set; }
        public string WeightSource { get; set; }
        // Gewichtsmethode en standaard kg per verpakking van de variant: invoerhulp bij ontvangst, per regel aanpasbaar.
        public string WeightMethod { get; set; }
        public decimal? DefaultNetKgPerPackage { get; set; }
        // Standaard bruto kg per verpakking van het artikel: alleen voor de rekenregel bij ontvangst ("× 10,3 kg = … bruto").
        public decimal? DefaultGrossKgPerPackage { get; set; }
        // Oorspronkelijke palletinvoer van de regel (null bij gewone invoer) en de standaard van de variant als invoerhulp.
        public decimal? PalletCount { get; set; }
        public decimal? PackagesPerPallet { get; set; }
        public decimal? LoosePackages { get; set; }
        public decimal? DefaultPackagesPerPallet { get; set; }
        // Alleen bij route DirectTrade: som van Active toewijzingen aan verkooporders en wat daarvan nog toewijsbaar is.
        // Null bij route Warehouse: magazijnaanvoer heeft geen toewijsbare verwachte capaciteit (die wordt pas voorraad na ontvangst en keuring).
        public decimal? DirectTradeAllocatedQuantity { get; set; }
        public decimal? DirectTradeRemainingQuantity { get; set; }
    }
}
