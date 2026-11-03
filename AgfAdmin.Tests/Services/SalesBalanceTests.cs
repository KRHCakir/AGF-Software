using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Beschikbaarheid uitgebreid met de verkoopstand per variant en eenheid. Vrij te verkopen = Voorraad + Verwacht - Verkocht en mag negatief
// zijn (tekort = waarschuwing, geen blokkade).
//
// | Scenario                                                    | Voorraad | Verwacht | Verkocht | Vrij |
// |-------------------------------------------------------------|---------:|---------:|---------:|-----:|
// | begin                                                       |       40 |       20 |        0 |   60 |
// | verkoop 50                                                  |       40 |       20 |       50 |   10 |
// | verkoop 70                                                  |       40 |       20 |       70 |  -10 |
// | goedgekeurde ontvangst van de 20                            |       60 |        0 |       70 |  -10 |
// | tweede inkoop van 10                                        |       60 |       10 |       70 |    0 |
public sealed class SalesBalanceTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public void Dispose() => _s.Dispose();

    private async Task<SalesBalanceDto> BalanceAsync(int? variantId = null)
    {
        await using var db = _s.Factory.CreateContext();
        var overview = (await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery())).Value;
        return overview.Variants.Single(v => v.ProductVariantId == (variantId ?? _s.TomatoId)).WarehouseSupply.Single(r => r.Unit == "colli").Sales;
    }

    private static (decimal Stock, decimal Expected, decimal Sold, decimal Free) Numbers(SalesBalanceDto b) => (b.Stock, b.Expected, b.Sold, b.Free);

    [Fact]
    public async Task The_Owners_Practice_40_In_Stock_20_Expected_70_Sold_Runs_Back_To_Zero_With_Purchases()
    {
        await _s.ArrangeAsync();
        Assert.Equal((40m, 20m, 0m, 60m), Numbers(await BalanceAsync()));
        Assert.Equal(40m, (await BalanceAsync()).FreeStock);

        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((40m, "Stock"), (10m, "Expected")))).Status);
        var afterFifty = await BalanceAsync();
        Assert.Equal((40m, 20m, 50m, 10m), Numbers(afterFifty));
        Assert.Equal(0m, afterFifty.FreeStock);

        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((20m, "Expected")))).Status);
        var afterSeventy = await BalanceAsync();
        Assert.Equal((40m, 20m, 70m, -10m), Numbers(afterSeventy));
        Assert.Equal((true, 10m), (afterSeventy.Shortage, afterSeventy.ShortageQuantity));

        await _s.ReceiveAsync(_s.OpenOrderLineId, 20m, "Approved", "LOT-B");
        Assert.Equal((60m, 0m, 70m, -10m), Numbers(await BalanceAsync()));

        await _s.SecondPurchaseOrderAsync(10m);
        var final = await BalanceAsync();
        Assert.Equal((60m, 10m, 70m, 0m), Numbers(final));
        Assert.False(final.Shortage);
    }

    [Fact]
    public async Task Reserved_And_Unreserved_Sales_Both_Count_As_Sold_And_Dispatched_Goods_Leave_Both_Stock_And_Sold()
    {
        await _s.ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((30m, "Stock")))).Value;
        var reservation = (await _s.ReserveAsync(order.Id, order.Lines[0].Id, _s.StockContentId, 10m, 60m)).Value;
        var balance = await BalanceAsync();
        Assert.Equal((40m, 30m, 10m, 30m, 10m), (balance.Stock, balance.Sold, balance.SoldReserved, balance.Free, balance.FreeStock));

        await SalesTestData.ConsumeAsync(_s.Factory, reservation.Id);
        var shipped = await BalanceAsync();
        Assert.Equal((30m, 20m, 0m, 30m, 10m), (shipped.Stock, shipped.Sold, shipped.SoldReserved, shipped.Free, shipped.FreeStock));
    }

    [Fact]
    public async Task Only_Approved_Stock_Counts_Quarantine_Is_Expected_In_Inspection_And_Blocked_Counts_Nowhere()
    {
        await _s.ArrangeAsync();
        await _s.ReceiveAsync(_s.OpenOrderLineId, 12m, "Quarantine", "LOT-Q");
        await _s.ReceiveAsync(_s.OpenOrderLineId, 3m, "Blocked", "LOT-X");
        var balance = await BalanceAsync();
        // 20 verwacht: 12 in keuring, 3 geblokkeerd (telt nergens), 5 nog niet ontvangen.
        Assert.Equal((40m, 12m, 5m, 17m), (balance.Stock, balance.ExpectedInInspection, balance.ExpectedNotReceived, balance.Expected));
    }

    [Fact]
    public async Task Customer_Bound_Supply_Never_Counts_As_Expected()
    {
        await _s.ArrangeAsync();
        await _s.SecondPurchaseOrderAsync(100m, route: "DirectTrade", customerId: _s.CustomerId);
        Assert.Equal((40m, 20m, 0m, 60m), Numbers(await BalanceAsync()));
    }

    [Fact]
    public async Task Two_Variants_With_Almost_The_Same_Name_Are_Never_Mixed()
    {
        await _s.ArrangeAsync();
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((40m, "Stock")))).Status);
        Assert.Equal((40m, 20m, 40m, 20m), Numbers(await BalanceAsync()));
        Assert.Equal((15m, 0m, 0m, 15m), Numbers(await BalanceAsync(_s.LookalikeId)));
    }

    [Fact]
    public async Task A_Variant_That_Is_Only_Sold_Gets_A_Row_With_Its_Shortage()
    {
        await _s.ArrangeAsync();
        await using (var db = _s.Factory.CreateContext())
        {
            db.ProductVariants.Add(new AgfAdmin.Models.ProductVariant { ProductId = (await db.ProductVariants.FindAsync(_s.TomatoId))!.ProductId, Name = "Cherry", Origin = "NL", Packaging = "Bak", DefaultPrice = 1m });
            await db.SaveChangesAsync();
        }
        int cherry;
        await using (var db = _s.Factory.CreateContext()) cherry = db.ProductVariants.Single(v => v.Name == "Cherry").Id;
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order(cherry, (8m, "Expected")))).Status);
        var balance = await BalanceAsync(cherry);
        Assert.Equal((0m, 0m, 8m, -8m, true), (balance.Stock, balance.Expected, balance.Sold, balance.Free, balance.Shortage));
    }

    [Fact]
    public async Task The_Drill_Down_Splits_Per_Sales_Order_And_Per_Purchase_Order()
    {
        await _s.ArrangeAsync();
        var order = (await _s.CreateAsync(_s.Order((40m, "Stock"), (30m, "Expected")))).Value;
        await using var db = _s.Factory.CreateContext();
        var drill = (await new AvailabilityService(db).GetVariantAsync(_s.TomatoId, new AvailabilityQuery())).Value;
        Assert.Equal(-10m, Assert.Single(drill.Sales).Free);
        Assert.Equal(new[] { ("Stock", 40m), ("Expected", 30m) }, drill.SalesLines.Select(l => (l.SupplySource, l.OpenQuantity)));
        Assert.All(drill.SalesLines, l => Assert.Equal((order.Id, order.OrderNumber), (l.SalesOrderId, l.OrderNumber)));
        // Oudste verkoop eerst: de regel uit voorraad neemt de 40, de verwachte regel de 20 van de inkooporder en 10 tekort.
        Assert.Equal((40m, 0m), (drill.SalesLines[0].Coverage.FromStock, drill.SalesLines[0].Coverage.Shortage));
        Assert.Equal((20m, 10m), (drill.SalesLines[1].Coverage.Expected.Sum(e => e.Quantity), drill.SalesLines[1].Coverage.Shortage));
        var supply = Assert.Single(drill.ExpectedSupplies);
        Assert.Equal((20m, 0m), (supply.NotReceived, supply.InInspection));
        Assert.StartsWith("IO-", supply.SupplyOrderNumber);
        Assert.Contains(drill.SourceLines, l => l.SupplyOrderNumber == supply.SupplyOrderNumber);
    }

    [Fact]
    public async Task With_Route_DirectTrade_There_Is_No_Sales_Balance_In_The_Overview_Or_The_Drill_Down()
    {
        await _s.ArrangeAsync();
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(_s.Order((10m, "Stock")))).Status);
        await using var db = _s.Factory.CreateContext();
        var service = new AvailabilityService(db);
        var overview = (await service.GetOverviewAsync(new AvailabilityQuery { Route = "DirectTrade" })).Value;
        Assert.All(overview.Variants.SelectMany(v => v.WarehouseSupply), row => Assert.Null(row.Sales));
        var drill = (await service.GetVariantAsync(_s.TomatoId, new AvailabilityQuery { Route = "DirectTrade" })).Value;
        Assert.Empty(drill.Sales);
        Assert.Empty(drill.SalesLines);
        Assert.Empty(drill.ExpectedSupplies);
    }

    [Fact]
    public void Coverage_Takes_Stock_Lines_First_Then_Inspection_Then_The_Earliest_Purchase_Order()
    {
        var early = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
        var sources = new List<SourceLineFact>
        {
            new(1, 1, "Warehouse", null, null, "Expected", early.AddDays(4), 7, "Tomaat", "Ronde", null, "colli", 10m, null, "IO-2026-0009"),
            new(2, 2, "Warehouse", null, null, "Expected", early, 7, "Tomaat", "Ronde", null, "colli", 10m, null, "IO-2026-0002"),
            new(3, 3, "Warehouse", null, null, "Received", early.AddDays(-5), 7, "Tomaat", "Ronde", null, "colli", 4m, null, "IO-2026-0001")
        };
        var contents = new List<ContentLineFact>
        {
            new(1, 1, "P1", "Received", "Warehouse", null, 3, "Warehouse", null, null, 7, "Tomaat", "Ronde", "L1", "colli", 4m, 24m, "Quarantine")
        };
        var lines = new List<SalesLineFact>
        {
            new(20, 2, "VO-2026-0002", 1, "K", null, early.AddDays(-1), early, 7, "Tomaat", "Ronde", "colli", "Expected", 20m),
            new(10, 1, "VO-2026-0001", 1, "K", null, early.AddDays(-2), early, 7, "Tomaat", "Ronde", "colli", "Expected", 6m)
        };
        var balance = SalesBalanceCalculator.Build(sources, contents, lines)[(7, "colli")];
        Assert.Equal((0m, 24m, 26m, -2m), (balance.Stock, balance.Expected, balance.Sold, balance.Free));
        var first = balance.Coverage[10];
        Assert.Equal(new[] { ("IO-2026-0001", 4m, true), ("IO-2026-0002", 2m, false) }, first.Expected.Select(e => (e.OrderNumber, e.Quantity, e.InInspection)));
        var second = balance.Coverage[20];
        Assert.Equal(new[] { ("IO-2026-0002", 8m), ("IO-2026-0009", 10m) }, second.Expected.Select(e => (e.OrderNumber, e.Quantity)));
        Assert.Equal(2m, second.Shortage);
    }
}
