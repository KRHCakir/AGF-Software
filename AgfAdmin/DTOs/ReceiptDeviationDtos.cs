namespace AgfAdmin.DTOs
{
    // Afwijkende ontvangst (ReceiptDeviationService, PalletService): het verschil tussen besteld en ontvangen en de gevolgen.

    // Momentopname van een variant zoals op het moment van de afwijking (ook in de opgeslagen afwijking).
    public sealed class ReceiptDeviationVariantDto
    {
        public int ProductVariantId { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string VariantName { get; set; }
        public string Origin { get; set; }
        public string OriginCountryCode { get; set; }
        public string Size { get; set; }
        public string Packaging { get; set; }
        public string PackagingUnit { get; set; }
        public string TradeClass { get; set; }
        public string Brand { get; set; }
    }

    // Een variant om als "Ander artikel ontvangen" te kiezen: alleen varianten van hetzelfde product. OtherArticle = ander ras of naam
    // (bijv. Fuerte bij Hass): dat is nooit een afwijking maar een nieuwe aanvoerregel.
    public sealed class ReceiptDeviationCandidateDto
    {
        public ReceiptDeviationVariantDto Variant { get; set; }
        public string ShortcutCode { get; set; }
        public bool OtherArticle { get; set; }
        public List<string> Kinds { get; set; } = new();
    }

    // Wat er bij "Ja, klopt" gebeurt; ook het `deviation`-deel van een 409-antwoord.
    public sealed class ReceiptDeviationPreviewDto
    {
        // Exact (zelfde variant, geen venster), Deviation (bevestiging nodig), OtherArticle (geweigerd) of BlockedAssigned (geweigerd).
        public string Status { get; set; }
        public int SupplyCommitmentId { get; set; }
        public string OrderNumber { get; set; }
        public int OriginalLineId { get; set; }
        public ReceiptDeviationVariantDto Expected { get; set; }
        public ReceiptDeviationVariantDto Received { get; set; }
        public List<string> Kinds { get; set; } = new();
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        // Oorspronkelijke regel: verwacht, al ontvangen, hoeveel hij omlaag gaat en wat er overblijft.
        public decimal OriginalExpectedQuantity { get; set; }
        public decimal OriginalReceivedQuantity { get; set; }
        public decimal ReducedQuantity { get; set; }
        public decimal OriginalExpectedQuantityAfter { get; set; }
        // Meer geleverd dan nog verwacht (ontvangen minus verlaging); de vervangende regel verwacht toch de hele ontvangen hoeveelheid.
        public decimal ExcessQuantity { get; set; }
        // Alleen DirectTrade: al aan een klant toegewezen (actief of verzonden) op de oorspronkelijke regel.
        public decimal? AssignedQuantity { get; set; }
        // Gewicht (netto, nooit bruto): verwacht en ontvangen per verpakking en voor deze hoeveelheid, en het verwachte kg van de
        // oorspronkelijke regel vóór en na Ja. Null zonder verwachting of zonder opgegeven kg.
        public decimal? ExpectedKgPerPackage { get; set; }
        public decimal? ReceivedKgPerPackage { get; set; }
        public decimal? ExpectedNetKg { get; set; }
        public decimal? ReceivedNetKg { get; set; }
        public decimal? OriginalExpectedNetKg { get; set; }
        public decimal? OriginalExpectedNetKgAfter { get; set; }
    }

    // 409-antwoord bij een afwijking zonder (geldige) bevestiging: code + het verschil, zodat de client het venster kan tonen.
    public sealed class ReceiptDeviationConflictDto
    {
        public string Message { get; set; }
        public string Code { get; set; }
        public ReceiptDeviationPreviewDto Deviation { get; set; }
    }

    // Vastgelegde afwijking op het orderdetail.
    public sealed class PurchaseOrderDeviationDto
    {
        public int Id { get; set; }
        public int OriginalLineId { get; set; }
        // Null bij een afwijking in alleen het gewicht (geen vervangende regel).
        public int? NewLineId { get; set; }
        public int PalletContentLineId { get; set; }
        public string PalletReference { get; set; }
        public List<string> Kinds { get; set; } = new();
        public ReceiptDeviationVariantDto Expected { get; set; }
        public ReceiptDeviationVariantDto Received { get; set; }
        public decimal Quantity { get; set; }
        public string Unit { get; set; }
        public decimal ReducedQuantity { get; set; }
        public decimal? ExpectedKgPerPackage { get; set; }
        public decimal? ReceivedKgPerPackage { get; set; }
        public string Reason { get; set; }
        public string ReasonNote { get; set; }
        public string ActorReference { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
