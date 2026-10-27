namespace AgfAdmin.DTOs
{
    // Inkooporders: lijst, detail en factuurnummers (PurchaseOrderService); afkeuren in PurchaseOrderRejectionDtos.cs.
    // Hoeveelheden in de eenheid van de regel; "colli" in de lijst is de som van alle verpakkingen, zonder eenheden om te rekenen.

    public sealed class PurchaseOrderListDto
    {
        public List<PurchaseOrderListItemDto> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public sealed class PurchaseOrderInvoiceNumberDto
    {
        public string Kind { get; set; }
        public string InvoiceNumber { get; set; }
    }

    public sealed class PurchaseOrderListItemDto
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public string SupplierReference { get; set; }
        public DateOnly OrderedOn { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public string Route { get; set; }
        public string CustomerName { get; set; }
        // Weergavestatus (PurchaseOrderRules.OrderStatus) en de opgeslagen status van de toezegging.
        public string Status { get; set; }
        public string StoredStatus { get; set; }
        public decimal ExpectedQuantity { get; set; }
        // Som van de verwachte netto kg van de regels die kg hebben; null als geen enkele regel kg heeft.
        public decimal? ExpectedNetWeightKg { get; set; }
        public decimal ReceivedQuantity { get; set; }
        public decimal RejectedQuantity { get; set; }
        public List<PurchaseOrderInvoiceNumberDto> InvoiceNumbers { get; set; } = new();
        // None, Received of CreditNoteExpected (PurchaseOrderRules.InvoiceIndicator).
        public string InvoiceIndicator { get; set; }
    }

    public sealed class PurchaseOrderDetailDto
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public string SupplierReference { get; set; }
        public DateOnly OrderedOn { get; set; }
        public string OrderChannel { get; set; }
        public string Note { get; set; }
        public string Route { get; set; }
        public int? CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Origin { get; set; }
        public string Destination { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; }
        public string StoredStatus { get; set; }
        public string InvoiceIndicator { get; set; }
        public decimal ExpectedQuantity { get; set; }
        public decimal? ExpectedNetWeightKg { get; set; }
        public decimal ReceivedQuantity { get; set; }
        public decimal ReceivedNetWeightKg { get; set; }
        public decimal RejectedQuantity { get; set; }
        public decimal RejectedNetWeightKg { get; set; }
        public List<PurchaseOrderLineDto> Lines { get; set; } = new();
        public List<PurchaseOrderReceiptLineDto> Receipts { get; set; } = new();
        public List<PurchaseOrderInvoiceRefDto> InvoiceRefs { get; set; } = new();
        public List<PurchaseOrderRejectionDto> Rejections { get; set; } = new();
        public List<PurchaseOrderHistoryEventDto> History { get; set; } = new();
    }

    public sealed class PurchaseOrderLineDto
    {
        public int Id { get; set; }
        public int ProductVariantId { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public string LotNumber { get; set; }
        public decimal ExpectedQuantity { get; set; }
        public string Unit { get; set; }
        public decimal? ExpectedNetWeightKg { get; set; }
        // Ontvangen = fysieke ontvangst in de eenheid van de regel (geen correctie- of afgesplitste inhoud, geen geannuleerde pallet).
        public decimal ReceivedQuantity { get; set; }
        public decimal ReceivedNetWeightKg { get; set; }
        public decimal RejectedQuantity { get; set; }
        public decimal RejectedNetWeightKg { get; set; }
    }

    // Ontvangen inhoud van de order (ook teruggeboekte en afgesplitste regels), met wat er nu nog afgekeurd kan worden.
    public sealed class PurchaseOrderReceiptLineDto
    {
        public int PalletContentLineId { get; set; }
        public int PalletId { get; set; }
        public string PalletReference { get; set; }
        public string PalletStatus { get; set; }
        public int SupplyCommitmentLineId { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public string LotNumber { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        public string WeightMethod { get; set; }
        public decimal? NetKgPerPackage { get; set; }
        public decimal OnHandQuantity { get; set; }
        public decimal OnHandNetWeightKg { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal ReservedNetWeightKg { get; set; }
        public string QualityStatus { get; set; }
        public string QualityReason { get; set; }
        public string Origin { get; set; }
        public DateTime ReceivedAt { get; set; }
        // Afkeurbaar = voorraad minus actieve verkoopreserveringen, alleen op een Received pallet en nog niet Rejected.
        public bool Rejectable { get; set; }
        public decimal RejectableQuantity { get; set; }
        public decimal RejectableNetWeightKg { get; set; }
        public int Version { get; set; }
    }

    public sealed class PurchaseOrderInvoiceRefDto
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public string InvoiceNumber { get; set; }
        public DateOnly? InvoiceDate { get; set; }
        public decimal? AmountExclVat { get; set; }
        public int? IncomingInvoiceId { get; set; }
        public string ActorReference { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class PurchaseOrderRejectionDto
    {
        public int Id { get; set; }
        public string Reason { get; set; }
        public string Explanation { get; set; }
        public string Decision { get; set; }
        public string ActorReference { get; set; }
        public DateTime RejectedAt { get; set; }
        public List<PurchaseOrderRejectionLineDto> Lines { get; set; } = new();
    }

    public sealed class PurchaseOrderRejectionLineDto
    {
        public int PalletContentLineId { get; set; }
        public string PalletReference { get; set; }
        public string LotNumber { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal NetWeightKg { get; set; }
        public bool SplitOff { get; set; }
    }

    // Geschiedenis uit de bestaande gegevens: besteld, ontvangen, kwaliteit, factuur/creditnota en afkeuring.
    public sealed class PurchaseOrderHistoryEventDto
    {
        public DateTime OccurredAt { get; set; }
        public string Type { get; set; }      // Ordered, Received, QualityChanged, InvoiceRecorded, CreditNoteRecorded, Rejected
        public string ActorReference { get; set; }
        public string Reference { get; set; } // leveranciersreferentie, pallet of factuurnummer
        public string Channel { get; set; }   // Ordered
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
        public string FromStatus { get; set; } // QualityChanged
        public string ToStatus { get; set; }
        public string Reason { get; set; }    // Rejected
        public string Decision { get; set; }
    }

    // Orders waaraan een factuurnummer hangt (melding bij het invoeren van een inkoopfactuur).
    public sealed class PurchaseOrderInvoiceMatchDto
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; }
        public string Kind { get; set; }
        public string VendorName { get; set; }
    }
}
