namespace AgfAdmin.DTOs
{
    // Certificaten deel 1: de certificaten van één relatie (of het eigen bedrijf) als lijst met een versie, zoals de documentontvangers.
    public sealed class CertificateListDto
    {
        public string Version { get; set; }
        public int WarningDays { get; set; }
        public List<CertificateDto> Certificates { get; set; } = new();
        // Na opslaan: waarschuwingen die niet blokkeren (GGN zonder producten, controlecijfer van een GGN).
        public List<CertificateWarningDto> Warnings { get; set; } = new();
    }

    public sealed class CertificateDto
    {
        public int Id { get; set; }
        public string TypeCode { get; set; }
        public string CustomTypeName { get; set; }
        public string TypeName { get; set; }
        public string Number { get; set; }
        public string CertificationBody { get; set; }
        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidUntil { get; set; }
        public List<CertificateProductDto> Products { get; set; } = new();
        public bool ProductsPending { get; set; }
        public string ProducerName { get; set; }
        public bool HasGgnLabel { get; set; }
        public DateOnly? CheckedOn { get; set; }
        public string CheckedBy { get; set; }
        public bool IsActive { get; set; }
        // Uit de einddatum: Valid, ExpiringSoon, Expired of NoEndDate, met de dagen die nog resten (negatief = zoveel dagen verlopen).
        public string Status { get; set; }
        public int? DaysLeft { get; set; }
        public List<CertificateFileDto> Files { get; set; } = new();
    }

    public sealed class CertificateProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public sealed class CertificateFileDto
    {
        public int Id { get; set; }
        public int Version { get; set; }
        public string FileName { get; set; }
        public string MediaType { get; set; }
        public long SizeBytes { get; set; }
        public string Sha256 { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }
    }

    public sealed class CertificateWarningDto
    {
        public string Code { get; set; }   // products-missing, ggn-check-digit
        public string Field { get; set; }  // certificates[i].productIds, certificates[i].number
        public string Message { get; set; }
    }

    // Vervangt de hele lijst, met de versie die de gebruiker zag (409 bij een gelijktijdige wijziging). Een opgeslagen certificaat kan niet
    // uit de lijst verdwijnen: archiveren (IsActive = false). Gecontroleerd door: de server (CurrentUser), nooit de client.
    public sealed class SaveCertificatesDto
    {
        public string ExpectedVersion { get; set; }
        public List<SaveCertificateDto> Certificates { get; set; }
    }

    public sealed class SaveCertificateDto
    {
        public int? Id { get; set; }
        public string TypeCode { get; set; }
        public string CustomTypeName { get; set; }
        public string Number { get; set; }
        public string CertificationBody { get; set; }
        public DateOnly? ValidFrom { get; set; }
        public DateOnly? ValidUntil { get; set; }
        public List<int> ProductIds { get; set; }
        public string ProducerName { get; set; }
        public bool HasGgnLabel { get; set; }
        public DateOnly? CheckedOn { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class CertificateReferenceDto
    {
        public List<CertificateTypeDto> Types { get; set; } = new();
        public List<string> CustomTypes { get; set; } = new();
        public int WarningDays { get; set; }
        public string GlobalGapPortalUrl { get; set; }
        public string GlobalGapDatabaseUrl { get; set; }
        public long MaxFileBytes { get; set; }
    }

    public sealed class CertificateTypeDto
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string TypicalHolder { get; set; }
        public bool ProductsRequired { get; set; }
        public bool GgnNumber { get; set; }
    }

    public sealed class CertificateSettingsDto
    {
        public int WarningDays { get; set; }
        public List<string> CustomTypes { get; set; } = new();
    }
}
