namespace AgfAdmin.DTOs
{
    // Kostenartikelen (type Dienst), api/cost-articles.
    public sealed class CostArticleDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
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
}
