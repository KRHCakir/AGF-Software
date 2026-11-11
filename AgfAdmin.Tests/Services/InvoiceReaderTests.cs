using System.Text;
using AgfAdmin.Services.DocumentAi;
using AgfAdmin.Tests.Infrastructure;
using AgfDocumentAi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AgfAdmin.Tests.Infrastructure.FakeDocumentAi;

namespace AgfAdmin.Tests.Services;

// De lezer (stap 7a) met een nagebootste aanbieder: Haiku leest, het vangnet (Sonnet) leest één keer bij een afwijkend totaal, een
// ontbrekend verplicht veld of ongeldige uitvoer, een tijdelijke fout krijgt één herhaling, en elke poging telt mee in tokens en kosten.
// En het voorbereiden van een bestand: type aan de inhoud, beveiligde of onleesbare documenten, HEIC als JPEG.
public class InvoiceReaderTests
{
    private readonly FakeDocumentAi _ai = new();
    private readonly DocumentAiOptions _options = FakeDocumentAi.DefaultOptions();
    private static readonly DocumentInput Document = new("factuur.pdf", Pdf, "application/pdf");

    private Task<ReadResult> ReadAsync() =>
        new InvoiceReader(_ai, Options.Create(_options), NullLogger<InvoiceReader>.Instance).ReadAsync(Document, "Mijn AGF Bedrijf", default);

    [Fact]
    public async Task A_consistent_invoice_is_read_by_Haiku_once_with_tokens_price_and_cost()
    {
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(), 4_500, 1_000);
        var result = await ReadAsync();
        Assert.Equal((Haiku, "claude-haiku-5-5-20261101", false, (string)null), (result.Model, result.ModelVersion, result.UsedFallback, result.FailureCode));
        Assert.Empty(result.Problems);
        var attempt = Assert.Single(result.Attempts);
        Assert.Equal((4_500L, 1_000L, 0.10m, 0.50m, "2026-11-11", 0.00095m, "ok"),
            (attempt.InputTokens, attempt.OutputTokens, attempt.InputPerMillionUsd, attempt.OutputPerMillionUsd, attempt.PriceCheckedOn, attempt.CostUsd, attempt.Outcome));
    }

    [Fact]
    public async Task Totals_that_do_not_add_up_are_read_once_more_by_Sonnet_and_both_attempts_count()
    {
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(p => p["totaal_excl"] = 150m), 4_500, 1_000);
        _ai.Answer(Sonnet, AiReadingRulesTests.Proposal(), 4_500, 1_000);
        var result = await ReadAsync();
        Assert.Equal((Sonnet, true), (result.Model, result.UsedFallback));
        Assert.Equal(new[] { Haiku, Sonnet }, _ai.Calls);
        Assert.Empty(result.Problems);
        Assert.Equal(0.00095m + 0.019m, result.CostUsd);
    }

    [Fact]
    public async Task Still_wrong_after_the_fallback_keeps_Sonnets_proposal_with_the_open_problems()
    {
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(p => p["factuurnummer"] = null));
        _ai.Answer(Sonnet, AiReadingRulesTests.Proposal(p => p["factuurnummer"] = null));
        var result = await ReadAsync();
        Assert.Equal(Sonnet, result.Model);
        Assert.Contains(result.Problems, p => p is { Field: "invoiceNumber", Code: AiReadingRules.Missing });
    }

    [Fact]
    public async Task With_the_fallback_off_Haikus_proposal_is_kept_with_its_problems()
    {
        _options.FallbackEnabled = false;
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(p => p["totaal_excl"] = 150m));
        var result = await ReadAsync();
        Assert.Equal((Haiku, false), (result.Model, result.UsedFallback));
        Assert.Equal(new[] { Haiku }, _ai.Calls);
        Assert.Contains(result.Problems, p => p.Code == AiReadingRules.LinesMismatch);
    }

    [Fact]
    public async Task Invalid_output_goes_to_the_fallback_and_a_temporary_error_gets_one_retry()
    {
        _ai.Text(Haiku, "geen json");
        _ai.Answer(Sonnet, AiReadingRulesTests.Proposal());
        Assert.Equal(Sonnet, (await ReadAsync()).Model);

        _ai.Calls.Clear();
        _ai.Fail(Haiku, new TransientAiException("Anthropic: rate limit"));
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal());
        var result = await ReadAsync();
        Assert.Equal((Haiku, false), (result.Model, result.UsedFallback));
        Assert.Equal(new[] { Haiku, Haiku }, _ai.Calls);
        Assert.Equal(new[] { "unavailable", "ok" }, result.Attempts.Select(a => a.Outcome));
    }

    [Fact]
    public async Task Unreachable_or_failing_AI_gives_no_proposal_and_a_failure_code()
    {
        foreach (var model in new[] { Haiku, Haiku, Sonnet, Sonnet }) _ai.Fail(model, new TransientAiException("Anthropic: 529"));
        var unreachable = await ReadAsync();
        Assert.Equal(((System.Text.Json.Nodes.JsonObject)null, "unavailable"), (unreachable.Proposal, unreachable.FailureCode));
        Assert.Equal(new[] { Haiku, Haiku, Sonnet, Sonnet }, _ai.Calls);

        _ai.Fail(Haiku, new InvalidOperationException("401"));
        _ai.Fail(Sonnet, new InvalidOperationException("401"));
        Assert.Equal("failed", (await ReadAsync()).FailureCode);
    }

    [Fact]
    public void Without_a_key_or_a_price_the_real_reader_is_not_configured_and_says_why()
    {
        var empty = new ConfigurationBuilder().Build();
        Assert.Contains("geen sleutel", new DocumentAiFactory(Options.Create(_options), empty).NotConfiguredReason);
        var withKey = new DocumentAiFactory(Options.Create(new DocumentAiOptions { ApiKey = "test-key-not-used", Models = new() }), empty);
        Assert.Contains("prijs van model claude-haiku-5-5 ontbreekt", withKey.NotConfiguredReason);
        var fromEnvironment = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "test-key-not-used" }).Build();
        Assert.Null(new DocumentAiFactory(Options.Create(_options), fromEnvironment).NotConfiguredReason);
        var demo = new DocumentAiFactory(Options.Create(new DocumentAiOptions { Reader = DocumentAiOptions.ReaderDemo }), empty);
        Assert.Equal((null, "Demo"), (demo.NotConfiguredReason, demo.Provider));
        Assert.IsType<DemoInvoiceAi>(demo.Create(Haiku));
    }

    [Theory]
    [InlineData(UploadedDocuments.Empty)]
    [InlineData(UploadedDocuments.TooLarge)]
    [InlineData(UploadedDocuments.Unsupported)]
    [InlineData(UploadedDocuments.Encrypted)]
    [InlineData(UploadedDocuments.Unreadable)]
    public void Unusable_files_are_explained(string expected)
    {
        var bytes = expected switch
        {
            UploadedDocuments.Empty => Array.Empty<byte>(),
            UploadedDocuments.TooLarge => Pdf.Concat(new byte[1_000]).ToArray(),
            UploadedDocuments.Unsupported => Encoding.UTF8.GetBytes("Factuur F-1 als platte tekst"),
            UploadedDocuments.Encrypted => Encoding.ASCII.GetBytes("%PDF-1.7\ntrailer << /Encrypt 5 0 R >>\n%%EOF"),
            _ => new byte[] { 0xFF, 0xD8, 0xFF, 0x00, 0x01, 0x02 },
        };
        var (document, error) = UploadedDocuments.Prepare("factuur.jpg", bytes, 1_000);
        Assert.Equal(((PreparedDocument)null, expected), (document, error));
    }

    [Fact]
    public void A_PDF_goes_as_it_is_a_HEIC_photo_is_read_and_shown_as_JPEG_with_the_hash_of_the_original()
    {
        var (pdf, _) = UploadedDocuments.Prepare("f.pdf", Pdf, 10_000);
        Assert.Equal(("application/pdf", ".pdf", (byte[])null), (pdf.ForAi.MediaType, pdf.Extension, pdf.Display));

        var heic = Convert.FromBase64String(SampleHeic);
        var (photo, error) = UploadedDocuments.Prepare("IMG_0042.HEIC", heic, 10_000_000);
        Assert.Null(error);
        Assert.Equal(("image/heic", ".heic", "image/jpeg", "image/jpeg"), (photo.MediaType, photo.Extension, photo.ForAi.MediaType, photo.DisplayMediaType));
        Assert.Equal("image/jpeg", UploadedDocuments.Sniff(photo.Display));
        Assert.Equal(UploadedDocuments.Sha256(heic), photo.Sha256);
        Assert.Same(heic, photo.Original);
    }

    // Een kleine HEIC (200 x 100, kleurverloop; gemaakt met sips op macOS): Magick.NET leest HEIC, maar schrijft het niet.
    public const string SampleHeic = "AAAAJGZ0eXBoZWljAAAAAG1pZjFNaVBybWlhZk1pSEJoZWljAAABwm1ldGEAAAAAAAAAIWhkbHIAAAAAAAAAAHBpY3QAAAAAAAAAAAAAAAAAAAAAJGRpbmYAAAAcZHJlZgAAAAAAAAABAAAADHVybCAAAAABAAAADnBpdG0AAAAAAAEAAAA4aWluZgAAAAAAAgAAABVpbmZlAgAAAAABAABodmMxAAAAABVpbmZlAgAAAQACAABFeGlmAAAAABppcmVmAAAAAAAAAA5jZHNjAAIAAQABAAAA5WlwcnAAAADEaXBjbwAAABNjb2xybmNseAACAAIABoAAAAAMY2xsaQDLAEAAAAAUaXNwZQAAAAAAAADIAAAAZAAAAAlpcm90AAAAABBwaXhpAAAAAAMICAgAAABwaHZjQwEDcAAAALAAAAAAAB7wAPz9+PgAAAsDoAABABdAAQwB//8DcAAAAwCwAAADAAADAB5wJKEAAQAiQgEBA3AAAAMAsAAAAwAAAwAeoBogccs+Ie5FlU3AgIGAIKIAAQAJRAHAYXLIQFMkAAAAGWlwbWEAAAAAAAAAAQABBoECAwWGhAAAACxpbG9jAAAAAEQAAAIAAQAAAAEAAAJEAAAC2wACAAAAAQAAAfYAAABOAAAAAW1kYXQAAAAAAAADOQAAAAZFeGlmAABNTQAqAAAACAABh2kABAAAAAEAAAAaAAAAAAADoAEAAwAAAAEAAQAAoAIABAAAAAEAAADIoAMABAAAAAEAAABkAAAAAAAAAtcoAa+QRwztJEaiJiEHRuGFl6DOArst1vI+y4MK8AzSuYAsfUCb9LdElqlTyO1mDxUGWUa/l4p3WMZ6iOEFkrUTppuTJonc4huT0LiBWeCQdfbsjIeDNwgPst1szmpT0iIgqWnAsxO7A9c0RzNc/c1VMaZ2Zs7Y1HtiJYj7870VFqyWTnYSahykJxCGTN4dZgNAq9fofoIS8KE7zlK39rr2YuE7smgiryFEyAAENE4IIGyT07nRf43tgvJSVq63yRbQVztDFBAF+17RTLow6UCCJ+N4Ul5e35Vna2hGaoMlQwdCwS3pWFrP/oA2F5/EPvsPSDNrDKO4XwMBaLfac29YAPVcrOfNrkuWtEHK2y6tqiA9OSQESWkotX3IDLgnYPueBhKuDOofo6SwcUBG1tkdguET62KzKDvI1j4vDJSH+ZlRINBuih29IEqhysm6dOKHeHYIJwSndXc3ntc/Qgtajg1+tWvjollyktHRYfyMMSXpheEcaNbGJari5o51GY2CZHiOiPCvQCGhlkm9rgffgS+R1duiCQmBQeBFIqvco7ifTuuLuMrHN5/i3MJdroLzLUSyIFHuTY70O/qu6p788YNgyNFdEVWEI9aizDNI3e2uXqw9+TXNdhtmdLgQrY8P9Xdkw8iR2twV1tqhFTu8z3LdoEwOT8tSO0pG2nMsyo1RT0sfAcIL8C+R3IRROO5hoUFLv5Ty+nVp5GcIef/bUB1ptIlrhkiT3Uc79GIAT9K/BJsIzar0bN3F+xeDDUvMtcSjpMQLNre6ddfxsnfuqhZp4vLyD4InXfXfX5GhwcO6SExp8PaqOdFf7K2bnGhC98iQau7szvmGRf0ytPwkCtc7penSwhM/HwdJwMHxWv2afWDd0+0WQ2lPumKdwfFJGr9bHzLUoY2et0e8M47TcmXD71geGuht5fmITxU90HCmUYCT0qwWS9E4lhQ6isDF7sGV3M2A";
}
