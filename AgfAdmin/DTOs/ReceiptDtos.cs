namespace AgfAdmin.DTOs
{
    // Feedback 22 november: ontvangen in één scherm. Per inkooporder de lijst "Dit wordt verwacht" met per regel besteld, ontvangen en
    // nog te ontvangen, en per regel één knop Ontvangen (ReceiptService). Hoeveelheden in de eenheid van de regel, nooit omgerekend.
    public sealed class ReceiptOrderDto
    {
        public int OrderId { get; set; }
        public string OrderNumber { get; set; }
        public string SupplierReference { get; set; }
        public int VendorId { get; set; }
        public string VendorName { get; set; }
        public DateTime? ExpectedAt { get; set; }
        public string PartijNumber { get; set; }
        // Feedback 22 november: waar ontvangen wordt (null = de eigen loods). Op een locatie van de soort Onderweg heet de knop Afgeleverd.
        public int? LocationId { get; set; }
        public string LocationName { get; set; }
        public string LocationKind { get; set; }
        // Weergavestatus zoals de inkooporderlijst (Ordered, PartiallyReceived, Received, Rejected, Cancelled).
        public string Status { get; set; }
        // Ontvangen kan (eigen magazijn, niet geannuleerd of afgekeurd); anders de reden.
        public bool Receivable { get; set; }
        public string NotReceivableReason { get; set; }
        public List<ReceiptLineDto> Lines { get; set; } = new();
        // Certificaten deel 1: staat een product niet in het GGN-certificaat van de leverancier (of is dat vandaag verlopen), dan een regel op
        // het scherm in plaats van een venster. Ontvangen legt vast dat de ontvanger de melding zag (CurrentUser, context Receipt).
        public List<CertificateCheckDto> CertificateWarnings { get; set; } = new();
    }

    public sealed class ReceiptLineDto
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string ProductName { get; set; }
        public string ProductVariantName { get; set; }
        public string Origin { get; set; }
        public string Size { get; set; }
        public string PartijNumber { get; set; }
        public string Unit { get; set; }
        public decimal OrderedQuantity { get; set; }
        public decimal ReceivedQuantity { get; set; }
        // Nog te ontvangen: besteld min ontvangen, nooit onder nul.
        public decimal OpenQuantity { get; set; }
        // Netto kg per verpakking zoals de order hem verwacht (regel, anders artikel); null = de ontvanger vult het netto kg in.
        public decimal? NetKgPerPackage { get; set; }
        // Voorstel voor het aantal pallets bij "Palletnummers" (hele pallets van de regel), anders null.
        public int? SuggestedPalletCount { get; set; }
        // Ontvangen kan op deze regel (een eenheid is bekend); anders de reden.
        public bool Receivable { get; set; }
        public List<ReceiptPalletDto> Pallets { get; set; } = new();
    }

    // Een pallet met inhoud van deze regel: voor de optionele knop Keuren / afwijzen (de bestaande keuring, ook later te doen).
    public sealed class ReceiptPalletDto
    {
        public int PalletId { get; set; }
        public string Reference { get; set; }
        public int ContentLineId { get; set; }
        public decimal Quantity { get; set; }
        public decimal NetWeightKg { get; set; }
        public string QualityStatus { get; set; }
    }

    // Eén klik Ontvangen voor een regel. Quantity is verplicht (het scherm vult het openstaande aantal in; een afwijkend aantal mag): zo
    // geeft een herhaling met dezelfde RequestId altijd dezelfde ontvangst, ook als er intussen iets anders ontvangen is.
    public sealed class ReceiveLineDto
    {
        public Guid? RequestId { get; set; }
        public decimal? Quantity { get; set; }
        // Alleen als de order geen kg per verpakking kent (NetKgPerPackage null): het netto kg van wat er ontvangen wordt.
        public decimal? NetWeightKg { get; set; }
        // Optioneel (knop Palletnummers): het aantal pallets en per pallet een nummer (getypt of gescand). Zonder nummer maakt de server er
        // een (P-000123), zoals altijd; zonder aantal is het één pallet.
        public int? PalletCount { get; set; }
        public List<string> PalletReferences { get; set; }
    }

    public sealed class ReceiveLineResultDto
    {
        public int LineId { get; set; }
        public decimal Quantity { get; set; }
        public decimal NetWeightKg { get; set; }
        public List<ReceiptPalletDto> Pallets { get; set; } = new();
        public ReceiptOrderDto Order { get; set; }
    }
}
