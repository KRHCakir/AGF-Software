using AgfAdmin.Controllers;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Controllers;

// Feedback 22 november: een directe factuur (verkoop zonder verkooporder, bijvoorbeeld contant aan de balie) krijgt een eigen verkoop-partij
// die nooit gelijk is aan een inkoopnummer, gekoppeld aan de inkoop-partijen van haar regels.
public sealed class DirectInvoiceLotTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public DirectInvoiceLotTests() => _factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 23));

    public void Dispose() => _factory.Dispose();

    private async Task<int> CustomerAsync()
    {
        await using var db = _factory.CreateContext();
        var customer = new Customer { Name = "Marktkraam De Zon" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<IActionResult> CreateAsync(int customerId, string batchNumber = null, params int?[] lots)
    {
        await using var db = _factory.CreateContext();
        var controller = new SalesInvoiceController(db, new PartijService(db));
        return await controller.CreateInvoice(new SalesInvoiceController.CreateInvoiceDto
        {
            CustomerId = customerId,
            BatchNumber = batchNumber,
            Lines = (lots.Length == 0 ? new int?[] { null } : lots).Select(lot => new SalesInvoiceController.CreateInvoiceLineDto
            {
                Description = "Mango Kent 4 kg", Quantity = 10m, Unit = "box", UnitPrice = 12m, VATPercentage = 9m, PartijId = lot
            }).ToList()
        });
    }

    [Fact]
    public async Task A_Direct_Invoice_Gets_Its_Own_V_Number_Linked_To_The_Purchase_Lots_Of_Its_Lines()
    {
        var customer = await CustomerAsync();
        var mango = await PartijTestData.CreateAsync(_factory, "I-2611007");
        var lime = await PartijTestData.CreateAsync(_factory, "I-2611008");

        var invoice = (Invoice)Assert.IsType<OkObjectResult>(await CreateAsync(customer, null, mango, lime, null)).Value;

        await using var db = _factory.CreateContext();
        var lot = await db.Partijen.SingleAsync(p => p.InvoiceId == invoice.Id);
        Assert.Equal((PartijRules.KindSales, "V-2611001", (int?)null), (lot.Kind, lot.Number, lot.SalesOrderId));
        Assert.Equal("V-2611001", (await db.Invoices.SingleAsync()).BatchNumber);
        Assert.Equal(new int?[] { mango, lime, null }, await db.Set<InvoiceLine>().OrderBy(l => l.Id).Select(l => l.PartijId).ToListAsync());
        // Een tweede directe factuur krijgt het volgende V-nummer; de inkoopreeks telt los.
        var second = (Invoice)Assert.IsType<OkObjectResult>(await CreateAsync(customer)).Value;
        Assert.Equal("V-2611002", second.BatchNumber);
    }

    [Theory]
    [InlineData("I-2611007", "purchase")]
    [InlineData("i 2611-007", "purchase")]
    [InlineData("V-2611001", "sales")]
    public async Task A_Typed_Number_That_Exists_As_Any_Lot_Is_Refused_With_An_Explanation(string typed, string kind)
    {
        var customer = await CustomerAsync();
        await PartijTestData.CreateAsync(_factory, "I-2611007");
        Assert.IsType<OkObjectResult>(await CreateAsync(customer));

        var refused = Assert.IsType<ConflictObjectResult>(await CreateAsync(customer, typed));

        var body = System.Text.Json.JsonSerializer.Serialize(refused.Value);
        Assert.Contains(PartijService.PartijTakenCode, body);
        Assert.Contains($"already exists as a {kind} lot", body);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.Invoices.CountAsync());
    }

    [Fact]
    public async Task A_Free_Typed_Number_Becomes_The_Sales_Lot_And_A_Sales_Lot_On_A_Line_Is_Refused()
    {
        var customer = await CustomerAsync();
        var own = (Invoice)Assert.IsType<OkObjectResult>(await CreateAsync(customer, "BALIE-23")).Value;
        Assert.Equal("BALIE-23", own.BatchNumber);

        await using var db = _factory.CreateContext();
        var salesLot = await db.Partijen.SingleAsync(p => p.InvoiceId == own.Id);
        var invalid = Assert.IsType<BadRequestObjectResult>(await CreateAsync(customer, null, salesLot.Id));
        Assert.Contains("lines[0].partijId", ((ValidationProblemDetails)invalid.Value).Errors.Keys);
        var unknown = Assert.IsType<BadRequestObjectResult>(await CreateAsync(customer, null, 999));
        Assert.Contains("lines[0].partijId", ((ValidationProblemDetails)unknown.Value).Errors.Keys);
    }

    [Fact]
    public async Task The_Guard_Keeps_The_Invoice_Lot_And_The_Purchase_Lot_Of_A_Line()
    {
        var customer = await CustomerAsync();
        var mango = await PartijTestData.CreateAsync(_factory, "I-2611007");
        var other = await PartijTestData.CreateAsync(_factory, "I-2611009");
        var invoice = (Invoice)Assert.IsType<OkObjectResult>(await CreateAsync(customer, null, mango)).Value;

        await using var db = _factory.CreateContext();
        var line = await db.Set<InvoiceLine>().SingleAsync();
        line.PartijId = other;
        Assert.Throws<TraceabilityViolationException>(() => db.SaveChanges());

        db.ChangeTracker.Clear();
        var lot = await db.Partijen.SingleAsync(p => p.InvoiceId == invoice.Id);
        lot.InvoiceId = null;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Partijen.Remove(await db.Partijen.SingleAsync(p => p.InvoiceId == invoice.Id));
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());

        // Een verkoop-partij zonder order of nieuwe factuur kan niet ontstaan; het nummer van de factuurpartij alleen met reden.
        db.ChangeTracker.Clear();
        db.Partijen.Add(new Partij { Kind = PartijRules.KindSales, Number = "LOS-1", NumberKey = "LOS1", CreatedBy = "test", InvoiceId = invoice.Id });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var service = new PartijService(db);
        var withoutReason = await service.CorrectNumberAsync(lot.Id, new CorrectPartijNumberDto { Number = "V-2611050" });
        Assert.Equal(ServiceResultStatus.Validation, withoutReason.Status);
        Assert.Equal(ServiceResultStatus.Ok, (await service.CorrectNumberAsync(lot.Id, new CorrectPartijNumberDto { Number = "V-2611050", Reason = "Typefout" })).Status);
    }
}
