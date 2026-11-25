using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace AgfAdmin.Tests.Services;

// Verkoopfactuur 1, de PDF: bedrijf met KvK en btw, klant met btw-nummer, nummer, factuurdatum, leverdatum, vervaldatum, V-nummer, regels met
// partij en GGN, emballage en diensten apart, btw per tarief en het totaal. Een concept draagt CONCEPT en wordt niet bewaard; Definitief
// bewaart versie 1, die daarna nooit verandert.
public sealed class SalesInvoicePdfTests : IDisposable
{
    static SalesInvoicePdfTests() => QuestPDF.Settings.License = LicenseType.Community;

    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static string Text(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    private async Task<SalesInvoiceService.InvoicePdf> PdfAsync(int id)
    {
        await using var db = _factory.CreateContext();
        return (await new SalesInvoiceService(db).PdfAsync(id)).Value;
    }

    // Een verkooporder in twee leveringen; bedrijfsgegevens, btw-nummer van de klant en een GGN op de pallet van de eerste levering.
    private async Task<SalesInvoiceTestData.Arranged> ArrangeAsync()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        await using var db = _factory.CreateContext();
        var settings = await db.CompanySettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new CompanySettings();
            db.CompanySettings.Add(settings);
        }
        settings.CompanyName = "DEMO Groenten BV";
        settings.Address = "Havenweg 9, 3089 JB Rotterdam";
        settings.KvkNumber = "12345678";
        settings.VatNumber = "NL001234567B01";
        settings.Iban = "NL00DEMO0123456789";
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE \"Customers\" SET \"VATNumber\" = 'NL009876543B01', \"BillingAddress\" = 'Veilingweg 5, 9723 AA Groningen' WHERE \"Id\" = {a.Seed.CustomerId}");
        await db.Database.ExecuteSqlAsync($"UPDATE \"PalletContentLines\" SET \"Ggn\" = '4049929999999' WHERE \"Id\" = {a.Seed.ApprovedBox}");
        return a;
    }

    [Fact]
    public async Task A_Concept_Pdf_Says_Concept_Has_No_Number_And_Is_Not_Stored()
    {
        var a = await ArrangeAsync();
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        var pdf = await PdfAsync(concept.Id);
        var text = Text(pdf.Bytes);
        Assert.Contains("CONCEPT", text);
        Assert.DoesNotContain("VF-", text);
        Assert.Equal("Factuur concept.pdf", pdf.FileName);
        await using var db = _factory.CreateContext();
        Assert.Equal(0, await db.SalesInvoiceDocuments.CountAsync());
    }

    [Fact]
    public async Task The_Final_Pdf_Has_The_Legal_Details_Lots_Ggn_Delivery_Date_And_Vat_Per_Rate()
    {
        var a = await ArrangeAsync();
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        SalesInvoiceDto final;
        await using (var db = _factory.CreateContext())
        {
            final = (await new SalesInvoiceService(db).FinalizeAsync(concept.Id, new FinalizeSalesInvoiceDto { ExpectedVersion = concept.Version })).Value;
        }
        var pdf = await PdfAsync(final.Id);
        var text = Text(pdf.Bytes);

        Assert.Equal($"Factuur {final.InvoiceNumber}.pdf", pdf.FileName);
        Assert.DoesNotContain("CONCEPT", text);
        foreach (var expected in new[]
        {
            "DEMO Groenten BV", "KvK 12345678", "Btw NL001234567B01", "NL00DEMO0123456789", "Customer T", "Veilingweg 5", "Btw-nummer NL009876543B01",
            final.InvoiceNumber, "Factuurdatum", "Leverdatum", "Vervaldatum", "PO-778", final.Deliveries[0].PartijNumber,
            "Avocado Hass", "LOT-A", "GGN 4049929999999", "€ 12,50", "10%", "€ 337,50",
            "Emballage en diensten", "Handling", "Pallet wegwerp",
            "Btw 9% over € 337,50", "€ 30,38", "Btw 21% over € 40,00", "€ 8,40", "Totaal incl. btw", "€ 416,28"
        })
        {
            Assert.Contains(expected, text);
        }
        Assert.DoesNotContain("Supplier B", text);
    }

    [Fact]
    public async Task Final_Stores_Version_1_That_Never_Changes_And_Is_Sent_Once()
    {
        var a = await ArrangeAsync();
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        await using (var db = _factory.CreateContext())
        {
            await new SalesInvoiceService(db).FinalizeAsync(concept.Id, new FinalizeSalesInvoiceDto { ExpectedVersion = concept.Version });
            var documents = (await new SalesInvoiceService(db).DocumentsAsync(concept.Id)).Value;
            Assert.Equal(1, Assert.Single(documents).Version);
        }
        // De bewaarde versie blijft zoals ze was, ook als de bedrijfsgegevens later veranderen.
        await using (var db = _factory.CreateContext())
        {
            (await db.CompanySettings.FirstAsync()).CompanyName = "Andere naam BV";
            await db.SaveChangesAsync();
        }
        Assert.Contains("DEMO Groenten BV", Text((await PdfAsync(concept.Id)).Bytes));

        await using (var db = _factory.CreateContext())
        {
            (await db.SalesInvoiceDocuments.SingleAsync()).SnapshotJson = "{}";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var document = await db.SalesInvoiceDocuments.SingleAsync();
            document.SentAt = DateTime.UtcNow;
            document.SentBy = "Jan";
            await db.SaveChangesAsync();
            document.SentBy = "Piet";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            db.SalesInvoiceDocuments.Remove(await db.SalesInvoiceDocuments.SingleAsync());
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task A_Document_Is_Only_Made_For_A_Final_Invoice()
    {
        var a = await ArrangeAsync();
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        await using var db = _factory.CreateContext();
        db.SalesInvoiceDocuments.Add(new SalesInvoiceDocument { InvoiceId = concept.Id, Version = 1, Number = "VF-X", SnapshotJson = "{}", ContentHash = "x", CreatedBy = "test" });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }
}
