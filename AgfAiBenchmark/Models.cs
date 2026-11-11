using System.Text.Json;
using System.Text.Json.Serialization;
using AgfDocumentAi;

namespace AgfAiBenchmark;

/// <summary>Eén regel uit models.json. De prijs wordt bij elke run in het resultaat vastgelegd, met de datum waarop hij is gecontroleerd.</summary>
public sealed record ModelSettings
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("aanbieder")] public required string Provider { get; init; }
    [JsonPropertyName("apiModel")] public required string ApiModel { get; init; }
    [JsonPropertyName("sleutel")] public string? KeyName { get; init; }
    [JsonPropertyName("basisUrl")] public string? BaseUrl { get; init; }
    [JsonPropertyName("prijsInvoer")] public decimal InputPricePerMillion { get; init; }
    [JsonPropertyName("prijsUitvoer")] public decimal OutputPricePerMillion { get; init; }
    [JsonPropertyName("prijsGecontroleerd")] public string PriceCheckedOn { get; init; } = "-";
    [JsonPropertyName("verwerking")] public string Processing { get; init; } = "";
    [JsonPropertyName("optIn")] public bool OptIn { get; init; }

    public bool SendsData => Provider != "fake";

    public decimal CostUsd(long inputTokens, long outputTokens) =>
        Math.Round((inputTokens * InputPricePerMillion + outputTokens * OutputPricePerMillion) / 1_000_000m, 6);

    public static IReadOnlyList<ModelSettings> Load(string path) =>
        JsonSerializer.Deserialize<ModelFile>(File.ReadAllText(path))?.Models ?? [];

    private sealed record ModelFile([property: JsonPropertyName("modellen")] List<ModelSettings> Models);
}

public sealed record SelectedModel(ModelSettings Settings, IDocumentAI Ai);

public static class ModelSelection
{
    /// <summary>
    /// Kiest de gevraagde modellen. Overgeslagen, met een melding: een onbekend model, een model zonder sleutel, en een opt-in-model
    /// (Qwen) zonder <c>--opt-in</c>. De sleutel gaat alleen naar de aanbiederklasse en komt nooit in een melding of bestand.
    /// </summary>
    public static List<SelectedModel> Select(IEnumerable<string> requested, IReadOnlyList<ModelSettings> all, Func<string, string?> key,
        ICollection<string> optIns, Func<ModelSettings, string, IDocumentAI> create, List<string> messages)
    {
        var selected = new List<SelectedModel>();
        foreach (var id in requested.Distinct())
        {
            var model = all.FirstOrDefault(m => m.Id == id);
            if (model == null) { messages.Add($"{id}: onbekend model, overgeslagen (zie models.json)."); continue; }
            if (model.OptIn && !optIns.Contains(id)) { messages.Add($"{id}: draait alleen met --opt-in {id} ({model.Processing}), overgeslagen."); continue; }
            var secret = model.KeyName == null ? "" : key(model.KeyName) ?? "";
            if (model.KeyName != null && string.IsNullOrWhiteSpace(secret)) { messages.Add($"{id}: geen sleutel {model.KeyName} gevonden, overgeslagen."); continue; }
            selected.Add(new SelectedModel(model, create(model, secret)));
        }
        return selected;
    }

    public static IDocumentAI Create(ModelSettings model, string key, HttpClient http, string truthDir) => model.Provider switch
    {
        "claude" => new ClaudeDocumentAi(model.ApiModel, key),
        "gemini" => new GeminiDocumentAi(model.ApiModel, key, http),
        "qwen" => new QwenDocumentAi(model.ApiModel, key, model.BaseUrl ?? throw new InvalidOperationException("qwen: basisUrl ontbreekt"), http),
        "fake" => new FakeDocumentAi(model.ApiModel, truthDir),
        _ => throw new InvalidOperationException($"{model.Id}: onbekende aanbieder {model.Provider}"),
    };
}
