using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Verkooporder krijgt een eigen nummer VO-jjjj-nnnn (zoals IO-nummers), elke regel een bron (Uit voorraad of Uit verwachte aanvoer) en de
// lijst is doorzoekbaar op VO-nummer, klantreferentie, klant, artikel en partij.
public sealed class SalesOrderNumberTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task Sales_Orders_Get_Consecutive_Numbers_Per_Year_That_Never_Change()
    {
        await _s.ArrangeAsync();
        var first = (await _s.CreateAsync(_s.Order((1m, "Stock")))).Value;
        var second = (await _s.CreateAsync(_s.Order((1m, "Stock")))).Value;
        Assert.Equal(("VO-2026-0001", "VO-2026-0002"), (first.OrderNumber, second.OrderNumber));

        // Guard-regel 22: een opgeslagen nummer (tekst, jaar of volgnummer) verandert nooit, ook niet buiten de service om.
        await using var db = _s.Factory.CreateContext();
        var order = await db.SalesOrders.SingleAsync(o => o.Id == first.Id);
        order.OrderNumber = "VO-2026-0099";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var stored = await db.SalesOrders.SingleAsync(o => o.Id == first.Id);
        stored.OrderSequence = 7;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_New_Order_With_A_Number_That_Does_Not_Match_Its_Sequence_Is_Refused()
    {
        await _s.ArrangeAsync();
        await using var db = _s.Factory.CreateContext();
        db.SalesOrders.Add(new AgfAdmin.Models.SalesOrder
        {
            RequestId = Guid.NewGuid(), CustomerId = _s.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "x",
            OrderNumber = "VO-2026-0005", OrderYear = 2026, OrderSequence = 4
        });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void Existing_Orders_Are_Numbered_In_Order_Of_Creation_Per_Amsterdam_Year_Without_Gaps()
    {
        var numbers = SalesSupplyRules.InitialNumbers(new[]
        {
            (Id: 3, CreatedAt: new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)),
            (Id: 1, CreatedAt: new DateTime(2026, 3, 1, 9, 0, 0, DateTimeKind.Utc)),
            (Id: 2, CreatedAt: new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc)), // al 2026 in Amsterdam
            (Id: 4, CreatedAt: new DateTime(2025, 6, 1, 9, 0, 0, DateTimeKind.Utc))
        }).OrderBy(n => n.Id).Select(n => (n.Id, n.OrderNumber)).ToList();
        Assert.Equal(new[] { (1, "VO-2026-0002"), (2, "VO-2026-0001"), (3, "VO-2026-0003"), (4, "VO-2025-0001") }, numbers);
        Assert.Equal("VO-2026-10000", SalesSupplyRules.FormatNumber(2026, 10000));
    }

    [Fact]
    public async Task A_Split_Line_Is_Two_Lines_Of_One_Variant_And_Unit_And_A_Repeated_Or_Unknown_Source_Is_Refused()
    {
        await _s.ArrangeAsync();
        var split = await _s.CreateAsync(_s.Order((40m, "Stock"), (30m, "Expected")));
        Assert.Equal(ServiceResultStatus.Created, split.Status);
        Assert.Equal(new[] { ("Stock", 40m), ("Expected", 30m) }, split.Value.Lines.Select(l => (l.SupplySource, l.Quantity)));

        Assert.Equal(ServiceResultStatus.Validation, (await _s.CreateAsync(_s.Order((5m, "Expected"), (5m, "Expected")))).Status);
        var unknown = await _s.CreateAsync(_s.Order((5m, "Later")));
        Assert.Equal(ServiceResultStatus.Validation, unknown.Status);
        Assert.True(unknown.Errors.ContainsKey("lines[0].supplySource"));
    }

    [Fact]
    public async Task A_Replay_Without_A_Source_Matches_The_Stored_Source_And_A_Different_Source_Is_409()
    {
        await _s.ArrangeAsync();
        var request = _s.Order((5m, "Expected"));
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(request)).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await _s.CreateAsync(request)).Status);
        request.Lines[0].SupplySource = null;
        Assert.Equal(ServiceResultStatus.Ok, (await _s.CreateAsync(request)).Status);
        request.Lines[0].SupplySource = "Stock";
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(request)).Status);
    }

    [Theory]
    [InlineData("vo-2026-0001", true)]
    [InlineData("po-778", true)]
    [InlineData("groothandel", true)]
    [InlineData("ronde 67", true)]
    [InlineData("lot-a", true)]
    [InlineData("onbekend", false)]
    public async Task The_List_Searches_Number_Customer_Reference_Customer_Article_And_Lot(string q, bool found)
    {
        await _s.ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((10m, "Stock")))).Value;
        Assert.Equal(ServiceResultStatus.Created, (await _s.ReserveAsync(order.Id, order.Lines[0].Id, _s.StockContentId, 10m, 60m)).Status);
        await using var read = _s.Factory.CreateContext();
        var list = (await new SalesOrderService(read).ListAsync(new SalesOrderFilter { Q = q })).Value;
        Assert.Equal(found, list.Any(o => o.Id == order.Id));
    }
}
