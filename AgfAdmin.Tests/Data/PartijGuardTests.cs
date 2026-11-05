using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Regel 23: een palletregel valt nooit stil in een andere partij, een nummer verandert alleen met historie, een gekoppelde partij
// verdwijnt nooit en de historie is append-only. Ook twee partijen met bijna hetzelfde nummer worden nooit gemengd.
public class PartijGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private PurchaseOrderSeed _seed;

    public void Dispose() => _factory.Dispose();

    private async Task<(int OrderId, int LineId, int PartijId)> OrderWithPartijAsync(string number = "2610251")
    {
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(_seed));
        var partijId = await PartijTestData.CreateAsync(_factory, number);
        await PartijTestData.LinkOrderAsync(_factory, order.Id, partijId);
        return (order.Id, order.Lines.Single().Id, partijId);
    }

    private static PalletContentLine Content(int lineId, string lot, int? partijId) => new()
    {
        RequestId = Guid.NewGuid(), Pallet = new Pallet { Reference = $"P-{Guid.NewGuid():N}"[..12], LogisticsStatus = "Received", LocationContext = "Warehouse" },
        SupplyCommitmentLineId = lineId, LotNumber = lot, PartijId = partijId, Quantity = 4m, Unit = "box", NetWeightKg = 16m, QualityStatus = "Quarantine"
    };

    private async Task<TraceabilityViolationException> RefusedAsync(Action<AppDbContext> change)
    {
        await using var db = _factory.CreateContext();
        change(db);
        return await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task New_Pallet_Line_Has_Exactly_The_Lot_Of_Its_Order_Line()
    {
        var a = await OrderWithPartijAsync("2610251");
        var other = await PartijTestData.CreateAsync(_factory, "2610252");

        // Zonder partij, met een bijna gelijke andere partij, of met het nummer van een andere partij: geweigerd.
        await RefusedAsync(db => db.PalletContentLines.Add(Content(a.LineId, "2610251", null)));
        await RefusedAsync(db => db.PalletContentLines.Add(Content(a.LineId, "2610252", other)));
        await RefusedAsync(db => db.PalletContentLines.Add(Content(a.LineId, "2610252", a.PartijId)));

        await using var ok = _factory.CreateContext();
        ok.PalletContentLines.Add(Content(a.LineId, "2610251", a.PartijId));
        await ok.SaveChangesAsync();
    }

    [Fact]
    public async Task Order_Line_Without_Lot_Never_Gets_Pallet_Lines_Of_A_Lot()
    {
        _seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(_seed));
        var partij = await PartijTestData.CreateAsync(_factory, "2610251");
        await RefusedAsync(db => db.PalletContentLines.Add(Content(order.Lines[0].Id, "2610251", partij)));
    }

    [Fact]
    public async Task Every_Line_Of_An_Order_With_A_Lot_Belongs_To_That_Lot()
    {
        var a = await OrderWithPartijAsync();
        var other = await PartijTestData.CreateAsync(_factory, "2610252");
        await RefusedAsync(db =>
        {
            var line = db.SupplyCommitmentLines.Single(l => l.Id == a.LineId);
            line.PartijId = other;
        });
        await RefusedAsync(db => db.SupplyCommitmentLines.Add(new SupplyCommitmentLine
        {
            SupplyCommitmentId = a.OrderId, ProductVariantId = _seed.KentId, ExpectedQuantity = 1m, ExpectedUnit = "box"
        }));
    }

    [Fact]
    public async Task Lot_Of_An_Order_Is_Fixed_Once_Goods_Are_Received()
    {
        var a = await OrderWithPartijAsync();
        var other = await PartijTestData.CreateAsync(_factory, "2610252");

        // Zonder ontvangst mag de order (met al haar regels) nog naar een andere partij.
        await using (var db = _factory.CreateContext())
        {
            var order = await db.SupplyCommitments.Include(c => c.Lines).SingleAsync(c => c.Id == a.OrderId);
            order.PartijId = other;
            order.Lines.Single().PartijId = other;
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            db.PalletContentLines.Add(Content(a.LineId, "2610252", other));
            await db.SaveChangesAsync();
        }
        await RefusedAsync(db =>
        {
            var order = db.SupplyCommitments.Include(c => c.Lines).Single(c => c.Id == a.OrderId);
            order.PartijId = a.PartijId;
            order.Lines.Single().PartijId = a.PartijId;
        });
    }

    [Fact]
    public async Task Pallet_Line_Lot_Is_Only_Set_Once_To_The_Lot_Of_Its_Order_Line()
    {
        _seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(_seed));
        var contentId = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 10m, 40m, lot: "OUD-1");
        var partij = await PartijTestData.CreateAsync(_factory, "2610251");
        var other = await PartijTestData.CreateAsync(_factory, "2610252");

        // De orderregel heeft nog geen partij: de palletregel kan er dan ook geen krijgen.
        await RefusedAsync(db => db.PalletContentLines.Single(c => c.Id == contentId).PartijId = partij);

        // Order, regel en palletregel samen koppelen (het gat uit de migratie): toegestaan; daarna ligt de partij vast.
        await using (var db = _factory.CreateContext())
        {
            var linked = await db.SupplyCommitments.Include(c => c.Lines).SingleAsync(c => c.Id == order.Id);
            linked.PartijId = partij;
            linked.Lines.Single().PartijId = partij;
            (await db.PalletContentLines.SingleAsync(c => c.Id == contentId)).PartijId = partij;
            await db.SaveChangesAsync();
        }
        await RefusedAsync(db => db.PalletContentLines.Single(c => c.Id == contentId).PartijId = other);
        await RefusedAsync(db => db.PalletContentLines.Single(c => c.Id == contentId).PartijId = null);
        await using var verify = _factory.CreateContext();
        Assert.Equal("OUD-1", (await verify.PalletContentLines.SingleAsync(c => c.Id == contentId)).LotNumber);
    }

    [Fact]
    public async Task Number_Only_Changes_With_A_Correction_Audit_Event_And_Keeps_Its_Key()
    {
        var partij = await PartijTestData.CreateAsync(_factory, "2610251");

        await RefusedAsync(db =>
        {
            var p = db.Partijen.Single(x => x.Id == partij);
            p.Number = "2610261";
            p.NumberKey = PartijRules.NumberKey(p.Number);
        });
        // Een sleutel die niet bij het nummer hoort, of een correctie zonder reden: geweigerd.
        await RefusedAsync(db => db.Partijen.Single(x => x.Id == partij).NumberKey = "ANDERS");
        await RefusedAsync(db =>
        {
            var p = db.Partijen.Single(x => x.Id == partij);
            p.Number = "2610261";
            p.NumberKey = "2610261";
            db.PartijAuditEvents.Add(new PartijAuditEvent { PartijId = partij, EventType = PartijRules.EventNumberCorrected, OldValue = "2610251", NewValue = "2610261", ActorReference = "Test" });
        });

        await using var ok = _factory.CreateContext();
        var corrected = await ok.Partijen.SingleAsync(x => x.Id == partij);
        corrected.Number = "2610261";
        corrected.NumberKey = "2610261";
        ok.PartijAuditEvents.Add(new PartijAuditEvent { PartijId = partij, EventType = PartijRules.EventNumberCorrected, OldValue = "2610251", NewValue = "2610261", Reason = "Typefout", ActorReference = "Test" });
        await ok.SaveChangesAsync();
    }

    [Fact]
    public async Task Linked_Lot_Cannot_Be_Deleted_And_History_Is_Append_Only()
    {
        var a = await OrderWithPartijAsync();
        await RefusedAsync(db => db.Partijen.Remove(db.Partijen.Single(p => p.Id == a.PartijId)));

        await using (var db = _factory.CreateContext())
        {
            db.PartijAuditEvents.Add(new PartijAuditEvent { PartijId = a.PartijId, EventType = PartijRules.EventOrderLinked, NewValue = "2610251", ActorReference = "Test" });
            await db.SaveChangesAsync();
        }
        await RefusedAsync(db => db.PartijAuditEvents.Single().Reason = "anders");
        await RefusedAsync(db => db.PartijAuditEvents.Remove(db.PartijAuditEvents.Single()));

        // Een ongekoppelde partij mag weg; de database bewaakt daarnaast de unieke sleutel.
        var loose = await PartijTestData.CreateAsync(_factory, "LOS-1");
        await using (var db = _factory.CreateContext())
        {
            db.Partijen.Remove(await db.Partijen.SingleAsync(p => p.Id == loose));
            await db.SaveChangesAsync();
        }
        await using var duplicate = _factory.CreateContext();
        duplicate.Partijen.Add(new Partij { Number = "2610 251", NumberKey = "2610251", CreatedBy = "Test" });
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }
}
