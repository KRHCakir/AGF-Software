using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgfAiBenchmark;

/// <summary>Eén document met juiste antwoorden, beoordeeld voor één model. Een ongeldige of mislukte uitvoer telt voor elk veld als fout.</summary>
public sealed record Evaluation(RawResult Result, string Source, string Language, List<FieldScore> Scores);

/// <summary>Schrijft overzicht.md, overzicht.csv, per-document.csv, oneens.md en run.json in de uitvoermap.</summary>
public static class Report
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static List<Evaluation> Evaluate(IEnumerable<RawResult> results, IReadOnlyDictionary<string, JsonObject> truths) =>
    [.. results.Where(r => truths.ContainsKey(r.Document)).Select(r =>
    {
        var truth = truths[r.Document];
        var scores = Comparer.Compare(truth, r.Output);
        if (r.Status != Status.Ok) scores = [.. scores.Where(s => s.Field != "regels.extra").Select(s => s with { Outcome = Outcome.Fout })];
        var meta = truth["_meta"];
        return new Evaluation(r, meta?["bron"]?.GetValue<string>() ?? "onbekend", meta?["taal"]?.GetValue<string>() ?? "onbekend", scores);
    })];

    public static string Write(string outDir, IReadOnlyList<ModelSettings> models, List<RawResult> results, IReadOnlyDictionary<string, JsonObject> truths, IEnumerable<string> notes)
    {
        Directory.CreateDirectory(outDir);
        var evaluations = Evaluate(results, truths);
        var md = Markdown(models, results, evaluations, notes);
        File.WriteAllText(Path.Combine(outDir, "overzicht.md"), md);
        File.WriteAllText(Path.Combine(outDir, "overzicht.csv"), SummaryCsv(models, results, evaluations));
        File.WriteAllText(Path.Combine(outDir, "per-document.csv"), DocumentCsv(results, evaluations));
        File.WriteAllText(Path.Combine(outDir, "oneens.md"), Disagreements(models, results, truths));
        var run = new { datum = DateTimeOffset.Now, instructie = AgfDocumentAi.ExtractionSchema.PromptHash, modellen = models };
        File.WriteAllText(Path.Combine(outDir, "run.json"), JsonSerializer.Serialize(run, new JsonSerializerOptions { WriteIndented = true }));
        return md;
    }

    private static string Pct(IEnumerable<FieldScore> scores)
    {
        var list = scores.ToList();
        return list.Count == 0 ? "–" : (100.0 * list.Count(s => s.Correct) / list.Count).ToString("0.0", Nl) + "%";
    }

    private static string Usd(decimal value) => "$" + value.ToString("0.0000", Nl);
    private static string Sec(double value) => value.ToString("0.0", Nl) + " s";

    private static string Markdown(IReadOnlyList<ModelSettings> models, List<RawResult> results, List<Evaluation> evaluations, IEnumerable<string> notes)
    {
        var ids = models.Select(m => m.Id).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# AI-testbench: overzicht").AppendLine();
        sb.AppendLine($"Run van {DateTimeOffset.Now:yyyy-MM-dd HH:mm}, instructie {AgfDocumentAi.ExtractionSchema.PromptHash}, {results.Select(r => r.Document).Distinct().Count()} documenten, waarvan {evaluations.Select(e => e.Result.Document).Distinct().Count()} met juiste antwoorden.").AppendLine();
        foreach (var note in notes) sb.AppendLine($"- {note}");
        sb.AppendLine().AppendLine("## Modellen").AppendLine();
        Table(sb, ["Model", "API-model", "Gemelde versie", "Prijs in / uit per 1M", "Prijs gecontroleerd", "Verwerking"], models.Select(m => new[]
        {
            m.Id, m.ApiModel, string.Join(", ", results.Where(r => r.Model == m.Id && r.ModelVersion != null).Select(r => r.ModelVersion).Distinct()),
            $"${m.InputPricePerMillion.ToString("0.00", Nl)} / ${m.OutputPricePerMillion.ToString("0.00", Nl)}", m.PriceCheckedOn, m.Processing,
        }));

        sb.AppendLine("## Samenvatting").AppendLine();
        Table(sb, ["Model", "Documenten", "Geldig", "Goed", "Kop", "Regels", "Bedragen", "Hoog zeker maar fout", "Kosten totaal", "Kosten per document", "Tijd per document"],
            ids.Select(id =>
            {
                var own = results.Where(r => r.Model == id).ToList();
                var scores = evaluations.Where(e => e.Result.Model == id).SelectMany(e => e.Scores).ToList();
                return new[]
                {
                    id, own.Count.ToString(), own.Count(r => r.Status == Status.Ok).ToString(), Pct(scores), Pct(scores.Where(s => s.Group == Comparer.Header)),
                    Pct(scores.Where(s => s.Group == Comparer.Lines)), Pct(scores.Where(s => s.Group == Comparer.Totals)),
                    scores.Count(s => s.HighCertainty && !s.Correct).ToString(), Usd(own.Sum(r => r.CostUsd)),
                    own.Count == 0 ? "–" : Usd(own.Sum(r => r.CostUsd) / own.Count), own.Count == 0 ? "–" : Sec(own.Average(r => r.Seconds)),
                };
            }));

        Breakdown(sb, "Per veld", ids, evaluations.SelectMany(e => e.Scores.Select(s => (key: s.Field, e.Result.Model, s))));
        Breakdown(sb, "Per soort bron (digitaal, scan, foto)", ids, evaluations.SelectMany(e => e.Scores.Select(s => (key: e.Source, e.Result.Model, s))));
        Breakdown(sb, "Per taal", ids, evaluations.SelectMany(e => e.Scores.Select(s => (key: e.Language, e.Result.Model, s))));

        sb.AppendLine("## Per document").AppendLine();
        Table(sb, ["Document", .. ids], results.Select(r => r.Document).Distinct().Select(doc => new[] { doc }.Concat(ids.Select(id =>
        {
            var r = results.FirstOrDefault(x => x.Document == doc && x.Model == id);
            if (r == null) return "–";
            var e = evaluations.FirstOrDefault(x => x.Result == r);
            return $"{(e == null ? "" : Pct(e.Scores) + " · ")}{Usd(r.CostUsd)} · {Sec(r.Seconds)}{(r.Status == Status.Ok ? "" : $" · {r.Status}")}";
        })).ToArray()));
        sb.AppendLine("Waar de modellen het oneens zijn: zie `oneens.md`. Ruwe uitvoer per model en document: map `ruw/`.");
        return sb.ToString();
    }

    private static void Breakdown(StringBuilder sb, string title, List<string> ids, IEnumerable<(string Key, string Model, FieldScore Score)> rows)
    {
        var list = rows.ToList();
        sb.AppendLine($"## {title}").AppendLine();
        Table(sb, ["", .. ids], list.Select(r => r.Key).Distinct().Select(key => new[] { key }
            .Concat(ids.Select(id => Pct(list.Where(r => r.Key == key && r.Model == id).Select(r => r.Score)))).ToArray()));
    }

    private static void Table(StringBuilder sb, string[] head, IEnumerable<string[]> rows)
    {
        sb.AppendLine("| " + string.Join(" | ", head) + " |").AppendLine("|" + string.Concat(head.Select(_ => "---|")));
        foreach (var row in rows) sb.AppendLine("| " + string.Join(" | ", row.Select(c => c.Replace("|", "\\|"))) + " |");
        sb.AppendLine();
    }

    private static string SummaryCsv(IReadOnlyList<ModelSettings> models, List<RawResult> results, List<Evaluation> evaluations)
    {
        var inv = CultureInfo.InvariantCulture;
        // De verbruikskolommen (documenten t/m aanroepen) zijn alleen gevuld op de regel met dimensie "totaal".
        var sb = new StringBuilder("model,dimensie,waarde,goed,totaal,percentage,documenten,geldig,input_tokens,output_tokens,kosten_usd,seconden,seconden_per_document,aanroepen\n");
        foreach (var m in models)
        {
            var own = evaluations.Where(e => e.Result.Model == m.Id).ToList();
            var ownResults = results.Where(r => r.Model == m.Id).ToList();
            var groups = own.SelectMany(e => e.Scores.SelectMany(s => new[] { ("totaal", "alles", s), ("groep", s.Group, s), ("veld", s.Field, s), ("bron", e.Source, s), ("taal", e.Language, s) }));
            var wroteTotal = false;
            foreach (var g in groups.GroupBy(x => (x.Item1, x.Item2)))
            {
                var correct = g.Count(x => x.s.Correct);
                var isTotal = g.Key.Item1 == "totaal";
                wroteTotal |= isTotal;
                sb.AppendLine(string.Join(',', m.Id, g.Key.Item1, g.Key.Item2, correct, g.Count(), (100.0 * correct / g.Count()).ToString("0.0", inv)) + (isTotal ? "," + Usage(ownResults, inv) : ",,,,,,,"));
            }
            // Model zonder beoordeelde velden: kosten en tijd horen er toch in.
            if (!wroteTotal && ownResults.Count > 0) sb.AppendLine(string.Join(',', m.Id, "totaal", "alles", 0, 0, "") + "," + Usage(ownResults, inv));
        }
        return sb.ToString();
    }

    private static string Usage(List<RawResult> own, CultureInfo inv) => string.Join(',', own.Count, own.Count(r => r.Status == Status.Ok), own.Sum(r => (long)r.InputTokens), own.Sum(r => (long)r.OutputTokens),
        own.Sum(r => r.CostUsd).ToString(inv), own.Sum(r => r.Seconds).ToString(inv), own.Average(r => r.Seconds).ToString(inv), own.Sum(r => (long)r.Calls));

    private static string DocumentCsv(List<RawResult> results, List<Evaluation> evaluations)
    {
        var sb = new StringBuilder("model,document,status,goed,velden,hoog_zeker_fout,input_tokens,output_tokens,kosten_usd,seconden,aanroepen\n");
        foreach (var r in results)
        {
            var scores = evaluations.FirstOrDefault(e => e.Result == r)?.Scores ?? [];
            sb.AppendLine(string.Join(',', r.Model, Csv(r.Document), r.Status, scores.Count(s => s.Correct), scores.Count, scores.Count(s => s.HighCertainty && !s.Correct),
                r.InputTokens, r.OutputTokens, r.CostUsd.ToString(CultureInfo.InvariantCulture), r.Seconds.ToString(CultureInfo.InvariantCulture), r.Calls));
        }
        return sb.ToString();
    }

    private static string Csv(string value) => value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    /// <summary>Per document de velden waar de geldige uitvoer van de modellen verschilt, naast het juiste antwoord (als dat er is).</summary>
    public static string Disagreements(IReadOnlyList<ModelSettings> models, List<RawResult> results, IReadOnlyDictionary<string, JsonObject> truths)
    {
        var sb = new StringBuilder("# Waar de modellen het oneens zijn\n\nAlleen geldige uitvoer, genormaliseerd. ✓ = gelijk aan het juiste antwoord.\n\n");
        var any = false;
        foreach (var doc in results.Select(r => r.Document).Distinct())
        {
            var own = results.Where(r => r.Document == doc && r.Status == Status.Ok).OrderBy(r => models.ToList().FindIndex(m => m.Id == r.Model)).ToList();
            if (own.Count < 2) continue;
            var truth = truths.GetValueOrDefault(doc);
            var scores = own.Select(r => Comparer.Compare(truth ?? [], r.Output).Where(s => s.Field != "regels.extra").ToDictionary(s => s.Path)).ToList();
            var counts = own.Select(r => (r.Output?["regels"] as JsonArray)?.Count.ToString() ?? "0").ToList();
            var rows = new List<string[]>();
            if (counts.Distinct().Count() > 1) rows.Add(["aantal regels", (truth?["regels"] as JsonArray)?.Count.ToString() ?? "?", .. counts]);
            foreach (var path in scores[0].Keys)
            {
                var values = scores.Select(s => s.TryGetValue(path, out var f) ? f.Actual : null).ToList();
                if (values.Distinct().Count() < 2) continue;
                var expected = scores[0][path].Expected;
                rows.Add([path, truth == null ? "?" : expected ?? "null", .. values.Select(v => (v ?? "null") + (truth != null && v == expected ? " ✓" : ""))]);
            }
            if (rows.Count == 0) continue;
            any = true;
            sb.AppendLine($"## {doc}").AppendLine();
            Table(sb, ["Veld", "Juist", .. own.Select(r => r.Model)], rows);
        }
        return any ? sb.ToString() : sb.AppendLine("Geen verschillen gevonden.").ToString();
    }
}
