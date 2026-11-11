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
        var tasks = documents.Select(Path.GetFullPath).Distinct().SelectMany(doc => models.Select(async model =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return await RunOneAsync(doc, model, cancellationToken); }
            finally { gate.Release(); }
        }));
        return [.. (await Task.WhenAll(tasks)).OrderBy(r => r.Document, StringComparer.Ordinal).ThenBy(r => r.Model, StringComparer.Ordinal)];
    }

    /// <summary>Eén resultaatbestand per model en document; de hash van het volledige pad houdt gelijke bestandsnamen uit verschillende mappen uit elkaar.</summary>
    public string RawPath(string model, string document)
    {
        var id = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(document)))).ToLowerInvariant()[..8];
        return Path.Combine(outDir, "ruw", model, $"{Path.GetFileName(document)}.{id}.json");
    }

    private async Task<RawResult> RunOneAsync(string path, SelectedModel model, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(path);
        byte[] bytes;
        try { bytes = await File.ReadAllBytesAsync(path, cancellationToken); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{model.Settings.Id} · {name}: {Status.Failed} ({e.Message})");
            return Failure(model, name, "", Status.Failed, $"Document onleesbaar: {e.Message}");
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()[..16];
        var mediaType = DocumentInput.MediaTypeFor(path);
        if (mediaType == null) return Failure(model, name, hash, Status.Unsupported, "Bestandstype wordt niet ondersteund.");

        var rawPath = RawPath(model.Settings.Id, path);
        if (!force && await ReadCachedAsync(rawPath, cancellationToken) is { } cached && cached.DocumentHash == hash && cached.PromptHash == ExtractionSchema.PromptHash && cached.ApiModel == model.Settings.ApiModel && IsReusable(cached))
        {
            lock (this) Skipped++;
            return cached;
        }

        var result = await CallAsync(new DocumentInput(name, bytes, mediaType), model, hash, cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
            var temp = rawPath + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(result, Json), cancellationToken);
            File.Move(temp, rawPath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"{model.Settings.Id} · {name}: resultaat niet opgeslagen ({e.Message})");
        }
        log?.Invoke($"{model.Settings.Id} · {name}: {result.Status}{(result.Error == null ? "" : $" ({result.Error})")}");
        return result;
    }

    // Mislukte en niet-ondersteunde resultaten draaien opnieuw; een ok-resultaat moet nog geldige uitvoer hebben.
    private static bool IsReusable(RawResult cached) => cached.Status switch
    {
        Status.Ok => cached.Text != null && ExtractionSchema.Validate(cached.Text, out _) != null,
        Status.Invalid => true,
        _ => false,
    };

    private static RawResult Failure(SelectedModel model, string name, string hash, string status, string error) => new()
    {
        Model = model.Settings.Id, ApiModel = model.Settings.ApiModel, Document = name, DocumentHash = hash,
        PromptHash = ExtractionSchema.PromptHash, Status = status, Error = error, At = DateTimeOffset.Now,
    };

    // Een beschadigd of half geschreven resultaat telt als niet gedraaid.
    private static async Task<RawResult?> ReadCachedAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<RawResult>(await File.ReadAllTextAsync(path, cancellationToken)); }
        catch (Exception e) when (e is JsonException or IOException) { return null; }
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
