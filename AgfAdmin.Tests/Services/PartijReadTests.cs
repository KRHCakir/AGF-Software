using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Partijlijst en -detail: twee inkooporders (twee leveranciers) op één partij, ontvangst, keuring, verkoop; Uitverkocht afgeleid.
// En het partijnummer in de inkooporder, de beschikbaarheid en de reservering van de verkooporder.
public class PartijReadTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new() { Calendar = new FixedBusinessCalendar(new DateOnly(2026, 10, 27)) };

    public void Dispose() => _factory.Dispose();

    private sealed record Scenario(PurchaseOrderSeed Seed, SupplyCommitmentDto First, SupplyCommitmentDto Second, int Approved, int Quarantine, int SalesOrderId);

    // Order 1 (Nature's Pride, 100 box) en order 2 (Fruitimport Oost, 50 box) op partij 2610251; 60 ontvangen en goedgekeurd, 10 in keuring
    // op order 2, en een verkooporder met 15 gereserveerd.
    private async Task<Scenario> ArrangeAsync()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(seed, quantity: 100m);
        request.NewPartij = new NewPartijForOrderDto { Number = "2610251" };
        request.SupplierLotReference = "NP-LOT-88";
        var first = await PurchaseOrderTestData.CreateOrderAsync(_factory, request);
        var second = PurchaseOrderTestData.OrderRequest(seed, quantity: 50m, vendorId: seed.OtherVendorId);
        second.PartijId = first.PartijId;
        var secondOrder = await PurchaseOrderTestData.CreateOrderAsync(_factory, second);
        var approved = await PurchaseOrderTestData.ReceiveAsync(_factory, first.Id, 60m, 240m);
        var quarantine = await PurchaseOrderTestData.ReceiveAsync(_factory, secondOrder.Id, 10m, 40m, quality: "Quarantine");

        await using var db = _factory.CreateContext();
        var customer = new Customer { Name = "Groothandel Jansen" };
        var order = new SalesOrder { RequestId = Guid.NewGuid(), Customer = customer, Status = "Open", RequestedDeliveryDate = new DateTime(2026, 10, 28, 6, 0, 0, DateTimeKind.Utc), ActorReference = "Admin" };
        var line = new SalesOrderLine { SalesOrder = order, ProductVariantId = seed.HassId, Quantity = 15m, Unit = "box" };
        db.AddRange(customer, order, line);
        await db.SaveChangesAsync();
        db.StockReservations.Add(new StockReservation
        {
            RequestId = Guid.NewGuid(), SalesOrderLineId = line.Id, PalletContentLineId = approved, SupplyCommitmentLineId = first.Lines[0].Id,
            Quantity = 15m, Unit = "box", NetWeightKg = 60m, Source = "Physical", Status = "Active"
        });
        await db.SaveChangesAsync();
        return new Scenario(seed, first, secondOrder, approved, quarantine, order.Id);
    }

    private async Task<PartijListDto> ListAsync(string query = null, string status = null)
    {
        await using var db = _factory.CreateContext();
        var result = await new PartijService(db).ListAsync(query, status, 1, 50);
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        return result.Value;
    }

    [Fact]
    public async Task List_Shows_Vendors_Products_And_Quantities_Per_Lot()
    {
        await ArrangeAsync();
        var item = (await ListAsync()).Items.Single();

        Assert.Equal("2610251", item.Number);
        Assert.Equal("Open", item.Status);
        Assert.Equal(new[] { "Fruitimport Oost", "Nature's Pride" }, item.Vendors);
        Assert.Equal(new[] { "Avocado Hass" }, item.Products);
        Assert.Equal(new[] { "NP-LOT-88" }, item.SupplierLotReferences);
        Assert.Equal("box", item.Unit);
        Assert.Equal((150m, 70m, 60m, 10m, 80m, 15m),
            (item.PurchasedQuantity, item.ReceivedQuantity, item.OnHandQuantity, item.InInspectionQuantity, item.OpenExpectedQuantity, item.SoldQuantity));
    }

    [Theory]
    [InlineData("2610 251")]
    [InlineData("2610-25")]
    [InlineData("np-lot")]
    [InlineData("hass")]
    [InlineData("fruitimport")]
    public async Task List_Searches_Number_Supplier_Lot_Product_And_Vendor(string query)
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            db.Partijen.Add(new Partij { Number = "2610300", NumberKey = "2610300", CreatedBy = "Test" });
            await db.SaveChangesAsync();
        }
        Assert.Equal(new[] { "2610251" }, (await ListAsync(query)).Items.Select(i => i.Number));
    }

    [Fact]
    public async Task Detail_Shows_Orders_Receipts_Pallets_Sales_And_History()
    {
        var s = await ArrangeAsync();
        await using var db = _factory.CreateContext();
        var detail = (await new PartijService(db).GetAsync(s.First.PartijId.Value)).Value;

        Assert.True(detail.Linked);
        Assert.Equal(new[] { s.First.OrderNumber, s.Second.OrderNumber }, detail.Orders.Select(o => o.OrderNumber));
        Assert.Equal(("PartiallyReceived", 100m, 60m), (detail.Orders[0].Status, detail.Orders[0].ExpectedQuantity, detail.Orders[0].ReceivedQuantity));
        Assert.Equal(new[] { "Approved", "Quarantine" }, detail.Receipts.Select(r => r.QualityStatus));
        Assert.All(detail.Receipts, r => Assert.Equal("Receipt", r.Kind));
        var sale = detail.SalesOrders.Single();
        Assert.Equal((s.SalesOrderId, "Groothandel Jansen", 15m, 0m), (sale.SalesOrderId, sale.CustomerName, sale.ReservedQuantity, sale.ShippedQuantity));
        Assert.Equal("Created", detail.History.Single().Type);
    }

    [Fact]
    public async Task Sold_Out_Is_Derived_When_Nothing_Is_In_Stock_Inspection_Or_Expected()
    {
        var s = await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            // Order 2 in keuring afgekeurd en beide orders afgesloten (niets meer verwacht): de goedgekeurde voorraad houdt de partij open.
            (await db.PalletContentLines.SingleAsync(c => c.Id == s.Quarantine)).QualityStatus = "Rejected";
            (await db.SupplyCommitments.SingleAsync(c => c.Id == s.Second.Id)).Status = "Received";
            (await db.SupplyCommitments.SingleAsync(c => c.Id == s.First.Id)).Status = "Received";
            await db.SaveChangesAsync();
        }
        Assert.Equal("Open", (await ListAsync()).Items.Single().Status);
        await using (var db = _factory.CreateContext())
        {
            var reservation = await db.StockReservations.SingleAsync();
            reservation.Status = "Released";
            reservation.ReleasedAt = DateTime.UtcNow;
            reservation.ReleaseReason = "Klant annuleert";
            await db.SaveChangesAsync();
            (await db.PalletContentLines.SingleAsync(c => c.Id == s.Approved)).QualityStatus = "Rejected";
            await db.SaveChangesAsync();
        }
        Assert.Equal("SoldOut", (await ListAsync()).Items.Single().Status);
        Assert.Single((await ListAsync(status: "SoldOut")).Items);
        Assert.Empty((await ListAsync(status: "Open")).Items);
    }

    [Fact]
    public async Task Purchase_Order_Availability_And_Reservation_Carry_The_Lot()
    {
        var s = await ArrangeAsync();
        await using var db = _factory.CreateContext();
        var order = (await new PurchaseOrderService(db).GetAsync(s.First.Id)).Value;
        Assert.Equal((s.First.PartijId, "2610251", "NP-LOT-88"), (order.PartijId, order.PartijNumber, order.SupplierLotReference));
        Assert.All(order.Receipts, r => Assert.Equal("2610251", r.PartijNumber));
        Assert.All(order.Lines, l => Assert.Equal((s.First.PartijId, "2610251"), (l.PartijId, l.PartijNumber)));
        Assert.Equal(2, (await new PurchaseOrderService(db).ListAsync("2610 251", "all", null, 1, 50)).Value.Total);

        var drill = (await new AvailabilityService(db).GetVariantAsync(s.Seed.HassId, new AvailabilityQuery())).Value;
        Assert.All(drill.ContentLines, c => Assert.Equal((s.First.PartijId, "2610251"), (c.PartijId, c.PartijNumber)));
        Assert.All(drill.SourceLines, l => Assert.Equal("2610251", l.PartijNumber));

        var sales = (await new SalesOrderService(db).GetAsync(s.SalesOrderId)).Value;
        var reservation = sales.Lines.Single().Reservations.Single();
        Assert.Equal((s.First.PartijId, "2610251"), (reservation.PartijId, reservation.PartijNumber));
    }
}
