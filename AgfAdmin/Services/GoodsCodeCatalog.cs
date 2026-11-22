using System.Text.Json;
using System.Text.Json.Serialization;
using AgfAdmin.DTOs;

namespace AgfAdmin.Services
{
    // Lokale referentielijst van goederencodes (Data/Reference/goods-codes.json): de hoofdstukken 07, 08, 09 en 20 van de GN
    // volledig, gemaakt met scripts/goods-codes/build_cn_reference.py uit de officiële EU-dataset, met bron, versie en datum.
    // Geen live API en geen automatische indeling: de gebruiker zoekt, kiest en bevestigt zelf; een code buiten de lijst mag met de
    // hand (IsKnown geeft dan een waarschuwing, nooit een blokkade). Synoniemen (goods-code-synonyms.json) zijn alleen een zoekhulp.
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
        public const string SynonymsFileName = "goods-code-synonyms.json";
        public const int MaxMatches = 25;

        // Text: alle omschrijvingen (pad vanaf de post, bij HS ook die van de onderliggende GN-codes), als zoeksleutel. Leaf: het pad
        // onder de post; een treffer daarin komt bovenaan ("gember" eerst 0910 11/12, daarna saffraan en de rest van post 0910).
        private sealed record Searchable(GoodsCodeMatchDto Match, string Text, string Leaf);

        // Een synoniem: elk van de termen (een of meer woorden) zoekt ook op de zoekwoorden. Sleutels al genormaliseerd (SearchKey).
        public sealed record Synonym(IReadOnlyList<string> Terms, IReadOnlyList<string> Search);

        private readonly IReadOnlyList<Synonym> _synonyms;

        private readonly IReadOnlyDictionary<string, IReadOnlyList<Searchable>> _bySystem;

        public GoodsCodeSourceDto Source { get; }

        // De opgenomen regels (GN en eventuele TARIC) zoals in het bestand, genormaliseerd; HS-posten staan apart.
        public IReadOnlyList<GoodsCodeMatchDto> Entries { get; }
        public IReadOnlyList<GoodsCodeMatchDto> HsHeadings { get; }

        public GoodsCodeCatalog(GoodsCodeSourceDto source, IReadOnlyList<GoodsCodeMatchDto> entries, IReadOnlyList<GoodsCodeMatchDto> hsHeadings = null,
            IReadOnlyList<Synonym> synonyms = null)
        {
            _synonyms = (synonyms ?? Array.Empty<Synonym>())
                .Select(s => new Synonym(s.Terms.Select(SearchKey).Where(t => t != "").ToList(), s.Search.Select(SearchKey).Where(t => t != "").ToList()))
                .Where(s => s.Terms.Count > 0 && s.Search.Count > 0)
                .ToList();
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
                [ArticleRules.SystemCn] = Entries.Where(e => e.System == ArticleRules.SystemCn).Select(e => new Searchable(e, Text(e), Leaf(e))).ToList(),
                [ArticleRules.SystemTaric] = Entries.Where(e => e.System == ArticleRules.SystemTaric).Select(e => new Searchable(e, Text(e), Leaf(e))).ToList()
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
            SearchKey(string.Join(" ", new[] { match.DescriptionNl, match.DescriptionEn }.Concat((children ?? Array.Empty<GoodsCodeMatchDto>()).SelectMany(c => new[] { c.DescriptionNl, c.DescriptionEn }))
                .Where(part => !string.IsNullOrWhiteSpace(part))));

        // Alles onder de post (zonder het eerste deel van het pad); een omschrijving van één deel telt helemaal.
        private static string Leaf(GoodsCodeMatchDto match) =>
            SearchKey(string.Join(" ", new[] { match.DescriptionNl, match.DescriptionEn }.Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Split('›') is { Length: > 1 } parts ? string.Join(" ", parts.Skip(1)) : part)));

        // Zoeksleutel: kleine letters zonder accenten; leestekens worden een spatie ("avocado's" vindt "avocado", "(Lactuca" vindt "lactuca").
        public static string SearchKey(string value)
        {
            var key = ArticleRules.AttributeKey(value);
            return System.Text.RegularExpressions.Regex.Replace(key, @"[^\p{L}\p{N}]+", " ").Trim();
        }

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
                    return new Searchable(match, Text(match, children), Leaf(match));
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
            var synonymsPath = Path.Combine(Path.GetDirectoryName(path) ?? "", SynonymsFileName);
            var synonyms = File.Exists(synonymsPath) ? JsonSerializer.Deserialize<SynonymsFile>(File.ReadAllText(synonymsPath), Options)?.Synonyms : null;
            return new GoodsCodeCatalog(file?.Source, file?.Entries, file?.HsHeadings,
                synonyms?.Where(s => s?.Terms != null && s.Search != null).Select(s => new Synonym(s.Terms, s.Search)).ToList());
        }

        public IReadOnlyList<GoodsCodeMatchDto> EntriesFor(string system) =>
            _bySystem.TryGetValue(system ?? "", out var list) ? list.Select(s => s.Match).ToList() : Array.Empty<GoodsCodeMatchDto>();

        // Zonder stelsel: GN (de basis van de lijst); bestaande aanroepen blijven werken.
        public IReadOnlyList<GoodsCodeMatchDto> Search(string query) => Search(query, ArticleRules.SystemCn);

        // Cijfers (met of zonder punten en spaties) = zoeken op (begin van) de code van het gekozen stelsel; een langere code (bijv. een
        // GN-code onder HS) vindt de post waar zij onder valt. Tekst = elk woord moet in de Nederlandse of Engelse omschrijving voorkomen
        // (bij HS ook in die van de onderliggende GN-codes), of een synoniem ervan; hoofdletters en accenten tellen niet. Een treffer in het
        // laatste deel van de omschrijving komt eerst. Nooit codes van een ander stelsel.
        public IReadOnlyList<GoodsCodeMatchDto> Search(string query, string system)
        {
            var term = (query ?? "").Trim();
            if (term == "" || !_bySystem.TryGetValue(system ?? "", out var candidates))
            {
                return Array.Empty<GoodsCodeMatchDto>();
            }
            var digits = ArticleRules.NormalizeGoodsCode(term);
            if (digits != null && digits.Length > 0 && term.All(c => char.IsDigit(c) || c == ' ' || c == '.'))
            {
                return candidates.Where(s => s.Match.Code.StartsWith(digits, StringComparison.Ordinal)
                        || (digits.Length > s.Match.Code.Length && digits.StartsWith(s.Match.Code, StringComparison.Ordinal)))
                    .Select(s => s.Match).Take(MaxMatches).ToList();
            }
            var words = Alternatives(SearchKey(term));
            if (words.Count == 0)
            {
                return Array.Empty<GoodsCodeMatchDto>();
            }
            return candidates
                .Where(s => words.All(options => options.Any(s.Text.Contains)))
                .OrderBy(s => words.All(options => options.Any(s.Leaf.Contains)) ? 0 : 1)
                .ThenBy(s => s.Match.Code, StringComparer.Ordinal)
                .Select(s => s.Match).Take(MaxMatches).ToList();
        }

        // Per gezocht woord de teksten waarvan er één moet voorkomen: het woord zelf, of bij een synoniem (ook van meer woorden, zoals
        // "blauwe bes") de term en zijn zoekwoorden.
        private List<IReadOnlyList<string>> Alternatives(string key)
        {
            var words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var result = new List<IReadOnlyList<string>>();
            for (var i = 0; i < words.Count;)
            {
                var hit = _synonyms
                    .SelectMany(s => s.Terms.Select(t => (Synonym: s, Term: t, Length: t.Split(' ').Length)))
                    .Where(x => i + x.Length <= words.Count && string.Join(' ', words.Skip(i).Take(x.Length)) == x.Term)
                    .OrderByDescending(x => x.Length)
                    .FirstOrDefault();
                if (hit.Synonym != null)
                {
                    result.Add(hit.Synonym.Search.Append(hit.Term).ToList());
                    i += hit.Length;
                }
                else
                {
                    result.Add(new[] { words[i] });
                    i++;
                }
            }
            return result;
        }

        // Staat deze code (genormaliseerd) als code van dit stelsel in de lijst? Nee = waarschuwing in het scherm, nooit een blokkade.
        public bool IsKnown(string system, string code)
        {
            var normalized = ArticleRules.NormalizeGoodsCode(code);
            return normalized != null && _bySystem.TryGetValue(system ?? "", out var list) && list.Any(s => s.Match.Code == normalized);
        }

        public GoodsCodeSearchDto Result(string query) => Result(query, ArticleRules.SystemCn);

        public GoodsCodeSearchDto Result(string query, string system) => new()
        {
            System = system,
            Source = Source,
            Complete = false,
            TotalEntries = EntriesFor(system).Count,
            Matches = Search(query, system).ToList(),
            Known = ArticleRules.IsGoodsCodeFor(system, ArticleRules.NormalizeGoodsCode(query)) ? IsKnown(system, query) : null
        };

        private sealed class CatalogFile
        {
            [JsonPropertyName("source")] public GoodsCodeSourceDto Source { get; set; }
            [JsonPropertyName("hsHeadings")] public List<GoodsCodeMatchDto> HsHeadings { get; set; }
            [JsonPropertyName("entries")] public List<GoodsCodeMatchDto> Entries { get; set; }
        }

        private sealed class SynonymsFile
        {
            [JsonPropertyName("synonyms")] public List<SynonymRow> Synonyms { get; set; }
        }

        private sealed class SynonymRow
        {
            [JsonPropertyName("terms")] public List<string> Terms { get; set; }
            [JsonPropertyName("search")] public List<string> Search { get; set; }
        }
    }
}
