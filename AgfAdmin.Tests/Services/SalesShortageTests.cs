using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Verkooporder-controle: een regel "Uit voorraad" past in de vrije voorraad (anders 409 met de splitsing in de reden); een tekort op
// Voorraad + Verwacht wordt toegestaan met een waarschuwing en de indicator Tekort; per regel de dekking, oudste verkoop eerst.
public sealed class SalesShortageTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public void Dispose() => _s.Dispose();

    [Theory]
    [InlineData(50, 40, 40, 10)]
    [InlineData(30, 40, 30, 0)]
    [InlineData(5, 0, 0, 5)]
    [InlineData(5, -3, 0, 5)]
    [InlineData(0, 40, 0, 0)]
    public void The_Proposal_Takes_What_Is_Free_In_Stock_And_The_Rest_From_Expected_Supply(decimal quantity, decimal freeStock, decimal stock, decimal expected) =>
        Assert.Equal((stock, expected), SalesSupplyRules.Propose(quantity, freeStock));

    [Fact]
    public async Task A_Line_From_Stock_Above_The_Free_Stock_Is_409_With_The_Split_In_The_Reason_And_Nothing_Is_Saved()
    {
        await _s.ArrangeAsync();
        var result = await _s.CreateAsync(_s.Order((50m, "Stock")));
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("only 40 colli", result.Message);
        Assert.Contains("40 colli from stock, 10 colli from expected supply", result.Message);
        await using var db = _s.Factory.CreateContext();
        Assert.Equal(0, await db.SalesOrders.CountAsync());

        // Reserveringen en niet-gereserveerde verkoop uit voorraad tellen allebei: na 30 uit voorraad is er nog 10 vrij op voorraad.
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((30m, "Stock")))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(_s.Order((11m, "Stock")))).Status);
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((10m, "Stock")))).Status);
    }

    [Fact]
    public async Task Only_Approved_Stock_Is_Free_To_Sell_From_Stock()
    {
        await _s.ArrangeAsync();
        await _s.ReceiveAsync(_s.OpenOrderLineId, 12m, "Quarantine", "LOT-Q");
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(_s.Order((41m, "Stock")))).Status);
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((40m, "Stock")))).Status);
    }

    [Fact]
    public async Task Selling_70_Is_Allowed_With_A_Warning_Of_10_And_The_Newest_Order_Carries_The_Shortage()
    {
        await _s.ArrangeAsync();
        var first = await _s.CreateAsync(_s.Order((40m, "Stock"), (10m, "Expected")));
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        Assert.Empty(first.Value.ShortageWarnings);
        Assert.Equal((false, true), (first.Value.Shortage, first.Value.AwaitingStock));
        var expectedLine = first.Value.Lines.Single(l => l.SupplySource == "Expected");
        Assert.Equal((40m, 20m, 10m), (expectedLine.Balance.Stock, expectedLine.Balance.Expected, expectedLine.Balance.Free));

        var second = await _s.CreateAsync(_s.Order((20m, "Expected")));
        Assert.Equal(ServiceResultStatus.Created, second.Status);
        var warning = Assert.Single(second.Value.ShortageWarnings);
        Assert.Equal((_s.TomatoId, "colli", 10m), (warning.ProductVariantId, warning.Unit, warning.ShortageQuantity));
        Assert.True(second.Value.Shortage);

        // De oudste verkoop is gedekt: 40 nu te reserveren uit voorraad, 10 op inkooporder 2. De tweede order heeft 10 gedekt en 10 tekort.
        await using (var db = _s.Factory.CreateContext())
        {
            var older = (await new SalesOrderService(db).GetAsync(first.Value.Id)).Value;
            Assert.False(older.Shortage);
            var cover = Assert.Single(older.Lines.Single(l => l.SupplySource == "Expected").Coverage.Expected);
            Assert.Equal((10m, false), (cover.Quantity, cover.InInspection));
            Assert.StartsWith("IO-", cover.SupplyOrderNumber);
            Assert.Equal(40m, older.Lines.Single(l => l.SupplySource == "Stock").Coverage.FromStock);
            var newer = (await new SalesOrderService(db).GetAsync(second.Value.Id)).Value;
            Assert.Equal((10m, 10m), (newer.Lines[0].Coverage.Expected.Sum(e => e.Quantity), newer.Lines[0].Coverage.Shortage));
            var list = (await new SalesOrderService(db).ListAsync(new SalesOrderFilter())).Value;
            Assert.Equal(new[] { (second.Value.Id, true, true), (first.Value.Id, false, true) }, list.Select(o => (o.Id, o.Shortage, o.AwaitingStock)));
        }

        // Een tweede inkoop van 10 dekt het tekort: de indicator verdwijnt vanzelf.
        await _s.SecondPurchaseOrderAsync(10m);
        await using (var db = _s.Factory.CreateContext())
        {
            Assert.False((await new SalesOrderService(db).GetAsync(second.Value.Id)).Value.Shortage);
        }
    }

    [Fact]
    public async Task Without_A_Source_A_Line_Is_From_Stock_Only_When_It_Fits_And_Is_Never_Split_Silently()
    {
        await _s.ArrangeAsync();
        var fits = await _s.CreateAsync(_s.Order((40m, null)));
        Assert.Equal("Stock", Assert.Single(fits.Value.Lines).SupplySource);
        var noFreeStock = await _s.CreateAsync(_s.Order((5m, null)));
        Assert.Equal("Expected", Assert.Single(noFreeStock.Value.Lines).SupplySource);
    }

    [Fact]
    public async Task A_Cancelled_Order_Has_No_Balance_And_No_Longer_Counts_As_Sold()
    {
        await _s.ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((40m, "Stock")))).Value;
        await using (var db = _s.Factory.CreateContext())
        {
            var cancelled = await new SalesOrderService(db).CancelAsync(order.Id, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = order.Version, Reason = "Klant zegt af" });
            Assert.Equal(ServiceResultStatus.Ok, cancelled.Status);
        }
        await using (var db = _s.Factory.CreateContext())
        {
            var line = (await new SalesOrderService(db).GetAsync(order.Id)).Value.Lines[0];
            Assert.Null(line.Balance);
            Assert.Null(line.Coverage);
        }
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((40m, "Stock")))).Status);
    }

    [Fact]
    public async Task Stock_Of_A_Variant_With_Almost_The_Same_Name_Never_Makes_Room_To_Sell_From_Stock()
    {
        await _s.ArrangeAsync();
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((40m, "Stock")))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(_s.Order((1m, "Stock")))).Status);
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order(_s.LookalikeId, (15m, "Stock")))).Status);
    }
}
