namespace AgfAdmin.DTOs
{
    // Certificaten deel 2: een certificaat (PDF of foto) door AI laten voorinvullen (api/certificates/ai). De AI doet een voorstel; opslaan
    // gebeurt pas bij Goedkeuren, via het gewone opslagpad van de certificaten (PUT met aiApproval).
    public sealed class AiCertificateExtractionDto
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public int? RelationId { get; set; }
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
        public AiCertificateProposalDto Proposal { get; set; }
        // Gele markeringen en "niet gevonden": veld (typeCode, number, holderName, validUntil, products[1], ...) en code.
        public List<AiProblemDto> Problems { get; set; } = new();
        // Na goedkeuren: het certificaat, wie en wanneer.
        public int? RelationCertificateId { get; set; }
        public string ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }

    public sealed class AiCertificateProposalDto
    {
        // Een soort uit de vaste lijst, of Custom met de naam uit het document (CustomTypeName); null als het niet te lezen was.
        public string TypeCode { get; set; }
        public string CustomTypeName { get; set; }
        public string DocumentTypeName { get; set; }
        public string Number { get; set; }
        public string HolderName { get; set; }
        public string HolderCountry { get; set; }
        public string CertificationBody { get; set; }
        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidUntil { get; set; }
        // Dezelfde status als in certificaten deel 1 (Valid, ExpiringSoon, Expired, NoEndDate).
        public string Status { get; set; }
        public int? DaysLeft { get; set; }
        public List<AiCertificateProductDto> Products { get; set; } = new();
        public List<AiCertificateSiteDto> Sites { get; set; } = new();
        // Het certificaat staat duidelijk op naam van een ander bedrijf dan de relatie: die naam (gele waarschuwing), anders null.
        public string HolderMismatch { get; set; }
        // Er staat al een certificaat met hetzelfde nummer bij deze relatie: het voorstel is daar een nieuwe versie van te maken.
        public int? SameNumberCertificateId { get; set; }
        public string SameNumberTypeName { get; set; }
    }

    // Een product zoals het in het document staat (letterlijk, met de pagina) en de voorgestelde koppeling met een eigen product. Nooit een
    // nieuw product: zonder koppeling blijft het "niet gekoppeld".
    public sealed class AiCertificateProductDto
    {
        public string Text { get; set; }
        public int? Page { get; set; }
        public int? ProductId { get; set; }
        public string ProductName { get; set; }
        // Exact (dezelfde naam), Translated (bijvoorbeeld Lettuce is Sla), Partial (de naam van een eigen product staat in de tekst) of None.
        public string Match { get; set; }
    }

    public sealed class AiCertificateSiteDto
    {
        public string Name { get; set; }
        public string Country { get; set; }
    }

    // Goedkeuren in het opslagpad van de certificaten: welke lezing en welke regel van de lijst het goedgekeurde certificaat is.
    public sealed class CertificateAiApprovalDto
    {
        public int ExtractionId { get; set; }
        public int CertificateIndex { get; set; }
    }

    // Wat bij een goedgekeurde versie van een certificaatbestand staat: "AI-voorstel goedgekeurd door …" met model en tijd.
    public sealed class CertificateFileAiDto
    {
        public string ApprovedBy { get; set; }
        public DateTime ApprovedAt { get; set; }
        public string Model { get; set; }
        public string ModelName { get; set; }
        public string ModelVersion { get; set; }
        public int AdjustedFields { get; set; }
    }
}
