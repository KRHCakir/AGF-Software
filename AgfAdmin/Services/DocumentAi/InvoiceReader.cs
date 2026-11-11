using System.Diagnostics;
using System.Text.Json.Nodes;
using AgfDocumentAi;
using Microsoft.Extensions.Options;

namespace AgfAdmin.Services.DocumentAi
{
    // Welke lezer er is: Claude met de sleutel uit de instellingen, of de nagebootste lezer (alleen tests en rondleiding).
    public interface IDocumentAiFactory
    {
        string Provider { get; }
        // Leest het systeem documenten? Zo niet: de reden in gewone taal (geen sleutel, geen prijs voor het model).
        string NotConfiguredReason { get; }
        IDocumentAI Create(string model);
    }

    public sealed class DocumentAiFactory(IOptions<DocumentAiOptions> options, IConfiguration configuration) : IDocumentAiFactory
    {
        private readonly DocumentAiOptions _options = options.Value;
        private bool Demo => string.Equals(_options.Reader, DocumentAiOptions.ReaderDemo, StringComparison.OrdinalIgnoreCase);
        private string Key => string.IsNullOrWhiteSpace(_options.ApiKey) ? configuration["ANTHROPIC_API_KEY"] : _options.ApiKey;

        public string Provider => Demo ? DocumentAiOptions.ReaderDemo : "Anthropic";

        public string NotConfiguredReason
        {
            get
            {
                if (Demo) return null;
                if (string.IsNullOrWhiteSpace(Key)) return "Er is geen sleutel voor de AI-dienst ingesteld (DocumentAi:ApiKey of ANTHROPIC_API_KEY).";
                var models = new[] { _options.PrimaryModel }.Concat(_options.FallbackEnabled ? new[] { _options.FallbackModel } : Array.Empty<string>());
                var missing = models.FirstOrDefault(m => !_options.Models.ContainsKey(m ?? ""));
                return missing == null ? null : $"De prijs van model {missing} ontbreekt in de instellingen (DocumentAi:Models).";
            }
        }

        public IDocumentAI Create(string model) => Demo ? new DemoInvoiceAi() : new ClaudeDocumentAi(model, Key, ExtractionSchema.Build(supplierIds: true));
    }

    public sealed record ReadAttempt(string Model, string ModelVersion, long InputTokens, long OutputTokens, decimal InputPerMillionUsd,
        decimal OutputPerMillionUsd, string PriceCheckedOn, decimal CostUsd, int DurationMs, string Outcome);

    public sealed record ReadResult(JsonObject Proposal, List<ReadingProblem> Problems, string Model, string ModelVersion, bool UsedFallback,
        List<ReadAttempt> Attempts, string FailureCode)
    {
        public long InputTokens => Attempts.Sum(a => a.InputTokens);
        public long OutputTokens => Attempts.Sum(a => a.OutputTokens);
        public decimal CostUsd => Attempts.Sum(a => a.CostUsd);
        public int DurationMs => Attempts.Sum(a => a.DurationMs);
    }

    // Leest een factuur met het hoofdmodel (Haiku 5.5). Een tijdelijke fout krijgt één herhaling. Klopt het voorstel niet (som van de
    // regels, btw, totaal, een verplicht veld ontbreekt of de AI twijfelt erover) of is de uitvoer ongeldig, dan leest het vangnetmodel
    // (Sonnet 5.5) het één keer; zijn voorstel wordt getoond, met de controles die dan nog openstaan. Elke poging telt mee in tokens en kosten.
    public sealed class InvoiceReader(IDocumentAiFactory factory, IOptions<DocumentAiOptions> options, ILogger<InvoiceReader> logger)
    {
        private readonly DocumentAiOptions _options = options.Value;

        public async Task<ReadResult> ReadAsync(DocumentInput document, string companyName, CancellationToken cancellationToken)
        {
            var instruction = ExtractionSchema.InstructionFor(companyName) + ExtractionSchema.SupplierIdInstruction;
            var attempts = new List<ReadAttempt>();
            // De nagebootste lezer (tests en rondleiding) leest alleen zichzelf: geen vangnet, en de lezing heet zoals ze is.
            var demo = factory.Provider == DocumentAiOptions.ReaderDemo;
            var primaryModel = demo ? DemoInvoiceAi.Model : _options.PrimaryModel;
            var primary = await ReadWithRetryAsync(primaryModel, document, instruction, attempts, cancellationToken);
            var problems = primary.Proposal == null ? null : AiReadingRules.Problems(primary.Proposal);
            var fallbackModel = _options.FallbackModel;
            if ((primary.Proposal == null || AiReadingRules.NeedsFallback(problems)) && _options.FallbackEnabled && !demo
                && !string.IsNullOrWhiteSpace(fallbackModel) && fallbackModel != _options.PrimaryModel && primary.Failure != "unsupported")
            {
                var fallback = await ReadWithRetryAsync(fallbackModel, document, instruction, attempts, cancellationToken);
                if (fallback.Proposal != null)
                {
                    return new ReadResult(fallback.Proposal, AiReadingRules.Problems(fallback.Proposal), fallbackModel, fallback.Version, true, attempts, null);
                }
            }
            return primary.Proposal != null
                ? new ReadResult(primary.Proposal, problems, primaryModel, primary.Version, false, attempts, null)
                : new ReadResult(null, new List<ReadingProblem>(), null, null, false, attempts, primary.Failure);
        }

        private async Task<(JsonObject Proposal, string Version, string Failure)> ReadWithRetryAsync(string model, DocumentInput document, string instruction,
            List<ReadAttempt> attempts, CancellationToken cancellationToken)
        {
            var first = await ReadOnceAsync(model, document, instruction, attempts, cancellationToken);
            return first.Failure == "unavailable" ? await ReadOnceAsync(model, document, instruction, attempts, cancellationToken) : first;
        }

        private async Task<(JsonObject Proposal, string Version, string Failure)> ReadOnceAsync(string model, DocumentInput document, string instruction,
            List<ReadAttempt> attempts, CancellationToken cancellationToken)
        {
            var price = _options.Models.GetValueOrDefault(model) ?? new DocumentAiModelPrice();
            var watch = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds)));
            void Record(string version, long input, long output, string outcome) => attempts.Add(new ReadAttempt(model, version, input, output,
                price.InputPerMillionUsd, price.OutputPerMillionUsd, price.CheckedOn, AiReadingRules.CostUsd(price, input, output), (int)watch.ElapsedMilliseconds, outcome));
            try
            {
                var response = await factory.Create(model).ExtractAsync(document, instruction, timeout.Token);
                var proposal = ExtractionSchema.Validate(response.Text, supplierIds: true, out var error);
                Record(response.ModelVersion, response.InputTokens, response.OutputTokens, proposal == null ? "invalid" : "ok");
                // Alleen de soort fout in het logboek, nooit de inhoud van het document of het antwoord.
                if (proposal == null) logger.LogWarning("AI-lezing met {Model}: ongeldige uitvoer ({Reason}).", model, error?.Split(':')[0]);
                return (proposal, response.ModelVersion, proposal == null ? "invalid" : null);
            }
            catch (TransientAiException e)
            {
                logger.LogWarning("AI-lezing met {Model}: tijdelijke fout ({Kind}).", model, e.InnerException?.GetType().Name ?? "onbekend");
                Record(null, 0, 0, "unavailable");
                return (null, null, "unavailable");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("AI-lezing met {Model}: geen antwoord binnen {Seconds} s.", model, _options.TimeoutSeconds);
                Record(null, 0, 0, "unavailable");
                return (null, null, "unavailable");
            }
            catch (UnsupportedDocumentException)
            {
                Record(null, 0, 0, "unsupported");
                return (null, null, "unsupported");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("AI-lezing met {Model}: fout bij de aanbieder ({Kind}).", model, e.GetType().Name);
                Record(null, 0, 0, "failed");
                return (null, null, "failed");
            }
        }
    }
}
