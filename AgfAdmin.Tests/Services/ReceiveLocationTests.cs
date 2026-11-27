using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Ontvangstlocatie (november 2026): Onderweg / direct naar klant is er altijd (bij het opstarten, idempotent; de laatste actieve blijft
// actief en Onderweg, guard-regel 47), en de standaard ontvangstlocatie bij meer dan één eigen loods is leeg of een actieve eigen loods.
public sealed class ReceiveLocationTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<StockLocationDto> LocationAsync(string name, string kind, bool active = true)
    {
        await using var db = _factory.CreateContext();
        var result = await new StockLocationService(db).CreateAsync(new SaveStockLocationDto { Name = name, Kind = kind, IsActive = active });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    private async Task<bool> EnsureAsync()
    {
        await using var db = _factory.CreateContext();
        return await StockLocationService.EnsureInTransitAsync(db);
    }

    private async Task<List<StockLocation>> InTransitAsync()
    {
        await using var db = _factory.CreateContext();
        return await db.StockLocations.AsNoTracking().Where(l => l.Kind == StockLocationRules.KindInTransit).OrderBy(l => l.Id).ToListAsync();
    }

    private async Task<ServiceResult<StockLocationDto>> UpdateAsync(StockLocationDto location, string kind = null, bool? active = null)
    {
        await using var db = _factory.CreateContext();
        var current = await db.StockLocations.AsNoTracking().SingleAsync(l => l.Id == location.Id);
        return await new StockLocationService(db).UpdateAsync(location.Id, new SaveStockLocationDto
        {
            Name = current.Name, Kind = kind ?? current.Kind, IsActive = active ?? current.IsActive, ExpectedVersion = current.Version
        });
    }

    [Fact]
    public async Task Onderweg_Is_Created_Once_At_Startup_And_A_Second_Start_Adds_Nothing()
    {
        Assert.True(await EnsureAsync());
        Assert.False(await EnsureAsync());
        var single = Assert.Single(await InTransitAsync());
        Assert.Equal((StockLocationRules.StandardInTransitName, true, StockLocationRules.SystemActor), (single.Name, single.IsActive, single.CreatedBy));
    }

    [Fact]
    public async Task An_Existing_Own_In_Transit_Location_Stays_As_It_Is_And_No_Second_Is_Made()
    {
        var greece = await LocationAsync("Onderweg naar Griekenland", StockLocationRules.KindInTransit);
        Assert.False(await EnsureAsync());
        Assert.Equal(new[] { greece.Id }, (await InTransitAsync()).Select(l => l.Id));
    }

    [Fact]
    public async Task An_Inactive_Standard_Location_Is_Made_Active_Again_And_A_Taken_Name_Gets_A_Number()
    {
        var old = await LocationAsync(StockLocationRules.StandardInTransitName, StockLocationRules.KindInTransit, active: false);
        Assert.True(await EnsureAsync());
        Assert.True(Assert.Single(await InTransitAsync()).IsActive);
        Assert.Equal(old.Id, (await InTransitAsync()).Single().Id);

        using var other = new SqliteAppDbContextFactory();
        await using (var db = other.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Created, (await new StockLocationService(db).CreateAsync(new SaveStockLocationDto
            {
                Name = StockLocationRules.StandardInTransitName, Kind = StockLocationRules.KindCrossdock
            })).Status);
        }
        await using (var db = other.CreateContext())
        {
            Assert.True(await StockLocationService.EnsureInTransitAsync(db));
            Assert.Equal($"{StockLocationRules.StandardInTransitName} (2)", (await db.StockLocations.SingleAsync(l => l.Kind == StockLocationRules.KindInTransit)).Name);
        }
    }

    [Fact]
    public async Task The_Last_Active_Onderweg_Is_Not_Made_Inactive_Or_Another_Kind_Not_Via_The_Service_And_Not_Via_The_Database()
    {
        await EnsureAsync();
        var standard = StockLocationService.ToDto((await InTransitAsync()).Single(), false);
        Assert.Equal(ServiceResultStatus.Validation, (await UpdateAsync(standard, active: false)).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await UpdateAsync(standard, kind: StockLocationRules.KindCrossdock)).Status);

        await using (var db = _factory.CreateContext())
        {
            var row = await db.StockLocations.SingleAsync(l => l.Id == standard.Id);
            row.IsActive = false;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }

        // Met een tweede actieve Onderweg-locatie mag de standaard wel inactief.
        await LocationAsync("Onderweg naar Griekenland", StockLocationRules.KindInTransit);
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(standard, active: false)).Status);
    }

    [Fact]
    public async Task The_Default_Receive_Location_Is_Empty_Or_An_Active_Own_Warehouse_And_Stays_So_While_It_Is_The_Default()
    {
        await EnsureAsync();
        var hall = await LocationAsync("Loods 2", StockLocationRules.KindOwnWarehouse);
        await using (var db = _factory.CreateContext())
        {
            db.CompanySettings.Add(new CompanySettings { LoadingSlipWarehouseAddress = "Fictieve loods\nVeilingweg 5" });
            await db.SaveChangesAsync();
            var service = new StockLocationService(db);
            Assert.Equal((null, "Fictieve loods\nVeilingweg 5"), ((await service.ReceiveDefaultsAsync()).DefaultReceiveLocationId, (await service.ReceiveDefaultsAsync()).OwnWarehouseAddress));
            var transit = (await InTransitAsync()).Single().Id;
            Assert.Equal(ServiceResultStatus.Validation, (await service.SaveReceiveDefaultsAsync(new SaveReceiveDefaultsDto { DefaultReceiveLocationId = transit })).Status);
            var saved = await service.SaveReceiveDefaultsAsync(new SaveReceiveDefaultsDto { DefaultReceiveLocationId = hall.Id });
            Assert.Equal((ServiceResultStatus.Ok, (int?)hall.Id), (saved.Status, saved.Value.DefaultReceiveLocationId));
        }

        Assert.Equal(ServiceResultStatus.Validation, (await UpdateAsync(hall, active: false)).Status);
        await using (var db = _factory.CreateContext())
        {
            var row = await db.StockLocations.SingleAsync(l => l.Id == hall.Id);
            row.Kind = StockLocationRules.KindCrossdock;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new StockLocationService(db).SaveReceiveDefaultsAsync(new SaveReceiveDefaultsDto { DefaultReceiveLocationId = null })).Status);
        }
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(hall, active: false)).Status);
    }
}
