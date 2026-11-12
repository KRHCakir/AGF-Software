using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;

namespace AgfDocumentAi;

/// <summary>
/// Claude via de officiële Anthropic SDK, met afgedwongen schema (structured output). Herhaalpogingen doet de aanroeper,
/// niet de SDK, zodat elke poging en elk token meetelt. Bewust geen refusal-fallback: een weigering is een lege uitvoer.
/// Zonder <paramref name="schema"/> het vaste schema van de testbench (<see cref="ExtractionSchema.Build()"/>).
/// </summary>
public sealed class ClaudeDocumentAi(string apiModel, string apiKey, JsonObject? schema = null) : IDocumentAI
{
    private readonly Dictionary<string, JsonElement> Schema =
        (schema ?? ExtractionSchema.Build()).ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));

    private readonly AnthropicClient client = new() { ApiKey = apiKey, MaxRetries = 0 };

    public async Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken)
    {
        var data = Convert.ToBase64String(document.Bytes);
        ContentBlockParam source = document.IsPdf
            ? new DocumentBlockParam { Source = new Base64PdfSource { Data = data } }
            : new ImageBlockParam { Source = new Base64ImageSource { Data = data, MediaType = document.MediaType } };
        var watch = Stopwatch.StartNew();
        try
        {
            var message = await client.Messages.Create(new MessageCreateParams
            {
                Model = apiModel,
                MaxTokens = 16000,
                Messages = [new UserMessageParam { Content = new List<ContentBlockParam> { source, new TextBlockParam { Text = instruction } } }],
                OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = Schema } },
            }, cancellationToken);
            var text = string.Concat(message.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
            if (message.StopReason == "refusal") text = "";
            return new AiResponse(text, message.Usage.InputTokens, message.Usage.OutputTokens, watch.Elapsed, message.Model);
        }
        catch (Exception e) when ((e is AnthropicRateLimitException or Anthropic5xxException or AnthropicIOException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            throw new TransientAiException($"Anthropic: {e.GetType().Name}", e);
        }
    }
}
