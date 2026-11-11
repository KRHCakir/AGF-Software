using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Migrations;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// De orderregel van de inkooporder (feedback 11 november): kg uit het artikel, bruto alleen op deze regel aangepast, prijs en totaal per
// regel en over de order, pallets op twee decimalen (niet naar hele pallets), en de snelcode uniek zonder op hoofdletters te letten.
public class PurchaseOrderLineTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record Articles(int VendorId, int Avocado, int Mango, int Loose);

    // Drie artikelen met verschillende colli per pallet (252, 264) en één zonder.
    private async Task<Articles> SeedAsync()
    {
        await using var db = _factory.CreateContext();
        var product = new Product { Name = "Fruit" };
        var avocado = new ProductVariant { Product = product, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", Brand = "Westfalia", DefaultNetKgPerPackage = 4m, DefaultGrossKgPerPackage = 4.4m, DefaultPackagesPerPallet = 252m };
        var mango = new ProductVariant { Product = product, Name = "Kent", Origin = "Brazilië", Packaging = "4kg Box", DefaultNetKgPerPackage = 4m, DefaultGrossKgPerPackage = 4.3m, DefaultPackagesPerPallet = 264m };
        var loose = new ProductVariant { Product = product, Name = "Lime", Origin = "Brazilië", Packaging = "4,5kg Box", DefaultNetKgPerPackage = 4.5m };
        var vendor = new Vendor { Name = "Nature's Pride" };
        db.AddRange(avocado, mango, loose, vendor);
        await db.SaveChangesAsync();
        return new Articles(vendor.Id, avocado.Id, mango.Id, loose.Id);
    }

    private static CreateSupplyCommitmentLineDto Line(int variantId, decimal quantity, decimal? net, decimal? gross, string source, decimal? price) =>
        new() { ProductVariantId = variantId, ExpectedQuantity = quantity, ExpectedUnit = "box", ExpectedNetWeightKg = net, ExpectedGrossWeightKg = gross, WeightSource = source, UnitPrice = price };

    private static CreateSupplyCommitmentDto Order(Articles a, params CreateSupplyCommitmentLineDto[] lines) => new()
    {
        RequestId = Guid.NewGuid(), VendorId = a.VendorId, Route = "Warehouse", ExpectedAt = PurchaseOrderTestData.ExpectedAt, Lines = lines.ToList()
    };

    private async Task<ServiceResult<SupplyCommitmentDto>> CreateAsync(CreateSupplyCommitmentDto request)
    {
        await using var db = _factory.CreateContext();
        return await new SupplyCommitmentService(db).CreateAsync(request);
    }

    private async Task<PurchaseOrderDetailDto> DetailAsync(int id)
    {
        await using var db = _factory.CreateContext();
        return (await new PurchaseOrderService(db).GetAsync(id)).Value;
    }

    [Fact]
    public void Pallets_are_quantity_over_packages_per_pallet_and_the_total_is_their_sum_on_two_decimals_never_rounded_up()
    {
        Assert.Equal(0.40m, PurchaseLineMath.Round2(PurchaseLineMath.Pallets(100m, 252m)!.Value));
        Assert.Equal(0.19m, PurchaseLineMath.Round2(PurchaseLineMath.Pallets(50m, 264m)!.Value));
        Assert.Null(PurchaseLineMath.Pallets(10m, null));
        Assert.Equal(120m, PurchaseLineMath.PackagesPerPallet(120m, 252m));
        Assert.Equal(252m, PurchaseLineMath.PackagesPerPallet(null, 252m));
        var totals = PurchaseLineMath.Sum(new[]
        {
            new PurchaseLineMath.Line(250m, null, null, null, PurchaseLineMath.Pallets(250m, 120m)),
            new PurchaseLineMath.Line(5m, null, null, null, null),
        });
        Assert.Equal(2.08m, totals.Pallets);
        Assert.Equal(1, totals.LinesWithoutPallets);
        Assert.Equal(40.04m, PurchaseLineMath.LineTotal(3m, 13.345m)); // 40,035: half van nul af
        Assert.Null(PurchaseLineMath.LineTotal(3m, null));
    }

    [Fact]
    public async Task Kg_come_from_the_article_gross_adjusted_on_one_line_only_and_the_order_totals_sit_under_the_columns()
    {
        var a = await SeedAsync();
        var created = await CreateAsync(Order(a,
            Line(a.Avocado, 100m, 400m, 440m, "Calculated", 12.5m),
            Line(a.Mango, 50m, 200m, 230m, "Entered", 9.75m),
            Line(a.Loose, 10m, 45m, null, "Calculated", null)));
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(new decimal?[] { 1250m, 487.5m, null }, created.Value.Lines.Select(l => l.LineTotal));

        var detail = await DetailAsync(created.Value.Id);
        Assert.Equal(new[] { "Calculated", "Entered", "Calculated" }, detail.Lines.Select(l => l.WeightSource));
        Assert.Equal(new decimal?[] { 440m, 230m, null }, detail.Lines.Select(l => l.ExpectedGrossWeightKg));
        Assert.Equal(new decimal?[] { 0.40m, 0.19m, null }, detail.Lines.Select(l => l.Pallets));
        Assert.Equal(new decimal?[] { 252m, 264m, null }, detail.Lines.Select(l => l.PackagesPerPallet));
        Assert.Equal("Westfalia", detail.Lines[0].Brand);
        Assert.Equal(12.5m, detail.Lines[0].UnitPrice);

        var totals = detail.Totals;
        Assert.Equal((160m, 645m, 670m, 1737.5m, 0.59m), (totals.Quantity, totals.NetWeightKg, totals.GrossWeightKg, totals.Amount, totals.Pallets));
        Assert.Equal((0, 1, 1, 1), (totals.LinesWithoutNet, totals.LinesWithoutGross, totals.LinesWithoutPrice, totals.LinesWithoutPallets));

        // De aangepaste bruto staat alleen op de regel: het artikel houdt zijn standaard.
        await using var db = _factory.CreateContext();
        Assert.Equal(4.3m, (await db.ProductVariants.SingleAsync(v => v.Id == a.Mango)).DefaultGrossKgPerPackage);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1.005)]
    public async Task A_negative_price_or_one_with_more_than_two_decimals_is_refused(double price)
    {
        var a = await SeedAsync();
        var result = await CreateAsync(Order(a, Line(a.Avocado, 10m, null, null, null, (decimal)price)));
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.True(result.Errors.ContainsKey("lines[0].unitPrice"));
    }

    [Fact]
    public async Task The_price_0_is_allowed_is_part_of_the_replay_and_is_fixed_after_saving()
    {
        var a = await SeedAsync();
        var request = Order(a, Line(a.Avocado, 10m, null, null, null, 0m));
        var first = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(request)).Status);
        request.Lines[0].UnitPrice = 1m;
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(request)).Status);

        await using var db = _factory.CreateContext();
        Assert.Equal(0m, (await db.SupplyCommitmentLines.SingleAsync(l => l.SupplyCommitmentId == first.Value.Id)).UnitPrice);
        var line = await db.SupplyCommitmentLines.SingleAsync(l => l.SupplyCommitmentId == first.Value.Id);
        line.UnitPrice = 2m;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task An_existing_order_without_price_or_pallets_stays_readable()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var detail = await DetailAsync(order.Id);
        var line = Assert.Single(detail.Lines);
        Assert.Equal((221m, 884m, (decimal?)null, (decimal?)null, (decimal?)null), (line.ExpectedQuantity, line.ExpectedNetWeightKg!.Value, line.UnitPrice, line.LineTotal, line.Pallets));
        Assert.Equal((221m, (decimal?)null, (decimal?)null, 1, 1), (detail.Totals.Quantity, detail.Totals.Amount, detail.Totals.Pallets, detail.Totals.LinesWithoutPrice, detail.Totals.LinesWithoutPallets));
    }

    [Fact]
    public async Task A_shortcut_is_unique_regardless_of_case_for_every_writer_and_the_migration_fills_the_key()
    {
        var a = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            var avocado = await db.ProductVariants.SingleAsync(v => v.Id == a.Avocado);
            avocado.ShortcutCode = " AvoHassPer13 ";
            await db.SaveChangesAsync();
            Assert.Equal("avohassper13", avocado.ShortcutKey);
        }
        await using (var db = _factory.CreateContext())
        {
            var request = new SaveArticleDto { ProductName = "Fruit", VariantName = "Hass 2", ShortcutCode = "avohassper13", OriginCountryCode = "PE", SizeCategory = "13", TradeClass = "I" };
            var refused = await new ArticleService(db).CreateAsync(request);
            Assert.Equal(ServiceResultStatus.Validation, refused.Status);
            Assert.Contains("already used", Assert.Single(refused.Errors["shortcutCode"]));
            request.ShortcutCode = "1";
            Assert.Equal(ServiceResultStatus.Created, (await new ArticleService(db).CreateAsync(request)).Status);
        }
        // Een schrijver buiten Artikelen om (de oude productpagina) botst op de unieke index in plaats van een dubbele te maken.
        await using (var db = _factory.CreateContext())
        {
            (await db.ProductVariants.SingleAsync(v => v.Id == a.Mango)).ShortcutCode = "AVOHASSPER13";
            Assert.True(DbErrors.IsUniqueViolation(await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync())));
        }
        await using (var db = _factory.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE \"ProductVariants\" SET \"ShortcutKey\" = NULL");
            await db.Database.ExecuteSqlRawAsync(AddPurchaseLinePriceAndShortcutKey.FillShortcutKeysSql);
            Assert.Equal(new[] { "1", "avohassper13" }, await db.ProductVariants.Where(v => v.ShortcutKey != null).OrderBy(v => v.ShortcutKey).Select(v => v.ShortcutKey).ToListAsync());
        }
    }
}
