using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Afwijkende ontvangst binnen een inkooporder: Avocado Hass Peru maat 18 besteld (100 colli, 2 × 48 + 4, 400 kg netto, 420 bruto),
// een andere variant ontvangen. Alleen dezelfde variant boekt zonder vragen; elk verschil vraagt bevestiging; een ander artikel nooit.
public sealed partial class ReceiptDeviationTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private Variants _v;
    private int _orderId;
    private int _lineId;
    private int _palletId;

    private sealed record Variants(int Peru, int SouthAfrica, int Size16, int Crate, int SouthAfrica16, int Twin, int AlmostSameName, int Fuerte, int Mango);

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync(decimal expected = 100m)
    {
        await using (var db = _factory.CreateContext())
        {
            var avocado = new Product { Name = "Avocado" };
            ProductVariant Hass(string origin = "Peru", string size = "18", string packaging = "4kg Box", string name = "Hass") => new()
            {
                Product = avocado, Name = name, Origin = origin, SizeCategory = size, Packaging = packaging, PackagingUnit = "colli",
                TradeClass = "I", Brand = "Verita", DefaultPrice = 12m, DefaultNetKgPerPackage = 4m
            };
            var peru = Hass();
            var za = Hass(origin: "Zuid-Afrika");
            var size16 = Hass(size: "16");
            var crate = Hass(packaging: "10kg Crate");
            var za16 = Hass(origin: "Zuid-Afrika", size: "16");
            var twin = Hass();
            var almost = Hass(name: "Hass.");
            var fuerte = Hass(name: "Fuerte");
            var mango = new ProductVariant { Product = new Product { Name = "Mango" }, Name = "Kent", Origin = "Peru", Packaging = "4kg Box", PackagingUnit = "colli", DefaultPrice = 9m };
            var vendor = new Vendor { Name = "4 Fruit Supplier" };
            db.AddRange(peru, za, size16, crate, za16, twin, almost, fuerte, mango, vendor);
            await db.SaveChangesAsync();
            _v = new Variants(peru.Id, za.Id, size16.Id, crate.Id, za16.Id, twin.Id, almost.Id, fuerte.Id, mango.Id);

            var created = await new SupplyCommitmentService(db).CreateAsync(new CreateSupplyCommitmentDto
            {
                RequestId = Guid.NewGuid(), VendorId = vendor.Id, Route = "Warehouse", Origin = "Peru", ExpectedAt = PurchaseOrderTestData.ExpectedAt,
                Lines = new List<CreateSupplyCommitmentLineDto>
                {
                    expected == 100m
                        ? new() { ProductVariantId = peru.Id, ExpectedUnit = "colli", PalletCount = 2m, PackagesPerPallet = 48m, LoosePackages = 4m, ExpectedNetWeightKg = 400m, ExpectedGrossWeightKg = 420m }
                        : new() { ProductVariantId = peru.Id, ExpectedUnit = "colli", ExpectedQuantity = expected }
                }
            });
            Assert.Equal(ServiceResultStatus.Created, created.Status);
            _orderId = created.Value.Id;
            _lineId = created.Value.Lines.Single().Id;
        }
        _palletId = await NewPalletAsync();
    }

    private async Task<int> NewPalletAsync()
    {
        await using var db = _factory.CreateContext();
        var pallet = await new PalletService(db).CreatePalletForTestAsync();
        return pallet;
    }

    private async Task<(List<SupplyCommitmentLine> Lines, int Contents, List<SupplyReceiptDeviation> Deviations)> StateAsync()
    {
        await using var db = _factory.CreateContext();
        return (await db.SupplyCommitmentLines.AsNoTracking().Where(l => l.SupplyCommitmentId == _orderId).OrderBy(l => l.Id).ToListAsync(),
            await db.PalletContentLines.CountAsync(),
            await db.SupplyReceiptDeviations.AsNoTracking().ToListAsync());
    }

    private int VariantByName(string name) => name switch
    {
        "SouthAfrica" => _v.SouthAfrica, "Size16" => _v.Size16, "Crate" => _v.Crate, "SouthAfrica16" => _v.SouthAfrica16, "Twin" => _v.Twin,
        "AlmostSameName" => _v.AlmostSameName, "Fuerte" => _v.Fuerte, "Mango" => _v.Mango, _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    // ---------------- Kandidaten en voorbeeld ----------------

    [Fact]
    public async Task Candidates_Are_Other_Variants_Of_The_Same_Product_Only()
    {
        await ArrangeAsync();
        await using var db = _factory.CreateContext();
        var candidates = (await new ReceiptDeviationService(db).CandidatesAsync(_lineId)).Value;

        Assert.DoesNotContain(candidates, c => c.Variant.ProductVariantId == _v.Peru || c.Variant.ProductVariantId == _v.Mango);
        Assert.Equal(new[] { "Origin" }, candidates.Single(c => c.Variant.ProductVariantId == _v.SouthAfrica).Kinds);
        Assert.True(candidates.Single(c => c.Variant.ProductVariantId == _v.Fuerte).OtherArticle);
        Assert.True(candidates.Single(c => c.Variant.ProductVariantId == _v.AlmostSameName).OtherArticle);
        Assert.False(candidates.Single(c => c.Variant.ProductVariantId == _v.Twin).OtherArticle);
    }

    [Fact]
    public async Task Preview_Books_Nothing()
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            var service = new ReceiptDeviationService(db);
            Assert.Equal("Deviation", (await service.PreviewAsync(_lineId, _v.SouthAfrica, 10m)).Value.Status);
            Assert.Equal("Exact", (await service.PreviewAsync(_lineId, _v.Peru, 10m)).Value.Status);
            Assert.Equal("OtherArticle", (await service.PreviewAsync(_lineId, _v.Mango, 10m)).Value.Status);
            Assert.Equal(ServiceResultStatus.NotFound, (await service.PreviewAsync(_lineId, 9999, 10m)).Status);
            Assert.Equal(ServiceResultStatus.Validation, (await service.PreviewAsync(_lineId, _v.SouthAfrica, 0m)).Status);
        }
        var (lines, contents, deviations) = await StateAsync();
        Assert.Equal((1, 0, 0), (lines.Count, contents, deviations.Count));
    }
}

internal static class PalletServiceTestExtensions
{
    // Lege, ontvangen magazijnpallet via de echte service.
    public static async Task<int> CreatePalletForTestAsync(this PalletService service)
    {
        var result = await service.CreateAsync(new CreatePalletDto
        {
            RequestId = Guid.NewGuid(), LogisticsStatus = "Received", LocationContext = "Warehouse", ActorReference = "Kees"
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value.Id;
    }
}
