namespace AgfAdmin.Services
{
    // Vaste waarden van kostenartikelen (type Dienst). Gespiegeld in agf-frontend/src/lib/costArticles.ts.
    public static class CostArticleRules
    {
        public const int MaxNameLength = 100;
        public const int MaxActorLength = 100;
        public const string StandardActor = "Standaard";

        // De standaardsoorten (in deze volgorde in de keuzelijst), aangemaakt door de migratie AddCostArticles. Overig is ook de soort van
        // een kostenregel zonder kostenartikel (alleen een omschrijving).
        public const string Other = "Overig";
        public static readonly IReadOnlyList<string> StandardNames = new[] { "Handling", "Transport", "QC", "Zeevracht", "Afval", Other };

        // Getrimd, in kleine letters, spaties samengevoegd. Null als er niets overblijft.
        public static string NameKey(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            return string.Join(' ', name.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }

        public static string NormalizeName(string name) =>
            string.IsNullOrWhiteSpace(name) ? null : string.Join(' ', name.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
    }
}
