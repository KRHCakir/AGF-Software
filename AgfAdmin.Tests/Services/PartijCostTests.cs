using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Partij P2, kosten verdelen: een factuurregel van €5.000 handling over twee verkoop-partijen (€500 + €1.000, de rest €3.500 is algemene
// kosten), nooit meer dan het regelbedrag, een regel over een inkoop- én een verkoop-partij, herverdelen met auditregels (oud en nieuw),
// gelijktijdig herverdelen geeft 409, en regel 25 bewaakt elke schrijver.
public sealed class PartijCostTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();
    private int _invoiceId;
    private int _lineId;
    private int _salesA;
    private int _salesB;
    private int _purchase;

    public PartijCostTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 5));

    public void Dispose() => _s.Dispose();

    private async Task ArrangeAsync(string currency = null)
    {
        await _s.ArrangeAsync();
        var costs = await CostArticleTestData.SeedStandardAsync(_s.Factory);
        var a = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;
        var b = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value;
        (_salesA, _salesB) = (a.SalesPartijId.Value, b.SalesPartijId.Value);
        await using var db = _s.Factory.CreateContext();
        _purchase = (await new PartijService(db).CreateAsync(new CreatePartijDto { RequestId = Guid.NewGuid(), Number = "52611002" })).Value.Id;
        var invoice = await new PurchaseInvoiceService(db).CreateAsync(new CreatePurchaseInvoiceDto
        {
            RequestId = Guid.NewGuid(), VendorId = _s.VendorId, InvoiceNumber = "BC-118", InvoiceDate = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc), Currency = currency,
            Lines = new() { new() { CostArticleId = costs["Handling"], Quantity = 1m, UnitPrice = 5000m, VatPercentage = 21m } }
        });
        Assert.Equal(ServiceResultStatus.Created, invoice.Status);
        (_invoiceId, _lineId) = (invoice.Value.Id, invoice.Value.Lines[0].Id);
    }

    private async Task<ServiceResult<InvoiceAllocationsDto>> SaveAsync(int version, Guid? requestId = null, params (int PartijId, decimal Amount)[] allocations)
    {
        await using var db = _s.Factory.CreateContext();
        return await new PartijCostService(db).SaveLineAsync(_invoiceId, _lineId, new SaveLineAllocationDto
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedVersion = version,
            Allocations = allocations.Select(a => new SaveLineAllocationItemDto { PartijId = a.PartijId, Amount = a.Amount }).ToList()
        });
    }

    [Fact]
    public async Task A_5000_Handling_Line_Over_Two_Sales_Lots_Leaves_3500_General_Costs()
    {
        await ArrangeAsync();
        var result = await SaveAsync(0, null, (_salesA, 500m), (_salesB, 1000m));
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(("Handling", 5000m, 1500m, 3500m, 1), (line.Kind, line.LineTotal, line.Allocated, line.General, line.Version));
        Assert.Equal(new[] { 500m, 1000m }, line.Allocations.OrderBy(a => a.Amount).Select(a => a.Amount));
        Assert.All(line.Allocations, a => Assert.Equal("Sales", a.PartijKind));
        Assert.Equal(new[] { "Allocated", "Allocated" }, line.History.Select(h => h.Type));

        await using var db = _s.Factory.CreateContext();
        var stored = (await db.PartijCosts.ToListAsync()).OrderBy(c => c.AmountExclVat).ToList();
        Assert.All(stored, c => Assert.Equal(("Handling", false), (c.Kind, string.IsNullOrWhiteSpace(c.CreatedBy))));
    }

    [Fact]
    public async Task More_Than_The_Line_Amount_Is_Refused_And_Nothing_Is_Saved()
    {
        await ArrangeAsync();
        var result = await SaveAsync(0, null, (_salesA, 4000m), (_salesB, 1000.01m));
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.True(result.Errors.ContainsKey("allocations"));
        Assert.Equal(ServiceResultStatus.Validation, (await SaveAsync(0, null, (_salesA, 0m))).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await SaveAsync(0, null, (_salesA, 1.005m))).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await SaveAsync(0, null, (_salesA, 1m), (_salesA, 2m))).Status);
        await using var db = _s.Factory.CreateContext();
        Assert.Equal(0, await db.PartijCosts.CountAsync());
        Assert.Equal(0, await db.PartijCostAuditEvents.CountAsync());
        // Precies het regelbedrag mag (geen algemene kosten meer).
        Assert.Equal(0m, (await SaveAsync(0, null, (_salesA, 4000m), (_salesB, 1000m))).Value.Lines[0].General);
    }

    [Fact]
    public async Task One_Line_Can_Go_To_A_Purchase_Lot_And_A_Sales_Lot()
    {
        await ArrangeAsync();
        var line = (await SaveAsync(0, null, (_purchase, 2000m), (_salesA, 500m))).Value.Lines[0];
        Assert.Equal(new[] { ("Purchase", 2000m), ("Sales", 500m) }, line.Allocations.OrderBy(a => a.PartijKind).Select(a => (a.PartijKind, a.Amount)));
    }

    [Fact]
    public async Task Reallocating_Writes_Old_And_New_And_A_Stale_Version_Is_A_Conflict()
    {
        await ArrangeAsync();
        await SaveAsync(0, null, (_salesA, 500m), (_salesB, 1000m));
        var requestId = Guid.NewGuid();
        var result = await SaveAsync(1, requestId, (_salesA, 600m), (_purchase, 900m));
        var line = result.Value.Lines[0];
        Assert.Equal((2, 1500m, 3500m), (line.Version, line.Allocated, line.General));
        var history = line.History.Skip(2).Select(h => (h.Type, h.PartijId, h.OldAmount, h.NewAmount)).OrderBy(h => h.Type).ToList();
        Assert.Equal(new[] { ("Allocated", _purchase, (decimal?)null, (decimal?)900m), ("Changed", _salesA, (decimal?)500m, (decimal?)600m), ("Removed", _salesB, (decimal?)1000m, (decimal?)null) }, history);

        // Een herhaling van dezelfde opdracht verandert niets; een oude versie is een gelijktijdige wijziging.
        Assert.Equal(ServiceResultStatus.Ok, (await SaveAsync(1, requestId, (_salesA, 600m), (_purchase, 900m))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await SaveAsync(1, null, (_salesA, 700m))).Status);
        await using var db = _s.Factory.CreateContext();
        Assert.Equal(5, await db.PartijCostAuditEvents.CountAsync());
        // Alles weghalen: alles is weer algemene kosten, de historie blijft.
        Assert.Equal(5000m, (await SaveAsync(2)).Value.Lines[0].General);
        Assert.Equal(0, await db.PartijCosts.CountAsync());
        Assert.Equal(7, await db.PartijCostAuditEvents.CountAsync());
    }

    [Fact]
    public async Task An_Invoice_In_Another_Currency_Is_Not_Allocated()
    {
        await ArrangeAsync(currency: "USD");
        var result = await SaveAsync(0, null, (_salesA, 500m));
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        await using var db = _s.Factory.CreateContext();
        Assert.False((await new PartijCostService(db).GetInvoiceAsync(_invoiceId)).Value.CanAllocate);
    }

    [Fact]
    public async Task A_Missing_Allocations_List_Is_Refused_And_Keeps_The_Existing_Costs()
    {
        await ArrangeAsync();
        await SaveAsync(0, null, (_salesA, 500m));
        await using var db = _s.Factory.CreateContext();
        var result = await new PartijCostService(db).SaveLineAsync(_invoiceId, _lineId, new SaveLineAllocationDto { RequestId = Guid.NewGuid(), ExpectedVersion = 1, Allocations = null });
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.True(result.Errors.ContainsKey("allocations"));
        Assert.Equal(1, await db.PartijCosts.CountAsync());
    }

    [Fact]
    public async Task Rule_25_Refuses_Lowering_The_Line_Amount_Below_The_Allocation()
    {
        await ArrangeAsync();
        await SaveAsync(0, null, (_salesA, 500m), (_salesB, 1000m));
        await using var db = _s.Factory.CreateContext();
        var line = await db.IncomingInvoiceLines.SingleAsync(l => l.Id == _lineId);
        line.LineTotal = 1400m;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        line = await db.IncomingInvoiceLines.SingleAsync(l => l.Id == _lineId);
        line.LineTotal = 1500m;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Rule_25_Guards_Every_Writer()
    {
        await ArrangeAsync();
        await SaveAsync(0, null, (_salesA, 500m));
        await using var db = _s.Factory.CreateContext();
        // Zonder auditregel: niet toevoegen, wijzigen of weghalen.
        db.PartijCosts.Add(new PartijCost { IncomingInvoiceLineId = _lineId, PartijId = _salesB, AmountExclVat = 10m, Kind = "Handling", CreatedBy = "x" });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var cost = await db.PartijCosts.SingleAsync();
        cost.AmountExclVat = 450m;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.PartijCosts.Remove(await db.PartijCosts.SingleAsync());
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        // Met auditregel, maar samen meer dan het regelbedrag.
        db.PartijCosts.Add(new PartijCost { IncomingInvoiceLineId = _lineId, PartijId = _salesB, AmountExclVat = 4600m, Kind = "Handling", CreatedBy = "x" });
        db.PartijCostAuditEvents.Add(new PartijCostAuditEvent { IncomingInvoiceId = _invoiceId, IncomingInvoiceLineId = _lineId, PartijId = _salesB, EventType = "Allocated", NewAmount = 4600m, RequestId = Guid.NewGuid(), ActorReference = "x" });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        // De historie is append-only, en een partij met kosten verdwijnt nooit.
        var audit = await db.PartijCostAuditEvents.FirstAsync();
        audit.NewAmount = 1m;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(ServiceResultStatus.Conflict, (await new PartijService(db).DeleteAsync(_salesA)).Status);
    }
}
