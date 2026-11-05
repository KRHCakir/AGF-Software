using AgfAdmin.Migrations;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// De data-SQL van de migratie AddPartijen, uitgevoerd op een kleine dataset in SQLite (dezelfde SQL draait in PostgreSQL): één partij
// per sleutel, spatie- en streepjesvarianten samengevoegd, regels zonder eenduidige partij ongekoppeld en LotNumber onaangeroerd.
public class PartijMigrationTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record Arranged(int Spaced, int Hyphened, int Plain, int Other, int Mixed, int Unreceived, int SingleOrder, int MixedOrder);

    private async Task<Arranged> ArrangeAsync()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        // Order 1: één regel, drie ontvangsten met dezelfde partij in drie schrijfwijzen.
        var single = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var plain = await PurchaseOrderTestData.ReceiveAsync(_factory, single.Id, 10m, 40m, lot: "2610251");
        var spaced = await PurchaseOrderTestData.ReceiveAsync(_factory, single.Id, 10m, 40m, lot: "2610 251");
        var hyphened = await PurchaseOrderTestData.ReceiveAsync(_factory, single.Id, 10m, 40m, lot: "2610-251");
        // Order 2: een regel met twee verschillende partijen en een regel zonder ontvangst.
        var request = PurchaseOrderTestData.OrderRequest(seed);
        request.Lines.Add(new() { ProductVariantId = seed.KentId, ExpectedQuantity = 5m, ExpectedUnit = "box", LotNumber = "VERWACHT-1" });
        var mixed = await PurchaseOrderTestData.CreateOrderAsync(_factory, request);
        var other = await PurchaseOrderTestData.ReceiveAsync(_factory, mixed.Id, 5m, 20m, lot: "2610252");
        await PurchaseOrderTestData.ReceiveAsync(_factory, mixed.Id, 5m, 20m, lot: "lot-a");
        return new Arranged(spaced, hyphened, plain, other, mixed.Lines[0].Id, mixed.Lines[1].Id, single.Id, mixed.Id);
    }

    private async Task RunMigrationSqlAsync()
    {
        await using var db = _factory.CreateContext();
        await db.Database.ExecuteSqlRawAsync(AddPartijen.LinkExistingLotsSql);
    }

    [Fact]
    public async Task One_Lot_Per_Key_With_Spacing_Variants_Merged_And_LotNumber_Untouched()
    {
        var a = await ArrangeAsync();
        await RunMigrationSqlAsync();

        await using var db = _factory.CreateContext();
        var partijen = await db.Partijen.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
        Assert.Equal(new[] { "2610251", "2610252", "lot-a" }, partijen.Select(p => p.Number));
        Assert.Equal(new[] { "2610251", "2610252", "LOTA" }, partijen.Select(p => p.NumberKey));
        Assert.All(partijen, p => Assert.Equal(p.NumberKey, PartijRules.NumberKey(p.Number)));
        Assert.All(partijen, p => { Assert.Equal("Migratie", p.CreatedBy); Assert.Equal("Open", p.Status); Assert.Null(p.Sequence); });

        var contents = await db.PalletContentLines.AsNoTracking().ToDictionaryAsync(c => c.Id);
        Assert.Equal(partijen[0].Id, contents[a.Plain].PartijId);
        Assert.Equal(partijen[0].Id, contents[a.Spaced].PartijId);
        Assert.Equal(partijen[0].Id, contents[a.Hyphened].PartijId);
        Assert.Equal(partijen[1].Id, contents[a.Other].PartijId);
        // LotNumber blijft zoals het was.
        Assert.Equal("2610 251", contents[a.Spaced].LotNumber);
        Assert.Equal("2610-251", contents[a.Hyphened].LotNumber);
    }

    [Fact]
    public async Task Lines_And_Orders_Are_Only_Linked_When_Unambiguous()
    {
        var a = await ArrangeAsync();
        await RunMigrationSqlAsync();

        await using var db = _factory.CreateContext();
        var partij = await db.Partijen.AsNoTracking().SingleAsync(p => p.NumberKey == "2610251");
        var lines = await db.SupplyCommitmentLines.AsNoTracking().ToDictionaryAsync(l => l.Id);
        var orders = await db.SupplyCommitments.AsNoTracking().ToDictionaryAsync(c => c.Id);
        // Eén regel met alleen 2610251 (drie schrijfwijzen): regel en order gekoppeld.
        Assert.Equal(partij.Id, lines.Values.Single(l => l.SupplyCommitmentId == a.SingleOrder).PartijId);
        Assert.Equal(partij.Id, orders[a.SingleOrder].PartijId);
        // Twee partijen op één regel en een regel zonder ontvangst: niets geraden.
        Assert.Null(lines[a.Mixed].PartijId);
        Assert.Null(lines[a.Unreceived].PartijId);
        Assert.Null(orders[a.MixedOrder].PartijId);
        Assert.Equal("VERWACHT-1", lines[a.Unreceived].LotNumber);
    }
}
