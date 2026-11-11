using System.Text.Json.Nodes;
using AgfAiBenchmark;
using AgfDocumentAi;
using Microsoft.Extensions.Configuration;

// AI-testbench: leest dezelfde documenten met dezelfde instructie door meerdere modellen en vergelijkt met de juiste antwoorden.
// Schrijft alleen in de uitvoermap; geen database, geen AgfAdmin.
return await Cli.MainAsync(args, Console.In, Console.Out);

public static class Cli
{
    public static string BenchHome => Environment.GetEnvironmentVariable("AGF_AI_BENCH") is { Length: > 0 } home
        ? home : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "agf-ai-bench");

    private const string Usage = """
        Gebruik:
          run --models <id,id> [--docs <map>] [--truth <map>] [--out <map>] [--opt-in <id>] [--parallel 4] [--force] [--yes]
          new-truth <document> [--truth <map>]
        Standaardmappen: <bench>/documenten, <bench>/juist, <bench>/resultaten, met <bench> = AGF_AI_BENCH of ~/agf-ai-bench.
        """;

    public static async Task<int> MainAsync(string[] args, TextReader input, TextWriter output, Func<ModelSettings, string, IDocumentAI>? create = null)
    {
        var options = Options.Parse(args.Skip(1));
        var truthDir = options.Get("truth") ?? Path.Combine(BenchHome, "juist");
        switch (args.FirstOrDefault())
        {
            case "new-truth" when options.Positional.Count == 1:
                return NewTruth(options.Positional[0], truthDir, output);
            case "run":
                break;
            default:
                output.WriteLine(Usage);
                return 2;
        }

        var docsDir = options.Get("docs") ?? Path.Combine(BenchHome, "documenten");
        var outDir = options.Get("out") ?? Path.Combine(BenchHome, "resultaten");
        if (RepoGuard(docsDir, truthDir, outDir) is { } refusal) { output.WriteLine(refusal); return 2; }
        if (!Directory.Exists(docsDir)) { output.WriteLine($"Map met documenten niet gevonden: {docsDir}"); return 2; }

        var notes = new List<string>();
        var documents = Directory.GetFiles(docsDir).Where(f => !Path.GetFileName(f).StartsWith('.')).Order(StringComparer.Ordinal).ToList();
        foreach (var skipped in documents.Where(f => DocumentInput.MediaTypeFor(f) == null)) notes.Add($"{Path.GetFileName(skipped)}: geen PDF, JPG, PNG of WEBP, overgeslagen.");
        documents = [.. documents.Where(f => DocumentInput.MediaTypeFor(f) != null)];
        var truths = LoadTruths(documents, truthDir, notes);

        var config = new ConfigurationBuilder().AddUserSecrets(typeof(Cli).Assembly, optional: true).AddEnvironmentVariables().Build();
        var all = ModelSettings.Load(Path.Combine(AppContext.BaseDirectory, "models.json"));
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var models = ModelSelection.Select(options.All("models"),
            all, name => config[name], options.All("opt-in"), create ?? ((m, key) => ModelSelection.Create(m, key, http, truthDir)), notes);
        foreach (var note in notes) output.WriteLine(note);
        if (models.Count == 0) { output.WriteLine("Geen model om te draaien."); return 2; }

        var sending = models.Where(m => m.Settings.SendsData).ToList();
        foreach (var m in sending) output.WriteLine($"Let op: {documents.Count} documenten uit {docsDir} gaan naar {m.Settings.Id} ({m.Settings.ApiModel}, {m.Settings.Processing}).");
        if (sending.Count > 0 && !options.Has("yes"))
        {
            output.Write("Doorgaan? Typ ja: ");
            if (input.ReadLine()?.Trim().ToLowerInvariant() is not ("ja" or "j")) { output.WriteLine("Gestopt, er is niets verstuurd."); return 1; }
        }

        var runner = new Runner(outDir, int.TryParse(options.Get("parallel"), out var p) ? p : 4, options.Has("force"), log: output.WriteLine);
        var results = await runner.RunAsync(documents, models, CancellationToken.None);
        if (runner.Skipped > 0) notes.Add($"{runner.Skipped} combinaties van model en document al eerder gedraaid met dezelfde instructie, overgeslagen (--force draait ze opnieuw).");
        Report.Write(outDir, [.. models.Select(m => m.Settings)], results, truths, notes);
        output.WriteLine($"Klaar: {Path.Combine(outDir, "overzicht.md")}");
        return 0;
    }

    /// <summary>Documenten, juiste antwoorden en resultaten horen buiten de repo; alleen de verzonnen voorbeelden in samples/ mogen erin.</summary>
    public static string? RepoGuard(string docsDir, string truthDir, string outDir)
    {
        static string? RepoRoot(string path)
        {
            for (var dir = new DirectoryInfo(Path.GetFullPath(path)); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Software-AGF.sln"))) return dir.FullName;
            return null;
        }
        // Alleen de verzonnen voorbeelden: AgfAiBenchmark/samples, en hun kopie in de uitvoer van het testproject.
        static bool InSamples(string path, string root)
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/') + "/";
            return relative.StartsWith("AgfAiBenchmark/samples/", StringComparison.Ordinal)
                || (relative.StartsWith("AgfAiBenchmark.Tests/bin/", StringComparison.Ordinal) && relative.Contains("/samples/"));
        }

        foreach (var (name, path) in new[] { ("--docs", docsDir), ("--truth", truthDir) })
            if (RepoRoot(path) is { } root && !InSamples(path, root)) return $"{name} ligt in de repo ({path}); zet echte documenten en antwoorden erbuiten, bijvoorbeeld in {BenchHome}.";
        return RepoRoot(outDir) != null ? $"--out ligt in de repo ({outDir}); kies een map erbuiten, bijvoorbeeld {Path.Combine(BenchHome, "resultaten")}." : null;
    }

    private static Dictionary<string, JsonObject> LoadTruths(List<string> documents, string truthDir, List<string> notes)
    {
        var truths = new Dictionary<string, JsonObject>();
        foreach (var doc in documents)
        {
            var path = Path.Combine(truthDir, Path.GetFileNameWithoutExtension(doc) + ".json");
            if (!File.Exists(path)) { notes.Add($"{Path.GetFileName(doc)}: geen juiste antwoorden, telt alleen mee in kosten, tijd en oneens.md."); continue; }
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject obj) { notes.Add($"{Path.GetFileName(path)}: geen JSON-object, overgeslagen."); continue; }
                truths[Path.GetFileName(doc)] = obj;
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { notes.Add($"{Path.GetFileName(path)}: geen geldige JSON ({e.Message}), overgeslagen."); }
        }
        return truths;
    }

    /// <summary>Maakt een leeg antwoordbestand in het vaste schema, om zelf in te vullen. Overschrijft nooit.</summary>
    public static int NewTruth(string document, string truthDir, TextWriter output)
    {
        var path = Path.Combine(truthDir, Path.GetFileNameWithoutExtension(document) + ".json");
        if (File.Exists(path)) { output.WriteLine($"Bestaat al, niet overschreven: {path}"); return 1; }
        var line = new JsonObject(ExtractionSchema.LineFields.Select(f => new KeyValuePair<string, JsonNode?>(f.Name, null)));
        var truth = new JsonObject
        {
            ["_meta"] = new JsonObject { ["bron"] = DocumentInput.MediaTypeFor(document) == "application/pdf" ? "digitaal" : "foto", ["taal"] = "nl", ["opmerking"] = null },
            ["soort"] = "inkoopfactuur",
        };
        foreach (var (name, _) in ExtractionSchema.HeaderFields) truth[name] = null;
        truth["regels"] = new JsonArray(line);
        foreach (var (name, _) in ExtractionSchema.TotalFields) truth[name] = null;
        Directory.CreateDirectory(truthDir);
        File.WriteAllText(path, truth.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
        output.WriteLine($"Aangemaakt: {path}");
        return 0;
    }
}

public sealed class Options
{
    private readonly Dictionary<string, List<string>> values = [];
    public List<string> Positional { get; } = [];

    public static Options Parse(IEnumerable<string> args)
    {
        var options = new Options();
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (!list[i].StartsWith("--")) { options.Positional.Add(list[i]); continue; }
            var name = list[i][2..];
            var hasValue = name is not ("force" or "yes") && i + 1 < list.Count && !list[i + 1].StartsWith("--");
            if (!options.values.TryGetValue(name, out var bucket)) options.values[name] = bucket = [];
            if (hasValue) bucket.Add(list[++i]);
        }
        return options;
    }

    public string? Get(string name) => values.TryGetValue(name, out var v) && v.Count > 0 ? v[^1] : null;
    public List<string> All(string name) => values.TryGetValue(name, out var v)
        ? [.. v.SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))] : [];
    public bool Has(string name) => values.ContainsKey(name);
}
