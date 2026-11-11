using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Services.DocumentAi;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AgfAdmin.Tests.Infrastructure.FakeDocumentAi;

namespace AgfAdmin.Tests.Services;

// Een factuur uploaden en laten lezen (stap 7a), met een nagebootste aanbieder (geen netwerk, geen sleutel): AI uit of niet ingesteld geeft
// een duidelijke melding en geen gegevens; een lezing wordt vastgelegd met model, verbruik, kosten en de hash van het bestand, maar nooit als
// factuur; maandlimiet en uploads per minuut; afwijzen en handmatig verder; opruimen na de bewaartermijn; de instelling onder Beheer.
public class AiInvoiceReadingTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly string _root = Directory.CreateTempSubdirectory("agf-ai-tests-").FullName;
    private readonly FakeDocumentAi _ai = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));
    private readonly DocumentAiOptions _options = FakeDocumentAi.DefaultOptions();
    private readonly UploadRateLimiter _limiter;

    public AiInvoiceReadingTests() => _limiter = new UploadRateLimiter(_clock);

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, true);
    }

    private AiInvoiceReadingService Service(AgfAdmin.Data.AppDbContext db, string user = "Jan")
    {
        var options = Options.Create(_options);
        return new AiInvoiceReadingService(db, _ai, new InvoiceReader(_ai, options, NullLogger<InvoiceReader>.Instance), new FileDocumentStore(_root), _limiter, options,
            new CurrentUser($"id-{user}", user, new[] { "Medewerker" }, Permissions.All), _clock);
    }

    private async Task EnableAsync(decimal limit = 25m)
    {
        await using var db = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.Ok, (await Service(db).UpdateSettingsAsync(new UpdateAiReadingSettingsDto { Enabled = true, CustomerInformed = true, MonthlyLimitEur = limit })).Status);
    }

    private async Task<ServiceResult<AiExtractionDto>> ReadAsync(byte[] bytes = null, string name = "factuur.pdf")
    {
        await using var db = _factory.CreateContext();
        return await Service(db).ReadAsync(name, bytes ?? Pdf);
    }

    private static string Code(ServiceResult<AiExtractionDto> result) => Assert.IsType<AiReadingErrorDto>(result.ConflictBody).Code;

    [Fact]
    public async Task With_AI_off_or_not_configured_nothing_is_read_or_stored_and_the_message_says_so()
    {
        var off = await ReadAsync();
        Assert.Equal((ServiceResultStatus.Unavailable, AiInvoiceReadingService.CodeOff), (off.Status, Code(off)));
        Assert.Contains("AI-lezen staat niet aan", off.Message);

        await EnableAsync();
        _ai.Reason = "Er is geen sleutel voor de AI-dienst ingesteld.";
        var notConfigured = await ReadAsync();
        Assert.Equal(AiInvoiceReadingService.CodeNotConfigured, Code(notConfigured));
        Assert.Contains("geen sleutel", notConfigured.Message);

        Assert.Empty(_ai.Calls);
        await using var db = _factory.CreateContext();
        Assert.Empty(db.AiExtractions);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_reading_is_stored_with_model_usage_cost_and_the_hash_of_the_file_but_never_as_an_invoice()
    {
        await EnableAsync();
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(), 4_500, 1_000);
        var result = await ReadAsync();
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var dto = result.Value;
        Assert.Equal((Haiku, "Claude Haiku 5.5", false, AiReadingRules.StatusProposed), (dto.Model, dto.ModelName, dto.UsedFallback, dto.Status));
        Assert.Equal(("F-2026-118", new DateOnly(2026, 11, 10), "EUR", "45-21"), (dto.Proposal.InvoiceNumber, dto.Proposal.InvoiceDate, dto.Proposal.Currency, dto.Proposal.SupplierOrderNumber));
        Assert.Equal(("box", "dozen", 12.50m), (dto.Proposal.Lines[0].Unit, dto.Proposal.Lines[0].UnitText, dto.Proposal.Lines[0].UnitPrice));
        Assert.Equal(("NL91ABNA0417164300", "12345678"), (dto.Proposal.VendorIban, dto.Proposal.VendorRegistrationNumber));

        await using var db = _factory.CreateContext();
        var stored = await db.AiExtractions.SingleAsync();
        Assert.Equal(("Jan", "Anthropic-fake", "claude-haiku-5-5-20261101"), (stored.CreatedBy, stored.Provider, stored.ModelVersion));
        Assert.Equal((4_500L, 1_000L, 0.00095m, 0.000817m, 0.86m), (stored.InputTokens, stored.OutputTokens, stored.CostUsd, stored.CostEur, stored.EurPerUsd));
        Assert.Equal(UploadedDocuments.Sha256(Pdf), stored.Sha256);
        Assert.Equal(Pdf, await File.ReadAllBytesAsync(Path.Combine(_root, stored.StorageKey)));
        Assert.Contains("\"priceCheckedOn\":\"2026-11-11\"", stored.AttemptsJson);
        Assert.Empty(db.IncomingInvoices);
    }

    [Fact]
    public async Task Unreachable_AI_is_a_plain_message_with_a_failed_reading_and_an_unusable_file_is_never_sent()
    {
        await EnableAsync();
        foreach (var model in new[] { Haiku, Haiku, Sonnet, Sonnet }) _ai.Fail(model, new AgfDocumentAi.TransientAiException("Anthropic: 529"));
        var result = await ReadAsync();
        Assert.Equal((ServiceResultStatus.Unavailable, AiInvoiceReadingService.CodeUnavailable), (result.Status, Code(result)));
        Assert.Contains("handmatig", result.Message);
        await using (var db = _factory.CreateContext())
        {
            var failed = await db.AiExtractions.SingleAsync();
            Assert.Equal((AiReadingRules.StatusFailed, "unavailable", (string)null), (failed.Status, failed.ErrorCode, failed.ProposalJson));
        }

        _ai.Calls.Clear();
        var unsupported = await ReadAsync(System.Text.Encoding.UTF8.GetBytes("platte tekst"), "factuur.txt");
        Assert.Equal((ServiceResultStatus.Validation, UploadedDocuments.Unsupported), (unsupported.Status, unsupported.Errors["file"].Single()));
        Assert.Empty(_ai.Calls);
    }

    [Fact]
    public async Task A_HEIC_photo_is_kept_as_original_and_shown_as_JPEG()
    {
        await EnableAsync();
        var heic = Convert.FromBase64String(InvoiceReaderTests.SampleHeic);
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal());
        var dto = (await ReadAsync(heic, "IMG_0042.HEIC")).Value;
        Assert.Equal(("image/heic", "image/jpeg", "IMG_0042.HEIC"), (dto.MediaType, dto.DisplayMediaType, dto.FileName));
        await using var db = _factory.CreateContext();
        var document = await Service(db).DocumentAsync(dto.Id);
        Assert.Equal(("image/jpeg", "image/jpeg"), (document!.Value.MediaType, UploadedDocuments.Sniff(document.Value.Bytes)));
        Assert.Equal(UploadedDocuments.Sha256(heic), (await db.AiExtractions.SingleAsync()).Sha256);
    }

    [Fact]
    public async Task Above_the_monthly_limit_reading_pauses_and_too_many_uploads_per_minute_are_refused()
    {
        await EnableAsync(limit: 0.01m);
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(), 100_000, 20_000);
        Assert.Equal(ServiceResultStatus.Ok, (await ReadAsync()).Status);
        Assert.Equal(AiInvoiceReadingService.CodeMonthlyLimit, Code(await ReadAsync()));
        Assert.Single(_ai.Calls);
        await using (var db = _factory.CreateContext())
        {
            var status = await Service(db).StatusAsync();
            Assert.Equal((true, 0.02m, 0.01m), (status.LimitReached, status.MonthSpentEur, status.MonthlyLimitEur));
        }

        // Volgende maand telt opnieuw; per gebruiker hooguit zoveel uploads per minuut.
        _clock.Advance(TimeSpan.FromDays(20));
        _options.MaxUploadsPerMinute = 1;
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(), 10, 10);
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(), 10, 10);
        Assert.Equal(ServiceResultStatus.Ok, (await ReadAsync()).Status);
        Assert.Equal(AiInvoiceReadingService.CodeRateLimit, Code(await ReadAsync()));
        await using var other = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.Ok, (await Service(other, "Steve").ReadAsync("f.pdf", Pdf)).Status);
    }

    [Fact]
    public async Task Rejecting_or_continuing_manually_closes_the_proposal_and_old_files_are_removed_later()
    {
        await EnableAsync();
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal());
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal());
        var first = (await ReadAsync()).Value.Id;
        var second = (await ReadAsync()).Value.Id;
        await using var db = _factory.CreateContext();
        var service = Service(db, "Steve");
        Assert.Equal(AiReadingRules.StatusRejected, (await service.CloseAsync(first, new CloseAiExtractionDto { Outcome = "Rejected" })).Value.Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await service.CloseAsync(first, new CloseAiExtractionDto { Outcome = "Manual" })).Status);
        Assert.Equal(AiReadingRules.StatusManual, (await service.CloseAsync(second, new CloseAiExtractionDto { Outcome = "Manual" })).Value.Status);
        Assert.Equal(ServiceResultStatus.Validation, (await service.CloseAsync(second, new CloseAiExtractionDto { Outcome = "Approved" })).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await service.CloseAsync(99, new CloseAiExtractionDto { Outcome = "Rejected" })).Status);
        Assert.Equal("Steve", (await db.AiExtractions.AsNoTracking().SingleAsync(e => e.Id == first)).ClosedBy);
        Assert.Empty(db.IncomingInvoices);

        Assert.Equal(0, await service.CleanupAsync());
        _clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(2, await service.CleanupAsync());
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.Null(await service.DocumentAsync(first));
        Assert.All(await db.AiExtractions.AsNoTracking().ToListAsync(), e => Assert.NotNull(e.FileDeletedAt));
    }

    [Fact]
    public async Task Turning_AI_on_needs_the_confirmation_that_the_customer_knows_and_records_who()
    {
        await using var db = _factory.CreateContext();
        var service = Service(db, "Beheerder Els");
        var refused = await service.UpdateSettingsAsync(new UpdateAiReadingSettingsDto { Enabled = true, CustomerInformed = false });
        Assert.Equal(ServiceResultStatus.Validation, refused.Status);
        Assert.True(refused.Errors.ContainsKey("customerInformed"));
        Assert.Equal(ServiceResultStatus.Validation, (await service.UpdateSettingsAsync(new UpdateAiReadingSettingsDto { Enabled = false, MonthlyLimitEur = -1m })).Status);
        var on = (await service.UpdateSettingsAsync(new UpdateAiReadingSettingsDto { Enabled = true, CustomerInformed = true, MonthlyLimitEur = 40m })).Value;
        Assert.Equal((true, "Beheerder Els", 40m), (on.Enabled, on.EnabledBy, on.MonthlyLimitEur));
        Assert.False((await service.UpdateSettingsAsync(new UpdateAiReadingSettingsDto { Enabled = false })).Value.Enabled);
        var status = await service.StatusAsync();
        Assert.Equal((false, true, "Claude Haiku 5.5", "Claude Sonnet 5.5"), (status.Enabled, status.Configured, status.PrimaryModel.Name, status.FallbackModel.Name));
    }
}
