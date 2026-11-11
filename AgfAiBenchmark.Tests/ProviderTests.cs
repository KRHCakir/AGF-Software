using System.Net;
using System.Text.Json.Nodes;
using AgfDocumentAi;

namespace AgfAiBenchmark.Tests;

/// <summary>De HTTP-aanbieders tegen een nagebootste server: geen netwerk, geen echte sleutel.</summary>
public class ProviderTests
{
    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Sent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            (Request, Sent) = (request, await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static readonly DocumentInput Image = new("foto.jpg", [1, 2, 3], "image/jpeg");

    [Fact]
    public async Task Gemini_sends_the_schema_and_the_key_in_a_header_and_reads_text_usage_and_version()
    {
        var stub = new Stub(HttpStatusCode.OK, """
            {"candidates":[{"content":{"parts":[{"text":"{\"soort\":"},{"text":"\"bon\"}"}]}}],
             "usageMetadata":{"promptTokenCount":1200,"candidatesTokenCount":80,"thoughtsTokenCount":20},"modelVersion":"gemini-3.8-flash-001"}
            """);
        var response = await new GeminiDocumentAi("gemini-3.8-flash", "sleutel-g", new HttpClient(stub)).ExtractAsync(Image, "lees", CancellationToken.None);
        Assert.Equal(("{\"soort\":\"bon\"}", 1200L, 100L, "gemini-3.8-flash-001"), (response.Text, response.InputTokens, response.OutputTokens, response.ModelVersion));
        Assert.DoesNotContain("sleutel-g", stub.Request!.RequestUri!.ToString());
        Assert.Equal("sleutel-g", stub.Request.Headers.GetValues("x-goog-api-key").Single());
        var sent = JsonNode.Parse(stub.Sent!)!;
        Assert.Equal("application/json", sent["generationConfig"]!["responseMimeType"]!.GetValue<string>());
        Assert.NotNull(sent["generationConfig"]!["responseJsonSchema"]!["properties"]!["regels"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task Qwen_errors_are_transient_only_for_429_and_5xx_and_never_repeat_the_body(HttpStatusCode status, bool transient)
    {
        var qwen = new QwenDocumentAi("qwen3-vl-plus", "sleutel-q", "https://voorbeeld.invalid/v1", new HttpClient(new Stub(status, "sleutel-q echo")));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => qwen.ExtractAsync(Image, "lees", CancellationToken.None));
        Assert.Equal(transient, error is TransientAiException);
        Assert.DoesNotContain("sleutel-q", error.Message);
    }
}
