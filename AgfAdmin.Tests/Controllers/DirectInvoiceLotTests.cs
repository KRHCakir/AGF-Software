using AgfAdmin.Controllers;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Controllers;

// Feedback 22 november: een directe factuur (verkoop zonder verkooporder, bijvoorbeeld contant aan de balie) krijgt een eigen verkoop-partij
// die nooit gelijk is aan een inkoopnummer, gekoppeld aan de inkoop-partijen van haar regels. Sinds verkoopfactuur 1 via
// POST /api/sales-invoices (concept met partijkeuze en voorraad); de oude route POST /api/SalesInvoice is vervangen.
public sealed class DirectInvoiceLotTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public DirectInvoiceLotTests() => _factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 23));

    public void Dispose() => _factory.Dispose();

    private Task<ServiceResult<SalesInvoiceDto>> CreateAsync(DirectInvoiceTestData.Arranged a, string lotNumber, params SalesInvoiceNewLineDto[] lines) =>
        DirectInvoiceTestData.CreateAsync(_factory, a.Seed.CustomerId, Guid.NewGuid(), lotNumber, lines.Length == 0 ? new[] { DirectInvoiceTestData.Service("Handling", 10m) } : lines);

    [Fact]
    public async Task A_Direct_Invoice_Gets_Its_Own_V_Number_Linked_To_The_Purchase_Lots_Of_Its_Lines()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var invoice = (await CreateAsync(a, null, DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 2m), DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotB, 3m),
            DirectInvoiceTestData.Service("Handling", 10m))).Value;

        await using var db = _factory.CreateContext();
        var lot = await db.Partijen.SingleAsync(p => p.InvoiceId == invoice.Id);
        Assert.Equal((PartijRules.KindSales, "V-2611001", (int?)null), (lot.Kind, lot.Number, lot.SalesOrderId));
        Assert.Equal("V-2611001", invoice.SalesLotNumber);
        Assert.Equal(new int?[] { a.LotA, a.LotB, null }, await db.InvoiceLines.OrderBy(l => l.Id).Select(l => l.PartijId).ToListAsync());
        // Een tweede directe factuur krijgt het volgende V-nummer; de inkoopreeks telt los.
        Assert.Equal("V-2611002", (await CreateAsync(a, null)).Value.SalesLotNumber);
    }

    [Theory]
    [InlineData("I-2611001", "purchase")]
    [InlineData("i 2611-001", "purchase")]
    [InlineData("V-2611001", "sales")]
    public async Task A_Typed_Number_That_Exists_As_Any_Lot_Is_Refused_With_An_Explanation(string typed, string kind)
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(a, null)).Status);

        var refused = await CreateAsync(a, typed);

        Assert.Equal(ServiceResultStatus.Conflict, refused.Status);
        Assert.Contains($"already exists as a {kind} lot", refused.Message);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.Invoices.CountAsync());
    }

    [Fact]
    public async Task A_Free_Typed_Number_Becomes_The_Sales_Lot_And_A_Sales_Lot_On_A_Line_Is_Refused()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var own = (await CreateAsync(a, "BALIE-23")).Value;
        Assert.Equal("BALIE-23", own.SalesLotNumber);

        await using var db = _factory.CreateContext();
        var salesLot = await db.Partijen.SingleAsync(p => p.InvoiceId == own.Id);
        var invalid = await CreateAsync(a, null, DirectInvoiceTestData.Product(a.Seed.AvocadoId, salesLot.Id, 1m));
        Assert.Contains("lines[0].partijId", invalid.Errors.Keys);
        var unknown = await CreateAsync(a, null, DirectInvoiceTestData.Product(a.Seed.AvocadoId, 999, 1m));
        Assert.Contains("lines[0].partijId", unknown.Errors.Keys);
    }

    [Fact]
    public async Task The_Guard_Keeps_The_Invoice_Lot_And_The_Purchase_Lot_Of_A_Line()
    {
        var a = await DirectInvoiceTestData.ArrangeAsync(_factory);
        var invoice = (await CreateAsync(a, null, DirectInvoiceTestData.Product(a.Seed.AvocadoId, a.LotA, 2m))).Value;

        await using var db = _factory.CreateContext();
        var line = await db.InvoiceLines.SingleAsync();
        line.PartijId = a.LotB;
        Assert.Throws<TraceabilityViolationException>(() => db.SaveChanges());

        db.ChangeTracker.Clear();
        var lot = await db.Partijen.SingleAsync(p => p.InvoiceId == invoice.Id);
        lot.InvoiceId = null;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Partijen.Remove(await db.Partijen.SingleAsync(p => p.InvoiceId == invoice.Id));
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());

        // Een verkoop-partij zonder order of nieuwe factuur kan niet ontstaan; het nummer van de factuurpartij alleen met reden (en alleen
        // zolang de factuur een concept is, zie SalesInvoiceLotNumberTests).
        db.ChangeTracker.Clear();
        db.Partijen.Add(new Partij { Kind = PartijRules.KindSales, Number = "LOS-1", NumberKey = "LOS1", CreatedBy = "test", InvoiceId = invoice.Id });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var service = new PartijService(db);
        var withoutReason = await service.CorrectNumberAsync(lot.Id, new CorrectPartijNumberDto { Number = "V-2611050" });
        Assert.Equal(ServiceResultStatus.Validation, withoutReason.Status);
        Assert.Equal(ServiceResultStatus.Ok, (await service.CorrectNumberAsync(lot.Id, new CorrectPartijNumberDto { Number = "V-2611050", Reason = "Typefout" })).Status);
    }

    [Fact]
    public async Task The_Old_Route_Says_Where_The_Direct_Invoice_Went()
    {
        await using var db = _factory.CreateContext();
        var result = Assert.IsType<ObjectResult>(new SalesInvoiceController(db).CreateInvoice());
        Assert.Equal(StatusCodes.Status410Gone, result.StatusCode);
        Assert.Contains("/api/sales-invoices", System.Text.Json.JsonSerializer.Serialize(result.Value));
    }
}
