using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Verkoopfactuur 1, Definitief maken: een concept heeft geen nummer; Definitief kent het volgende nummer van het jaar toe, zonder gaten
// (ook als een factuur uit een selectie geweigerd wordt), legt een momentopname vast die blijft als klant of artikel later verandert, en
// boekt bij een directe factuur de voorraad af. Daarna verandert er niets meer.
public sealed class SalesInvoiceFinalizeTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private static readonly int Year = AmsterdamBusinessCalendar.ToAmsterdamDate(DateTime.UtcNow).Year;

    public void Dispose() => _factory.Dispose();

    private async Task<ServiceResult<SalesInvoiceDto>> FinalizeAsync(SalesInvoiceDto invoice)
    {
        await using var db = _factory.CreateContext();
        return await new SalesInvoiceService(db).FinalizeAsync(invoice.Id, new FinalizeSalesInvoiceDto { ExpectedVersion = invoice.Version });
    }

    private async Task<SalesInvoiceDto> GetAsync(int id)
    {
        await using var db = _factory.CreateContext();
        return (await new SalesInvoiceService(db).GetAsync(id)).Value;
    }

    [Fact]
    public async Task Final_Gives_The_Next_Number_And_A_Snapshot_Of_The_Customer_And_Then_Nothing_Changes()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        await using (var raw = _factory.CreateContext())
        {
            await raw.Database.ExecuteSqlAsync($"UPDATE \"Customers\" SET \"BillingAddress\" = 'Veilingweg 5, 9723 AA Groningen', \"VATNumber\" = 'NL001234567B01', \"PaymentTermsDays\" = 21 WHERE \"Id\" = {a.Seed.CustomerId}");
        }
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        Assert.Null(concept.InvoiceNumber);

        var final = await FinalizeAsync(concept);
        Assert.Equal(ServiceResultStatus.Ok, final.Status);
        var invoice = final.Value;
        Assert.Equal(SalesInvoiceRules.StatusFinal, invoice.Status);
        Assert.Equal($"VF-{Year}-00001", invoice.InvoiceNumber);
        Assert.Equal(("Customer T", "Veilingweg 5, 9723 AA Groningen", "NL001234567B01", 21), (invoice.CustomerName, invoice.CustomerAddress, invoice.CustomerVatNumber, invoice.PaymentTermDays.Value));
        Assert.Equal(AmsterdamBusinessCalendar.ToAmsterdamDate(invoice.InvoiceDate).AddDays(21), DateOnly.FromDateTime(invoice.DueDate.Value));
        Assert.NotNull(invoice.FinalizedAt);
        Assert.Equal(416.28m, invoice.Totals.Gross);

        // Een herhaling (bijvoorbeeld na een netwerkfout) geeft dezelfde factuur, geen tweede nummer.
        var again = await FinalizeAsync(concept);
        Assert.Equal((ServiceResultStatus.Ok, invoice.InvoiceNumber), (again.Status, again.Value.InvoiceNumber));

        await using var db = _factory.CreateContext();
        var service = new SalesInvoiceService(db);
        var change = await service.UpdateAsync(invoice.Id, new UpdateSalesInvoiceDto { ExpectedVersion = invoice.Version, ReferenceNumber = "X" });
        Assert.Equal(ServiceResultStatus.Conflict, change.Status);
        Assert.Contains("final", change.Message);
        Assert.Equal(ServiceResultStatus.Conflict, (await service.DiscardAsync(invoice.Id, new DiscardSalesInvoiceDto { ExpectedVersion = invoice.Version })).Status);
    }

    [Fact]
    public async Task The_Snapshot_Stays_When_Customer_And_Article_Change_Later()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        var final = (await FinalizeAsync(concept)).Value;
        await using (var raw = _factory.CreateContext())
        {
            await raw.Database.ExecuteSqlAsync($"UPDATE \"Customers\" SET \"Name\" = 'Andere naam', \"BillingAddress\" = 'Elders 1' WHERE \"Id\" = {a.Seed.CustomerId}");
            await raw.Database.ExecuteSqlAsync($"UPDATE \"ProductVariants\" SET \"Name\" = 'Fuerte', \"Packaging\" = '10kg Box' WHERE \"Id\" = {a.Seed.AvocadoId}");
        }
        var later = await GetAsync(final.Id);
        Assert.Equal("Customer T", later.CustomerName);
        Assert.Null(later.CustomerAddress);
        Assert.StartsWith("Avocado Hass, 4kg Box", later.Lines[0].Description);
        Assert.Equal(final.Totals.Gross, later.Totals.Gross);
    }

    [Fact]
    public async Task A_Selection_Numbers_Without_Gaps_When_An_Invoice_In_Between_Is_Refused()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var first = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Service("Handling", 10m))).Value;
        var noPrice = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Service("Transport", 0m))).Value;
        await using (var db = _factory.CreateContext())
        {
            noPrice = (await new SalesInvoiceService(db).UpdateAsync(noPrice.Id, new UpdateSalesInvoiceDto
            {
                ExpectedVersion = noPrice.Version,
                Lines = new List<UpdateSalesInvoiceLineDto> { new() { Id = noPrice.Lines[0].Id, Quantity = 1m, UnitPrice = null, VatPercentage = 21m } }
            })).Value;
        }
        var third = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Service("Loodskosten", 30m))).Value;

        await using (var db = _factory.CreateContext())
        {
            var result = (await new SalesInvoiceService(db).FinalizeManyAsync(new FinalizeSalesInvoicesDto
            {
                Invoices = new[] { first, noPrice, third }.Select(i => new FinalizeSalesInvoiceItemDto { Id = i.Id, ExpectedVersion = i.Version }).ToList()
            })).Value.Results;
            Assert.Equal(new[] { true, false, true }, result.Select(r => r.Final));
            Assert.Equal(new[] { $"VF-{Year}-00001", null, $"VF-{Year}-00002" }, result.Select(r => r.InvoiceNumber));
            Assert.Contains("lines[0].unitPrice", result[1].Errors.Keys);
        }
        Assert.Null((await GetAsync(noPrice.Id)).InvoiceNumber);
    }

    [Fact]
    public async Task The_Database_Refuses_A_Second_Invoice_With_The_Same_Number()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var first = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Service("Handling", 10m))).Value;
        var second = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Service("Transport", 20m))).Value;
        await FinalizeAsync(first);
        await using var db = _factory.CreateContext();
        var invoice = await db.Invoices.SingleAsync(i => i.Id == second.Id);
        invoice.Status = SalesInvoiceRules.StatusFinal;
        invoice.InvoiceYear = Year;
        invoice.InvoiceSequence = 1;
        invoice.InvoiceNumber = SalesInvoiceRules.FormatNumber(Year, 1);
        invoice.FinalizedAt = DateTime.UtcNow;
        invoice.FinalizedBy = "test";
        invoice.PaymentTermDays = 14;
        invoice.DueDate = DateTime.UtcNow;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(DbErrors.IsUniqueViolation(error));
    }

    [Fact]
    public async Task A_Direct_Invoice_Books_Out_Its_Stock_When_It_Becomes_Final()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var concept = (await DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 10m))).Value;
        var final = await FinalizeAsync(concept);
        Assert.Equal(ServiceResultStatus.Ok, final.Status);

        await using var db = _factory.CreateContext();
        var content = await db.PalletContentLines.SingleAsync(c => c.Id == a.Seed.ApprovedBox);
        Assert.Equal((10m, 40m), (content.DispatchedQuantity, content.DispatchedNetWeightKg));
        var reservation = await db.StockReservations.SingleAsync(r => r.InvoiceLineId != null);
        Assert.Equal(ReservationRules.StatusConsumed, reservation.Status);
        var stockLine = await db.SalesInvoiceStockLines.SingleAsync();
        Assert.Equal((concept.Id, reservation.Id, 10m), (stockLine.InvoiceId, stockLine.StockReservationId, stockLine.Quantity));
        Assert.Equal(ReservationRules.MovementTypeDispatch, (await db.StockMovements.SingleAsync(m => m.Id == stockLine.StockMovementId)).Type);
        Assert.Equal(30m, await DirectInvoiceTestData.FreeAsync(_factory, a, a.LotA));
    }

    [Fact]
    public async Task An_Invoice_Without_Price_Or_Lines_Is_Not_Final_And_Uses_No_Number()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var concept = (await SalesInvoiceTestData.FromDeliveriesAsync(_factory, Guid.NewGuid(), a.FirstDelivery)).Value;
        await using (var db = _factory.CreateContext())
        {
            concept = (await new SalesInvoiceService(db).UpdateAsync(concept.Id, new UpdateSalesInvoiceDto
            {
                ExpectedVersion = concept.Version,
                Lines = concept.Lines.Select(l => new UpdateSalesInvoiceLineDto { Id = l.Id, Quantity = l.Quantity, UnitPrice = l.LineKind == SalesInvoiceRules.LineProduct ? null : l.UnitPrice, VatPercentage = l.VatPercentage }).ToList()
            })).Value;
        }
        var refused = await FinalizeAsync(concept);
        Assert.Equal(ServiceResultStatus.Validation, refused.Status);
        Assert.Contains("lines[0].unitPrice", refused.Errors.Keys);
        await using var check = _factory.CreateContext();
        Assert.Equal(0, await check.Invoices.CountAsync(i => i.InvoiceNumber != null));
    }
}
