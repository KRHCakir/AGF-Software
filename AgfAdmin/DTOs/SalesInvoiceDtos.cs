namespace AgfAdmin.DTOs
{
    // Verkoopfactuur 1: aanvragen en antwoorden van /api/sales-invoices.

    // Concept uit één of meer leveringen van dezelfde klant (Factureren op de levering, Factuur maken in de werklijst).
    public sealed class CreateInvoiceFromDeliveriesDto
    {
        public Guid? RequestId { get; set; }
        public List<int> DeliveryIds { get; set; } = new();
    }

    // Directe factuur (zonder order): klant, regels (product uit een partij of dienst/vrije regel), leverdatum, referentie en eventueel
    // een eigen nummer voor de verkoop-partij (leeg = het volgende V-nummer).
    public sealed class CreateDirectInvoiceDto
    {
        public Guid? RequestId { get; set; }
        public int? CustomerId { get; set; }
        public DateOnly? DeliveryDate { get; set; }
        public string ReferenceNumber { get; set; }
        public string LotNumber { get; set; }
        public List<SalesInvoiceNewLineDto> Lines { get; set; } = new();
    }

    // Leveringen bij een bestaand concept zetten (zelfde klant).
    public sealed class AddInvoiceDeliveriesDto
    {
        public int? ExpectedVersion { get; set; }
        public List<int> DeliveryIds { get; set; } = new();
    }

    // Een concept bijwerken: de kop en bestaande regels (aantal, prijs, korting, btw, weghalen). Nieuwe regels van een directe factuur gaan
    // via NewLines.
    public sealed class UpdateSalesInvoiceDto
    {
        public int? ExpectedVersion { get; set; }
        public string ReferenceNumber { get; set; }
        // Alleen bij een directe factuur: de leverdatum (yyyy-MM-dd); bij een factuur uit leveringen komt die uit de verzending.
        public DateOnly? DeliveryDate { get; set; }
        public List<UpdateSalesInvoiceLineDto> Lines { get; set; } = new();
        public List<SalesInvoiceNewLineDto> NewLines { get; set; } = new();
    }

    public sealed class UpdateSalesInvoiceLineDto
    {
        public int Id { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public decimal? VatPercentage { get; set; }
        public bool Remove { get; set; }
        // Alleen bij een vrije regel van een directe factuur.
        public string Description { get; set; }
        public int? RevenueTypeId { get; set; }
    }

    // Een nieuwe regel van een directe factuur: Product (artikel uit een partij) of Service (dienst-artikel of vrije omschrijving).
    public sealed class SalesInvoiceNewLineDto
    {
        public string LineKind { get; set; }
        public int? ProductVariantId { get; set; }
        public int? PartijId { get; set; }
        public int? CostArticleId { get; set; }
        public string Description { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public decimal? VatPercentage { get; set; }
        public int? RevenueTypeId { get; set; }
    }

    public sealed class FinalizeSalesInvoiceDto
    {
        public int? ExpectedVersion { get; set; }
    }

    // Definitief maken van een selectie uit de lijst: elke factuur met de versie die de gebruiker zag.
    public sealed class FinalizeSalesInvoicesDto
    {
        public List<FinalizeSalesInvoiceItemDto> Invoices { get; set; } = new();
    }

    public sealed class FinalizeSalesInvoiceItemDto
    {
        public int Id { get; set; }
        public int? ExpectedVersion { get; set; }
    }

    public sealed record FinalizeSalesInvoiceResultDto(int Id, bool Final, string InvoiceNumber, int? Version, string Message, IReadOnlyDictionary<string, string[]> Errors);

    public sealed record FinalizeSalesInvoicesResultDto(IReadOnlyList<FinalizeSalesInvoiceResultDto> Results);

    public sealed class DiscardSalesInvoiceDto
    {
        public int? ExpectedVersion { get; set; }
        public string Reason { get; set; }
    }

    public sealed record SalesInvoiceVatRateDto(decimal Percentage, decimal Net, decimal Vat);

    public sealed record SalesInvoiceTotalsDto(decimal? Net, IReadOnlyList<SalesInvoiceVatRateDto> VatRates, decimal? Vat, decimal? Gross, int LinesWithoutPrice);

    public sealed record SalesInvoiceDeliveryDto(int SalesDeliveryId, int SalesOrderId, string OrderNumber, int Sequence, string PartijNumber, DateTime? DeliveredAt, string CustomerReference);

    public sealed record SalesInvoiceLineDto(
        int Id, string LineKind, int SortOrder, int? SalesDeliveryId, int? SalesOrderLineId, int? SalesOrderServiceLineId, int? SalesOrderPackagingLineId,
        int? ProductVariantId, int? PartijId, string PartijNumber, int? CostArticleId, int? RevenueTypeId, string RevenueTypeName,
        string Description, decimal Quantity, string Unit, decimal? UnitPrice, decimal DiscountPercentage, decimal VatPercentage, decimal? Amount, decimal? VatAmount,
        decimal? NetWeightKg, string Origin, string LotNumber, string Ggn, decimal? MaxQuantity);

    public sealed record SalesInvoiceDto(
        int Id, string Kind, string Status, string InvoiceNumber, DateTime InvoiceDate, DateTime? DeliveryDate, DateTime? DueDate, int? PaymentTermDays,
        int? CustomerId, string CustomerName, string CustomerAddress, string CustomerVatNumber, string ReferenceNumber, string SalesLotNumber,
        IReadOnlyList<SalesInvoiceDeliveryDto> Deliveries, IReadOnlyList<SalesInvoiceLineDto> Lines, SalesInvoiceTotalsDto Totals,
        DateTime CreatedAt, string CreatedBy, DateTime? FinalizedAt, string FinalizedBy, DateTime? DiscardedAt, string DiscardedBy, string DiscardReason,
        bool Stale, int Version);

    // Wat er van een levering nog te factureren is (op de verkooporder en in de werklijst).
    public sealed record DeliveryInvoicingLineDto(int SalesOrderLineId, string Description, string Unit, decimal Delivered, decimal Invoiced, decimal Rest);

    public sealed record DeliveryInvoicingInvoiceDto(int Id, string Status, string InvoiceNumber);

    public sealed record DeliveryInvoicingDto(
        int SalesDeliveryId, int SalesOrderId, string OrderNumber, int Sequence, string PartijNumber, int CustomerId, string CustomerName,
        DateTime? DeliveredAt, int? DaysSinceDelivery, IReadOnlyList<DeliveryInvoicingLineDto> Lines, bool HasRest, int? OpenConceptId,
        IReadOnlyList<DeliveryInvoicingInvoiceDto> Invoices);

    public sealed record InvoicingCustomerDto(int CustomerId, string CustomerName, IReadOnlyList<DeliveryInvoicingDto> Deliveries);
}
