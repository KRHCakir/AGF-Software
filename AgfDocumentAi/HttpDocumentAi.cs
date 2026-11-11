using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace AgfDocumentAi;

/// <summary>Gedeeld stuk voor aanbieders zonder officiële .NET-SDK: één POST met JSON, tijdelijke HTTP-fouten als <see cref="TransientAiException"/>.</summary>
public abstract class HttpDocumentAi(HttpClient http) : IDocumentAI
{
    public abstract Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken);

    protected async Task<(JsonNode Body, TimeSpan Duration)> PostAsync(string provider, string url, JsonObject body, Action<HttpRequestHeaders> auth, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
        auth(request.Headers);
        var watch = Stopwatch.StartNew();
        HttpResponseMessage response;
        string text;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
            // Ook het lezen van de body kan een time-out of netwerkfout geven: dat is net zo tijdelijk als het verzenden.
            try { text = await response.Content.ReadAsStringAsync(cancellationToken); }
            catch { response.Dispose(); throw; }
        }
        catch (Exception e) when ((e is HttpRequestException or TaskCanceledException or IOException) && !cancellationToken.IsCancellationRequested)
        {
            throw new TransientAiException($"{provider}: geen antwoord ({e.GetType().Name})", e);
        }
        using (response)
        {
            var status = (int)response.StatusCode;
            // Alleen de statuscode in de melding: de body kan delen van het verzoek herhalen.
            if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500) throw new TransientAiException($"{provider}: HTTP {status}");
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{provider}: HTTP {status}");
            return (JsonNode.Parse(text) ?? new JsonObject(), watch.Elapsed);
        }
    }
}

/// <summary>Google Gemini via generateContent, met afgedwongen JSON-schema (responseJsonSchema). Sleutel in de header, nooit in de URL.</summary>
public sealed class GeminiDocumentAi(string apiModel, string apiKey, HttpClient http) : HttpDocumentAi(http)
{
    public override async Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject { ["inline_data"] = new JsonObject { ["mime_type"] = document.MediaType, ["data"] = Convert.ToBase64String(document.Bytes) } },
                    new JsonObject { ["text"] = instruction }),
            }),
            ["generationConfig"] = new JsonObject { ["responseMimeType"] = "application/json", ["responseJsonSchema"] = ExtractionSchema.Build() },
        };
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{apiModel}:generateContent";
        var (json, duration) = await PostAsync("Gemini", url, body, h => h.Add("x-goog-api-key", apiKey), cancellationToken);
        var parts = json["candidates"]?[0]?["content"]?["parts"] as JsonArray ?? [];
        var text = string.Concat(parts.Select(p => p?["text"]?.GetValue<string>() ?? ""));
        var usage = json["usageMetadata"];
        long Count(string name) => usage?[name]?.GetValue<long>() ?? 0;
        // Denktokens worden als uitvoer gerekend.
        return new AiResponse(text, Count("promptTokenCount"), Count("candidatesTokenCount") + Count("thoughtsTokenCount"), duration, json["modelVersion"]?.GetValue<string>());
    }
}

/// <summary>
/// Alibaba Qwen-VL via de OpenAI-compatibele chat-API. Qwen-VL leest geen PDF: elke pagina gaat eerst door pdftoppm (poppler)
/// naar PNG. Zonder pdftoppm meldt hij "niet ondersteund" in plaats van te raden. Het schema wordt hier zelf gecontroleerd.
/// </summary>
public sealed class QwenDocumentAi(string apiModel, string apiKey, string baseUrl, HttpClient http) : HttpDocumentAi(http)
{
    public const int MaxPages = 10;

    public override async Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew(); // inclusief PDF-conversie, zodat de snelheid vergelijkbaar is met de andere modellen
        var images = document.IsPdf ? await PdfPagesAsync(document.Bytes, cancellationToken) : [(document.MediaType, document.Bytes)];
        var content = new JsonArray(images.Select(i => (JsonNode?)new JsonObject
        {
            ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = $"data:{i.MediaType};base64,{Convert.ToBase64String(i.Bytes)}" },
        }).ToArray());
        content.Add(new JsonObject { ["type"] = "text", ["text"] = instruction + "\nJSON-schema:\n" + ExtractionSchema.Build().ToJsonString() });
        var body = new JsonObject
        {
            ["model"] = apiModel,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
        };
        var (json, _) = await PostAsync("Qwen", baseUrl.TrimEnd('/') + "/chat/completions", body, h => h.Add("Authorization", $"Bearer {apiKey}"), cancellationToken);
        var usage = json["usage"];
        return new AiResponse(json["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "",
            usage?["prompt_tokens"]?.GetValue<long>() ?? 0, usage?["completion_tokens"]?.GetValue<long>() ?? 0, total.Elapsed, json["model"]?.GetValue<string>());
    }

    // p-1.png, p-02.png, p-10.png: numeriek sorteren op paginanummer, ongeacht de zero-padding
    private static int PageNumber(string fileName) =>
        int.TryParse(Path.GetFileNameWithoutExtension(fileName)[(fileName.LastIndexOf('-') + 1)..], out var n) ? n : int.MaxValue;

    private static async Task<List<(string MediaType, byte[] Bytes)>> PdfPagesAsync(byte[] pdf, CancellationToken cancellationToken)
    {
        var dir = Directory.CreateTempSubdirectory("agf-ai-qwen-");
        try
        {
            var input = Path.Combine(dir.FullName, "in.pdf");
            await File.WriteAllBytesAsync(input, pdf, cancellationToken);
            var start = new ProcessStartInfo("pdftoppm", ["-png", "-r", "150", "-l", (MaxPages + 1).ToString(), input, Path.Combine(dir.FullName, "p")]) { RedirectStandardError = true };
            Process? process;
            try { process = Process.Start(start); }
            catch (System.ComponentModel.Win32Exception) { throw new UnsupportedDocumentException("Qwen-VL leest geen PDF en pdftoppm (poppler) is niet geïnstalleerd"); }
            using (process)
            {
                // stderr leeg lezen, anders kan een volle pijp pdftoppm laten blokkeren
                var stderr = process!.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                await stderr;
                if (process.ExitCode != 0) throw new UnsupportedDocumentException("pdftoppm kon de PDF niet omzetten");
            }
            var pages = dir.GetFiles("p*.png").OrderBy(f => PageNumber(f.Name)).ToList();
            // Er wordt één pagina extra omgezet om te zien dat het document te lang is: liever "niet ondersteund" dan stilzwijgend afkappen.
            if (pages.Count > MaxPages) throw new UnsupportedDocumentException($"PDF heeft meer dan {MaxPages} pagina's; Qwen verwerkt er maximaal {MaxPages}");
            return [.. pages.Select(f => ("image/png", File.ReadAllBytes(f.FullName)))];
        }
        finally { dir.Delete(true); }
    }
}
