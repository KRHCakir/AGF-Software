using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Keten-strip (alleen lezen): het inkooporder-detail noemt de verkooporders die van die order reserveren of verzonden hebben,
// en een reservering op de verkooporder noemt het nummer van haar inkooporder. Niets nieuws opgeslagen; afgeleid uit de reserveringen.
public sealed class ChainLinkTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<StockReservationDto> ReserveAsync(int orderId, int lineId, int contentLineId, decimal quantity, decimal kg)
    {
        await using var db = _factory.CreateContext();
        var result = await new StockReservationService(db).ReserveAsync(orderId, lineId, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = contentLineId, Quantity = quantity, Unit = "box", NetWeightKg = kg
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    [Fact]
    public async Task Purchase_Order_Detail_Lists_The_Sales_Orders_That_Reserve_Or_Shipped_From_It_And_The_Reservation_Names_The_Purchase_Order()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, boxLine, _) = await SalesTestData.SeedOrderAsync(_factory, seed, 30m, 10m);

        var kept = await ReserveAsync(orderId, boxLine, seed.ApprovedBox, 10m, 40m);
        var released = await ReserveAsync(orderId, boxLine, seed.ApprovedBox, 5m, 20m);
        var shipped = await ReserveAsync(orderId, boxLine, seed.ApprovedBox, 3m, 12m);
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new StockReservationService(db).ReleaseAsync(released.Id, new ReleaseStockReservationDto
            {
                RequestId = Guid.NewGuid(), ExpectedReservationVersion = released.Version, Reason = "Andere partij"
            })).Status);
        }
        await using (var db = _factory.CreateContext())
        {
            var version = (await new SalesOrderService(db).GetAsync(orderId)).Value.Version;
            var dispatch = await new SalesDispatchService(db).DispatchAsync(orderId, new CreateSalesDispatchDto
            {
                RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, Reference = "VRB-1",
                Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = shipped.Id, ExpectedReservationVersion = shipped.Version } }
            });
            Assert.Equal(ServiceResultStatus.Created, dispatch.Status);
        }

        int purchaseOrderId;
        string orderNumber;
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.AsNoTracking().Include(c => c.SupplyCommitmentLine).ThenInclude(l => l.SupplyCommitment).SingleAsync(c => c.Id == seed.ApprovedBox);
            purchaseOrderId = content.SupplyCommitmentLine.SupplyCommitmentId;
            orderNumber = content.SupplyCommitmentLine.SupplyCommitment.OrderNumber;
            var detail = (await new PurchaseOrderService(db).GetAsync(purchaseOrderId)).Value;
            var link = Assert.Single(detail.SalesOrders);
            Assert.Equal((orderId, "box", 10m, 3m, "Open"), (link.SalesOrderId, link.Unit, link.ReservedQuantity, link.ShippedQuantity, link.Status));
            Assert.False(string.IsNullOrEmpty(link.CustomerName));
        }

        await using (var db = _factory.CreateContext())
        {
            var order = (await new SalesOrderService(db).GetAsync(orderId)).Value;
            var reservations = order.Lines.SelectMany(l => l.Reservations).ToList();
            Assert.All(reservations, r => Assert.Equal((purchaseOrderId, orderNumber), (r.SupplyCommitmentId, r.SupplyOrderNumber)));
            Assert.Contains(reservations, r => r.Id == kept.Id);
        }
    }

    [Fact]
    public async Task A_Purchase_Order_Nobody_Sells_From_Has_No_Sales_Links()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        await using var db = _factory.CreateContext();
        var commitmentId = (await db.PalletContentLines.AsNoTracking().Include(c => c.SupplyCommitmentLine).SingleAsync(c => c.Id == seed.ApprovedBox)).SupplyCommitmentLine.SupplyCommitmentId;
        Assert.Empty((await new PurchaseOrderService(db).GetAsync(commitmentId)).Value.SalesOrders);
    }
}
