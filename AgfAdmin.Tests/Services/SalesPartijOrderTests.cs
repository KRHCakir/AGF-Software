using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Migrations;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Partij P2: de verkoop-partij op de verkooporder (voorstel of zelf getypt, verplicht in modus Own), de replay, het nummer aanpasbaar tot er
// iets gereserveerd is en daarna alleen met reden, zoeken op het nummer en per verkoop-partij de inkoop-partijen waar de colli vandaan komen.
public sealed class SalesPartijOrderTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public SalesPartijOrderTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 5));

    public void Dispose() => _s.Dispose();

    private async Task SettingsAsync(string format = "5{JJ}{MM}{volgnr}", string salesFormat = null, int? digits = null, string mode = "Auto", string reset = "Yearly", int? salesDigits = null)
    {
        await using var db = _s.Factory.CreateContext();
        var result = await new PartijService(db).UpdateSettingsAsync(new PartijSettingsDto
        {
            Mode = mode, Format = format, SequenceReset = reset, SalesFormat = salesFormat, SequenceDigits = digits, SalesSequenceDigits = salesDigits
        });
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
    }

    private async Task<ServiceResult<PartijSummaryDto>> PurchaseAsync(string number = null)
    {
        await using var db = _s.Factory.CreateContext();
        return await new PartijService(db).CreateAsync(new CreatePartijDto { RequestId = Guid.NewGuid(), Number = number });
    }

    private async Task<ServiceResult<SalesOrderDto>> SaleAsync(string partijNumber = null, Guid? requestId = null)
    {
        var request = _s.Order((5m, "Stock"));
        request.SalesPartijNumber = partijNumber;
        if (requestId != null) request.RequestId = requestId;
        return await _s.CreateAsync(request);
    }

    private async Task<ServiceResult<PartijSummaryDto>> CorrectAsync(int partijId, string number, string reason = null)
    {
        await using var db = _s.Factory.CreateContext();
        return await new PartijService(db).CorrectNumberAsync(partijId, new CorrectPartijNumberDto { Number = number, Reason = reason });
    }

    [Fact]
    public async Task The_Duplicate_Check_Uses_The_Key_Within_One_Series()
    {
        await _s.ArrangeAsync();
        await SettingsAsync();
        Assert.Equal(ServiceResultStatus.Created, (await PurchaseAsync("5261145")).Status);
        var duplicate = await PurchaseAsync("5261-145");
        Assert.Equal(ServiceResultStatus.Conflict, duplicate.Status);
        Assert.Equal("5261145", ((PartijExistsConflictDto)duplicate.ConflictBody).Partij.Number);

        // Een verkoop-partij is een andere reeks: hetzelfde nummer mag daar één keer.
        Assert.Equal("5261145", (await SaleAsync("5261 145")).Value.SalesPartijNumber.Replace(" ", ""));
        var twice = await SaleAsync("5261-145");
        Assert.Equal(ServiceResultStatus.Conflict, twice.Status);
        await using var db = _s.Factory.CreateContext();
        Assert.Equal(1, await db.SalesOrders.CountAsync());
        Assert.NotNull(await new PartijService(db).FindByNumberAsync("5261145", "Sales"));
        Assert.NotNull(await new PartijService(db).FindByNumberAsync("5261145"));
    }

    [Fact]
    public async Task Own_Mode_Needs_A_Typed_Sales_Lot_And_A_Replay_Compares_The_Requested_Number()
    {
        await _s.ArrangeAsync();
        await SettingsAsync(mode: "Own");
        var missing = await SaleAsync();
        Assert.Equal(ServiceResultStatus.Validation, missing.Status);
        Assert.True(missing.Errors.ContainsKey("salesPartijNumber"));

        var requestId = Guid.NewGuid();
        Assert.Equal("261140", (await SaleAsync("261140", requestId)).Value.SalesPartijNumber);
        Assert.Equal(ServiceResultStatus.Ok, (await SaleAsync("2611-40", requestId)).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await SaleAsync("261141", requestId)).Status);
    }

    [Fact]
    public async Task The_Number_Can_Be_Changed_Until_Something_Is_Reserved_Then_Only_With_A_Reason()
    {
        await _s.ArrangeAsync();
        var order = (await SaleAsync()).Value;
        var partijId = order.SalesPartijId.Value;

        // Nog niets gereserveerd: aanpassen zonder reden, met historie (NumberChanged).
        await using (var before = _s.Factory.CreateContext())
        {
            Assert.False((await new PartijService(before).GetAsync(partijId)).Value.NumberFixed);
        }
        var changed = await CorrectAsync(partijId, "261140");
        Assert.Equal(ServiceResultStatus.Ok, changed.Status);

        Assert.Equal(ServiceResultStatus.Created, (await _s.ReserveAsync(order.Id, order.Lines[0].Id, _s.StockContentId, 5m, 30m)).Status);
        var fixedNow = await CorrectAsync(partijId, "261141");
        Assert.Equal(ServiceResultStatus.Validation, fixedNow.Status);
        Assert.True(fixedNow.Errors.ContainsKey("reason"));
        Assert.Equal(ServiceResultStatus.Ok, (await CorrectAsync(partijId, "261141", "Verkeerd overgenomen van de bon")).Status);

        await using var db = _s.Factory.CreateContext();
        var events = await db.PartijAuditEvents.Where(e => e.PartijId == partijId).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(new[] { ("NumberChanged", "2611001", "261140", (string)null), ("NumberCorrected", "261140", "261141", "Verkeerd overgenomen van de bon") },
            events.Select(e => (e.EventType, e.OldValue, e.NewValue, e.Reason)));
        var read = (await new SalesOrderService(db).GetAsync(order.Id)).Value;
        Assert.Equal(("261141", true), (read.SalesPartijNumber, read.SalesPartijFixed));
        Assert.True((await new PartijService(db).GetAsync(partijId)).Value.NumberFixed);

        // Regel 24 buiten de service om: na reserveren geen aanpassing zonder reden, en een inkoop-partij nooit zonder reden.
        var partij = await db.Partijen.SingleAsync(p => p.Id == partijId);
        db.PartijAuditEvents.Add(new PartijAuditEvent { PartijId = partijId, EventType = "NumberChanged", OldValue = "261141", NewValue = "261142", ActorReference = "x" });
        partij.Number = "261142";
        partij.NumberKey = "261142";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var purchaseId = (await PurchaseAsync()).Value.Id;
        var purchase = await db.Partijen.SingleAsync(p => p.Id == purchaseId);
        db.PartijAuditEvents.Add(new PartijAuditEvent { PartijId = purchaseId, EventType = "NumberChanged", OldValue = purchase.Number, NewValue = "X1", ActorReference = "x" });
        purchase.Number = "X1";
        purchase.NumberKey = "X1";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_Order_List_Finds_The_Sales_Lot_Without_Spaces_Or_Hyphens()
    {
        await _s.ArrangeAsync();
        var order = (await SaleAsync("261140")).Value;
        await using var db = _s.Factory.CreateContext();
        Assert.Contains((await new SalesOrderService(db).ListAsync(new SalesOrderFilter { Q = "2611-40" })).Value, o => o.Id == order.Id);
        Assert.DoesNotContain((await new SalesOrderService(db).ListAsync(new SalesOrderFilter { Q = "261199" })).Value, o => o.Id == order.Id);
    }
}
