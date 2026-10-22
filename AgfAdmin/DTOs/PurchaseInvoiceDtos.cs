namespace AgfAdmin.DTOs
{
    // Nieuwe invoerwerkplek voor inkoopfacturen (POST api/purchase-invoices). De oude endpoints onder api/incominginvoices
    // blijven ongewijzigd; beide schrijven in dezelfde tabellen.
    public sealed class CreatePurchaseInvoiceDto
    {
        public Guid? RequestId { get; set; }
        public int? VendorId { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime? InvoiceDate { get; set; }
        // Op het document bewaarde waarden; leeg = standaard van de leverancier (en anders van het bedrijf).
        public int? PaymentTermsDays { get; set; }
        public DateTime? DueDate { get; set; }
        public string Currency { get; set; }
        public List<CreatePurchaseInvoiceLineDto> Lines { get; set; } = new();
        // Interne opmerking bij dit document (altijd beschikbaar, nooit extern zichtbaar; telt mee in de replayvergelijking).
        public string InternalNote { get; set; }
        // Optionele velden; alleen de door het bedrijf ingeschakelde velden worden overgenomen.
        public DateTime? ReceivedDate { get; set; }
        public string PaymentReference { get; set; }
        public string Remarks { get; set; }
        public decimal? GrossWeightKg { get; set; }
        public decimal? NetWeightKg { get; set; }
        public string ContainerNumber { get; set; }
        public string Transporter { get; set; }
        public string DriverName { get; set; }
        public string LicensePlate { get; set; }
        public string Incoterms { get; set; }
        public decimal? CustomsValue { get; set; }
        public string OriginCountry { get; set; }
        public string ReceiptReference { get; set; }
    }

    public sealed class CreatePurchaseInvoiceLineDto
    {
        public int? ProductVariantId { get; set; }
        public string Description { get; set; }
        public decimal? Quantity { get; set; }
        public string Unit { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public decimal? VatPercentage { get; set; }
    }

    public sealed class PurchaseInvoiceDto
    {
        public int Id { get; set; }
        public Guid? RequestId { get; set; }
        public int? VendorId { get; set; }
        public string VendorName { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public int? PaymentTermsDays { get; set; }
        public DateTime? DueDate { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public decimal SubTotal { get; set; }
        public decimal VatAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime CreatedAt { get; set; }
        public string InternalNote { get; set; }
        public DateTime? ReceivedDate { get; set; }
        public string PaymentReference { get; set; }
        public string Remarks { get; set; }
        public decimal? GrossWeightKg { get; set; }
        public decimal? NetWeightKg { get; set; }
        public string ContainerNumber { get; set; }
        public string Transporter { get; set; }
        public string DriverName { get; set; }
        public string LicensePlate { get; set; }
        public string Incoterms { get; set; }
        public decimal? CustomsValue { get; set; }
        public string OriginCountry { get; set; }
        public string ReceiptReference { get; set; }
        public List<PurchaseInvoiceLineDto> Lines { get; set; } = new();
    }

    public sealed class PurchaseInvoiceLineDto
    {
        public int Id { get; set; }
        public int? ProductVariantId { get; set; }
        public string Description { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal DiscountPercentage { get; set; }
        public decimal LineTotal { get; set; }
        public decimal VatPercentage { get; set; }
        public decimal VatAmount { get; set; }
    }

    public sealed class PurchaseVendorDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string VatNumber { get; set; }
        public int PaymentTermsDays { get; set; }
        public string DefaultCurrency { get; set; }
        // Zoekcode en actief-vlag van de relatie: zoeken op code; inactieve leveranciers staan niet in de keuzelijst voor nieuwe facturen.
        public string SearchCode { get; set; }
        public bool IsActive { get; set; } = true;
    }

    // Bedrijfsinstellingen voor de invoer: welke optionele velden in gebruik zijn en welke voorstellen gelden.
    public sealed class PurchaseInvoiceSettingsDto
    {
        public List<string> AllOptionalFields { get; set; } = new();
        public List<string> OptionalFields { get; set; } = new();
        public decimal DefaultVatPercentage { get; set; }
        public int DefaultPaymentTermsDays { get; set; }
        public string DefaultCurrency { get; set; }
    }

    public sealed class UpdatePurchaseInvoiceSettingsDto
    {
        public List<string> OptionalFields { get; set; }
        public decimal? DefaultVatPercentage { get; set; }
        public int? DefaultPaymentTermsDays { get; set; }
        public string DefaultCurrency { get; set; }
    }
}
