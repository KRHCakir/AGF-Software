using System.Text;
using System.Text.Json.Nodes;
using AgfAdmin.Services.DocumentAi;
using AgfDocumentAi;

namespace AgfAdmin.Tests.Infrastructure;

// Nagebootste AI-aanbieder voor tests van AI-lezen: per model een rij antwoorden of fouten, geen netwerk en geen sleutel. Houdt bij welk
// model las en welk document het kreeg.
public sealed class FakeDocumentAi : IDocumentAiFactory, IDocumentAI
{
    public const string Haiku = "claude-haiku-5-5";
    public const string Sonnet = "claude-sonnet-5-5";
    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF");

    private readonly Dictionary<string, Queue<Func<AiResponse>>> _script = new();
    private string _model;
    public List<string> Calls { get; } = new();
    public DocumentInput LastDocument { get; private set; }
    public string Reason { get; set; }
    public string Provider => "Anthropic-fake";
    public string NotConfiguredReason => Reason;

    // De instellingen van de echte werking: Haiku 5.5 met Sonnet 5.5 als vangnet, prijzen zoals gecontroleerd op 11 november 2026.
    public static DocumentAiOptions DefaultOptions() => new()
    {
        PrimaryModel = Haiku, FallbackModel = Sonnet, FallbackEnabled = true, EurPerUsd = 0.86m, MaxUploadsPerMinute = 100, RetentionDays = 30,
        Models = new()
        {
            [Haiku] = new() { DisplayName = "Claude Haiku 5.5", InputPerMillionUsd = 0.10m, OutputPerMillionUsd = 0.50m, CheckedOn = "2026-11-11" },
            [Sonnet] = new() { DisplayName = "Claude Sonnet 5.5", InputPerMillionUsd = 2.00m, OutputPerMillionUsd = 10.00m, CheckedOn = "2026-11-11" },
        },
    };

    public void Answer(string model, JsonObject proposal, long input = 1_000, long output = 200) =>
        Enqueue(model, () => new AiResponse(proposal.ToJsonString(), input, output, TimeSpan.FromMilliseconds(10), $"{model}-20261101"));
    public void Text(string model, string text) => Enqueue(model, () => new AiResponse(text, 100, 10, TimeSpan.Zero, model));
    public void Fail(string model, Exception error) => Enqueue(model, () => throw error);

    private void Enqueue(string model, Func<AiResponse> step)
    {
        if (!_script.TryGetValue(model, out var queue)) _script[model] = queue = new Queue<Func<AiResponse>>();
        queue.Enqueue(step);
    }

    public IDocumentAI Create(string model) { _model = model; return this; }

    public Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken)
    {
        Calls.Add(_model);
        LastDocument = document;
        Assert.Contains("leverancier_iban", instruction);
        return Task.FromResult(_script[_model].Dequeue()());
    }
}

// Een klok die vooruit kan (bewaartermijn, uploads per minuut).
public sealed class MutableClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public void Advance(TimeSpan by) => _now += by;
    public override DateTimeOffset GetUtcNow() => _now;
}
