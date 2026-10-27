using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Infrastructure;

// Gedeelde testdata voor inkooporders: leveranciers, een gewogen (Hass) en een berekende variant (Kent, 4 kg per doos),
// een telefonische order via de service en een ontvangst rechtstreeks in de database (Received + Warehouse, na keuring).
public sealed record PurchaseOrderSeed(int VendorId, int OtherVendorId, int HassId, int KentId);

public static class PurchaseOrderTestData
{
    public static readonly DateTimeOffset ExpectedAt = new(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(1));

    public static async Task<PurchaseOrderSeed> SeedAsync(SqliteAppDbContextFactory factory)
    {
        await using var db = factory.CreateContext();
        var hass = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12m };
        var kent = new ProductVariant { Product = new Product { Name = "Mango" }, Name = "Kent", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 9m, WeightMethod = "Calculated", DefaultNetKgPerPackage = 4m };
        var vendor = new Vendor { Name = "Nature's Pride" };
        var other = new Vendor { Name = "Fruitimport Oost" };
        db.AddRange(hass, kent, vendor, other);
        await db.SaveChangesAsync();
        return new PurchaseOrderSeed(vendor.Id, other.Id, hass.Id, kent.Id);
    }

    public static CreateSupplyCommitmentDto OrderRequest(PurchaseOrderSeed seed, Guid? requestId = null, decimal quantity = 221m, int? vendorId = null, int? variantId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        VendorId = vendorId ?? seed.VendorId,
        Route = "Warehouse",
        Origin = "Peru",
        ExpectedAt = ExpectedAt,
        SupplierReference = "NP-88123",
        OrderChannel = "Phone",
        Lines = new List<CreateSupplyCommitmentLineDto>
        {
            new() { ProductVariantId = variantId ?? seed.HassId, ExpectedQuantity = quantity, ExpectedUnit = "box", ExpectedNetWeightKg = 884m }
        }
    };

    public static async Task<SupplyCommitmentDto> CreateOrderAsync(SqliteAppDbContextFactory factory, CreateSupplyCommitmentDto request, params IInterceptor[] interceptors)
    {
        await using var db = interceptors.Length == 0 ? factory.CreateContext() : factory.CreateContext(interceptors);
        var result = await new SupplyCommitmentService(db).CreateAsync(request);
        Assert.True(result.Status is ServiceResultStatus.Created or ServiceResultStatus.Ok, $"{result.Status}: {result.Message} {string.Join("; ", result.Errors.SelectMany(e => e.Value))}");
        return result.Value;
    }

    // Ontvangst rechtstreeks: Received + Warehouse (of InTransit), inhoud in de gevraagde kwaliteit (zoals na keuring).
    public static async Task<int> ReceiveAsync(SqliteAppDbContextFactory factory, int commitmentId, decimal quantity, decimal kg, string quality = "Approved", string palletStatus = "Received", string lot = "LOT-1")
    {
        await using var db = factory.CreateContext();
        var source = await db.SupplyCommitmentLines.Include(l => l.ProductVariant).FirstAsync(l => l.SupplyCommitmentId == commitmentId);
        var calculated = source.ProductVariant.WeightMethod == "Calculated";
        var pallet = new Pallet { Reference = $"P-{Guid.NewGuid():N}"[..12], LogisticsStatus = palletStatus, LocationContext = palletStatus == "Received" ? "Warehouse" : "InTransit" };
        var content = new PalletContentLine
        {
            RequestId = Guid.NewGuid(), Pallet = pallet, SupplyCommitmentLineId = source.Id, LotNumber = lot, Quantity = quantity, Unit = "box",
            NetWeightKg = kg, QualityStatus = quality, WeightMethod = calculated ? "Calculated" : "Weighed", NetKgPerPackage = calculated ? source.ProductVariant.DefaultNetKgPerPackage : null
        };
        db.AddRange(pallet, content);
        await db.SaveChangesAsync();
        return content.Id;
    }
}
