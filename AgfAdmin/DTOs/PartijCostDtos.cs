namespace AgfAdmin.DTOs
{
    // Verdeling van een inkoopfactuurregel over partijen (PartijCostService). Bedragen excl. btw, in euro.
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
        // False bij een factuur in een andere valuta dan EUR: dan kan er niet verdeeld worden.
        public bool CanAllocate { get; set; }
        public List<InvoiceLineAllocationDto> Lines { get; set; } = new();
    }

    public sealed class InvoiceLineAllocationDto
    {
        public int LineId { get; set; }
        public string Description { get; set; }
        public string Kind { get; set; }
        public decimal LineTotal { get; set; }
        public decimal Allocated { get; set; }
        // Wat niet verdeeld is: algemene kosten (waarschuwing, geen blokkade).
        public decimal General { get; set; }
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
