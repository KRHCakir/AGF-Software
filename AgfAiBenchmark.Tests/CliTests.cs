namespace AgfAiBenchmark.Tests;

/// <summary>Het programma zelf: leeg antwoordbestand.</summary>
public sealed class CliTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("agf-ai-cli-test-").FullName;

    public void Dispose() => Directory.Delete(dir, true);

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
