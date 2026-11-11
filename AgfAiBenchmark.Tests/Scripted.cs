using AgfDocumentAi;

namespace AgfAiBenchmark.Tests;

/// <summary>Geeft de antwoorden in volgorde terug; een Exception in de lijst wordt gegooid.</summary>
public sealed class Scripted(params object[] steps) : IDocumentAI
{
    /// <summary>Kleinste uitvoer die het strikte schema aanneemt: elk veld aanwezig, null waar niets staat.</summary>
    public const string Valid = """
        {"soort":"bon","leverancier":null,"ordernummer_leverancier":null,"factuurnummer":null,"datum":null,"valuta":null,"regels":[],
         "totaal_excl":null,"btw_totaal":null,"totaal_incl":null,"zekerheden":[]}
        """;

    public int Calls { get; private set; }

    public Task<AiResponse> ExtractAsync(DocumentInput document, string instruction, CancellationToken cancellationToken)
    {
        var step = steps[Math.Min(Calls++, steps.Length - 1)];
        if (step is Exception e) throw e;
        return Task.FromResult(new AiResponse((string)step, 1000, 200, TimeSpan.FromSeconds(1.5), "test-1"));
    }
}
