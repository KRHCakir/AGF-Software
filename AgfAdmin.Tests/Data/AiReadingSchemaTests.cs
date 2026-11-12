using AgfAdmin.Models;
using AgfAdmin.Services.DocumentAi;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Tabellen en opslag van AI-lezen (stap 7a): AI-lezen staat standaard uit met een maandlimiet van 25 euro; "goedgekeurd" kan alleen met
// factuur, wie en wanneer; de documentmap overschrijft nooit en komt er nooit buiten.
public class AiReadingSchemaTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly string _root = Directory.CreateTempSubdirectory("agf-ai-store-").FullName;

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, true);
    }

    private static AiExtraction Reading(string status = AiReadingRules.StatusProposed) => new()
    {
        Status = status, DocumentType = AiReadingRules.DocumentPurchaseInvoice, FileName = "f.pdf", MediaType = "application/pdf", SizeBytes = 10,
        Sha256 = new string('a', 64), StorageKey = "2026-11/x.pdf", Provider = "Anthropic", CreatedBy = "Jan",
    };

    [Fact]
    public async Task AI_reading_is_off_by_default_with_a_monthly_limit_of_25_euro()
    {
        await using var db = _factory.CreateContext();
        db.CompanySettings.Add(new CompanySettings());
        await db.SaveChangesAsync();
        var settings = await db.CompanySettings.AsNoTracking().SingleAsync();
        Assert.Equal((false, 25m, (string)null), (settings.AiReadingEnabled, settings.AiReadingMonthlyLimitEur, settings.AiReadingEnabledBy));
    }

    [Fact]
    public async Task Approved_needs_an_invoice_who_and_when_and_an_unknown_status_is_refused()
    {
        await using (var db = _factory.CreateContext())
        {
            db.AiExtractions.Add(Reading());
            await db.SaveChangesAsync();
        }
        foreach (var bad in new[] { Reading(AiReadingRules.StatusApproved), Reading("Booked") })
        {
            await using var db = _factory.CreateContext();
            db.AiExtractions.Add(bad);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task The_document_store_keeps_each_file_under_its_own_key_and_never_leaves_its_folder()
    {
        var store = new FileDocumentStore(_root);
        var first = await store.SaveAsync(new byte[] { 1, 2, 3 }, ".pdf", default);
        var second = await store.SaveAsync(new byte[] { 4 }, ".pdf", default);
        Assert.NotEqual(first, second);
        Assert.Matches(@"^\d{4}-\d{2}/[0-9a-f]{32}\.pdf$", first);
        Assert.Equal(new byte[] { 1, 2, 3 }, await store.ReadAsync(first, default));
        store.Delete(first);
        Assert.False(File.Exists(Path.Combine(_root, first)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync("../buiten.pdf", default));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new byte[] { 1 }, "/../x.pdf", default));
    }
}
