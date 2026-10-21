using System.Text.Json;
using System.Text.Json.Serialization;
using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    // Lokale referentielijst van goederencodes (Data/Reference/goods-codes.json): een kleine, handmatig uit de officiële
    // EU-bron overgenomen selectie met herkomst, versie en geldigheidsdatum. Bewust geen volledige catalogus en geen
    // automatische indeling: de gebruiker zoekt, kiest en bevestigt zelf; de officiële zoekpagina staat altijd in het antwoord.
    // Actualiseren = het bestand vervangen door een nieuwe export van dezelfde bron (zie "source" in het bestand).
    public sealed class GoodsCodeCatalog
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public const string DefaultRelativePath = "Data/Reference/goods-codes.json";
        public const int MaxMatches = 25;

        public GoodsCodeSourceDto Source { get; }
        public IReadOnlyList<GoodsCodeMatchDto> Entries { get; }

        public GoodsCodeCatalog(GoodsCodeSourceDto source, IReadOnlyList<GoodsCodeMatchDto> entries)
        {
            Source = source ?? new GoodsCodeSourceDto { System = ArticleRules.SystemCn, Name = "onbekend" };
            Entries = (entries ?? Array.Empty<GoodsCodeMatchDto>())
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Code))
                .Select(e => new GoodsCodeMatchDto
                {
                    Code = ArticleRules.NormalizeGoodsCode(e.Code),
                    System = string.IsNullOrWhiteSpace(e.System) ? Source.System : e.System,
                    HsCode = ArticleRules.NormalizeGoodsCode(e.Code).Length >= 6 ? ArticleRules.NormalizeGoodsCode(e.Code)[..6] : null,
                    DescriptionNl = e.DescriptionNl?.Trim(),
                    DescriptionEn = e.DescriptionEn?.Trim()
                })
                .OrderBy(e => e.Code, StringComparer.Ordinal)
                .ToList();
        }

        public static GoodsCodeCatalog Empty() => new(null, null);

        public static GoodsCodeCatalog Load(string path)
        {
            if (!File.Exists(path))
            {
                return Empty();
            }
            var file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), Options);
            return new GoodsCodeCatalog(file?.Source, file?.Entries);
        }

        // Cijfers = zoeken op (begin van) de code; tekst = elke term moet in de Nederlandse of Engelse omschrijving voorkomen.
        public IReadOnlyList<GoodsCodeMatchDto> Search(string query)
        {
            var term = (query ?? "").Trim();
            if (term == "")
            {
                return Array.Empty<GoodsCodeMatchDto>();
            }
            var digits = ArticleRules.NormalizeGoodsCode(term);
            IEnumerable<GoodsCodeMatchDto> matches;
            if (digits != null && digits.Length > 0 && term.All(c => char.IsDigit(c) || c == ' ' || c == '.'))
            {
                matches = Entries.Where(e => e.Code.StartsWith(digits, StringComparison.Ordinal));
            }
            else
            {
                var words = term.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                matches = Entries.Where(e =>
                {
                    var text = $"{e.DescriptionNl} {e.DescriptionEn}".ToLowerInvariant();
                    return words.All(text.Contains);
                });
            }
            return matches.Take(MaxMatches).ToList();
        }

        public GoodsCodeSearchDto Result(string query) => new()
        {
            Source = Source,
            Complete = false,
            TotalEntries = Entries.Count,
            Matches = Search(query).ToList()
        };

        private sealed class CatalogFile
        {
            [JsonPropertyName("source")] public GoodsCodeSourceDto Source { get; set; }
            [JsonPropertyName("entries")] public List<GoodsCodeMatchDto> Entries { get; set; }
        }
    }
}
