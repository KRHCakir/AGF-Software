namespace AgfAdmin.DTOs
{
    // Overzicht over alle relaties (Stamgegevens › Certificaten, startpagina): per certificaat de relatie, haar rollen en de status.
    public sealed class CertificateOverviewDto
    {
        public int WarningDays { get; set; }
        public int Total { get; set; }          // alle certificaten bij het filter, ook buiten deze pagina
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<CertificateOverviewItemDto> Items { get; set; } = new();
    }

    public sealed class CertificateOverviewItemDto
    {
        public int CertificateId { get; set; }
        public int? RelationId { get; set; }      // null = eigen bedrijf
        public string RelationName { get; set; }
        public bool IsCustomer { get; set; }
        public bool IsVendor { get; set; }
        public bool IsCarrier { get; set; }
        public string TypeCode { get; set; }
        public string TypeName { get; set; }
        public string Number { get; set; }
        public DateOnly? ValidUntil { get; set; }
        public int? DaysLeft { get; set; }
        public string Status { get; set; }
        public bool ProductsPending { get; set; }
        public bool IsActive { get; set; }
        public List<string> ProductNames { get; set; } = new();
    }

    // Samenvatting per relatie in de relatielijst: per actief certificaat de soort en de status.
    public sealed class RelationCertificateSummaryDto
    {
        public string TypeCode { get; set; }
        public string TypeName { get; set; }
        public string Status { get; set; }
        public bool ProductsPending { get; set; }
    }
}
