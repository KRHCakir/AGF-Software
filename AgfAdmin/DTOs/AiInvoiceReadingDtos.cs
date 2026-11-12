namespace AgfAdmin.DTOs
{
    // Staat AI-lezen aan, en kan het (sleutel, prijzen, maandlimiet)? Het scherm zegt zonder dit "AI-lezen staat niet aan", nooit nepgegevens.
    public sealed class AiReadingStatusDto
    {
        public bool Enabled { get; set; }
        public bool Configured { get; set; }
        public string NotConfiguredReason { get; set; }
        public string Provider { get; set; }
        public AiModelDto PrimaryModel { get; set; }
        public bool FallbackEnabled { get; set; }
        public AiModelDto FallbackModel { get; set; }
        public decimal MonthSpentEur { get; set; }
        public decimal MonthlyLimitEur { get; set; }
        public bool LimitReached { get; set; }
        public long MaxFileBytes { get; set; }
    }

    public sealed class AiModelDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public decimal InputPerMillionUsd { get; set; }
        public decimal OutputPerMillionUsd { get; set; }
        public string PriceCheckedOn { get; set; }
    }

    // Beheer: aan/uit (aanzetten alleen met de bevestiging dat de klant weet dat documenten naar een externe dienst gaan) en de maandlimiet.
    public sealed class AiReadingSettingsDto
    {
        public bool Enabled { get; set; }
        public string EnabledBy { get; set; }
        public DateTime? EnabledAt { get; set; }
        public decimal MonthlyLimitEur { get; set; }
    }

    public sealed class UpdateAiReadingSettingsDto
    {
        public bool Enabled { get; set; }
        public bool CustomerInformed { get; set; }
        public decimal? MonthlyLimitEur { get; set; }
    }

    // Het voorstel van de AI, in de velden van het factuurformulier. Ontbrekend = null; er wordt niets aangevuld.
    public sealed class AiExtractionDto
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public string FileName { get; set; }
        public string MediaType { get; set; }
        public string DisplayMediaType { get; set; }
        public long SizeBytes { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string ModelName { get; set; }
        public string ModelVersion { get; set; }
        public bool UsedFallback { get; set; }
        public decimal CostEur { get; set; }
        public AiInvoiceProposalDto Proposal { get; set; }
        public List<AiProblemDto> Problems { get; set; } = new();
    }

    public sealed class AiInvoiceProposalDto
    {
        public string VendorName { get; set; }
        public string VendorVatNumber { get; set; }
        public string VendorRegistrationNumber { get; set; }
        public string VendorIban { get; set; }
        public string SupplierOrderNumber { get; set; }
        public string InvoiceNumber { get; set; }
        public DateOnly? InvoiceDate { get; set; }
        public string Currency { get; set; }
        public List<AiInvoiceLineProposalDto> Lines { get; set; } = new();
        public decimal? TotalNet { get; set; }
        public decimal? TotalVat { get; set; }
        public decimal? TotalGross { get; set; }
    }

    public sealed class AiInvoiceLineProposalDto
    {
        public string Description { get; set; }
        public decimal? Quantity { get; set; }
        public string UnitText { get; set; }
        // Alleen een eenheid van de regel (box, colli, tray, piece, dozen); een andere (zoals kg) blijft leeg.
        public string Unit { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? Amount { get; set; }
        public decimal? VatPercentage { get; set; }
    }

    public sealed class AiProblemDto
    {
        public string Field { get; set; }
        public string Code { get; set; }
    }

    // Afwijzen (voorstel weg, niets opgeslagen) of handmatig verder (het voorstel als startpunt, zonder AI-goedkeuring).
    public sealed class CloseAiExtractionDto
    {
        public string Outcome { get; set; }
    }

    // Foutantwoord met een code, zodat het scherm de juiste uitleg en de knop "Handmatig invoeren" toont.
    public sealed class AiReadingErrorDto
    {
        public string Code { get; set; }
        public string Message { get; set; }
    }
}
