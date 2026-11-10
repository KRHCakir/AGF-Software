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

        // Soorten: kosten (P2) en emballage (fust). Emballage staat ook als regel op een verkooporder, met prijs en btw.
        public const string KindCost = "Cost";
        public const string KindPackaging = "Packaging";
        public static readonly IReadOnlyList<string> Kinds = new[] { KindCost, KindPackaging };
        // Standaard-emballage uit de migratie AddPackaging, met 9% btw: verpakking die met groente en fruit meegaat, volgt het tarief van
        // de goederen. Per artikel aan te passen.
        public static readonly IReadOnlyList<string> StandardPackagingNames = new[] { "Pallet wegwerp", "Euro-pallet", "Krat" };
        public const decimal StandardPackagingVat = 9m;
        public const int MaxPackagingQuantity = 99999;
        public const int MaxPackagingLines = 50;

        public static bool IsKind(string kind) => kind is KindCost or KindPackaging;

        // Bedrag per stuk: >= 0, hoogstens 2 decimalen, past in decimal(18,2).
        public static bool IsUnitPrice(decimal value) => value >= 0m && value < 10_000_000_000_000_000m && decimal.Round(value, 2) == value;

        public static bool IsVatPercentage(decimal value) => value >= 0m && value <= 100m && decimal.Round(value, 2) == value;

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
