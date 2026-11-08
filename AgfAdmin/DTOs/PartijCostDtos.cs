namespace AgfAdmin.DTOs
{
    // Verdeling van een inkoopfactuurregel over partijen (PartijCostService). Bedragen op partijen excl. btw, altijd in euro; een
    // factuur in een andere valuta wordt omgerekend met de ECB-koers van de factuur (InvoiceExchangeRateService).
    public sealed class SaveLineAllocationDto
    {
        public Guid? RequestId { get; set; }
        // Versie van de verdeling die de gebruiker zag (InvoiceLineAllocationDto.Version); een gelijktijdige wijziging geeft 409.
        public int? ExpectedVersion { get; set; }
        // Geen standaardwaarde: een ontbrekende of null-lijst is een validatiefout; alleen een expliciete lege lijst wist de verdeling.
        public List<SaveLineAllocationItemDto> Allocations { get; set; }
    }

    public sealed class SaveLineAllocationItemDto
    {
        public int? PartijId { get; set; }
        public decimal? Amount { get; set; }
    }

    public sealed class InvoiceAllocationsDto
    {
        public int InvoiceId { get; set; }
        public string Currency { get; set; }
        // Bij een factuur in een andere valuta: de ECB-koers (eenheden valuta per 1 euro), de publicatiedag en de bron; leeg bij EUR of
        // zolang er geen koers is.
        public decimal? ExchangeRate { get; set; }
        public DateOnly? ExchangeRateDate { get; set; }
        public string ExchangeRateSource { get; set; }
        // True zodra er kosten op partijen staan: dan ligt de koers vast (regel 26).
        public bool RateLocked { get; set; }
        // False bij een factuur in een andere valuta zonder koers: dan kan er (nog) niet verdeeld worden.
        public bool CanAllocate { get; set; }
        public List<InvoiceLineAllocationDto> Lines { get; set; } = new();
    }

    public sealed class InvoiceLineAllocationDto
    {
        public int LineId { get; set; }
        public string Description { get; set; }
        public string Kind { get; set; }
        // Regelbedrag excl. btw in de valuta van de factuur.
        public decimal LineTotal { get; set; }
        // Hetzelfde in euro (de grens voor de verdeling): bij EUR gelijk aan LineTotal; null bij een andere valuta zonder koers.
        public decimal? LineTotalEur { get; set; }
        // Verdeeld, in euro.
        public decimal Allocated { get; set; }
        // Wat niet verdeeld is, in euro: algemene kosten (waarschuwing, geen blokkade); null zonder koers.
        public decimal? General { get; set; }
        public int Version { get; set; }
        public List<LineAllocationDto> Allocations { get; set; } = new();
        public List<AllocationHistoryDto> History { get; set; } = new();
    }

    public sealed class LineAllocationDto
    {
        public int PartijId { get; set; }
        public string Number { get; set; }
        public string PartijKind { get; set; }
        public decimal Amount { get; set; }
        // Wat het is (leverancier of klant, product) en de basis voor verdelen: ontvangen (inkoop) of besteld (verkoop) en netto kg.
        public string Party { get; set; }
        public string Products { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetKg { get; set; }
    }

    public sealed class AllocationHistoryDto
    {
        public DateTime OccurredAt { get; set; }
        public string Type { get; set; }
        public int PartijId { get; set; }
        public string Number { get; set; }
        public decimal? OldAmount { get; set; }
        public decimal? NewAmount { get; set; }
        public string ActorReference { get; set; }
    }
}

namespace AgfAdmin.DTOs
{
    // Een ECB-koers voor een valuta en factuurdatum (alleen tonen; zie InvoiceExchangeRateService.QuoteAsync).
    public sealed class ExchangeRateDto
    {
        public string Currency { get; set; }
        // Eenheden valuta per 1 euro (1 EUR = 1,1165 USD).
        public decimal UnitsPerEuro { get; set; }
        // De publicatiedag van de koers (de laatste op of vóór ForDate).
        public DateOnly RateDate { get; set; }
        public DateOnly ForDate { get; set; }
        public string Source { get; set; }
    }
}
