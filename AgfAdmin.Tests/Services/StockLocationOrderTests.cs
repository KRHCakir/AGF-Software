using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: directe handel is een gewone inkooporder met als ontvangstlocatie een locatie van de soort Onderweg: de voorraad telt
// mee in Beschikbaarheid met de locatie erbij. Laadadres en afleveradres staan op de inkooporder; op de levering is het laadadres standaard de
// locatie van de gereserveerde pallets.
public class StockLocationOrderTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly PurchaseOrderEditHarness _h;

    public StockLocationOrderTests() => _h = new PurchaseOrderEditHarness(_factory);

    public void Dispose() => _factory.Dispose();

    private async Task<StockLocationDto> LocationAsync(string name, string kind, string address = null, string country = null)
    {
        await using var db = _factory.CreateContext();
        var result = await new StockLocationService(db).CreateAsync(new SaveStockLocationDto { Name = name, Kind = kind, Address = address, CountryCode = country });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    [Fact]
    public async Task Direct_Trade_Is_A_Normal_Order_Received_On_A_Location_In_Transit_That_Counts_In_Availability()
    {
        var greece = await LocationAsync("Onderweg naar Griekenland", StockLocationRules.KindInTransit, "Fictieve Klant AE\nAthene", "gr");
        Assert.Equal("GR", greece.CountryCode);
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, quantity: 120m, change: r =>
        {
            r.Lines[0].ExpectedNetWeightKg = 480m;
            r.Logistics = new OrderLogisticsDto { ReceiveLocationId = greece.Id, LoadingAddress = "Fictieve Teler\nLima", DeliveryAddress = "Fictieve Klant AE\nAthene" };
        }));

        await using (var db = _factory.CreateContext())
        {
            var view = (await new ReceiptService(db).GetAsync(order.Id)).Value;
            Assert.Equal((greece.Id, "Onderweg naar Griekenland", StockLocationRules.KindInTransit), (view.LocationId, view.LocationName, view.LocationKind));
            var received = await new ReceiptService(db).ReceiveLineAsync(order.Id, order.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 120m });
            Assert.Equal(ServiceResultStatus.Created, received.Status);
        }
        await using (var db = _factory.CreateContext())
        {
            var pallet = await db.Pallets.SingleAsync();
            Assert.Equal((greece.Id, "Received", "Warehouse"), (pallet.StockLocationId, pallet.LogisticsStatus, pallet.LocationContext));
            // Beschikbaarheid: de voorraad telt mee, met de locatie erbij.
            var overview = (await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery())).Value;
            var row = overview.Variants.Single().WarehouseSupply.Single();
            var location = Assert.Single(row.StockLocations);
            Assert.Equal(("Onderweg naar Griekenland", StockLocationRules.KindInTransit, "GR", 120m), (location.Name, location.Kind, location.CountryCode, location.Quantity));
            var drill = (await new AvailabilityService(db).GetVariantAsync(seed.HassId, new AvailabilityQuery())).Value;
            Assert.Equal("Onderweg naar Griekenland", drill.ContentLines.Single().StockLocationName);

            var detail = (await new PurchaseOrderService(db).GetAsync(order.Id)).Value;
            Assert.Equal(("Onderweg naar Griekenland", StockLocationRules.KindInTransit, "Fictieve Teler\nLima", "Fictieve Klant AE\nAthene"),
                (detail.ReceiveLocationName, detail.ReceiveLocationKind, detail.LoadingAddress, detail.DeliveryAddress));
        }
    }

    [Fact]
    public async Task Without_A_Location_An_Order_Is_Received_In_The_Own_Warehouse_And_Addresses_Change_With_An_Edit()
    {
        var order = await _h.OrderAsync();
        Assert.Equal((null, StockLocationRules.KindOwnWarehouse, null, null), (order.ReceiveLocationId, order.ReceiveLocationKind, order.LoadingAddress, order.DeliveryAddress));

        var crossdock = await LocationAsync("Cross-dock Venlo", StockLocationRules.KindCrossdock, "Fictief Cross-dock\nVenlo");
        var edit = PurchaseOrderEditHarness.EditOf(order);
        edit.Logistics = new OrderLogisticsDto { ReceiveLocationId = crossdock.Id, LoadingAddress = "Fictieve Leverancier\nRotterdam", DeliveryAddress = null };
        var saved = await _h.RunAsync(s => s.UpdateAsync(order.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        Assert.Equal((crossdock.Id, "Fictieve Leverancier\nRotterdam"), (saved.Value.ReceiveLocationId, saved.Value.LoadingAddress));
        var fields = saved.Value.Changes[^1].Fields;
        Assert.Contains(fields, f => f.Field == "receiveLocation" && f.Old == null && f.New == "Cross-dock Venlo");
        Assert.Contains(fields, f => f.Field == "loadingAddress" && f.New == "Fictieve Leverancier\nRotterdam");

        // Een edit zonder Logistics laat ze staan; een onbekende of inactieve locatie wordt geweigerd.
        var untouched = PurchaseOrderEditHarness.EditOf(saved.Value);
        untouched.Note = "Alleen de notitie";
        Assert.Equal(crossdock.Id, (await _h.RunAsync(s => s.UpdateAsync(order.Id, untouched))).Value.ReceiveLocationId);
        var unknown = PurchaseOrderEditHarness.EditOf((await _h.DetailAsync(order.Id)));
        unknown.Logistics = new OrderLogisticsDto { ReceiveLocationId = 9999 };
        Assert.Equal(ServiceResultStatus.NotFound, (await _h.RunAsync(s => s.UpdateAsync(order.Id, unknown))).Status);
    }

    [Fact]
    public void The_Loading_Place_Of_A_Delivery_Is_The_Location_Of_The_Reserved_Pallets_Unless_Set_Per_Delivery()
    {
        var crossdock = new StockLocation { Id = 3, Name = "Cross-dock Venlo", Kind = StockLocationRules.KindCrossdock, Address = "Fictief Cross-dock\nVenlo" };
        var onCrossdock = new Pallet { StockLocationId = 3, StockLocation = crossdock };
        var inOwnWarehouse = new Pallet { StockLocationId = null };
        Assert.Equal("Cross-dock Venlo\nFictief Cross-dock\nVenlo", TransportOrderBuilder.PalletLoadingPlace(new[] { onCrossdock, onCrossdock }));
        Assert.Null(TransportOrderBuilder.PalletLoadingPlace(new[] { onCrossdock, inOwnWarehouse }));
        Assert.Null(TransportOrderBuilder.PalletLoadingPlace(Array.Empty<Pallet>()));

        var settings = new CompanySettings { CompanyName = "Fictief BV", LoadingSlipWarehouseAddress = "Eigen loods\nBarendrecht" };
        Assert.Equal("Cross-dock Venlo", TransportOrderBuilder.LoadingPlace(null, settings, "Cross-dock Venlo"));
        Assert.Equal("Eigen loods\nBarendrecht", TransportOrderBuilder.LoadingPlace(null, settings, null));
        Assert.Equal("Fictieve Teler, Lima", TransportOrderBuilder.LoadingPlace(new SalesOrderTransport { LoadingPlace = "Fictieve Teler, Lima" }, settings, "Cross-dock Venlo"));
    }
}
