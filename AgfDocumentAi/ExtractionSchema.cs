using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgfDocumentAi;

public enum FieldKind { Text, Number, Money, Date }

/// <summary>
/// Het vaste uitvoerformaat voor alle modellen: dezelfde velden, dezelfde namen, een ontbrekend veld is null.
/// Het schema wordt afgedwongen waar de aanbieder dat kan (structured output); <see cref="Validate"/> controleert het daarnaast altijd.
/// </summary>
public static class ExtractionSchema
{
    public static readonly string[] Kinds = ["inkoopfactuur", "verkoopfactuur", "bon", "overig"];
    public static readonly string[] Certainties = ["hoog", "midden", "laag"];

    public static readonly (string Name, FieldKind Kind)[] HeaderFields =
    [
        ("leverancier", FieldKind.Text), ("ordernummer_leverancier", FieldKind.Text), ("factuurnummer", FieldKind.Text),
        ("datum", FieldKind.Date), ("valuta", FieldKind.Text),
    ];

    public static readonly (string Name, FieldKind Kind)[] LineFields =
    [
        ("omschrijving", FieldKind.Text), ("aantal", FieldKind.Number), ("eenheid", FieldKind.Text), ("kg_netto", FieldKind.Number),
        ("kg_bruto", FieldKind.Number), ("prijs", FieldKind.Money), ("bedrag", FieldKind.Money), ("btw_percentage", FieldKind.Number),
    ];

    public static readonly (string Name, FieldKind Kind)[] TotalFields =
        [("totaal_excl", FieldKind.Money), ("btw_totaal", FieldKind.Money), ("totaal_incl", FieldKind.Money)];

    /// <summary>Grens van Anthropic voor velden met een union-type (zoals ["string","null"]) in een afgedwongen schema.</summary>
    public const int MaxNullableFields = 16;

    public const string Instruction = """
        Je leest één zakelijk document van een groothandel in groente en fruit: een inkoopfactuur, verkoopfactuur, bon
        (pakbon, weegbon, ontvangstbon, kassabon) of iets anders. Geef uitsluitend JSON volgens het schema.
        - Neem waarden over zoals ze in het document staan. Verzin niets: wat je niet ziet of niet zeker kunt lezen is null.
        - soort: inkoopfactuur als wij (de ontvanger) moeten betalen, verkoopfactuur als wij de uitgever zijn, bon of overig.
        - leverancier: het bedrijf dat het document uitgeeft. ordernummer_leverancier: hun order- of referentienummer.
          factuurnummer: het factuurnummer, bij een bon het bonnummer.
        - datum: de factuurdatum (anders de documentdatum) als JJJJ-MM-DD. valuta: ISO-code zoals EUR of USD.
        - Getallen als JSON-getal met een punt als decimaalteken, zonder valutateken en zonder duizendtallen.
        - regels: elke artikel- of kostenregel (ook transport, handling, emballage), in de volgorde van het document;
          geen subtotaal-, korting- of btw-totaalregels. prijs is per eenheid, bedrag is het regelbedrag excl. btw.
        - zekerheden: per veld waar je over twijfelt of juist zeker bent: veld (bijvoorbeeld "factuurnummer" of
          "regels[0].bedrag"), zekerheid (hoog, midden of laag) en plek in het document (bijvoorbeeld "pagina 1, rechtsboven") of "" als je dat niet weet.
        """;

    /// <summary>
    /// JSON Schema in de subset die Anthropic, Gemini en Qwen alle drie aannemen: elk veld verplicht, null toegestaan.
    /// Anthropic weigert meer dan <see cref="MaxNullableFields"/> velden met een type-lijst; daarom is plek gewone tekst.
    /// </summary>
    public static JsonObject Build()
    {
        static JsonObject Field(FieldKind kind) => new() { ["type"] = new JsonArray(kind is FieldKind.Number or FieldKind.Money ? "number" : "string", "null") };
        static JsonObject Obj(IEnumerable<KeyValuePair<string, JsonNode?>> props)
        {
            var properties = new JsonObject(props);
            return new JsonObject
            {
                ["type"] = "object", ["properties"] = properties, ["additionalProperties"] = false,
                ["required"] = new JsonArray(properties.Select(p => (JsonNode?)p.Key).ToArray()),
            };
        }
        static IEnumerable<KeyValuePair<string, JsonNode?>> Props((string Name, FieldKind Kind)[] fields) =>
            fields.Select(f => new KeyValuePair<string, JsonNode?>(f.Name, Field(f.Kind)));
        static JsonNode Enum(string[] values) => new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode?)v).ToArray()) };

        var certainty = Obj([new("veld", new JsonObject { ["type"] = "string" }), new("zekerheid", Enum(Certainties)), new("plek", new JsonObject { ["type"] = "string" })]);
        return Obj(
            new KeyValuePair<string, JsonNode?>[] { new("soort", Enum(Kinds)) }
                .Concat(Props(HeaderFields))
                .Append(new("regels", new JsonObject { ["type"] = "array", ["items"] = Obj(Props(LineFields)) }))
                .Concat(Props(TotalFields))
                .Append(new("zekerheden", new JsonObject { ["type"] = "array", ["items"] = certainty })));
    }

    /// <summary>Vingerafdruk van instructie plus schema: een run wordt alleen overgeslagen als die gelijk is gebleven.</summary>
    public static string PromptHash { get; } =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Instruction + Build().ToJsonString()))).ToLowerInvariant()[..12];

    /// <summary>Controleert de uitvoer van een model. Een ongeldige uitvoer telt als fout (de runner probeert één keer opnieuw).</summary>
    public static JsonObject? Validate(string text, out string? error)
    {
        error = null;
        JsonNode? root;
        try { root = JsonNode.Parse(StripFence(text)); }
        catch (JsonException e) { error = $"geen geldige JSON: {e.Message}"; return null; }
        if (root is not JsonObject obj) { error = "geen JSON-object"; return null; }
        if (obj["soort"] is not JsonValue soort || !soort.TryGetValue(out string? kind) || !Kinds.Contains(kind)) error = "soort ontbreekt of is onbekend";
        else error = CheckObject(obj, [.. HeaderFields, .. TotalFields], ["soort", "regels", "zekerheden"], "")
            ?? CheckArray(obj, "regels", (item, prefix) => CheckObject(item, LineFields, [], prefix))
            ?? CheckArray(obj, "zekerheden", CheckCertainty);
        return error == null ? obj : null;
    }

    // Spiegelt Build(): elk veld verplicht (null mag bij de gewone velden), geen onbekende eigenschappen, juiste typen.
    private static string? CheckObject(JsonObject obj, (string Name, FieldKind Kind)[] fields, string[] otherNames, string prefix)
    {
        foreach (var (name, _) in obj)
            if (!fields.Any(f => f.Name == name) && !otherNames.Contains(name)) return $"{prefix}{name} is een onbekende eigenschap";
        foreach (var name in otherNames)
            if (!obj.ContainsKey(name)) return $"{prefix}{name} ontbreekt";
        foreach (var (name, kind) in fields)
        {
            if (!obj.TryGetPropertyValue(name, out var value)) return $"{prefix}{name} ontbreekt";
            if (value == null) continue;
            var expected = kind is FieldKind.Number or FieldKind.Money ? JsonValueKind.Number : JsonValueKind.String;
            if (value.GetValueKind() != expected) return $"{prefix}{name} heeft het verkeerde type";
        }
        return null;
    }

    private static string? CheckArray(JsonObject obj, string name, Func<JsonObject, string, string?> checkItem)
    {
        if (obj[name] is not JsonArray array) return $"{name} ontbreekt of is geen lijst";
        for (var i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject item) return $"{name}[{i}] is geen object";
            var error = checkItem(item, $"{name}[{i}].");
            if (error != null) return error;
        }
        return null;
    }

    private static string? CheckCertainty(JsonObject item, string prefix)
    {
        if (item["veld"] is not JsonValue veld || !veld.TryGetValue(out string? _)) return $"{prefix}veld ontbreekt of is geen tekst";
        if (item["zekerheid"] is not JsonValue z || !z.TryGetValue(out string? level) || !Certainties.Contains(level)) return $"{prefix}zekerheid ontbreekt of is onbekend";
        return CheckObject(item, [("plek", FieldKind.Text)], ["veld", "zekerheid"], prefix);
    }

    // Sommige modellen zetten JSON zonder afgedwongen schema tussen ```json ... ```.
    private static string StripFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```")) return t;
        var start = t.IndexOf('\n');
        var end = t.LastIndexOf("```", StringComparison.Ordinal);
        return start >= 0 && end > start ? t[(start + 1)..end] : t;
    }
}
