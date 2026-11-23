using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: een voorraadlocatie heeft een soort (Eigen loods, Leverancier, Cross-dock, Externe opslag, Onderweg). De ontvangst
// zet de ontvangstlocatie van de order op de pallet; guard-regel 40 houdt locaties, de locatie van een pallet en een gebruikte soort vast.
public class StockLocationTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private async Task<StockLocationDto> LocationAsync(string name, string kind, string address = null, string country = null)
    {
        await using var db = _factory.CreateContext();
        var result = await new StockLocationService(db).CreateAsync(new SaveStockLocationDto { Name = name, Kind = kind, Address = address, CountryCode = country });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    [Fact]
    public async Task The_Receipt_Puts_The_Location_On_The_Pallet_And_Guard_Rule_40_Keeps_Locations_And_Used_Kinds()
    {
        var external = await LocationAsync("Externe opslag Ridderkerk", StockLocationRules.KindExternalStorage);
        var unused = await LocationAsync("Leverancier ophalen", StockLocationRules.KindSupplier);
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        await using (var db = _factory.CreateContext())
        {
            (await db.SupplyCommitments.SingleAsync(c => c.Id == order.Id)).ReceiveLocationId = external.Id;
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            var view = (await new ReceiptService(db).GetAsync(order.Id)).Value;
            Assert.Equal((external.Id, "Externe opslag Ridderkerk", StockLocationRules.KindExternalStorage), (view.LocationId, view.LocationName, view.LocationKind));
            await new ReceiptService(db).ReceiveLineAsync(order.Id, order.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 10m });
            Assert.Equal(external.Id, (await db.Pallets.AsNoTracking().SingleAsync()).StockLocationId);
        }

        await using (var db = _factory.CreateContext())
        {
            (await db.Pallets.SingleAsync()).StockLocationId = null;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            db.StockLocations.Remove(await db.StockLocations.SingleAsync(l => l.Id == unused.Id));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.StockLocations.SingleAsync(l => l.Id == external.Id)).Kind = StockLocationRules.KindInTransit;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        // Een nieuwe verwijzing in dezelfde save telt ook als gebruik.
        await using (var db = _factory.CreateContext())
        {
            (await db.StockLocations.SingleAsync(l => l.Id == unused.Id)).Kind = StockLocationRules.KindInTransit;
            (await db.SupplyCommitments.SingleAsync(c => c.Id == order.Id)).ReceiveLocationId = unused.Id;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        // Een inactieve locatie blokkeert de ontvangst van een order die haar al had niet.
        await using (var db = _factory.CreateContext())
        {
            (await db.StockLocations.SingleAsync(l => l.Id == external.Id)).IsActive = false;
            await db.SaveChangesAsync();
            var again = await new ReceiptService(db).ReceiveLineAsync(order.Id, order.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 5m });
            Assert.Equal(ServiceResultStatus.Created, again.Status);
            (await db.StockLocations.SingleAsync(l => l.Id == external.Id)).IsActive = true;
            await db.SaveChangesAsync();
            external = (await new StockLocationService(db).ListAsync()).Single(l => l.Id == external.Id);
        }
        await using (var db = _factory.CreateContext())
        {
            var service = new StockLocationService(db);
            var refused = await service.UpdateAsync(external.Id, new SaveStockLocationDto { Name = external.Name, Kind = StockLocationRules.KindInTransit, ExpectedVersion = external.Version });
            Assert.Contains("kind", refused.Errors.Keys);
            // Een ongebruikte locatie mag van soort wisselen; inactief zetten mag altijd.
            var changed = await service.UpdateAsync(unused.Id, new SaveStockLocationDto { Name = unused.Name, Kind = StockLocationRules.KindCrossdock, IsActive = false, ExpectedVersion = unused.Version });
            Assert.Equal((ServiceResultStatus.Ok, StockLocationRules.KindCrossdock, false), (changed.Status, changed.Value.Kind, changed.Value.IsActive));
            var list = await service.ListAsync();
            Assert.Equal(new[] { (external.Id, true), (unused.Id, false) }, list.Select(l => (l.Id, l.InUse)));
            Assert.Contains("name", (await service.CreateAsync(new SaveStockLocationDto { Name = "Externe opslag Ridderkerk", Kind = StockLocationRules.KindCrossdock })).Errors.Keys);
        }
    }
}
