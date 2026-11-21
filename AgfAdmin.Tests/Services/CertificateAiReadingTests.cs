using System.Text.Json.Nodes;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using AgfAdmin.Services;
using AgfAdmin.Services.DocumentAi;
using AgfAdmin.Tests.Infrastructure;
using AgfDocumentAi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static AgfAdmin.Tests.Infrastructure.FakeDocumentAi;

namespace AgfAdmin.Tests.Services;

// Certificaten deel 2: een certificaat-PDF door AI laten voorinvullen, met een nagebootste aanbieder (geen netwerk, geen sleutel). Het
// voorbeeld van de eigenaar: Lettuce op pagina 1, Arugula en Mizuna in de bijlage op pagina 2. Een voorstel slaat niets op; Goedkeuren gaat
// via het gewone opslagpad van de certificaten en legt vast wie goedkeurde, met model en wat er is aangepast; hetzelfde nummer wordt een
// nieuwe versie; een houder die duidelijk een ander bedrijf is geeft een waarschuwing; AI uit zegt dat duidelijk.
public class CertificateAiReadingTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 21);
    private const string Holder = "Kwekerij Verde Demo S.L.";
    private readonly SqliteAppDbContextFactory _factory = new() { Calendar = new FixedBusinessCalendar(Today) };
    private readonly string _root = Directory.CreateTempSubdirectory("agf-ai-cert-tests-").FullName;
    private readonly FakeDocumentAi _ai = new();
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 11, 21, 10, 0, 0, TimeSpan.Zero));
    private readonly DocumentAiOptions _options = FakeDocumentAi.DefaultOptions();
    private readonly UploadRateLimiter _limiter;
    private int _relationId;
    private int _sla;
    private int _rucola;
    private int _mix;

    public CertificateAiReadingTests() => _limiter = new UploadRateLimiter(_clock);

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, true);
    }

    private static ICurrentUser User(string name) => new CurrentUser($"id-{name}", name, new[] { "Medewerker" }, Permissions.All);

    private AiInvoiceReadingService Reading(AgfAdmin.Data.AppDbContext db, string user = "Jan")
    {
        var options = Options.Create(_options);
        return new AiInvoiceReadingService(db, _ai, new InvoiceReader(_ai, options, NullLogger<InvoiceReader>.Instance), new FileDocumentStore(_root), _limiter, options, User(user), _clock);
    }

    private async Task SeedAsync(string relationName = Holder, bool enable = true)
    {
        await using var db = _factory.CreateContext();
        var relation = new Relation { Name = relationName, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var sla = new Product { Name = "Sla" };
        var rucola = new Product { Name = "Rucola" };
        var mix = new Product { Name = "Bladgroente mix" };
        db.AddRange(relation, sla, rucola, mix);
        await db.SaveChangesAsync();
        (_relationId, _sla, _rucola, _mix) = (relation.Id, sla.Id, rucola.Id, mix.Id);
        if (enable)
        {
            Assert.Equal(ServiceResultStatus.Ok, (await Reading(db).UpdateSettingsAsync(new UpdateAiReadingSettingsDto { Enabled = true, CustomerInformed = true, MonthlyLimitEur = 25m })).Status);
        }
    }

    private async Task<ServiceResult<AiCertificateExtractionDto>> ReadAsync(int? relationId = -1, JsonObject answer = null)
    {
        _ai.Answer(Haiku, answer ?? CertificateProposalsTests.Example(), 3_000, 400);
        await using var db = _factory.CreateContext();
        return await Reading(db).ReadCertificateAsync(relationId == -1 ? _relationId : relationId, "certificaat.pdf", Pdf);
    }

    private async Task<CertificateListDto> ListAsync()
    {
        await using var db = _factory.CreateContext();
        return (await new CertificateService(db).GetForRelationAsync(_relationId)).Value;
    }

    private async Task<ServiceResult<CertificateListDto>> SaveAsync(SaveCertificatesDto request, string user = "Piet")
    {
        await using var db = _factory.CreateContext();
        return await new CertificateService(db, User(user)).SaveForRelationAsync(_relationId, request);
    }

    private static SaveCertificateDto Row(AiCertificateProposalDto p, int? id = null, IEnumerable<int> products = null, string body = null) => new()
    {
        Id = id, TypeCode = p.TypeCode, Number = p.Number, CertificationBody = body ?? p.CertificationBody, ValidFrom = p.ValidFrom, ValidUntil = p.ValidUntil,
        ProducerName = p.HolderName, ProductIds = (products ?? p.Products.Where(x => x.ProductId != null).Select(x => x.ProductId!.Value)).ToList(), IsActive = true,
    };

    [Fact]
    public async Task The_proposal_has_the_products_of_page_1_and_the_annex_with_a_suggested_link_and_nothing_is_saved()
    {
        await SeedAsync();
        var result = await ReadAsync();
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var dto = result.Value;
        Assert.Equal((AiReadingRules.StatusProposed, _relationId, Haiku), (dto.Status, dto.RelationId, dto.Model));
        var p = dto.Proposal;
        Assert.Equal((CertificateRules.GlobalGapIfa, DemoCertificateAi.Number, Holder, "ES", "Demo Certificering B.V."), (p.TypeCode, p.Number, p.HolderName, p.HolderCountry, p.CertificationBody));
        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2026, 12, 10), CertificateRules.StatusExpiringSoon, 19), (p.ValidFrom!.Value, p.ValidUntil!.Value, p.Status, p.DaysLeft!.Value));
        Assert.Equal(new[] { ("Lettuce", 1, (int?)_sla, "Translated"), ("Arugula", 2, _rucola, "Translated"), ("Mizuna", 2, null, "None") },
            p.Products.Select(x => (x.Text, x.Page!.Value, x.ProductId, x.Match)));
        Assert.Equal(("Finca Demo Norte", "ES"), (p.Sites.Single().Name, p.Sites.Single().Country));
        // Gele markering: de AI twijfelt aan de einddatum; Mizuna is niet gekoppeld. De houder is de relatie zelf: geen waarschuwing.
        Assert.Contains(dto.Problems, x => (x.Field, x.Code) == ("validUntil", AiReadingRules.Uncertain));
        Assert.Contains(dto.Problems, x => (x.Field, x.Code) == ("products[2]", CertificateProposals.ProblemUnmatched));
        Assert.Null(p.HolderMismatch);
        Assert.Null(p.SameNumberCertificateId);

        await using var db = _factory.CreateContext();
        Assert.Empty(db.RelationCertificates);
        Assert.Empty(db.RelationCertificateFiles);
        var reading = await db.AiExtractions.SingleAsync();
        Assert.Equal((AiReadingRules.DocumentCertificate, _relationId, 3_000L, 400L, "Jan"), (reading.DocumentType, reading.RelationId, reading.InputTokens, reading.OutputTokens, reading.CreatedBy));
        Assert.True(reading.CostEur > 0m);
        Assert.Equal(UploadedDocuments.Sha256(Pdf), reading.Sha256);
        // Een factuurlezing en een certificaatlezing zijn elkaars niet: via Inkoop is deze lezing niet te zien.
        Assert.Equal(ServiceResultStatus.NotFound, (await Reading(db).GetAsync(reading.Id)).Status);
    }

    [Fact]
    public async Task A_holder_that_is_clearly_another_company_gives_a_warning_and_the_same_company_with_another_legal_form_does_not()
    {
        await SeedAsync("Kwekerij Verde Demo B.V.");
        Assert.Null((await ReadAsync()).Value.Proposal.HolderMismatch);
        var other = (await ReadAsync(answer: CertificateProposalsTests.Example("Agrícola Sol Naciente S.A."))).Value;
        Assert.Equal("Agrícola Sol Naciente S.A.", other.Proposal.HolderMismatch);
        Assert.Contains(other.Problems, x => (x.Field, x.Code) == ("holderName", CertificateProposals.ProblemHolder));
        // Bij het eigen bedrijf: de bedrijfsnaam uit de instellingen.
        await using (var db = _factory.CreateContext())
        {
            (await db.CompanySettings.SingleAsync()).CompanyName = "Groothandel Noordzee B.V.";
            await db.SaveChangesAsync();
        }
        Assert.Equal(Holder, (await ReadAsync(null)).Value.Proposal.HolderMismatch);
    }

    [Fact]
    public async Task With_AI_off_the_message_is_clear_and_manual_entry_still_works()
    {
        await SeedAsync(enable: false);
        await using var db = _factory.CreateContext();
        var off = await Reading(db).ReadCertificateAsync(_relationId, "certificaat.pdf", Pdf);
        Assert.Equal((ServiceResultStatus.Unavailable, AiInvoiceReadingService.CodeOff), (off.Status, Assert.IsType<AiReadingErrorDto>(off.ConflictBody).Code));
        Assert.Contains("handmatig invoeren werkt gewoon", off.Message);
        Assert.Empty(_ai.Calls);
        Assert.Empty(db.AiExtractions);
        // Handmatig blijft precies zoals in certificaten deel 1.
        var manual = await SaveAsync(new SaveCertificatesDto { ExpectedVersion = "0", Certificates = new() { new SaveCertificateDto { TypeCode = "Skal", Number = "NL-BIO-01.528.123", IsActive = true } } });
        Assert.Equal(ServiceResultStatus.Ok, manual.Status);
    }

    [Fact]
    public async Task Rejecting_or_continuing_manually_saves_nothing_and_a_closed_proposal_stays_closed()
    {
        await SeedAsync();
        var read = (await ReadAsync()).Value;
        await using (var db = _factory.CreateContext())
        {
            var closed = await Reading(db, "Anja").CloseCertificateAsync(read.Id, new CloseAiExtractionDto { Outcome = AiReadingRules.StatusManual });
            Assert.Equal(AiReadingRules.StatusManual, closed.Value.Status);
            Assert.Equal(ServiceResultStatus.Conflict, (await Reading(db).CloseCertificateAsync(read.Id, new CloseAiExtractionDto { Outcome = AiReadingRules.StatusRejected })).Status);
            Assert.Equal("Anja", (await db.AiExtractions.SingleAsync()).ClosedBy);
        }
        Assert.Empty((await ListAsync()).Certificates);
        await using (var db = _factory.CreateContext())
        {
            Assert.Empty(db.RelationCertificateFiles);
            // Afwijzen van een gesloten voorstel kan niet meer; een factuurlezing is via deze weg niet te sluiten.
            Assert.Equal(ServiceResultStatus.NotFound, (await Reading(db).CloseAsync(read.Id, new CloseAiExtractionDto { Outcome = AiReadingRules.StatusRejected })).Status);
        }
    }
}
