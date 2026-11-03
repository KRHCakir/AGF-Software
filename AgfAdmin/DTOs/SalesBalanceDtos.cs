namespace AgfAdmin.DTOs
{
    // Stand van één variant in één eenheid voor de verkoop (SalesBalanceCalculator). Vrij te verkopen mag negatief zijn: dat is een tekort,
    // een waarschuwing en geen blokkade. Vrij op voorraad is de grens voor een regel "Uit voorraad".
    public sealed class SalesBalanceDto
    {
        public int ProductVariantId { get; set; }
        public string Unit { get; set; }
        // Goedgekeurde inhoud in het eigen magazijn (route Warehouse); quarantaine, geblokkeerd en afgekeurd tellen niet.
        public decimal Stock { get; set; }
        // Open inkoop (route Warehouse): nog niet ontvangen plus ontvangen maar nog in keuring. Klantgebonden aanvoer telt niet.
        public decimal Expected { get; set; }
        public decimal ExpectedNotReceived { get; set; }
        public decimal ExpectedInInspection { get; set; }
        // Open verkooporderregels (besteld min verzonden), gereserveerd en niet gereserveerd; waarvan gereserveerd.
        public decimal Sold { get; set; }
        public decimal SoldReserved { get; set; }
        // Voorraad + Verwacht - Verkocht.
        public decimal Free { get; set; }
        // Voorraad - gereserveerd - niet gereserveerd verkocht uit voorraad; kan door gelijktijdige orders of een blokkade onder nul komen.
        public decimal FreeStock { get; set; }
        // Tekort = max(0, -Vrij); Shortage = er is een tekort.
        public decimal ShortageQuantity { get; set; }
        public bool Shortage => ShortageQuantity > 0m;
    }

    // Een deel van een verwachte inkooporder dat (een deel van) een orderregel dekt.
    public sealed class ExpectedCoverDto
    {
        public int SupplyCommitmentId { get; set; }
        public string SupplyOrderNumber { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public decimal Quantity { get; set; }
        // Al ontvangen, nog in keuring (quarantaine).
        public bool InInspection { get; set; }
    }

    // Dekking van het niet-gereserveerde deel van een open orderregel, oudste verkoop eerst: nu te reserveren uit vrije voorraad, uit
    // verwachte aanvoer (per inkooporder) of tekort. Gereserveerd staat al op de regel zelf.
    public sealed class SalesLineCoverageDto
    {
        public decimal FromStock { get; set; }
        public List<ExpectedCoverDto> Expected { get; set; } = new();
        public decimal Shortage { get; set; }
    }
}
