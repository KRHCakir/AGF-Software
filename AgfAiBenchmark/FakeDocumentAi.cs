using System.Text.Json.Nodes;
using AgfDocumentAi;

namespace AgfAiBenchmark;

/// <summary>
/// Nagebootst model zonder netwerk: geeft de juiste antwoorden terug ("fake-goed") of een versie met typische fouten
/// ("fake-slordig"). Zo laat een proefrun het hele overzicht zien zonder dat er een document de deur uit gaat.
/// </summary>
public sealed class FakeDocumentAi(string variant, string truthDir) : IDocumentAI
{
    public Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken)
    {
        var truthPath = Path.Combine(truthDir, Path.GetFileNameWithoutExtension(document.Name) + ".json");
        var output = File.Exists(truthPath) ? JsonNode.Parse(File.ReadAllText(truthPath))!.AsObject() : new JsonObject { ["soort"] = "overig" };
        output.Remove("_meta");
        AsModelOutput(output);
        output["zekerheden"] = new JsonArray();
        if (variant == "fake-slordig") Sloppy(output);
        var text = output.ToJsonString();
        var duration = TimeSpan.FromMilliseconds(variant == "fake-slordig" ? 400 : 1200);
        return Task.FromResult(new AiResponse(text, 1500 + document.Bytes.Length / 3, text.Length / 4, duration, variant + "-1"));
    }

    // De juiste antwoorden mogen nl-notatie bevatten; een model geeft getallen en een ISO-datum.
    private static void AsModelOutput(JsonObject output)
    {
        output["datum"] = Comparer.Date(output["datum"]?.ToString());
        static void Numbers(JsonObject o, IEnumerable<(string Name, FieldKind Kind)> fields)
        {
            foreach (var (name, kind) in fields)
                if (kind is FieldKind.Number or FieldKind.Money) o[name] = Comparer.Number(o[name]) is decimal d ? JsonValue.Create(d) : null;
        }
        Numbers(output, ExtractionSchema.TotalFields);
        foreach (var line in (output["regels"] as JsonArray ?? []).OfType<JsonObject>()) Numbers(line, ExtractionSchema.LineFields);
    }

    // Fouten die echte modellen ook maken: een bedrag verkeerd terwijl hij "hoog zeker" is, een vergeten regel, een verzonnen
    // valuta, een ontbrekend totaal. Datum en factuurnummer in een andere schrijfwijze tellen na normaliseren als goed.
    private static void Sloppy(JsonObject output)
    {
        if (output["datum"]?.GetValue<string>() is { Length: 10 } date && date[4] == '-') output["datum"] = $"{date[8..]}-{date[5..7]}-{date[..4]}";
        if (output["factuurnummer"]?.GetValue<string>() is { } number) output["factuurnummer"] = " " + number.ToLowerInvariant() + " ";
        if (output["valuta"] == null) output["valuta"] = "EUR";
        output["totaal_incl"] = null;
        if (output["regels"] is JsonArray { Count: > 0 } lines)
        {
            if (Comparer.Number(lines[0]?["bedrag"]) is decimal amount) lines[0]!["bedrag"] = amount + 1m;
            ((JsonArray)output["zekerheden"]!).Add(new JsonObject { ["veld"] = "regels[0].bedrag", ["zekerheid"] = "hoog", ["plek"] = "pagina 1" });
            if (lines.Count > 1) lines.RemoveAt(lines.Count - 1);
        }
    }
}
