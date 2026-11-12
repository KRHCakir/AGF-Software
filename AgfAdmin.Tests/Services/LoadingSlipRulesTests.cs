using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Documenten deel 1, de basis: de laadbon als snapshot (rijen, pallets, emballage, nooit een prijs) en TraceabilityGuard regel 31
// (bevestiging vast, een documentversie verandert of verdwijnt nooit, versies volgen elkaar op).
public class LoadingSlipRulesTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private async Task<int> ConfirmedOrderAsync()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        await using var db = _factory.CreateContext();
        var variant = await db.ProductVariants.FindAsync(seed.AvocadoId);
        variant!.DefaultPackagesPerPallet = 252m;
        variant.OriginCountryCode = "PE";
        var order = await db.SalesOrders.FindAsync(orderId);
        order!.ConfirmedAt = new DateTime(2026, 11, 12, 9, 0, 0, DateTimeKind.Utc);
        order.ConfirmedBy = "Jan";
        var euro = new CostArticle { RequestId = Guid.NewGuid(), Name = "Euro-pallet", NameKey = "euro-pallet", Kind = "Packaging", DefaultUnitPrice = 9.50m, VatPercentage = 21m, CreatedBy = "test", CreatedAt = DateTime.UtcNow };
        db.AddRange(euro, new SalesOrderPackagingLine { SalesOrderId = orderId, CostArticle = euro, Quantity = 3, UnitPrice = 9.50m, VatPercentage = 21m });
        await db.SaveChangesAsync();
        return orderId;
    }

    private async Task<LoadingSlipSnapshot> BuildAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        var order = await db.SalesOrders.Include(o => o.Customer).Include(o => o.SalesPartij).Include(o => o.PackagingLines).ThenInclude(p => p.CostArticle)
            .Include(o => o.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v.Product)
            .Include(o => o.Lines).ThenInclude(l => l.Reservations).AsSplitQuery().SingleAsync(o => o.Id == orderId);
        return LoadingSlipBuilder.Build(order, new CompanySettings { CompanyName = "Kwekerij Voorbeeld B.V.", Address = "Loodsweg 1, Barendrecht" });
    }

    [Fact]
    public async Task The_slip_has_a_row_per_order_line_pallets_unrounded_packaging_without_price_and_a_stable_fingerprint()
    {
        var orderId = await ConfirmedOrderAsync();
        var slip = await BuildAsync(orderId);
        Assert.Equal(("Kwekerij Voorbeeld B.V.", "Customer T", "en"), (slip.CompanyName, slip.CustomerName, slip.Language));
        Assert.Equal(new[] { (50m, "box", "PE"), (10m, "colli", "PE") }, slip.Rows.Select(r => (r.Quantity, r.Unit, r.Origin)));
        Assert.Equal(50m / 252m, slip.Rows[0].Pallets);
        Assert.Equal(60m / 252m, slip.TotalPallets);
        Assert.Equal(new[] { (3, "Euro-pallet") }, slip.Packaging.Select(p => (p.Quantity, p.Description)));
        Assert.Equal(LoadingSlipBuilder.Hash(slip), LoadingSlipBuilder.Hash(await BuildAsync(orderId)));
        var json = LoadingSlipBuilder.Serialize(slip);
        Assert.DoesNotContain("price", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9.5", json);
        Assert.Equal("LB-2026-0012", SalesOrderDocumentRules.Number("VO-2026-0012"));
    }

    private static SalesOrderDocument Version(int orderId, int version) => new()
    {
        SalesOrderId = orderId, Kind = SalesOrderDocumentRules.KindLoadingSlip, Number = "LB-1", Version = version, Reason = version == 1 ? "Confirmed" : "Changed",
        SnapshotJson = "{}", ContentHash = $"h{version}", CreatedBy = "Jan",
    };

    [Fact]
    public async Task The_guard_keeps_confirmation_and_versions_fixed()
    {
        var orderId = await ConfirmedOrderAsync();
        await using (var db = _factory.CreateContext())
        {
            db.Add(Version(orderId, 1));
            await db.SaveChangesAsync();
        }
        async Task Refused(Action<AppDbContext> change)
        {
            await using var db = _factory.CreateContext();
            await db.SalesOrderDocuments.LoadAsync();
            await db.SalesOrders.Where(o => o.Id == orderId).LoadAsync();
            change(db);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await Refused(db => db.SalesOrderDocuments.Local.Single().SnapshotJson = "{\"x\":1}");
        await Refused(db => db.SalesOrderDocuments.Remove(db.SalesOrderDocuments.Local.Single()));
        await Refused(db => db.SalesOrders.Local.Single().ConfirmedBy = "iemand anders");
        await Refused(db => db.SalesOrderDocuments.Add(Version(orderId, 3)));

        // Doorgegeven mag één keer worden gezet; daarna niet meer anders.
        await using (var db = _factory.CreateContext())
        {
            var document = await db.SalesOrderDocuments.SingleAsync();
            document.SentAt = DateTime.UtcNow;
            document.SentBy = "Jan";
            await db.SaveChangesAsync();
        }
        await Refused(db => db.SalesOrderDocuments.Local.Single().SentBy = "Piet");
        // Versie 2 volgt versie 1 op.
        await using (var db = _factory.CreateContext())
        {
            db.Add(Version(orderId, 2));
            await db.SaveChangesAsync();
        }

        // Een document hoort bij een bevestigde order.
        await using (var db = _factory.CreateContext())
        {
            var open = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = (await db.Customers.FirstAsync()).Id, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow.Date, ActorReference = "t" };
            db.Add(open);
            await db.SaveChangesAsync();
            db.Add(Version(open.Id, 1));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }
}
