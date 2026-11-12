using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Regel 29: een opgeslagen inkooporder verandert buiten een afwijkende ontvangst alleen samen met één auditregel: Edit zolang ze Open
// is (met de grenzen na ontvangst), Finalize naar Definitive (nooit terug) en Correction met reden (alleen prijs, ordernummer van de
// leverancier en notitie). Regel 18 blijft zonder auditregel staan.
public class PurchaseOrderChangeGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private PurchaseOrderSeed _seed;

    public void Dispose() => _factory.Dispose();

    private async Task<(int OrderId, int LineId)> OrderAsync()
    {
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(_seed));
        return (order.Id, order.Lines.Single().Id);
    }

    private static PurchaseOrderChange Change(int orderId, string kind, string reason = null) => new()
    {
        RequestId = Guid.NewGuid(), SupplyCommitmentId = orderId, Kind = kind, Reason = reason, ActorReference = "Jan", CreatedAt = DateTime.UtcNow,
        ChangesJson = "[]",
    };

    private async Task<TraceabilityViolationException> RefusedAsync(Func<AppDbContext, Task> change)
    {
        await using var db = _factory.CreateContext();
        await change(db);
        return await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    private async Task SavedAsync(Func<AppDbContext, Task> change)
    {
        await using var db = _factory.CreateContext();
        await change(db);
        await db.SaveChangesAsync();
    }

    private async Task FinalizeAsync(int orderId)
    {
        await SavedAsync(async db =>
        {
            var order = await db.SupplyCommitments.SingleAsync(c => c.Id == orderId);
            db.PurchaseOrderChanges.Add(PurchaseOrderDocumentRules.Finalize(order, PurchaseOrderDocumentRules.ViaManual, "Jan", DateTime.UtcNow, Guid.NewGuid()));
        });
    }

    [Fact]
    public async Task Open_Order_Changes_Only_Together_With_An_Edit_Record()
    {
        var (orderId, lineId) = await OrderAsync();

        // Zonder auditregel: prijs en aantal geweigerd (regel 18, zoals altijd). Kopvelden en nieuwe regels van een Open order bewaakte de
        // guard niet en blijft hij niet bewaken; de service schrijft er altijd een auditregel bij.
        await RefusedAsync(async db => (await db.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId)).UnitPrice = 12.5m);
        await RefusedAsync(async db => (await db.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId)).ExpectedQuantity = 100m);

        // Met een Edit-regel: alles tegelijk in één save.
        await SavedAsync(async db =>
        {
            var order = await db.SupplyCommitments.Include(c => c.Lines).SingleAsync(c => c.Id == orderId);
            order.SupplierReference = "NP-1";
            order.Note = "Bel bij aankomst";
            order.VendorId = _seed.OtherVendorId;
            var line = order.Lines.Single();
            line.UnitPrice = 12.5m;
            line.ExpectedQuantity = 100m;
            line.ExpectedNetWeightKg = 400m;
            line.ProductVariantId = _seed.KentId;
            order.Lines.Add(new SupplyCommitmentLine { ProductVariantId = _seed.HassId, ExpectedQuantity = 5m, ExpectedUnit = "box" });
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindEdit));
        });
        await SavedAsync(async db =>
        {
            var added = await db.SupplyCommitmentLines.Where(l => l.SupplyCommitmentId == orderId).OrderBy(l => l.Id).LastAsync();
            db.SupplyCommitmentLines.Remove(added);
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindEdit));
        });

        await using var check = _factory.CreateContext();
        var stored = await check.SupplyCommitments.Include(c => c.Lines).SingleAsync(c => c.Id == orderId);
        Assert.Equal(("NP-1", _seed.OtherVendorId, 1, 100m, 12.5m), (stored.SupplierReference, stored.VendorId, stored.Lines.Count, stored.Lines.Single().ExpectedQuantity, stored.Lines.Single().UnitPrice));
    }

    [Fact]
    public async Task After_A_Receipt_The_Line_Stays_Keeps_Its_Article_Never_Goes_Below_Received_And_The_Supplier_Is_Fixed()
    {
        var (orderId, lineId) = await OrderAsync();
        await PurchaseOrderTestData.ReceiveAsync(_factory, orderId, 40m, 160m);

        async Task Edit(AppDbContext db, Action<SupplyCommitment, SupplyCommitmentLine> change)
        {
            var order = await db.SupplyCommitments.Include(c => c.Lines).SingleAsync(c => c.Id == orderId);
            change(order, order.Lines.Single(l => l.Id == lineId));
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindEdit));
        }

        Assert.Contains("never removed", (await RefusedAsync(async db => await Edit(db, (o, l) => db.SupplyCommitmentLines.Remove(l)))).Message);
        Assert.Contains("article and unit", (await RefusedAsync(async db => await Edit(db, (o, l) => l.ProductVariantId = _seed.KentId))).Message);
        Assert.Contains("already received", (await RefusedAsync(async db => await Edit(db, (o, l) => l.ExpectedQuantity = 39m))).Message);
        Assert.Contains("supplier", (await RefusedAsync(async db => await Edit(db, (o, l) => o.VendorId = _seed.OtherVendorId))).Message);

        // Prijs en aantal tot en met het ontvangen aantal blijven aanpasbaar.
        await SavedAsync(async db => await Edit(db, (o, l) => { l.ExpectedQuantity = 40m; l.UnitPrice = 11m; }));
    }

    [Fact]
    public async Task A_Change_Record_Never_Goes_Together_With_A_New_Receipt_On_That_Order()
    {
        var (orderId, lineId) = await OrderAsync();
        var refused = await RefusedAsync(async db =>
        {
            var order = await db.SupplyCommitments.Include(c => c.Lines).SingleAsync(c => c.Id == orderId);
            order.Lines.Single().ExpectedQuantity = 10m;
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindEdit));
            db.PalletContentLines.Add(new PalletContentLine
            {
                RequestId = Guid.NewGuid(), Pallet = new Pallet { Reference = $"P-{Guid.NewGuid():N}"[..12], LogisticsStatus = "Received", LocationContext = "Warehouse" },
                SupplyCommitmentLineId = lineId, LotNumber = "LOT-1", Quantity = 40m, Unit = "box", NetWeightKg = 160m, QualityStatus = "Quarantine",
            });
        });
        Assert.Contains("new receipt", refused.Message);
    }

    [Fact]
    public async Task Definitive_Never_Goes_Back_And_Only_A_Correction_With_Reason_Changes_Price_Reference_And_Note()
    {
        var (orderId, lineId) = await OrderAsync();

        // Definitief alleen samen met de Finalize-regel, en daarna nooit terug of met een ander tijdstip.
        await RefusedAsync(async db =>
        {
            var order = await db.SupplyCommitments.SingleAsync(c => c.Id == orderId);
            (order.DocumentStatus, order.FinalizedAt, order.FinalizedBy, order.FinalizedVia) = ("Definitive", DateTime.UtcNow, "Jan", "Manual");
        });
        await FinalizeAsync(orderId);
        await RefusedAsync(async db =>
        {
            var order = await db.SupplyCommitments.SingleAsync(c => c.Id == orderId);
            (order.DocumentStatus, order.FinalizedAt, order.FinalizedBy, order.FinalizedVia) = ("Open", null, null, null);
        });
        await RefusedAsync(async db => (await db.SupplyCommitments.SingleAsync(c => c.Id == orderId)).FinalizedBy = "Piet");

        // Zonder auditregel, een gewone aanpassing, of een correctie van aantal, verwachte datum of regels, of zonder reden: geweigerd.
        await RefusedAsync(async db => (await db.SupplyCommitments.SingleAsync(c => c.Id == orderId)).Note = "Stil gewijzigd");
        await RefusedAsync(db =>
        {
            db.SupplyCommitmentLines.Add(new SupplyCommitmentLine { SupplyCommitmentId = orderId, ProductVariantId = _seed.KentId, ExpectedQuantity = 5m, ExpectedUnit = "box" });
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindCorrection, "Vergeten regel"));
            return Task.CompletedTask;
        });
        await RefusedAsync(async db =>
        {
            (await db.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId)).UnitPrice = 9m;
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindEdit));
        });
        await RefusedAsync(async db =>
        {
            (await db.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId)).ExpectedQuantity = 200m;
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindCorrection, "Telfout"));
        });
        await RefusedAsync(async db =>
        {
            (await db.SupplyCommitments.SingleAsync(c => c.Id == orderId)).ExpectedAt = DateTime.UtcNow;
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindCorrection, "Later"));
        });
        await RefusedAsync(async db =>
        {
            (await db.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId)).UnitPrice = 9m;
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindCorrection, " "));
        });

        await SavedAsync(async db =>
        {
            (await db.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId)).UnitPrice = 9m;
            var order = await db.SupplyCommitments.SingleAsync(c => c.Id == orderId);
            order.SupplierReference = "NP-2";
            order.Note = "Prijs volgens factuur";
            db.PurchaseOrderChanges.Add(Change(orderId, PurchaseOrderDocumentRules.KindCorrection, "Prijsafspraak na levering"));
        });
    }

    [Fact]
    public async Task Making_Definitive_Changes_Nothing_Else_And_Change_Records_Are_Append_Only()
    {
        var (orderId, lineId) = await OrderAsync();
        await RefusedAsync(async db =>
        {
            var order = await db.SupplyCommitments.SingleAsync(c => c.Id == orderId);
            db.PurchaseOrderChanges.Add(PurchaseOrderDocumentRules.Finalize(order, PurchaseOrderDocumentRules.ViaManual, "Jan", DateTime.UtcNow, Guid.NewGuid()));
            (await db.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId)).UnitPrice = 9m;
        });
        await FinalizeAsync(orderId);

        // Een tweede keer definitief maken verandert niets (de eerste vastlegging blijft), en de auditregel is nooit te wijzigen of te verwijderen.
        await using (var db = _factory.CreateContext())
        {
            var order = await db.SupplyCommitments.SingleAsync(c => c.Id == orderId);
            Assert.Null(PurchaseOrderDocumentRules.Finalize(order, PurchaseOrderDocumentRules.ViaInvoice, "Piet", DateTime.UtcNow, Guid.NewGuid()));
        }
        await RefusedAsync(async db => (await db.PurchaseOrderChanges.SingleAsync()).ActorReference = "Piet");
        await RefusedAsync(async db => db.PurchaseOrderChanges.Remove(await db.PurchaseOrderChanges.SingleAsync()));

        // Een nieuwe order begint altijd Open.
        await RefusedAsync(db =>
        {
            db.SupplyCommitments.Add(new SupplyCommitment
            {
                RequestId = Guid.NewGuid(), VendorId = _seed.VendorId, Route = "Warehouse", Status = "Expected", Origin = "", ExpectedAt = DateTime.UtcNow,
                DocumentStatus = "Definitive", FinalizedAt = DateTime.UtcNow, FinalizedBy = "Jan", FinalizedVia = "Manual",
                Lines = { new SupplyCommitmentLine { ProductVariantId = _seed.HassId, ExpectedQuantity = 1m, ExpectedUnit = "box" } },
            });
            return Task.CompletedTask;
        });
    }
}
