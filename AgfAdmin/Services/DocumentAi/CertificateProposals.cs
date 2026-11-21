using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using AgfAdmin.DTOs;

namespace AgfAdmin.Services.DocumentAi
{
    // Certificaten deel 2: van de uitvoer van de AI naar het voorstel op het scherm, zonder database (met tests). Soort, nummer (GGN met de
    // controle uit certificaten deel 1), houder, instantie, geldigheid met de status, de producten van alle pagina's met een voorgestelde
    // koppeling aan een eigen product, en de controles: twijfel (gele markering), niet gevonden, een houder die duidelijk een ander bedrijf
    // is, en een certificaat met hetzelfde nummer bij deze relatie (dan een nieuwe versie). Er wordt nooit een product aangemaakt.
    public static class CertificateProposals
    {
        public const string MatchExact = "Exact";
        public const string MatchTranslated = "Translated";
        public const string MatchPartial = "Partial";
        public const string MatchNone = "None";
        public const string ProblemGgnInvalid = "ggnInvalid";
        public const string ProblemGgnCheckDigit = "ggnCheckDigit";
        public const string ProblemHolder = "holderMismatch";
        public const string ProblemUnmatched = "unmatched";
        public const string ProblemTooLong = "tooLong";
        // Meer producten gelezen dan het voorstel toont: de gebruiker controleert de rest zelf in het document.
        public const string ProblemTruncated = "truncated";
        public const int MaxProducts = 200;
        public const int MaxSites = 50;

        public sealed record OwnProduct(int Id, string Name);
        public sealed record StoredCertificate(int Id, string TypeCode, string CustomTypeName, string Number);

        // Engelse (en enkele Duitse en Spaanse) productnamen op certificaten met de Nederlandse naam van het product. Meervoud en
        // hoofdletters doen niet mee; de langste naam gaat voor ("iceberg lettuce" vóór "lettuce").
        private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
        {
            ["iceberg lettuce"] = "ijsbergsla", ["lettuce"] = "sla", ["salat"] = "sla", ["lechuga"] = "sla", ["arugula"] = "rucola", ["rocket"] = "rucola",
            ["rucola"] = "rucola", ["spinach"] = "spinazie", ["avocado"] = "avocado", ["aguacate"] = "avocado", ["mango"] = "mango", ["lime"] = "limoen",
            ["lemon"] = "citroen", ["orange"] = "sinaasappel", ["mandarin"] = "mandarijn", ["tangerine"] = "mandarijn", ["clementine"] = "clementine",
            ["grapefruit"] = "grapefruit", ["apple"] = "appel", ["pear"] = "peer", ["table grape"] = "druif", ["grape"] = "druif", ["tomato"] = "tomaat",
            ["cherry tomato"] = "cherrytomaat", ["cucumber"] = "komkommer", ["sweet pepper"] = "paprika", ["bell pepper"] = "paprika", ["pepper"] = "paprika",
            ["chili pepper"] = "peper", ["onion"] = "ui", ["potato"] = "aardappel", ["sweet potato"] = "zoete aardappel", ["carrot"] = "wortel",
            ["strawberry"] = "aardbei", ["blueberry"] = "blauwe bes", ["raspberry"] = "framboos", ["blackberry"] = "braam", ["banana"] = "banaan",
            ["pineapple"] = "ananas", ["melon"] = "meloen", ["watermelon"] = "watermeloen", ["kiwi"] = "kiwi", ["kiwifruit"] = "kiwi", ["cabbage"] = "kool",
            ["broccoli"] = "broccoli", ["cauliflower"] = "bloemkool", ["leek"] = "prei", ["garlic"] = "knoflook", ["ginger"] = "gember",
            ["pomegranate"] = "granaatappel", ["passion fruit"] = "passievrucht", ["asparagus"] = "asperge", ["mushroom"] = "champignon",
            ["zucchini"] = "courgette", ["courgette"] = "courgette", ["eggplant"] = "aubergine", ["aubergine"] = "aubergine", ["cherry"] = "kers",
            ["plum"] = "pruim", ["peach"] = "perzik", ["nectarine"] = "nectarine", ["apricot"] = "abrikoos", ["fig"] = "vijg", ["coconut"] = "kokosnoot",
            ["papaya"] = "papaja", ["lychee"] = "lychee", ["persimmon"] = "kaki", ["kaki"] = "kaki", ["fennel"] = "venkel", ["celery"] = "selderij",
            ["radish"] = "radijs", ["beetroot"] = "rode biet", ["green bean"] = "sperzieboon", ["bean"] = "boon", ["pea"] = "erwt", ["sugar snap"] = "sugarsnap",
            ["corn"] = "mais", ["sweetcorn"] = "mais", ["pumpkin"] = "pompoen", ["squash"] = "pompoen", ["artichoke"] = "artisjok", ["endive"] = "andijvie",
            ["chicory"] = "witlof", ["kale"] = "boerenkool", ["brussels sprout"] = "spruitjes", ["herbs"] = "kruiden", ["mizuna"] = "mizuna",
        };

        private static readonly string[] LegalWords =
        [
            "bv", "b.v.", "nv", "n.v.", "vof", "v.o.f.", "gmbh", "ag", "kg", "ltd", "limited", "plc", "llc", "inc", "co", "corp", "company", "sl", "s.l.", "sa", "s.a.",
            "sas", "sarl", "srl", "spa", "s.p.a.", "lda", "pty", "cv", "c.v.", "the", "de", "het", "van", "en", "und", "y", "and", "group", "groep", "holding", "international",
            "kwekerij", "kwekerijen", "nursery", "nurseries", "tuinbouw", "tuinderij", "telers", "teelt", "kwekers", "growers", "grower", "producers", "produce", "packhouse", "greenhouse",
            "greenhouses", "kassen", "handel", "bedrijf", "vers", "fresh", "vegetables", "groenten", "orchards", "orchard", "finca", "huerta", "gartenbau", "trading", "export", "import", "sociedad", "cooperativa", "coop", "agricola", "agrícola", "farms", "farm", "fruit", "fruits",
        ];

        public static AiCertificateProposalDto Build(JsonObject json, IReadOnlyCollection<OwnProduct> products, IReadOnlyCollection<StoredCertificate> stored, IReadOnlyCollection<string> ownerNames,
            DateOnly today, int warningDays, out List<AiProblemDto> problems)
        {
            problems = new List<AiProblemDto>();
            var list = problems;
            void Problem(string field, string code) { if (!list.Any(p => p.Field == field && p.Code == code)) list.Add(new AiProblemDto { Field = field, Code = code }); }

            var code = AiReadingRules.Text(json["soort"]);
            var documentName = AiReadingRules.Text(json["soort_naam"]);
            var known = CertificateRules.Find(code);
            string typeCode = known?.Code, customName = null;
            if (known == null && documentName != null)
            {
                typeCode = CertificateRules.Custom;
                customName = documentName.Length > CertificateRules.MaxCustomTypeLength ? documentName[..CertificateRules.MaxCustomTypeLength].Trim() : documentName;
            }
            if (typeCode == null) Problem("typeCode", AiReadingRules.Missing);

            var raw = AiReadingRules.Text(json["nummer"]);
            var number = raw == null ? null : CertificateRules.NormalizeNumber(typeCode, raw);
            if (number == null) Problem("number", AiReadingRules.Missing);
            else if (CertificateRules.IsGgnType(typeCode) && !ArticleRules.IsGgn(number)) Problem("number", ProblemGgnInvalid);
            else if (CertificateRules.IsGgnType(typeCode) && !CertificateRules.HasValidCheckDigit(number)) Problem("number", ProblemGgnCheckDigit);
            // Te lang: niet stil inkorten (dan zou het een ander nummer worden), maar markeren; opslaan weigert het tot de gebruiker het aanpast.
            if (number?.Length > CertificateRules.MaxNumberLength) Problem("number", ProblemTooLong);

            var holder = AiReadingRules.Text(json["houder_naam"]);
            if (holder == null) Problem("holderName", AiReadingRules.Missing);
            var body = AiReadingRules.Text(json["certificeringsinstantie"]);
            if (body == null) Problem("certificationBody", AiReadingRules.Missing);
            DateOnly? Date(string field, string key)
            {
                var text = AiReadingRules.Text(json[key]);
                if (text == null) { Problem(field, AiReadingRules.Missing); return null; }
                var date = AiReadingRules.Date(json[key]);
                if (date == null) Problem(field, AiReadingRules.InvalidDate);
                return date;
            }
            var from = Date("validFrom", "geldig_van");
            var until = Date("validUntil", "geldig_tot");

            var dto = new AiCertificateProposalDto
            {
                TypeCode = typeCode, CustomTypeName = customName, DocumentTypeName = documentName, Number = number, HolderName = holder,
                HolderCountry = AiReadingRules.Text(json["houder_land"]), CertificationBody = body, ValidFrom = from, ValidUntil = until,
                Status = CertificateRules.Status(until, today, warningDays), DaysLeft = until is DateOnly u ? CertificateRules.DaysLeft(u, today) : null,
            };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var productIndexes = new Dictionary<int, int>(); // index in de uitvoer van de AI -> index in het voorstel (na weglaten van lege en dubbele)
            var firstIndexByText = new Dictionary<string, int>(StringComparer.Ordinal);
            var rawProducts = json["producten"] as JsonArray ?? new JsonArray();
            if (rawProducts.Count > MaxProducts) Problem("products", ProblemTruncated);
            for (var rawIndex = 0; rawIndex < rawProducts.Count && rawIndex < MaxProducts; rawIndex++)
            {
                if (rawProducts[rawIndex] is not JsonObject item) continue;
                var text = AiReadingRules.Text(item["tekst"]);
                if (text == null) continue;
                var textKey = string.Join(' ', text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant(); // letterlijke tekst: haakjes onderscheiden producten
                if (!seen.Add(textKey))
                {
                    if (firstIndexByText.TryGetValue(textKey, out var firstIndex)) productIndexes[rawIndex] = firstIndex;
                    continue;
                }
                productIndexes[rawIndex] = dto.Products.Count;
                firstIndexByText[textKey] = dto.Products.Count;
                var (product, match) = MatchProduct(text, products);
                dto.Products.Add(new AiCertificateProductDto
                {
                    Text = text, Page = item["pagina"] is JsonValue page && page.TryGetValue(out int p) && p > 0 ? p : null, ProductId = product?.Id, ProductName = product?.Name, Match = match,
                });
                if (product == null) Problem($"products[{dto.Products.Count - 1}]", ProblemUnmatched);
            }
            if (dto.Products.Count == 0) Problem("products", AiReadingRules.Missing);
            dto.Sites = (json["productiesites"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Take(MaxSites)
                .Select(s => new AiCertificateSiteDto { Name = AiReadingRules.Text(s["naam"]), Country = AiReadingRules.Text(s["land"]) })
                .Where(s => s.Name != null).ToList();

            // De twijfels van de AI zelf (midden of laag): gele markering bij het veld.
            foreach (var doubt in (json["zekerheden"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                if (AiReadingRules.Text(doubt["zekerheid"]) is not ("laag" or "midden")) continue;
                var aiField = (AiReadingRules.Text(doubt["veld"]) ?? "").Trim();
                if (aiField.StartsWith("producten[", StringComparison.Ordinal) && aiField.EndsWith(']'))
                {
                    // Producttwijfel: via de mapping naar het product in het voorstel; een weggelaten product krijgt geen markering.
                    if (int.TryParse(aiField[10..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var rawIdx) && productIndexes.TryGetValue(rawIdx, out var index))
                    {
                        Problem($"products[{index}]", AiReadingRules.Uncertain);
                    }
                    continue;
                }
                if (FormField(aiField) is string field) Problem(field, AiReadingRules.Uncertain);
            }

            if (holder != null && ownerNames.Count > 0 && ownerNames.All(name => !SameCompany(holder, name)))
            {
                dto.HolderMismatch = holder;
                Problem("holderName", ProblemHolder);
            }
            var key = CertificateRules.NumberKey(number);
            var same = key == null ? null : stored.FirstOrDefault(c => CertificateRules.NumberKey(c.Number) == key);
            if (same != null)
            {
                dto.SameNumberCertificateId = same.Id;
                dto.SameNumberTypeName = CertificateRules.TypeName(same.TypeCode, same.CustomTypeName);
            }
            return dto;
        }

        // Veldnaam van het model naar het veld van het voorstel; onbekend: null.
        public static string FormField(string aiField)
        {
            var field = (aiField ?? "").Trim();
            if (field.StartsWith("producten[", StringComparison.Ordinal) && field.EndsWith(']') && int.TryParse(field[10..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                return $"products[{index}]";
            }
            return field switch
            {
                "soort" or "soort_naam" => "typeCode", "nummer" => "number", "houder_naam" => "holderName", "houder_land" => "holderCountry",
                "certificeringsinstantie" => "certificationBody", "geldig_van" => "validFrom", "geldig_tot" => "validUntil", "producten" => "products",
                _ => null,
            };
        }

        // De voorgestelde koppeling: dezelfde naam, anders de vertaling uit de lijst, anders een eigen product waarvan de naam als woord in
        // de tekst staat. Meer dan één kandidaat of geen: niet gekoppeld (de gebruiker kiest zelf).
        public static (OwnProduct Product, string Match) MatchProduct(string text, IReadOnlyCollection<OwnProduct> products)
        {
            var normalized = Normalize(text);
            var own = products.Select(p => (Product: p, Key: Normalize(p.Name))).Where(p => p.Key.Length > 0).ToList();
            OwnProduct Single(IEnumerable<OwnProduct> candidates)
            {
                var found = candidates.DistinctBy(p => p.Id).Take(2).ToList();
                return found.Count == 1 ? found[0] : null;
            }
            var exact = Single(own.Where(p => p.Key == normalized || Singular(p.Key) == Singular(normalized)).Select(p => p.Product));
            if (exact != null) return (exact, MatchExact);
            var words = Singular(normalized);
            // Alle genoemde producten uit de lijst; een kortere naam die in een langere genoemde naam zit ("tomato" in "cherry tomato") telt niet mee.
            // Per tekstpositie: een kortere naam telt alleen niet mee waar hij binnen een langere naam valt; een afzonderlijke vermelding blijft.
            var tokens = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var spans = Names.Select(n => (Name: n, Spans: Occurrences(tokens, Singular(n.Key)))).Where(n => n.Spans.Count > 0).ToList();
            var named = spans.Where(n => n.Spans.Any(s => !spans.Any(o => o.Name.Key != n.Name.Key
                && o.Spans.Any(t => t.Start <= s.Start && t.End >= s.End && t.End - t.Start > s.End - s.Start)))).Select(n => n.Name).ToList();
            var dutchNames = named.Select(n => Singular(n.Value)).Distinct(StringComparer.Ordinal).ToList();
            // Ook eigen productnamen die letterlijk als woord in de tekst staan ("Lettuce and Tomaat"): samen meer dan één product = zelf kiezen.
            var mentioned = own.Where(p => p.Key.Length >= 3 && ContainsWords(words, Singular(p.Key))).Select(p => Singular(p.Key));
            if (dutchNames.Concat(mentioned).Distinct(StringComparer.Ordinal).Count() > 1) return (null, MatchNone);
            if (dutchNames.Count > 1) return (null, MatchNone); // meerdere producten in één tekst: de gebruiker kiest zelf
            if (dutchNames.Count == 1)
            {
                // Een specifiekere naam valt niet terug op een ander product.
                var translated = Single(own.Where(p => Singular(p.Key) == dutchNames[0]).Select(p => p.Product));
                return translated != null ? (translated, MatchTranslated) : (null, MatchNone);
            }
            var partial = Single(own.Where(p => p.Key.Length >= 3 && ContainsWords(words, Singular(p.Key))).Select(p => p.Product));
            return partial != null ? (partial, MatchPartial) : (null, MatchNone);
        }

        // Is de houder dezelfde als de relatie? Ja zodra ze een betekenisvol woord delen (geen rechtsvorm of algemeen woord als "fruit").
        // Alleen een duidelijk ander bedrijf geeft de waarschuwing; zonder betekenisvolle woorden geen waarschuwing.
        public static bool SameCompany(string holder, string relation)
        {
            var a = Significant(holder);
            var b = Significant(relation);
            if (a.Count == 0 || b.Count == 0) return true;
            return a.Overlaps(b) || Normalize(holder).Replace(" ", "", StringComparison.Ordinal).Contains(Normalize(relation).Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal)
                || Normalize(relation).Replace(" ", "", StringComparison.Ordinal).Contains(Normalize(holder).Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
        }

        private static HashSet<string> Significant(string name) =>
            Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3 && !LegalWords.Contains(w)).ToHashSet(StringComparer.Ordinal);

        // Kleine letters zonder accenten, zonder wat tussen haakjes staat (de Latijnse naam) en zonder leestekens.
        public static string Normalize(string text)
        {
            var withoutParentheses = new StringBuilder();
            var depth = 0;
            foreach (var c in text ?? "")
            {
                if (c is '(' or '[') { depth++; continue; }
                if (c is ')' or ']') { depth = Math.Max(0, depth - 1); continue; }
                if (depth == 0) withoutParentheses.Append(c);
            }
            var decomposed = withoutParentheses.ToString().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        // Eenvoudig enkelvoud van elk woord (Engels en Nederlands): tomatoes, berries, avocados, druiven, appels.
        private static string Singular(string words) => string.Join(' ', words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w =>
            w.Length <= 3 ? w
            : w.EndsWith("ies", StringComparison.Ordinal) ? w[..^3] + "y"
            : w.EndsWith("oes", StringComparison.Ordinal) || w.EndsWith("ches", StringComparison.Ordinal) || w.EndsWith("shes", StringComparison.Ordinal) || w.EndsWith("xes", StringComparison.Ordinal) ? w[..^2]
            : w.EndsWith("ven", StringComparison.Ordinal) ? w[..^3] + "f"
            : w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) ? w[..^1]
            : w));

        private static List<(int Start, int End)> Occurrences(string[] tokens, string words)
        {
            var key = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var result = new List<(int, int)>();
            for (var i = 0; key.Length > 0 && i + key.Length <= tokens.Length; i++)
            {
                if (tokens.Skip(i).Take(key.Length).SequenceEqual(key)) result.Add((i, i + key.Length));
            }
            return result;
        }

        private static bool ContainsWords(string text, string words) => $" {text} ".Contains($" {Singular(words)} ", StringComparison.Ordinal);
    }
}
