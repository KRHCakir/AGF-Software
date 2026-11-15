using AgfAdmin.Configuration;
using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Services.DocumentAi;
using AgfAdmin.Services.Mail;
using AgfAdmin.Tests.Infrastructure;
using AgfAdmin.Tests.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AgfAdmin.Tests.Infrastructure.FakeDocumentAi;

namespace AgfAdmin.Tests.Configuration;

// De demo-omgeving online (AGF_DEMO_ENVIRONMENT=true): de API start alleen op een agf_demo-database, mail gaat altijd via de nagebootste
// afzender (ook met SMTP-instellingen), en AI-lezen heeft een daglimiet en nooit een hogere maandlimiet dan die van de demo.
public class DemoEnvironmentTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly string _root = Directory.CreateTempSubdirectory("agf-demo-tests-").FullName;
    private readonly FakeDocumentAi _ai = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly DocumentAiOptions _options = FakeDocumentAi.DefaultOptions();

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, true);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    private static readonly IConfiguration Demo = Config((DemoEnvironment.EnabledKey, "true"));

    [Fact]
    public void In_demo_mode_the_API_only_starts_on_a_database_whose_name_starts_with_agf_demo()
    {
        const string smoke = "Host=localhost;Port=5432;Database=agf_smoke;Username=agf";
        var refused = Assert.Throws<InvalidOperationException>(() => DemoEnvironment.EnsureAllowedDatabase(Demo, smoke));
        Assert.Contains("'agf_smoke'", refused.Message);
        Assert.Contains("agf_demo", refused.Message);
        Assert.Throws<InvalidOperationException>(() => DemoEnvironment.EnsureAllowedDatabase(Demo, "Host=db;Username=agf"));
        Assert.Throws<InvalidOperationException>(() => DemoEnvironment.EnsureAllowedDatabase(Demo, "Host=db;Database=AGF_DEMO_x;Username=agf"));
        Assert.Throws<InvalidOperationException>(() => DemoEnvironment.EnsureAllowedDatabase(Demo, "Host=db;Database=klant_agf_demo;Username=agf"));
        Assert.Throws<InvalidOperationException>(() => DemoEnvironment.EnsureAllowedDatabase(Demo, "dit is geen verbindingsreeks"));

        DemoEnvironment.EnsureAllowedDatabase(Demo, "Host=db;Database=agf_demo_20261115;Username=agf;Password=x");
        // Zonder demo-modus (lokaal, de tests, het gewone systeem) geldt de bewaking niet.
        DemoEnvironment.EnsureAllowedDatabase(Config(), smoke);
        DemoEnvironment.EnsureAllowedDatabase(Config((DemoEnvironment.EnabledKey, "false")), smoke);
        DemoEnvironment.EnsureAllowedDatabase(Config((DemoEnvironment.EnabledKey, "ja")), smoke);
    }

    [Fact]
    public void The_limits_have_defaults_and_an_unreadable_limit_stops_the_start()
    {
        Assert.Same(DemoEnvironment.Off, DemoEnvironment.From(Config()));
        var demo = DemoEnvironment.From(Demo);
        Assert.Equal((true, 20, 5m), (demo.Enabled, demo.AiDailyLimit, demo.AiMonthlyLimitEur));
        var set = DemoEnvironment.From(Config((DemoEnvironment.EnabledKey, "true"), (DemoEnvironment.AiDailyLimitKey, "3"), (DemoEnvironment.AiMonthlyLimitKey, "2.50")));
        Assert.Equal((3, 2.50m), (set.AiDailyLimit, set.AiMonthlyLimitEur));
        Assert.Equal((2.50m, 1m), (set.MonthlyLimitEur(25m), set.MonthlyLimitEur(1m)));
        Assert.Equal(25m, DemoEnvironment.Off.MonthlyLimitEur(25m));
        Assert.Throws<InvalidOperationException>(() => DemoEnvironment.From(Config((DemoEnvironment.EnabledKey, "true"), (DemoEnvironment.AiDailyLimitKey, "-1"))));
        Assert.Throws<InvalidOperationException>(() => DemoEnvironment.From(Config((DemoEnvironment.EnabledKey, "true"), (DemoEnvironment.AiMonthlyLimitKey, "vijf"))));
    }

    [Fact]
    public void In_demo_mode_only_the_simulated_mail_sender_is_registered_even_when_SMTP_is_configured()
    {
        IMailSender Resolve(IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            services.AddAgfServices(AppContext.BaseDirectory);
            using var provider = services.BuildServiceProvider();
            return provider.GetRequiredService<IMailSender>();
        }

        Assert.IsType<FakeMailSender>(Resolve(Config((DemoEnvironment.EnabledKey, "true"), ("Mail:Sender", "Smtp"))));
        Assert.IsType<FakeMailSender>(Resolve(Config((DemoEnvironment.EnabledKey, "true"))));
        // Buiten de demo blijft het zoals het was: SMTP, of de nagebootste afzender als die gevraagd wordt.
        Assert.IsType<SmtpMailSender>(Resolve(Config()));
        Assert.IsType<FakeMailSender>(Resolve(Config(("Mail:Sender", "Fake"))));
    }

    [Fact]
    public async Task The_simulated_sender_keeps_only_the_last_mails_in_memory()
    {
        var sender = new FakeMailSender();
        var mail = new OutgoingMail("demo@voorbeeld.nl", "Demo", new[] { "a@voorbeeld.nl" }, Array.Empty<string>(), "Onderwerp", "Tekst", "a.pdf", new byte[] { 1 });
        for (var i = 0; i < FakeMailSender.MaxKept + 5; i++)
        {
            var result = await sender.SendAsync(mail with { Subject = $"Mail {i}" }, null, default);
            Assert.True(result.Success);
        }
        Assert.Equal(FakeMailSender.MaxKept, sender.Sent.Count);
        Assert.Equal("Mail 5", sender.Sent.First().Subject);
    }

    private AiInvoiceReadingService Service(AgfAdmin.Data.AppDbContext db, DemoEnvironment demo)
    {
        var options = Options.Create(_options);
        return new AiInvoiceReadingService(db, _ai, new InvoiceReader(_ai, options, NullLogger<InvoiceReader>.Instance), new FileDocumentStore(_root),
            new UploadRateLimiter(_clock), options, new CurrentUser("id-demo", "Demo", new[] { "Admin" }, Permissions.All), _clock, demo);
    }

    private async Task<ServiceResult<AiExtractionDto>> ReadAsync(DemoEnvironment demo)
    {
        await using var db = _factory.CreateContext();
        return await Service(db, demo).ReadAsync("factuur.pdf", Pdf);
    }

    private static string Code(ServiceResult<AiExtractionDto> result) => Assert.IsType<AiReadingErrorDto>(result.ConflictBody).Code;

    [Fact]
    public async Task In_demo_mode_AI_reading_stops_at_the_daily_limit_and_starts_again_the_next_day()
    {
        var demo = new DemoEnvironment { Enabled = true, AiDailyLimit = 2, AiMonthlyLimitEur = 5m };
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await Service(db, demo).UpdateSettingsAsync(new UpdateAiReadingSettingsDto { Enabled = true, CustomerInformed = true, MonthlyLimitEur = 25m })).Status);
        }
        for (var i = 0; i < 3; i++) _ai.Answer(Haiku, AiReadingRulesTests.Proposal());
        Assert.Equal(ServiceResultStatus.Ok, (await ReadAsync(demo)).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await ReadAsync(demo)).Status);

        var refused = await ReadAsync(demo);
        Assert.Equal((ServiceResultStatus.Unavailable, AiInvoiceReadingService.CodeDemoDailyLimit), (refused.Status, Code(refused)));
        Assert.Contains("Voor de demo is de limiet voor vandaag bereikt", refused.Message);
        Assert.Equal(2, _ai.Calls.Count);
        // Zonder demo-modus geldt geen daglimiet.
        Assert.Equal(ServiceResultStatus.Ok, (await ReadAsync(DemoEnvironment.Off)).Status);

        _clock.Advance(TimeSpan.FromDays(1));
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal());
        Assert.Equal(ServiceResultStatus.Ok, (await ReadAsync(demo)).Status);
    }

    [Fact]
    public async Task In_demo_mode_the_monthly_limit_is_never_higher_than_the_demo_limit()
    {
        var demo = new DemoEnvironment { Enabled = true, AiDailyLimit = 20, AiMonthlyLimitEur = 0.0005m };
        await using (var db = _factory.CreateContext())
        {
            // In Instellingen staat 25 euro; de demo houdt het bij zijn eigen grens.
            await Service(db, demo).UpdateSettingsAsync(new UpdateAiReadingSettingsDto { Enabled = true, CustomerInformed = true, MonthlyLimitEur = 25m });
            var status = await Service(db, demo).StatusAsync();
            Assert.Equal((0.0005m, false), (status.MonthlyLimitEur, status.LimitReached));
            Assert.Equal(25m, (await Service(db, DemoEnvironment.Off).StatusAsync()).MonthlyLimitEur);
        }
        _ai.Answer(Haiku, AiReadingRulesTests.Proposal(), 4_500, 1_000);
        Assert.Equal(ServiceResultStatus.Ok, (await ReadAsync(demo)).Status);

        var refused = await ReadAsync(demo);
        Assert.Equal(AiInvoiceReadingService.CodeMonthlyLimit, Code(refused));
        await using (var db = _factory.CreateContext())
        {
            Assert.True((await Service(db, demo).StatusAsync()).LimitReached);
        }
    }

    [Fact]
    public void In_demo_mode_the_simulated_reader_and_a_missing_key_both_say_AI_is_not_available_in_this_demo()
    {
        var demoReader = new DocumentAiFactory(Options.Create(new DocumentAiOptions { Reader = DocumentAiOptions.ReaderDemo }), Demo);
        Assert.Contains("niet beschikbaar in deze demo", demoReader.NotConfiguredReason);
        Assert.Throws<InvalidOperationException>(() => demoReader.Create(DemoInvoiceAi.Model));
        Assert.Null(new DocumentAiFactory(Options.Create(new DocumentAiOptions { Reader = DocumentAiOptions.ReaderDemo }), Config()).NotConfiguredReason);

        var noKey = new DocumentAiFactory(Options.Create(FakeDocumentAi.DefaultOptions()), Demo);
        Assert.Contains("niet beschikbaar in deze demo (er is geen sleutel ingesteld)", noKey.NotConfiguredReason);
        var withKey = FakeDocumentAi.DefaultOptions();
        withKey.ApiKey = "sk-test-niet-echt";
        Assert.Null(new DocumentAiFactory(Options.Create(withKey), Demo).NotConfiguredReason);
    }
}
