using System.Text.Json;
using System.Text.Json.Serialization;
using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    // Lokale referentielijst van goederencodes (Data/Reference/goods-codes.json): een kleine, handmatig uit de officiële
    // EU-bron overgenomen selectie met herkomst, versie en geldigheidsdatum. Bewust geen volledige catalogus en geen
    // automatische indeling: de gebruiker zoekt, kiest en bevestigt zelf; de officiële zoekpagina staat altijd in het antwoord.
    //
    // Zoeken gebeurt per codestelsel en levert uitsluitend codes van dát stelsel:
    //   HS    - de 6-cijferige GS/HS-posten: dubbele treffers uit de GN-lijst worden tot één post samengevoegd, met de
    //           omschrijving op postniveau (hsHeadings in het bestand) en de GN-codes die eronder vallen.
    //   CN/GN - de 8-cijferige GN-codes zoals opgenomen.
    //   TARIC - alleen daadwerkelijk opgenomen 10-cijferige referenties (system "TARIC" in het bestand). Er wordt nooit
    //           blind "00" achter een GN-code gezet; bevat de lijst er geen, dan is het antwoord eerlijk leeg.
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

        private sealed record Searchable(GoodsCodeMatchDto Match, string Text);

        private readonly IReadOnlyDictionary<string, IReadOnlyList<Searchable>> _bySystem;

        public GoodsCodeSourceDto Source { get; }

        // De opgenomen regels (GN en eventuele TARIC) zoals in het bestand, genormaliseerd; HS-posten staan apart.
        public IReadOnlyList<GoodsCodeMatchDto> Entries { get; }
        public IReadOnlyList<GoodsCodeMatchDto> HsHeadings { get; }

        public GoodsCodeCatalog(GoodsCodeSourceDto source, IReadOnlyList<GoodsCodeMatchDto> entries, IReadOnlyList<GoodsCodeMatchDto> hsHeadings = null)
        {
            Source = source ?? new GoodsCodeSourceDto { System = ArticleRules.SystemCn, Name = "onbekend" };
            Entries = (entries ?? Array.Empty<GoodsCodeMatchDto>())
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Code))
                .Select(e => Normalize(e, string.IsNullOrWhiteSpace(e.System) ? Source.System : e.System.Trim().ToUpperInvariant()))
                .OrderBy(e => e.Code, StringComparer.Ordinal)
                .ToList();
            HsHeadings = (hsHeadings ?? Array.Empty<GoodsCodeMatchDto>())
                .Where(h => h != null && !string.IsNullOrWhiteSpace(h.Code))
                .Select(h => Normalize(h, ArticleRules.SystemHs))
                .Where(h => ArticleRules.IsGoodsCodeFor(ArticleRules.SystemHs, h.Code))
                .OrderBy(h => h.Code, StringComparer.Ordinal)
                .ToList();
            _bySystem = new Dictionary<string, IReadOnlyList<Searchable>>(StringComparer.Ordinal)
            {
                [ArticleRules.SystemHs] = BuildHs(),
                [ArticleRules.SystemCn] = Entries.Where(e => e.System == ArticleRules.SystemCn).Select(e => new Searchable(e, Text(e))).ToList(),
                [ArticleRules.SystemTaric] = Entries.Where(e => e.System == ArticleRules.SystemTaric).Select(e => new Searchable(e, Text(e))).ToList()
            };
        }

        private static GoodsCodeMatchDto Normalize(GoodsCodeMatchDto e, string system)
        {
            var code = ArticleRules.NormalizeGoodsCode(e.Code);
            return new GoodsCodeMatchDto
            {
                Code = code,
                System = system,
                HsCode = code.Length >= 6 ? code[..6] : null,
                DescriptionNl = e.DescriptionNl?.Trim(),
                DescriptionEn = e.DescriptionEn?.Trim()
            };
        }

        private static string Text(GoodsCodeMatchDto match, IEnumerable<GoodsCodeMatchDto> children = null) =>
            string.Join(" ", new[] { match.DescriptionNl, match.DescriptionEn }.Concat((children ?? Array.Empty<GoodsCodeMatchDto>()).SelectMany(c => new[] { c.DescriptionNl, c.DescriptionEn }))
                .Where(part => !string.IsNullOrWhiteSpace(part))).ToLowerInvariant();

        // HS: één treffer per 6-cijferige post. Omschrijving van de post uit hsHeadings; ontbreekt die, dan de (samengevoegde)
        // omschrijvingen van de onderliggende codes, zodat er nooit een GN-omschrijving als HS-omschrijving wordt voorgesteld.
        private IReadOnlyList<Searchable> BuildHs()
        {
            var headings = HsHeadings.ToDictionary(h => h.Code, StringComparer.Ordinal);
            var groups = Entries.Where(e => e.HsCode != null).GroupBy(e => e.HsCode, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            return headings.Keys.Union(groups.Keys, StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .Select(code =>
                {
                    var children = groups.GetValueOrDefault(code) ?? new List<GoodsCodeMatchDto>();
                    headings.TryGetValue(code, out var heading);
                    var match = new GoodsCodeMatchDto
                    {
                        Code = code,
                        System = ArticleRules.SystemHs,
                        HsCode = code,
                        DescriptionNl = heading?.DescriptionNl ?? Join(children.Select(c => c.DescriptionNl)),
                        DescriptionEn = heading?.DescriptionEn ?? Join(children.Select(c => c.DescriptionEn)),
                        MergedCodes = children.Select(c => c.Code).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal).ToList()
                    };
                    return new Searchable(match, Text(match, children));
                })
                .ToList();
        }

        private static string Join(IEnumerable<string> parts)
        {
            var distinct = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToList();
            return distinct.Count == 0 ? null : string.Join(" / ", distinct);
        }

        public static GoodsCodeCatalog Empty() => new(null, null);

        public static GoodsCodeCatalog Load(string path)
        {
            if (!File.Exists(path))
            {
                return Empty();
            }
            var file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), Options);
            return new GoodsCodeCatalog(file?.Source, file?.Entries, file?.HsHeadings);
        }

        public IReadOnlyList<GoodsCodeMatchDto> EntriesFor(string system) =>
            _bySystem.TryGetValue(system ?? "", out var list) ? list.Select(s => s.Match).ToList() : Array.Empty<GoodsCodeMatchDto>();

        // Zonder stelsel: GN (de basis van de lijst); bestaande aanroepen blijven werken.
        public IReadOnlyList<GoodsCodeMatchDto> Search(string query) => Search(query, ArticleRules.SystemCn);

        // Cijfers = zoeken op (begin van) de code van het gekozen stelsel; een langere code (bijv. een GN-code onder HS) vindt
        // de post waar zij onder valt. Tekst = elke term moet in de Nederlandse of Engelse omschrijving voorkomen (bij HS
        // ook in die van de onderliggende GN-codes). Nooit codes van een ander stelsel.
        public IReadOnlyList<GoodsCodeMatchDto> Search(string query, string system)
        {
            var term = (query ?? "").Trim();
            if (term == "" || !_bySystem.TryGetValue(system ?? "", out var candidates))
            {
                return Array.Empty<GoodsCodeMatchDto>();
            }
            var digits = ArticleRules.NormalizeGoodsCode(term);
            IEnumerable<Searchable> matches;
            if (digits != null && digits.Length > 0 && term.All(c => char.IsDigit(c) || c == ' ' || c == '.'))
            {
                matches = candidates.Where(s => s.Match.Code.StartsWith(digits, StringComparison.Ordinal)
                    || (digits.Length > s.Match.Code.Length && digits.StartsWith(s.Match.Code, StringComparison.Ordinal)));
            }
            else
            {
                var words = term.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                matches = candidates.Where(s => words.All(s.Text.Contains));
            }
            return matches.Select(s => s.Match).Take(MaxMatches).ToList();
        }

        public GoodsCodeSearchDto Result(string query) => Result(query, ArticleRules.SystemCn);

        public GoodsCodeSearchDto Result(string query, string system) => new()
        {
            System = system,
            Source = Source,
            Complete = false,
            TotalEntries = EntriesFor(system).Count,
            Matches = Search(query, system).ToList()
        };

        private sealed class CatalogFile
        {
            [JsonPropertyName("source")] public GoodsCodeSourceDto Source { get; set; }
            [JsonPropertyName("hsHeadings")] public List<GoodsCodeMatchDto> HsHeadings { get; set; }
            [JsonPropertyName("entries")] public List<GoodsCodeMatchDto> Entries { get; set; }
        }
    }
}
