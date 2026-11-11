using System.Text.Json;
using System.Text.Json.Nodes;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgfAdmin.Services.DocumentAi
{
    // Een inkoopfactuur uploaden en door AI laten voorinvullen (stap 7a). De AI doet alleen een voorstel: hier wordt het document bewaard,
    // gelezen en de lezing vastgelegd (model, versie, verbruik, kosten). Opslaan als factuur gebeurt pas als een gebruiker goedkeurt, via
    // het gewone opslagpad van de inkoopfactuur. Zonder AI aan of zonder sleutel: een duidelijke melding, nooit nepgegevens.
    public sealed class AiInvoiceReadingService(AppDbContext db, IDocumentAiFactory factory, InvoiceReader reader, IDocumentStore store,
        UploadRateLimiter rateLimiter, IOptions<DocumentAiOptions> options, ICurrentUser user, TimeProvider clock)
    {
        public const string CodeOff = "AI_OFF";
        public const string CodeNotConfigured = "AI_NOT_CONFIGURED";
        public const string CodeRateLimit = "AI_RATE_LIMIT";
        public const string CodeMonthlyLimit = "AI_MONTHLY_LIMIT";
        public const string CodeUnavailable = "AI_UNAVAILABLE";
        public const string CodeInvalid = "AI_INVALID";
        public const string CodeUnsupported = "AI_UNSUPPORTED";
        public const string CodeFailed = "AI_FAILED";
        private const int MaxActorLength = 100;

        private readonly DocumentAiOptions _options = options.Value;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        // ---- Status en instellingen ----

        public async Task<AiReadingStatusDto> StatusAsync(CancellationToken cancellationToken = default)
        {
            var settings = await SettingsAsync(cancellationToken);
            var spent = await MonthSpentEurAsync(cancellationToken);
            var reason = factory.NotConfiguredReason;
            return new AiReadingStatusDto
            {
                Enabled = settings.AiReadingEnabled,
                Configured = reason == null,
                NotConfiguredReason = reason,
                Provider = factory.Provider,
                PrimaryModel = ModelDto(_options.PrimaryModel),
                FallbackEnabled = _options.FallbackEnabled,
                FallbackModel = _options.FallbackEnabled ? ModelDto(_options.FallbackModel) : null,
                MonthSpentEur = decimal.Round(spent, 2),
                MonthlyLimitEur = settings.AiReadingMonthlyLimitEur,
                LimitReached = spent >= settings.AiReadingMonthlyLimitEur,
                MaxFileBytes = _options.MaxFileBytes,
            };
        }

        public async Task<AiReadingSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) => SettingsDto(await SettingsAsync(cancellationToken));

        public async Task<ServiceResult<AiReadingSettingsDto>> UpdateSettingsAsync(UpdateAiReadingSettingsDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, string[]>();
            var settings = await SettingsAsync(cancellationToken);
            if (request == null)
            {
                return ServiceResult<AiReadingSettingsDto>.Validation(new Dictionary<string, string[]> { ["request"] = new[] { "A request body is required." } });
            }
            // Aanzetten stuurt documenten naar een externe dienst: alleen met de bevestiging dat de klant daarvan weet.
            if (request.Enabled && !settings.AiReadingEnabled && !request.CustomerInformed)
            {
                errors["customerInformed"] = new[] { "Confirm that the customer knows documents are sent to an external AI service." };
            }
            var limit = request.MonthlyLimitEur ?? settings.AiReadingMonthlyLimitEur;
            if (limit < 0m || limit > AiReadingRules.MaxMonthlyLimitEur || decimal.Round(limit, 2) != limit)
            {
                errors["monthlyLimitEur"] = new[] { $"The monthly limit must be between 0 and {AiReadingRules.MaxMonthlyLimitEur} euro with at most 2 decimals." };
            }
            if (errors.Count > 0) return ServiceResult<AiReadingSettingsDto>.Validation(errors);
            if (request.Enabled && !settings.AiReadingEnabled)
            {
                settings.AiReadingEnabledBy = user.Actor(MaxActorLength);
                settings.AiReadingEnabledAt = clock.GetUtcNow().UtcDateTime;
            }
            settings.AiReadingEnabled = request.Enabled;
            settings.AiReadingMonthlyLimitEur = limit;
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<AiReadingSettingsDto>.Ok(SettingsDto(settings));
        }

        // ---- Lezen ----

        public async Task<ServiceResult<AiExtractionDto>> ReadAsync(string fileName, byte[] bytes, CancellationToken cancellationToken = default)
        {
            var settings = await SettingsAsync(cancellationToken);
            if (!settings.AiReadingEnabled)
            {
                return Refused(CodeOff, "AI-lezen staat niet aan. Een beheerder kan het aanzetten bij Instellingen; handmatig invoeren werkt gewoon.");
            }
            if (factory.NotConfiguredReason is string reason)
            {
                return Refused(CodeNotConfigured, $"AI-lezen staat niet aan: {reason}");
            }
            if (await MonthSpentEurAsync(cancellationToken) >= settings.AiReadingMonthlyLimitEur)
            {
                return Refused(CodeMonthlyLimit, $"De maandlimiet voor AI-lezen ({settings.AiReadingMonthlyLimitEur:0.00} euro) is bereikt; lezen is gepauzeerd. Handmatig invoeren werkt gewoon.");
            }
            if (!rateLimiter.TryAcquire(user.UserId ?? "", _options.MaxUploadsPerMinute))
            {
                return Refused(CodeRateLimit, "Te veel uploads in korte tijd. Wacht een minuut en probeer het opnieuw.");
            }
            var (document, error) = UploadedDocuments.Prepare(fileName, bytes, _options.MaxFileBytes);
            if (error != null)
            {
                return ServiceResult<AiExtractionDto>.Validation(new Dictionary<string, string[]> { ["file"] = new[] { error } });
            }

            var originalName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "document" + document.Extension : fileName.Trim());
            var extraction = new AiExtraction
            {
                Status = AiReadingRules.StatusProposed,
                DocumentType = AiReadingRules.DocumentPurchaseInvoice,
                FileName = originalName.Length > 255 ? originalName[^255..] : originalName,
                MediaType = document.MediaType,
                SizeBytes = document.Original.LongLength,
                Sha256 = document.Sha256,
                StorageKey = await store.SaveAsync(document.Original, document.Extension, cancellationToken),
                DisplayMediaType = document.DisplayMediaType,
                Provider = factory.Provider,
                EurPerUsd = _options.EurPerUsd,
                CreatedBy = user.Actor(MaxActorLength),
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            };
            if (document.Display != null) extraction.DisplayStorageKey = await store.SaveAsync(document.Display, ".jpg", cancellationToken);

            var result = await reader.ReadAsync(document.ForAi, settings.CompanyName, cancellationToken);
            extraction.Model = result.Model;
            extraction.ModelVersion = result.ModelVersion;
            extraction.UsedFallback = result.UsedFallback;
            extraction.AttemptsJson = JsonSerializer.Serialize(result.Attempts, Json);
            extraction.InputTokens = result.InputTokens;
            extraction.OutputTokens = result.OutputTokens;
            extraction.CostUsd = result.CostUsd;
            extraction.CostEur = AiReadingRules.ToEur(result.CostUsd, _options.EurPerUsd);
            extraction.DurationMs = result.DurationMs;
            extraction.ProposalJson = result.Proposal?.ToJsonString();
            extraction.ProblemsJson = JsonSerializer.Serialize(result.Problems.Select(p => new AiProblemDto { Field = p.Field, Code = p.Code }), Json);
            if (result.Proposal == null)
            {
                extraction.Status = AiReadingRules.StatusFailed;
                extraction.ErrorCode = result.FailureCode;
            }
            db.AiExtractions.Add(extraction);
            await db.SaveChangesAsync(cancellationToken);

            return result.FailureCode switch
            {
                null => ServiceResult<AiExtractionDto>.Ok(ToDto(extraction)),
                "unavailable" => Refused(CodeUnavailable, "De AI-dienst is nu niet bereikbaar, ook niet na een tweede poging. Voer de factuur handmatig in of probeer het later opnieuw."),
                "invalid" => Refused(CodeInvalid, "De AI gaf geen bruikbaar voorstel voor dit document. Voer de factuur handmatig in."),
                "unsupported" => Refused(CodeUnsupported, "Dit document kan de AI niet lezen. Voer de factuur handmatig in."),
                _ => Refused(CodeFailed, "AI-lezen lukte niet door een fout bij de aanbieder. Voer de factuur handmatig in."),
            };
        }

        public async Task<ServiceResult<AiExtractionDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var extraction = await db.AiExtractions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
            return extraction == null ? ServiceResult<AiExtractionDto>.NotFound($"AI reading {id} does not exist.") : ServiceResult<AiExtractionDto>.Ok(ToDto(extraction));
        }

        // Het document voor de weergave (bij een HEIC-foto de JPEG); weg na de bewaartermijn van een niet-goedgekeurde lezing.
        public async Task<(byte[] Bytes, string MediaType, string FileName)?> DocumentAsync(int id, CancellationToken cancellationToken = default)
        {
            var extraction = await db.AiExtractions.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (extraction == null || extraction.FileDeletedAt != null) return null;
            var key = extraction.DisplayStorageKey ?? extraction.StorageKey;
            return (await store.ReadAsync(key, cancellationToken), extraction.DisplayMediaType ?? extraction.MediaType, extraction.FileName);
        }

        // Afwijzen (Rejected) of handmatig verder (Manual): alleen een openstaand voorstel; er wordt niets opgeslagen als factuur.
        public async Task<ServiceResult<AiExtractionDto>> CloseAsync(int id, CloseAiExtractionDto request, CancellationToken cancellationToken = default)
        {
            var outcome = request?.Outcome;
            if (outcome is not (AiReadingRules.StatusRejected or AiReadingRules.StatusManual))
            {
                return ServiceResult<AiExtractionDto>.Validation(new Dictionary<string, string[]> { ["outcome"] = new[] { "Outcome must be Rejected or Manual." } });
            }
            var extraction = await db.AiExtractions.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (extraction == null) return ServiceResult<AiExtractionDto>.NotFound($"AI reading {id} does not exist.");
            if (extraction.Status == outcome) return ServiceResult<AiExtractionDto>.Ok(ToDto(extraction));
            if (extraction.Status != AiReadingRules.StatusProposed)
            {
                return ServiceResult<AiExtractionDto>.Conflict($"AI reading {id} is {extraction.Status}; only an open proposal can be closed.");
            }
            extraction.Status = outcome;
            extraction.ClosedBy = user.Actor(MaxActorLength);
            extraction.ClosedAt = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(cancellationToken);
            return ServiceResult<AiExtractionDto>.Ok(ToDto(extraction));
        }

        // Opruimen: bestanden van niet-goedgekeurde lezingen (open, afgewezen, handmatig verder, mislukt) ouder dan de bewaartermijn.
        // De regel blijft (verbruik per maand); goedgekeurde documenten zijn bijlagen en blijven altijd.
        public async Task<int> CleanupAsync(CancellationToken cancellationToken = default)
        {
            var before = clock.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, _options.RetentionDays));
            var old = await db.AiExtractions.Where(e => e.Status != AiReadingRules.StatusApproved && e.FileDeletedAt == null && e.CreatedAt < before).ToListAsync(cancellationToken);
            foreach (var extraction in old)
            {
                store.Delete(extraction.StorageKey);
                if (extraction.DisplayStorageKey != null) store.Delete(extraction.DisplayStorageKey);
                extraction.FileDeletedAt = clock.GetUtcNow().UtcDateTime;
            }
            if (old.Count > 0) await db.SaveChangesAsync(cancellationToken);
            return old.Count;
        }

        // ---- Hulp ----

        // Kosten van alle lezingen in de lopende kalendermaand (Europe/Amsterdam), ook mislukte: elk verzoek kost geld.
        private async Task<decimal> MonthSpentEurAsync(CancellationToken cancellationToken)
        {
            var today = AmsterdamBusinessCalendar.ToAmsterdamDate(clock.GetUtcNow().UtcDateTime);
            var recent = await db.AiExtractions.AsNoTracking().Where(e => e.CreatedAt >= clock.GetUtcNow().UtcDateTime.AddDays(-32))
                .Select(e => new { e.CreatedAt, e.CostEur }).ToListAsync(cancellationToken);
            return recent.Where(e => AmsterdamBusinessCalendar.ToAmsterdamDate(e.CreatedAt) is var day && day.Year == today.Year && day.Month == today.Month).Sum(e => e.CostEur);
        }

        private async Task<CompanySettings> SettingsAsync(CancellationToken cancellationToken)
        {
            var settings = await db.CompanySettings.FirstOrDefaultAsync(cancellationToken);
            if (settings != null) return settings;
            settings = new CompanySettings();
            db.CompanySettings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
            return settings;
        }

        private static ServiceResult<AiExtractionDto> Refused(string code, string message) =>
            ServiceResult<AiExtractionDto>.Unavailable(message, new AiReadingErrorDto { Code = code, Message = message });

        private static AiReadingSettingsDto SettingsDto(CompanySettings settings) => new()
        {
            Enabled = settings.AiReadingEnabled,
            EnabledBy = settings.AiReadingEnabledBy,
            EnabledAt = settings.AiReadingEnabledAt == null ? null : DateTime.SpecifyKind(settings.AiReadingEnabledAt.Value, DateTimeKind.Utc),
            MonthlyLimitEur = settings.AiReadingMonthlyLimitEur,
        };

        private AiModelDto ModelDto(string model)
        {
            var price = _options.Models.GetValueOrDefault(model ?? "");
            return new AiModelDto
            {
                Id = model, Name = price?.DisplayName ?? model, InputPerMillionUsd = price?.InputPerMillionUsd ?? 0m,
                OutputPerMillionUsd = price?.OutputPerMillionUsd ?? 0m, PriceCheckedOn = price?.CheckedOn,
            };
        }

        public AiExtractionDto ToDto(AiExtraction extraction)
        {
            var proposal = extraction.ProposalJson == null ? null : JsonNode.Parse(extraction.ProposalJson) as JsonObject;
            return new AiExtractionDto
            {
                Id = extraction.Id,
                Status = extraction.Status,
                FileName = extraction.FileName,
                MediaType = extraction.MediaType,
                DisplayMediaType = extraction.DisplayMediaType ?? extraction.MediaType,
                SizeBytes = extraction.SizeBytes,
                Provider = extraction.Provider,
                Model = extraction.Model,
                ModelName = extraction.Model == null ? null : _options.Models.GetValueOrDefault(extraction.Model)?.DisplayName ?? extraction.Model,
                ModelVersion = extraction.ModelVersion,
                UsedFallback = extraction.UsedFallback,
                CostEur = decimal.Round(extraction.CostEur, 4),
                Proposal = proposal == null ? null : ToProposal(proposal),
                Problems = extraction.ProblemsJson == null ? new() : JsonSerializer.Deserialize<List<AiProblemDto>>(extraction.ProblemsJson, Json),
            };
        }

        public static AiInvoiceProposalDto ToProposal(JsonObject proposal) => new()
        {
            VendorName = AiReadingRules.Text(proposal["leverancier"]),
            VendorVatNumber = AiReadingRules.Text(proposal["leverancier_btw"]),
            VendorRegistrationNumber = AiReadingRules.Text(proposal["leverancier_kvk"]),
            VendorIban = AiReadingRules.Text(proposal["leverancier_iban"]),
            SupplierOrderNumber = AiReadingRules.Text(proposal["ordernummer_leverancier"]),
            InvoiceNumber = AiReadingRules.Text(proposal["factuurnummer"]),
            InvoiceDate = AiReadingRules.Date(proposal["datum"]),
            Currency = AiReadingRules.Text(proposal["valuta"])?.ToUpperInvariant() is { Length: 3 } currency ? currency : null,
            Lines = (proposal["regels"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(l => new AiInvoiceLineProposalDto
            {
                Description = AiReadingRules.Text(l["omschrijving"]),
                Quantity = AiReadingRules.Number(l["aantal"]),
                UnitText = AiReadingRules.Text(l["eenheid"]),
                Unit = AiReadingRules.Unit(AiReadingRules.Text(l["eenheid"])),
                UnitPrice = AiReadingRules.Number(l["prijs"]),
                Amount = AiReadingRules.Number(l["bedrag"]),
                VatPercentage = AiReadingRules.Number(l["btw_percentage"]),
            }).ToList(),
            TotalNet = AiReadingRules.Number(proposal["totaal_excl"]),
            TotalVat = AiReadingRules.Number(proposal["btw_totaal"]),
            TotalGross = AiReadingRules.Number(proposal["totaal_incl"]),
        };
    }

    // Ruimt dagelijks de bestanden van niet-goedgekeurde lezingen op (na DocumentAi:RetentionDays). Een fout stopt de API nooit.
    public sealed class AiUploadCleanupService(IServiceScopeFactory scopes, ILogger<AiUploadCleanupService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = scopes.CreateScope();
                        var removed = await scope.ServiceProvider.GetRequiredService<AiInvoiceReadingService>().CleanupAsync(stoppingToken);
                        if (removed > 0) logger.LogInformation("AI-lezen: {Count} oude uploads opgeruimd.", removed);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        logger.LogWarning("AI-lezen: opruimen lukte niet ({Kind}).", e.GetType().Name);
                    }
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // de API stopt
            }
        }
    }
}
