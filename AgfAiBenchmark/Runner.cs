using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgfDocumentAi;

namespace AgfAiBenchmark;

public static class Status
{
    public const string Ok = "ok", Invalid = "ongeldig", Failed = "fout", Unsupported = "niet-ondersteund";
}

/// <summary>Ruwe uitvoer van één model voor één document, met verbruik, kosten, tijd en de modelversie zoals de aanbieder die meldde.</summary>
public sealed record RawResult
{
    public required string Model { get; init; }
    public required string ApiModel { get; init; }
    public string? ModelVersion { get; init; }
    public required string Document { get; init; }
    public required string DocumentHash { get; init; }
    public required string PromptHash { get; init; }
    public required string Status { get; init; }
    public string? Error { get; init; }
    public string? Text { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal CostUsd { get; init; }
    public double Seconds { get; init; }
    public int Calls { get; init; }
    public DateTimeOffset At { get; init; }

    public JsonObject? Output => Status == AgfAiBenchmark.Status.Ok ? ExtractionSchema.Validate(Text ?? "", out _) : null;
}

/// <summary>
/// Draait alle documenten tegen alle modellen, parallel met een grens. Tijdelijke fouten: tot <see cref="MaxTransientAttempts"/> pogingen
/// met wachttijd; een ongeldige uitvoer: één keer opnieuw. Een mislukt document stopt de run niet. Wat al gedraaid is voor
/// hetzelfde model, document en dezelfde instructie wordt overgeslagen, tenzij <c>force</c>.
/// </summary>
public sealed class Runner(string outDir, int parallel, bool force, Func<int, TimeSpan>? backoff = null, Action<string>? log = null)
{
    public const int MaxTransientAttempts = 3;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly Func<int, TimeSpan> backoff = backoff ?? (attempt => TimeSpan.FromSeconds(2 * Math.Pow(2, attempt)));

    public int Skipped { get; private set; }

    public async Task<List<RawResult>> RunAsync(IReadOnlyList<string> documents, IReadOnlyList<SelectedModel> models, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, parallel));
        var tasks = documents.SelectMany(doc => models.Select(async model =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return await RunOneAsync(doc, model, cancellationToken); }
            finally { gate.Release(); }
        }));
        return [.. (await Task.WhenAll(tasks)).OrderBy(r => r.Document, StringComparer.Ordinal).ThenBy(r => r.Model, StringComparer.Ordinal)];
    }

    public string RawPath(string model, string document) => Path.Combine(outDir, "ruw", model, Path.GetFileName(document) + ".json");

    private async Task<RawResult> RunOneAsync(string path, SelectedModel model, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var name = Path.GetFileName(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..16];
        var rawPath = RawPath(model.Settings.Id, name);
        if (!force && await ReadCachedAsync(rawPath, cancellationToken) is { } cached && cached.DocumentHash == hash && cached.PromptHash == ExtractionSchema.PromptHash && cached.ApiModel == model.Settings.ApiModel && cached.Status != Status.Failed)
        {
            lock (this) Skipped++;
            return cached;
        }

        var result = await CallAsync(new DocumentInput(name, bytes, DocumentInput.MediaTypeFor(path)!), model, hash, cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
        await File.WriteAllTextAsync(rawPath, JsonSerializer.Serialize(result, Json), cancellationToken);
        log?.Invoke($"{model.Settings.Id} · {name}: {result.Status}{(result.Error == null ? "" : $" ({result.Error})")}");
        return result;
    }

    // Een beschadigd of half geschreven resultaat telt als niet gedraaid.
    private static async Task<RawResult?> ReadCachedAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<RawResult>(await File.ReadAllTextAsync(path, cancellationToken)); }
        catch (JsonException) { return null; }
    }

    private async Task<RawResult> CallAsync(DocumentInput document, SelectedModel model, string hash, CancellationToken cancellationToken)
    {
        long input = 0, output = 0;
        double seconds = 0;
        int calls = 0, transient = 0, invalid = 0;
        string? version = null, text = null, error;
        string status;
        while (true)
        {
            try
            {
                calls++;
                var response = await model.Ai.ExtractAsync(document, ExtractionSchema.Instruction, cancellationToken);
                (input, output, seconds, version, text) = (input + response.InputTokens, output + response.OutputTokens, seconds + response.Duration.TotalSeconds, response.ModelVersion, response.Text);
                if (ExtractionSchema.Validate(response.Text, out error) != null) { status = Status.Ok; break; }
                if (++invalid > 1) { status = Status.Invalid; break; }
            }
            catch (TransientAiException e)
            {
                error = e.Message;
                if (++transient >= MaxTransientAttempts) { status = Status.Failed; break; }
                await Task.Delay(backoff(transient), cancellationToken);
            }
            catch (UnsupportedDocumentException e) { (status, error) = (Status.Unsupported, e.Message); break; }
            catch (Exception e) when (e is not OperationCanceledException) { (status, error) = (Status.Failed, $"{e.GetType().Name}: {e.Message}"); break; }
        }
        return new RawResult
        {
            Model = model.Settings.Id, ApiModel = model.Settings.ApiModel, ModelVersion = version, Document = document.Name, DocumentHash = hash,
            PromptHash = ExtractionSchema.PromptHash, Status = status, Error = status == Status.Ok ? null : error, Text = text,
            InputTokens = input, OutputTokens = output, CostUsd = model.Settings.CostUsd(input, output), Seconds = Math.Round(seconds, 2), Calls = calls,
            At = DateTimeOffset.Now,
        };
    }
}
