namespace AgfAiBenchmark.Tests;

/// <summary>Het programma zelf: run met nagebootste modellen, bevestiging, mappen buiten de repo, leeg antwoordbestand.</summary>
public sealed class CliTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("agf-ai-cli-test-").FullName;
    private static readonly string Samples = Path.Combine(AppContext.BaseDirectory, "samples");

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public async Task A_full_run_with_the_fake_models_writes_the_overview_without_any_network_call()
    {
        var output = new StringWriter();
        var outDir = Path.Combine(dir, "resultaten");
        var code = await Cli.MainAsync(["run", "--docs", Path.Combine(Samples, "docs"), "--truth", Path.Combine(Samples, "truth"), "--out", outDir, "--models", "fake-goed,fake-slordig"], new StringReader(""), output);
        Assert.Equal(0, code);
        var md = File.ReadAllText(Path.Combine(outDir, "overzicht.md"));
        Assert.Contains("| fake-goed | 2 | 2 | 100,0% |", md);
        Assert.Contains("| fake-slordig | 2 | 2 |", md);
        Assert.Contains("regels[0].bedrag | 1250.00 | 1250.00 ✓ | 1251.00", File.ReadAllText(Path.Combine(outDir, "oneens.md")));
        Assert.Single(Directory.GetFiles(Path.Combine(outDir, "ruw", "fake-slordig"), "voorbeeld-bon-es.pdf.*.json"));
        Assert.DoesNotContain("Doorgaan?", output.ToString());
    }

    [Fact]
    public async Task A_model_that_sends_documents_asks_for_confirmation_once_and_stops_on_no()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "test-sleutel");
        try
        {
            var ai = new Scripted(Scripted.Valid);
            var output = new StringWriter();
            var code = await Cli.MainAsync(["run", "--docs", Path.Combine(Samples, "docs"), "--truth", dir, "--out", Path.Combine(dir, "o"), "--models", "claude-sonnet,claude-haiku"],
                new StringReader("nee\n"), output, (_, _) => ai);
            Assert.Equal((1, 0), (code, ai.Calls));
            Assert.Contains("gaan naar claude-sonnet (claude-sonnet-5-5", output.ToString());
            Assert.Single(output.ToString().Split("Doorgaan?").Skip(1));
            Assert.DoesNotContain("test-sleutel", output.ToString());
        }
        finally { Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null); }
    }

    [Fact]
    public void Documents_answers_and_results_must_live_outside_the_repo_except_the_samples()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        Assert.True(File.Exists(Path.Combine(root, "Software-AGF.sln")));
        var samples = Path.Combine(root, "AgfAiBenchmark", "samples");
        Assert.Null(Cli.RepoGuard(Path.Combine(samples, "docs"), Path.Combine(samples, "truth"), dir));
        Assert.StartsWith("--docs ligt in de repo", Cli.RepoGuard(Path.Combine(root, "docs", "samples"), dir, dir));
        Assert.StartsWith("--docs ligt in de repo", Cli.RepoGuard(Path.Combine(root, "docs"), dir, dir));
        Assert.StartsWith("--out ligt in de repo", Cli.RepoGuard(dir, dir, Path.Combine(root, "resultaten")));
    }

    [Fact]
    public void New_truth_makes_an_empty_answer_file_in_the_schema_and_never_overwrites()
    {
        var output = new StringWriter();
        Assert.Equal(0, Cli.NewTruth("scan-01.jpg", dir, output));
        var text = File.ReadAllText(Path.Combine(dir, "scan-01.json"));
        Assert.Contains("\"bron\": \"foto\"", text);
        Assert.Contains("\"kg_bruto\": null", text);
        Assert.Equal(1, Cli.NewTruth("scan-01.jpg", dir, output));
    }
}
