using AgfAdmin.Models;
using AgfAdmin.Services;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: laadbon en transportopdracht per levering, gebouwd uit de order zoals die levering haar ziet. Een order met één
// levering geeft precies dezelfde bon (dezelfde vingerafdruk, dus geen nieuwe versie); bij twee leveringen staat elke reservering op haar
// eigen bon en wat nog open is op de laatste.
public sealed class SalesDeliveryViewTests
{
    private static (SalesOrder Order, SalesDelivery First, StockReservation A, StockReservation B) Order()
    {
        var variant = new ProductVariant { Id = 3, Name = "Hass", Packaging = "4kg Box", Product = new Product { Name = "Avocado" }, DefaultPackagesPerPallet = 100m };
        var lot1 = new Partij { Id = 11, Kind = PartijRules.KindSales, Number = "V-2611001" };
        var first = new SalesDelivery { Id = 1, Sequence = 1, PartijId = 11, Partij = lot1 };
        var a = new StockReservation { Id = 101, Status = ReservationRules.StatusActive, Quantity = 30m, NetWeightKg = 120m, PalletContentLine = new PalletContentLine { Pallet = new Pallet { Reference = "P-1" } } };
        var b = new StockReservation { Id = 102, Status = ReservationRules.StatusActive, Quantity = 15m, NetWeightKg = 60m, PalletContentLine = new PalletContentLine { Pallet = new Pallet { Reference = "P-2" } } };
        var released = new StockReservation { Id = 103, Status = ReservationRules.StatusReleased, Quantity = 5m };
        var line = new SalesOrderLine { Id = 7, ProductVariant = variant, Quantity = 50m, Unit = "box", NetWeightKg = 200m, Reservations = new List<StockReservation> { a, b, released } };
        var order = new SalesOrder
        {
            Id = 5, OrderNumber = "VO-2026-0005", Customer = new Customer { Name = "Groothandel Noord" }, ConfirmedAt = new DateTime(2026, 11, 23, 8, 0, 0, DateTimeKind.Utc),
            LoadAt = new DateTime(2026, 11, 24, 6, 0, 0, DateTimeKind.Utc), LicensePlate = "AB-12-CD",
            Lines = new List<SalesOrderLine> { line },
            PackagingLines = new List<SalesOrderPackagingLine> { new() { Id = 1, Quantity = 2, CostArticle = new CostArticle { Name = "Euro-pallet" } } },
            Deliveries = new List<SalesDelivery> { first },
        };
        return (order, first, a, b);
    }

    private static readonly CompanySettings Settings = new() { CompanyName = "Kwekerij Voorbeeld B.V." };

    [Fact]
    public void With_One_Delivery_The_View_Gives_Exactly_The_Same_Loading_Slip_As_The_Order()
    {
        var (order, first, _, _) = Order();
        var direct = LoadingSlipBuilder.Build(order, Settings);
        var viewed = LoadingSlipBuilder.Build(SalesDeliveryRules.View(order, first), Settings);
        Assert.Equal(LoadingSlipBuilder.Hash(direct), LoadingSlipBuilder.Hash(viewed));
        Assert.Equal("LB-2026-0005", SalesDeliveryRules.DocumentNumber(SalesOrderDocumentRules.KindLoadingSlip, order.OrderNumber, 1));
    }

    [Fact]
    public void With_Two_Deliveries_Each_Slip_Has_Its_Own_Reservations_And_The_Open_Rest_Goes_On_The_Last()
    {
        var (order, first, _, b) = Order();
        var second = new SalesDelivery { Id = 2, Sequence = 2, PartijId = 12, Partij = new Partij { Id = 12, Kind = PartijRules.KindSales, Number = "V-2611002" },
            LoadAt = new DateTime(2026, 11, 26, 6, 0, 0, DateTimeKind.Utc), LicensePlate = "XY-99-Z" };
        order.Deliveries.Add(second);
        b.SalesDeliveryId = 2;

        var slip1 = LoadingSlipBuilder.Build(SalesDeliveryRules.View(order, first), Settings);
        var slip2 = LoadingSlipBuilder.Build(SalesDeliveryRules.View(order, second), Settings);

        Assert.Equal(("V-2611001", 30m, "AB-12-CD"), (slip1.Position, slip1.TotalQuantity, slip1.LicensePlate));
        Assert.Equal(new[] { "P-1" }, slip1.Rows.Select(r => r.PalletReference));
        Assert.Equal(120m, slip1.TotalNetKg);
        Assert.Single(slip1.Packaging);
        Assert.Equal(("V-2611002", 20m, "XY-99-Z", second.LoadAt), (slip2.Position, slip2.TotalQuantity, slip2.LicensePlate, slip2.LoadAt));
        Assert.Equal(new[] { "P-2", null }, slip2.Rows.Select(r => r.PalletReference));
        Assert.Equal(new decimal?[] { 60m, 20m }, slip2.Rows.Select(r => r.NetKg));
        Assert.Empty(slip2.Packaging);
        Assert.Equal("TO-2026-0005-2", SalesDeliveryRules.DocumentNumber(SalesOrderDocumentRules.KindTransportOrder, order.OrderNumber, 2));
    }
}
