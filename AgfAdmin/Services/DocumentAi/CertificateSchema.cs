using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgfAdmin.Services.DocumentAi
{
    // Certificaten deel 2: het vaste uitvoerformaat voor het lezen van een certificaat (PDF of foto). Elk veld is verplicht en gewone tekst,
    // met "" voor "niet gevonden" (geen null: zo blijft het schema binnen de grens van Anthropic voor velden met een type-lijst). Het schema
    // wordt afgedwongen (structured output); Validate controleert de uitvoer daarnaast altijd. De AI doet alleen een voorstel.
    public static class CertificateSchema
    {
        public const string Other = "Anders";
        public static readonly string[] Certainties = ["hoog", "midden", "laag"];
        // De codes uit de vaste lijst van certificaten deel 1, plus "Anders" voor een soort die er niet in staat.
        public static IReadOnlyList<string> TypeCodes { get; } = CertificateRules.Types.Select(t => t.Code).Append(Other).ToList();

        private static readonly string[] TextFields = ["soort_naam", "nummer", "houder_naam", "houder_land", "certificeringsinstantie", "geldig_van", "geldig_tot"];

        public static string Instruction => $$"""
            Je leest één certificaat van een bedrijf in de keten van groente en fruit (bijvoorbeeld GLOBALG.A.P. met een GGN, Chain of Custody,
            SKAL of EU-biologisch, BRCGS, IFS, GRASP, SMETA, Rainforest Alliance of Fairtrade). Lees ALLE pagina's, ook een bijlage ("Annex",
            "Anhang", "Anexo", "Appendix") op een volgende pagina. Geef uitsluitend JSON volgens het schema.
            - Neem waarden over zoals ze in het document staan. Verzin niets: wat je niet ziet of niet zeker kunt lezen is een lege tekst "".
            - soort: één van deze codes: {{string.Join(", ", CertificateRules.Types.Select(t => $"{t.Code} ({t.Name})"))}}. Past er geen, gebruik "{{Other}}".
            - soort_naam: de naam van het certificaat of programma zoals in het document (bijvoorbeeld "GLOBALG.A.P. Integrated Farm Assurance").
            - nummer: het certificaatnummer. Bij GLOBALG.A.P. het GGN (13 cijfers, zonder spaties), niet het nummer van de certificeringsinstantie.
            - houder_naam en houder_land: het bedrijf op wiens naam het certificaat staat (houder of producent) en zijn land (ISO-code zoals ES
              of NL als het land duidelijk is, anders zoals in het document).
            - certificeringsinstantie: de naam van de certificeringsinstantie (certification body).
            - geldig_van en geldig_tot: de geldigheid als JJJJ-MM-DD. Let op de schrijfwijze van het document: in Europa dag-maand-jaar, in
              Amerikaanse documenten maand/dag/jaar, maandnamen in het Engels, Duits of Spaans. Twijfel je, zet dan een zekerheid "laag".
            - producten: elk product waarvoor het certificaat geldt, van alle pagina's en bijlagen, elk met de letterlijke tekst uit het document
              (bijvoorbeeld "Lettuce" of "Rocket (Eruca sativa)") en het paginanummer. Geen productielocaties, adressen of oppervlaktes.
            - productiesites: staan er productielocaties in een bijlage, dan alleen per locatie de naam en het land; anders een lege lijst.
            - zekerheden: per veld waar je over twijfelt: veld (bijvoorbeeld "nummer", "geldig_tot" of "producten[2]"), zekerheid (hoog, midden
              of laag) en plek in het document ("pagina 2, bijlage") of "" als je dat niet weet.
            """;

        public static JsonObject Build()
        {
            static JsonObject Text() => new() { ["type"] = "string" };
            static JsonObject Obj(params (string Name, JsonNode Type)[] props)
            {
                var properties = new JsonObject(props.Select(p => new KeyValuePair<string, JsonNode>(p.Name, p.Type)));
                return new JsonObject
                {
                    ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false,
                    ["required"] = new JsonArray(props.Select(p => (JsonNode)p.Name).ToArray()),
                };
            }
            static JsonObject Enum(IEnumerable<string> values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) };
            static JsonObject List(JsonNode items) => new() { ["type"] = "array", ["items"] = items };
            var fields = new List<(string, JsonNode)> { ("soort", Enum(TypeCodes)) };
            fields.AddRange(TextFields.Select(f => (f, (JsonNode)Text())));
            fields.Add(("producten", List(Obj(("tekst", Text()), ("pagina", new JsonObject { ["type"] = "integer" })))));
            fields.Add(("productiesites", List(Obj(("naam", Text()), ("land", Text())))));
            fields.Add(("zekerheden", List(Obj(("veld", Text()), ("zekerheid", Enum(Certainties)), ("plek", Text())))));
            return Obj(fields.ToArray());
        }

        // Controleert de uitvoer van een model; ongeldig = null met de reden (alleen de soort fout, nooit de inhoud, gaat naar het logboek).
        public static JsonObject Validate(string text, out string error)
        {
            error = null;
            JsonNode root;
            try { root = JsonNode.Parse(StripFence(text ?? "")); }
            catch (JsonException e) { error = $"geen geldige JSON: {e.Message}"; return null; }
            if (root is not JsonObject obj) { error = "geen JSON-object"; return null; }
            var known = new HashSet<string>(TextFields.Concat(["soort", "producten", "productiesites", "zekerheden"]));
            // Zonder de naam van de sleutel: die komt uit de uitvoer van het model en kan inhoud van het document bevatten (het logboek krijgt alleen de soort fout).
            error = (obj.Any(p => !known.Contains(p.Key)) ? "onbekende eigenschap" : null)
                ?? (obj["soort"] is JsonValue soort && soort.TryGetValue(out string code) && TypeCodes.Contains(code) ? null : "soort ontbreekt of is onbekend")
                ?? TextFields.Where(f => !IsText(obj[f])).Select(f => $"{f} ontbreekt of is geen tekst").FirstOrDefault()
                ?? Items(obj, "producten", item => IsText(item["tekst"]) && item["pagina"] is JsonValue page && page.TryGetValue(out int _), ["tekst", "pagina"])
                ?? Items(obj, "productiesites", item => IsText(item["naam"]) && IsText(item["land"]), ["naam", "land"])
                ?? Items(obj, "zekerheden", item => IsText(item["veld"]) && IsText(item["plek"]) && item["zekerheid"] is JsonValue z && z.TryGetValue(out string level) && Certainties.Contains(level),
                    ["veld", "zekerheid", "plek"]);
            return error == null ? obj : null;
        }

        private static bool IsText(JsonNode node) => node is JsonValue value && value.TryGetValue(out string _);

        private static string Items(JsonObject obj, string name, Func<JsonObject, bool> valid, string[] fields)
        {
            if (obj[name] is not JsonArray array) return $"{name} ontbreekt of is geen lijst";
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject item || !valid(item) || item.Any(p => !fields.Contains(p.Key))) return $"{name}[{i}] klopt niet";
            }
            return null;
        }

        private static string StripFence(string text)
        {
            var t = text.Trim();
            if (!t.StartsWith("```", StringComparison.Ordinal)) return t;
            var start = t.IndexOf('\n');
            var end = t.LastIndexOf("```", StringComparison.Ordinal);
            return start >= 0 && end > start ? t[(start + 1)..end] : t;
        }
    }
}
