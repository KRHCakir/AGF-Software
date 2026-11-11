using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AgfDocumentAi;

namespace AgfAiBenchmark;

public enum Outcome { Goed, Fout, Ontbreekt, Verzonnen }

/// <summary>Eén veld van één document: <see cref="Field"/> is de veldsoort (bijv. "regels.bedrag"), <see cref="Path"/> de plek volgens de juiste antwoorden.</summary>
public sealed record FieldScore(string Field, string Group, string Path, Outcome Outcome, string? Expected, string? Actual, bool HighCertainty)
{
    public bool Correct => Outcome == Outcome.Goed;
}

/// <summary>
/// Vergelijkt de uitvoer van een model met de juiste antwoorden, veld voor veld, na normaliseren. Groepen: kop, regels, bedragen.
/// Regels worden eerst op omschrijving gekoppeld en wat overblijft op volgorde (zie README).
/// </summary>
public static class Comparer
{
    public const string Header = "kop", Lines = "regels", Totals = "bedragen";
    private const double MinSimilarity = 0.5;

    public static List<FieldScore> Compare(JsonObject truth, JsonObject? output)
    {
        output ??= [];
        var certain = HighCertaintyPaths(output);
        var scores = new List<FieldScore> { Score("soort", Header, "soort", FieldKind.Text, truth["soort"], output["soort"], certain.Contains("soort")) };
        scores.AddRange(ExtractionSchema.HeaderFields.Select(f => Score(f.Name, Header, f.Name, f.Kind, truth[f.Name], output[f.Name], certain.Contains(f.Name))));
        scores.AddRange(ExtractionSchema.TotalFields.Select(f => Score(f.Name, Totals, f.Name, f.Kind, truth[f.Name], output[f.Name], certain.Contains(f.Name))));

        var expected = Objects(truth["regels"]);
        var actual = Objects(output["regels"]);
        var pairs = MatchLines(expected, actual);
        foreach (var (t, o) in pairs)
            foreach (var (name, kind) in ExtractionSchema.LineFields)
                scores.Add(Score($"regels.{name}", Lines, $"regels[{t}].{name}", kind, expected[t][name], o >= 0 ? actual[o][name] : null,
                    o >= 0 && certain.Contains($"regels[{o}].{name}")));
        var used = pairs.Select(p => p.Actual).ToHashSet();
        for (var o = 0; o < actual.Count; o++)
            if (!used.Contains(o)) scores.Add(new FieldScore("regels.extra", Lines, $"extra regel {o + 1}", Outcome.Verzonnen, null, Text(actual[o]["omschrijving"]), false));
        return scores;
    }

    /// <summary>Koppelt elke juiste regel aan de best passende modelregel op omschrijving (overlap van woorden ≥ 0,5), daarna de rest op volgorde.</summary>
    public static List<(int Expected, int Actual)> MatchLines(IReadOnlyList<JsonObject> expected, IReadOnlyList<JsonObject> actual)
    {
        var pairs = new int[expected.Count];
        Array.Fill(pairs, -1);
        var free = Enumerable.Range(0, actual.Count).ToList();
        var expectedWords = expected.Select(e => Words(e["omschrijving"])).ToList();
        var actualWords = actual.Select(a => Words(a["omschrijving"])).ToList();
        // Sterkste koppelingen over alle regels heen eerst, zodat een vergeten regel niet de modelregel van een ander opeist.
        var candidates = (from t in Enumerable.Range(0, expected.Count)
                          from o in Enumerable.Range(0, actual.Count)
                          let sim = Similarity(expectedWords[t], actualWords[o])
                          where sim.Overlap >= MinSimilarity
                          select (t, o, sim))
            .OrderByDescending(x => x.sim.Shared).ThenByDescending(x => x.sim.Overlap).ThenByDescending(x => x.sim.Jaccard).ThenBy(x => x.t).ThenBy(x => x.o);
        foreach (var (t, o, _) in candidates)
            if (pairs[t] < 0 && free.Contains(o)) { pairs[t] = o; free.Remove(o); }
        for (var t = 0; t < expected.Count; t++)
            if (pairs[t] < 0 && free.Count > 0) { pairs[t] = free[0]; free.RemoveAt(0); }
        return [.. pairs.Select((o, t) => (t, o))];
    }

    private static FieldScore Score(string field, string group, string path, FieldKind kind, JsonNode? expected, JsonNode? actual, bool high)
    {
        var e = Normalize(kind, field, expected);
        var a = Normalize(kind, field, actual);
        var outcome = (e, a) switch
        {
            (null, null) => Outcome.Goed,
            (null, _) => Outcome.Verzonnen,
            (_, null) => Outcome.Ontbreekt,
            _ => e == a || (field == "regels.omschrijving" && ContainsAllWords(a, e)) ? Outcome.Goed : Outcome.Fout,
        };
        return new FieldScore(field, group, path, outcome, e, a, high);
    }

    /// <summary>
    /// Datums naar JJJJ-MM-DD (dag vóór maand, zoals in Europa), bedragen op centen, overige getallen op drie decimalen,
    /// nummers zonder hoofdletters en spaties, tekst zonder hoofdletters, leestekens en dubbele spaties. Leeg is null.
    /// </summary>
    public static string? Normalize(FieldKind kind, string field, JsonNode? node)
    {
        if (node == null || node.GetValueKind() == JsonValueKind.Null || string.IsNullOrWhiteSpace(Text(node))) return null;
        switch (kind)
        {
            case FieldKind.Money: return Number(node) is decimal m ? Math.Round(m, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture) : Raw(node);
            case FieldKind.Number: return Number(node) is decimal n ? Math.Round(n, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture) : Raw(node);
            case FieldKind.Date: return Date(Text(node)) ?? Raw(node);
        }
        var text = Text(node)!;
        if (field is "factuurnummer" or "ordernummer_leverancier") return Regex.Replace(text, @"\s+", "").ToLowerInvariant();
        return string.Join(' ', Words(node));
    }

    /// <summary>Getal uit JSON of uit tekst in nl-notatie: "1.234,56" en "€ 12,50" lezen als 1234.56 en 12.50.</summary>
    public static decimal? Number(JsonNode? node)
    {
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.Number) return v.GetValue<decimal>();
        var text = Regex.Replace(Text(node) ?? "", @"[^\d,.\-]", "");
        if (text.Length == 0) return null;
        if (text.Contains(',')) text = text.Replace(".", "").Replace(',', '.');
        else if (Regex.IsMatch(text, @"^-?\d{1,3}(\.\d{3})+$")) text = text.Replace(".", "");
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy", "d.M.yyyy", "d-M-yy", "d/M/yy", "yyyy/MM/dd"];

    public static string? Date(string? text)
    {
        var t = text?.Trim() ?? "";
        if (t.Length > 10 && t[4] == '-' && t[10] == 'T') t = t[..10];
        return DateOnly.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd") : null;
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue(out string? s) ? s : node?.ToJsonString();
    private static string Raw(JsonNode node) => (Text(node) ?? "").Trim().ToLowerInvariant();

    private static List<string> Words(JsonNode? node)
    {
        // Leestekens verdwijnen (ze splitsen geen woorden): "S.L." en "SL" zijn gelijk.
        var plain = new string((Text(node) ?? "").Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation
                or UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation)).ToArray());
        return [.. Regex.Split(plain.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(w => w.Length > 0)];
    }

    // Een omschrijving is goed als alle woorden van het juiste antwoord erin staan: een artikelcode of extra woorden erachter mogen.
    private static bool ContainsAllWords(string actual, string expected)
    {
        var words = actual.Split(' ').ToHashSet();
        return expected.Split(' ').All(words.Contains);
    }

    // Shared: aantal gelijke woorden (bepaalt de rangorde). Overlap: gedeelde woorden t.o.v. de kortste omschrijving (alleen de drempel, zodat extra woorden een regel niet uitsluiten). Jaccard ontloopt gelijke scores.
    private static (int Shared, double Overlap, double Jaccard) Similarity(List<string> a, List<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return (0, 0, 0);
        var set = a.ToHashSet();
        var other = b.ToHashSet();
        var shared = set.Intersect(other).Count();
        return (shared, (double)shared / Math.Min(set.Count, other.Count), (double)shared / set.Union(other).Count());
    }

    private static List<JsonObject> Objects(JsonNode? node) => node is JsonArray array ? [.. array.OfType<JsonObject>()] : [];

    private static HashSet<string> HighCertaintyPaths(JsonObject output) =>
        [.. Objects(output["zekerheden"]).Where(z => Text(z["zekerheid"]) == "hoog").Select(z => (Text(z["veld"]) ?? "").Replace(" ", ""))];
}
