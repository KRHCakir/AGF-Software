using AgfDocumentAi;

namespace AgfAiBenchmark.Tests;

/// <summary>Runner, modelkeuze en de hele run, alleen met nagebootste modellen: geen echte sleutel, geen netwerk.</summary>
public sealed class RunnerTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("agf-ai-bench-test-").FullName;
    private const string Valid = Scripted.Valid;

    public void Dispose() => Directory.Delete(dir, true);

    private static ModelSettings Settings(string id = "test", string provider = "claude", string? key = "TEST_KEY", bool optIn = false) => new()
    {
        Id = id, Provider = provider, ApiModel = id + "-api", KeyName = key, InputPricePerMillion = 2m, OutputPricePerMillion = 10m, OptIn = optIn,
    };

    private string Doc(string name = "a.pdf", string content = "%PDF-1.4 test")
    {
        var path = Path.Combine(dir, "docs", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<(RawResult Result, Runner Runner)> Run(IDocumentAI ai, string? doc = null, bool force = false)
    {
        var runner = new Runner(Path.Combine(dir, "out"), 2, force, _ => TimeSpan.Zero);
        var results = await runner.RunAsync([doc ?? Doc()], [new SelectedModel(Settings(), ai)], CancellationToken.None);
        return (results.Single(), runner);
    }

    [Fact]
    public async Task Cost_and_time_come_from_the_real_token_usage_of_every_call()
    {
        Assert.Equal(12m, Settings().CostUsd(1_000_000, 1_000_000));
        var (result, _) = await Run(new Scripted("{ kapot", Valid));
        Assert.Equal((Status.Ok, 2, 2000L, 400L, 0.008m, 3.0), (result.Status, result.Calls, result.InputTokens, result.OutputTokens, result.CostUsd, result.Seconds));
        Assert.Equal("test-1", result.ModelVersion);
    }

    [Fact]
    public async Task Invalid_output_is_retried_once_then_counts_as_ongeldig()
    {
        var ai = new Scripted("geen json");
        var (result, _) = await Run(ai);
        Assert.Equal((Status.Invalid, 2), (result.Status, ai.Calls));
        Assert.Null(result.Output);
    }

    [Fact]
    public async Task Transient_errors_are_retried_and_a_document_that_keeps_failing_does_not_stop_the_run()
    {
        var flaky = new Scripted(new TransientAiException("HTTP 529"), new TransientAiException("HTTP 429"), Valid);
        Assert.Equal((Status.Ok, 3), ((await Run(flaky)).Result.Status, flaky.Calls));

        var down = new Scripted(new TransientAiException("HTTP 503"));
        var runner = new Runner(Path.Combine(dir, "out2"), 2, false, _ => TimeSpan.Zero);
        var results = await runner.RunAsync([Doc("x.pdf"), Doc("y.png")], [new SelectedModel(Settings(), down)], CancellationToken.None);
        Assert.All(results, r => Assert.Equal((Status.Failed, "HTTP 503"), (r.Status, r.Error)));
        Assert.Equal(2 * Runner.MaxTransientAttempts, down.Calls);

        var unsupported = new Scripted(new UnsupportedDocumentException("geen pdftoppm"));
        Assert.Equal(Status.Unsupported, (await Run(unsupported, Doc("z.pdf"))).Result.Status);
    }

    [Fact]
    public async Task A_document_already_run_with_the_same_model_and_instruction_is_skipped_unless_forced_or_changed()
    {
        var doc = Doc();
        await Run(new Scripted(Valid), doc);
        var again = new Scripted(Valid);
        var (cached, runner) = await Run(again, doc);
        Assert.Equal((0, 1, Status.Ok), (again.Calls, runner.Skipped, cached.Status));

        await Run(again, doc, force: true);
        Assert.Equal(1, again.Calls);
        File.WriteAllText(doc, "%PDF-1.4 andere inhoud");
        await Run(again, doc);
        Assert.Equal(2, again.Calls);
        File.WriteAllText(runner.RawPath("test", "a.pdf"), "{ half geschreven");
        await Run(again, doc);
        Assert.Equal(3, again.Calls);
    }

    [Fact]
    public void A_model_without_key_is_skipped_and_qwen_needs_an_explicit_opt_in()
    {
        var all = new[] { Settings("claude-sonnet"), Settings("qwen-vl", "qwen", "DASHSCOPE_API_KEY", optIn: true), Settings("fake-goed", "fake", key: null) };
        var keys = new Dictionary<string, string> { ["DASHSCOPE_API_KEY"] = "geheim-123" };
        var messages = new List<string>();
        var created = new List<string>();
        IDocumentAI Create(ModelSettings m, string key) { created.Add(m.Id); return new Scripted(Valid); }

        var selected = ModelSelection.Select(["claude-sonnet", "qwen-vl", "fake-goed", "onbekend"], all, keys.GetValueOrDefault, [], Create, messages);
        Assert.Equal(["fake-goed"], selected.Select(s => s.Settings.Id));
        Assert.Contains(messages, m => m.StartsWith("claude-sonnet: geen sleutel TEST_KEY"));
        Assert.Contains(messages, m => m.StartsWith("qwen-vl: draait alleen met --opt-in qwen-vl"));
        Assert.Contains(messages, m => m.StartsWith("onbekend: onbekend model"));
        Assert.DoesNotContain(messages, m => m.Contains("geheim-123"));

        Assert.Equal(["qwen-vl"], ModelSelection.Select(["qwen-vl"], all, keys.GetValueOrDefault, ["qwen-vl"], Create, []).Select(s => s.Settings.Id));
    }
}
