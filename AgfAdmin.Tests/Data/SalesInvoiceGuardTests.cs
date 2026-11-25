using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Regel 44 (verkoopfactuur 1): een nieuwe factuur is een concept zonder nummer; alleen Concept -> Definitief of Vervallen; daarna verandert
// er niets meer, regels worden nooit verwijderd en hun herkomst ligt vast. Oude facturen (Kind leeg) blijven zoals ze waren.
public class SalesInvoiceGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(int InvoiceId, int LineId)> ConceptAsync()
    {
        await using var db = _factory.CreateContext();
        var customer = new Customer { Name = "Groothandel Noord" };
        var invoice = new Invoice
        {
            Kind = SalesInvoiceRules.KindDirect, Status = SalesInvoiceRules.StatusConcept, Customer = customer, RequestId = Guid.NewGuid(), CreatedBy = "Jan",
            InvoiceDate = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc), CustomCustomerName = customer.Name,
            InvoiceLines = new List<InvoiceLine>
            {
                new() { LineKind = SalesInvoiceRules.LineService, Description = "Handling", Quantity = 2m, UnitPrice = 25m, VATPercentage = 21m, Unit = "piece", RevenueTypeId = 1 }
            }
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return (invoice.Id, invoice.InvoiceLines.Single().Id);
    }

    private static void MakeFinal(Invoice invoice)
    {
        invoice.Status = SalesInvoiceRules.StatusFinal;
        invoice.InvoiceYear = 2026;
        invoice.InvoiceSequence = 1;
        invoice.InvoiceNumber = SalesInvoiceRules.FormatNumber(2026, 1);
        invoice.FinalizedAt = new DateTime(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);
        invoice.FinalizedBy = "Jan";
        invoice.PaymentTermDays = 14;
        invoice.DueDate = new DateTime(2026, 12, 9, 0, 0, 0, DateTimeKind.Utc);
    }

    private async Task FinalAsync(int invoiceId)
    {
        await using var db = _factory.CreateContext();
        MakeFinal(await db.Invoices.SingleAsync(i => i.Id == invoiceId));
        await db.SaveChangesAsync();
    }

    private async Task<TraceabilityViolationException> RefusedAsync(Func<AppDbContext, Task> change)
    {
        await using var db = _factory.CreateContext();
        await change(db);
        return await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_New_Sales_Invoice_Is_A_Concept_Without_A_Number()
    {
        await using var db = _factory.CreateContext();
        var invoice = new Invoice
        {
            Kind = SalesInvoiceRules.KindDirect, Status = SalesInvoiceRules.StatusFinal, Customer = new Customer { Name = "Klant" }, RequestId = Guid.NewGuid(), CreatedBy = "Jan"
        };
        MakeFinal(invoice);
        db.Invoices.Add(invoice);
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_Concept_Becomes_Final_And_Then_Never_Changes()
    {
        var (invoiceId, lineId) = await ConceptAsync();
        await FinalAsync(invoiceId);

        await RefusedAsync(async db => (await db.Invoices.SingleAsync(i => i.Id == invoiceId)).ReferenceNumber = "PO-1");
        await RefusedAsync(async db => db.Invoices.Remove(await db.Invoices.SingleAsync(i => i.Id == invoiceId)));
        await RefusedAsync(async db => (await db.InvoiceLines.SingleAsync(l => l.Id == lineId)).UnitPrice = 30m);
        await RefusedAsync(async db =>
        {
            await Task.CompletedTask;
            db.InvoiceLines.Add(new InvoiceLine { InvoiceId = invoiceId, LineKind = SalesInvoiceRules.LineService, Description = "Extra", Quantity = 1m, UnitPrice = 1m, VATPercentage = 21m });
        });

        await using var check = _factory.CreateContext();
        var stored = await check.Invoices.Include(i => i.InvoiceLines).SingleAsync(i => i.Id == invoiceId);
        Assert.Equal("VF-2026-00001", stored.InvoiceNumber);
        Assert.Null(stored.ReferenceNumber);
        Assert.Equal(25m, stored.InvoiceLines.Single().UnitPrice);
    }

    [Fact]
    public async Task A_Discarded_Concept_Never_Changes_And_Is_Never_Final()
    {
        var (invoiceId, _) = await ConceptAsync();
        await using (var db = _factory.CreateContext())
        {
            var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
            invoice.Status = SalesInvoiceRules.StatusDiscarded;
            invoice.DiscardedAt = DateTime.UtcNow;
            invoice.DiscardedBy = "Jan";
            await db.SaveChangesAsync();
        }
        await RefusedAsync(async db => MakeFinal(await db.Invoices.SingleAsync(i => i.Id == invoiceId)));
    }

    [Fact]
    public async Task Lines_Of_A_Concept_Change_But_Are_Never_Deleted_And_Keep_Their_Origin()
    {
        var (invoiceId, lineId) = await ConceptAsync();
        await using (var db = _factory.CreateContext())
        {
            var line = await db.InvoiceLines.SingleAsync(l => l.Id == lineId);
            line.UnitPrice = 30m;
            line.Quantity = 3m;
            await db.SaveChangesAsync();
        }
        await RefusedAsync(async db => db.InvoiceLines.Remove(await db.InvoiceLines.SingleAsync(l => l.Id == lineId)));
        await RefusedAsync(async db => (await db.InvoiceLines.SingleAsync(l => l.Id == lineId)).LineKind = SalesInvoiceRules.LinePackaging);

        await using (var db = _factory.CreateContext())
        {
            (await db.InvoiceLines.SingleAsync(l => l.Id == lineId)).RemovedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await RefusedAsync(async db => (await db.InvoiceLines.SingleAsync(l => l.Id == lineId)).RemovedAt = null);
    }

    [Fact]
    public async Task The_Lines_Are_Recorded_Before_The_Invoice_Becomes_Final_Never_In_The_Same_Save()
    {
        var (invoiceId, lineId) = await ConceptAsync();
        await RefusedAsync(async db =>
        {
            MakeFinal(await db.Invoices.SingleAsync(i => i.Id == invoiceId));
            (await db.InvoiceLines.SingleAsync(l => l.Id == lineId)).Description = "Handling (momentopname)";
        });
    }

    [Fact]
    public async Task A_Final_Invoice_Needs_Its_Number_In_The_Database()
    {
        var (invoiceId, _) = await ConceptAsync();
        await using var db = _factory.CreateContext();
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
        invoice.Status = SalesInvoiceRules.StatusFinal;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Old_Invoices_Stay_As_They_Were_And_Never_Become_New_Ones()
    {
        await using (var db = _factory.CreateContext())
        {
            db.Invoices.Add(new Invoice { InvoiceNumber = "FAC-20261020-1234", Status = "Nieuw", CustomCustomerName = "Oud", InvoiceDate = DateTime.UtcNow,
                InvoiceLines = new List<InvoiceLine> { new() { Description = "Avocado", Quantity = 1m, UnitPrice = 10m, Unit = "kg" } } });
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            var old = await db.Invoices.SingleAsync(i => i.Kind == null);
            old.Status = "Finalized";
            await db.SaveChangesAsync();
        }
        await RefusedAsync(async db => (await db.Invoices.SingleAsync(i => i.Kind == null)).Kind = SalesInvoiceRules.KindDirect);
    }

    [Fact]
    public async Task Revenue_Types_Are_Never_Deleted_And_There_Is_A_Default()
    {
        await using (var db = _factory.CreateContext())
        {
            var standard = await db.RevenueTypes.SingleAsync();
            Assert.Equal("Omzet diensten", standard.Name);
            Assert.True(standard.IsDefault);
        }
        await RefusedAsync(async db => db.RevenueTypes.Remove(await db.RevenueTypes.SingleAsync()));
    }
}

// Het V-nummer van een directe factuur staat op de factuur zelf: na Definitief wordt een correctie netjes geweigerd (409), geen guardfout.
public class SalesInvoiceLotNumberTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Theory]
    [InlineData(SalesInvoiceRules.StatusConcept, ServiceResultStatus.Ok)]
    [InlineData(SalesInvoiceRules.StatusFinal, ServiceResultStatus.Conflict)]
    public async Task The_Lot_Number_Of_A_Direct_Invoice_Only_Changes_While_It_Is_A_Concept(string status, ServiceResultStatus expected)
    {
        int lotId;
        await using (var db = _factory.CreateContext())
        {
            var invoice = new Invoice
            {
                Kind = SalesInvoiceRules.KindDirect, Status = SalesInvoiceRules.StatusConcept, Customer = new Customer { Name = "Klant" }, RequestId = Guid.NewGuid(),
                CreatedBy = "Jan", BatchNumber = "V-2611001"
            };
            var lot = new Partij { Number = "V-2611001", NumberKey = PartijRules.NumberKey("V-2611001"), Kind = PartijRules.KindSales, Invoice = invoice, CreatedBy = "Jan" };
            db.AddRange(invoice, lot);
            await db.SaveChangesAsync();
            lotId = lot.Id;
            if (status == SalesInvoiceRules.StatusFinal)
            {
                invoice.Status = status;
                invoice.InvoiceYear = 2026;
                invoice.InvoiceSequence = 7;
                invoice.InvoiceNumber = SalesInvoiceRules.FormatNumber(2026, 7);
                invoice.FinalizedAt = DateTime.UtcNow;
                invoice.FinalizedBy = "Jan";
                invoice.PaymentTermDays = 14;
                invoice.DueDate = DateTime.UtcNow.Date.AddDays(14);
                await db.SaveChangesAsync();
            }
        }
        await using var write = _factory.CreateContext();
        var result = await new PartijService(write).CorrectNumberAsync(lotId, new CorrectPartijNumberDto { Number = "V-2611009", Reason = "Typefout" });
        Assert.Equal(expected, result.Status);
        await using var check = _factory.CreateContext();
        Assert.Equal(expected == ServiceResultStatus.Ok ? "V-2611009" : "V-2611001", (await check.Invoices.SingleAsync()).BatchNumber);
    }
}
