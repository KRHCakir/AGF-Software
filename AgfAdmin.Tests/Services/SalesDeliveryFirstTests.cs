using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: elke verkooporder heeft een eerste levering (volgnummer 1) met de verkoop-partij van de order. Reserveringen zonder
// levering horen bij die eerste levering; de laadbon hoort bij die levering. Zo verandert er voor een order met één levering niets.
public sealed class SalesDeliveryFirstTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public SalesDeliveryFirstTests() => _factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 23));

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task A_New_Order_Has_One_First_Delivery_With_Its_V_Number_And_The_Loading_Slip_Belongs_To_It()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, box, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        await using (var db = _factory.CreateContext())
        {
            var reserved = await new StockReservationService(db).ReserveAsync(orderId, box, new CreateStockReservationDto
            {
                RequestId = Guid.NewGuid(), PalletContentLineId = seed.ApprovedBox, Quantity = 30m, Unit = "box", NetWeightKg = 120m
            });
            Assert.Equal(ServiceResultStatus.Created, reserved.Status);
            Assert.Null(reserved.Value.SalesDeliveryId);
            Assert.Equal(ServiceResultStatus.Ok, (await new LoadingSlipService(db).ConfirmAsync(orderId, new ConfirmSalesOrderDto())).Status);
        }

        await using var verify = _factory.CreateContext();
        var order = (await new SalesOrderService(verify).GetAsync(orderId)).Value;
        var delivery = Assert.Single(order.Deliveries);
        Assert.Equal((1, order.SalesPartijId!.Value, order.SalesPartijNumber, true), (delivery.Sequence, delivery.PartijId, delivery.PartijNumber, delivery.Last));
        Assert.StartsWith("V-", delivery.PartijNumber);
        Assert.Equal(order.Lines.SelectMany(l => l.Reservations).Select(r => r.Id), delivery.ReservationIds);
        var slip = await verify.SalesOrderDocuments.AsNoTracking().SingleAsync(d => d.SalesOrderId == orderId);
        Assert.Equal(delivery.Id, slip.SalesDeliveryId);
        Assert.Equal(delivery.PartijNumber, LoadingSlipBuilder.Deserialize(slip.SnapshotJson).Position);
        // Zoeken op het V-nummer vindt de order.
        var found = (await new SalesOrderService(verify).ListAsync(new SalesOrderFilter { Q = delivery.PartijNumber.Replace("V-", "") })).Value;
        Assert.Contains(found, o => o.Id == orderId);
    }
}
