namespace AgfAdmin.DTOs
{
    // Kostenartikelen (type Dienst), api/cost-articles.
    public sealed class CostArticleDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        // Cost of Packaging (emballage); alleen emballage heeft een standaardprijs per stuk (excl. btw) en een btw-percentage.
        public string Kind { get; set; }
        public decimal? DefaultUnitPrice { get; set; }
        public decimal? VatPercentage { get; set; }
        public bool IsStandard { get; set; }
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
        // Aantal factuurregels met dit kostenartikel (informatief; een gebruikt kostenartikel kan wel inactief worden).
        public int LineCount { get; set; }
    }

    public sealed class CreateCostArticleDto
    {
        public Guid? RequestId { get; set; }
        public string Name { get; set; }
    }

    public sealed class SetCostArticleActiveDto
    {
        public bool? IsActive { get; set; }
    }

    // Emballage-artikel (api/packaging-articles): naam alleen bij aanmaken; prijs (leeg = geen standaardprijs) en btw ook daarna.
    public sealed class SavePackagingArticleDto
    {
        public Guid? RequestId { get; set; }
        public string Name { get; set; }
        public decimal? DefaultUnitPrice { get; set; }
        public decimal? VatPercentage { get; set; }
    }
}
