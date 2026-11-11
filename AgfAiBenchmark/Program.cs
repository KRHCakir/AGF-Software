using System.Text.Json.Nodes;
using AgfDocumentAi;

// AI-testbench: leest dezelfde documenten met dezelfde instructie door meerdere modellen en vergelijkt met de juiste antwoorden.
// Schrijft alleen in de uitvoermap; geen database, geen AgfAdmin.
return await Cli.MainAsync(args, Console.In, Console.Out);

public static class Cli
{
    public static string BenchHome => Environment.GetEnvironmentVariable("AGF_AI_BENCH") is { Length: > 0 } home
        ? home : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "agf-ai-bench");

    private const string Usage = """
        Gebruik:
          new-truth <document> [--truth <map>]
        Standaardmap voor de juiste antwoorden: <bench>/juist, met <bench> = AGF_AI_BENCH of ~/agf-ai-bench.
        """;

    public static async Task<int> MainAsync(string[] args, TextReader input, TextWriter output)
    {
        var options = Options.Parse(args.Skip(1));
        var truthDir = options.Get("truth") ?? Path.Combine(BenchHome, "juist");
        if (args.FirstOrDefault() == "new-truth" && options.Positional.Count == 1) return await Task.FromResult(NewTruth(options.Positional[0], truthDir, output));
        output.WriteLine(Usage);
        return 2;
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
