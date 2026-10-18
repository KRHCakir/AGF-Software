using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Tests.Controllers;

public class AvailabilityControllerTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<int> SeedVariantWithStockAsync()
    {
        await using var db = _factory.CreateContext();
        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass 6", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.5m };
        var commitment = new SupplyCommitment { RequestId = Guid.NewGuid(), Vendor = new Vendor { Name = "Supplier B" }, Route = "Warehouse", Status = "Expected", Origin = "Peru" };
        var line = new SupplyCommitmentLine { SupplyCommitment = commitment, ProductVariant = variant, ExpectedQuantity = 10m, ExpectedUnit = "box" };
        var pallet = new Pallet { Reference = "P1", LogisticsStatus = "Received", LocationContext = "Warehouse" };
        db.AddRange(variant, commitment, line, pallet, new PalletContentLine
        {
            RequestId = Guid.NewGuid(),
            Pallet = pallet,
            SupplyCommitmentLine = line,
            LotNumber = "LOT-1",
            Quantity = 4m,
            Unit = "box",
            NetWeightKg = 16m,
            QualityStatus = "Approved"
        });
        await db.SaveChangesAsync();
        return variant.Id;
    }

    private async Task<IActionResult> Run(Func<AvailabilityController, Task<IActionResult>> action)
    {
        await using var db = _factory.CreateContext();
        return await action(new AvailabilityController(new AvailabilityService(db)));
    }

    [Fact]
    public async Task Overview_Returns_200_Or_400()
    {
        var variantId = await SeedVariantWithStockAsync();

        var ok = Assert.IsType<OkObjectResult>(await Run(c => c.GetOverview(productVariantId: variantId, unit: null, route: null, customerId: null)));
        var overview = Assert.IsType<AvailabilityOverviewDto>(ok.Value);
        Assert.Equal(4m, overview.Variants.Single().WarehouseSupply.Single().AvailableBeforeReservations.Quantity);

        var badRequest = Assert.IsType<BadRequestObjectResult>(await Run(c => c.GetOverview(productVariantId: null, unit: null, route: "warehouse", customerId: null)));
        Assert.Contains("route", Assert.IsType<ValidationProblemDetails>(badRequest.Value).Errors.Keys);
    }

    [Fact]
    public async Task DrillDown_Returns_200_400_Or_404()
    {
        var variantId = await SeedVariantWithStockAsync();

        var ok = Assert.IsType<OkObjectResult>(await Run(c => c.GetVariant(variantId, customerId: null, route: null, unit: null, lot: null, category: null, qualityStatus: null, includeClosed: false)));
        Assert.Single(Assert.IsType<AvailabilityDrillDownDto>(ok.Value).ContentLines);

        var badRequest = Assert.IsType<BadRequestObjectResult>(await Run(c => c.GetVariant(variantId, customerId: null, route: null, unit: null, lot: null, category: "closed", qualityStatus: null, includeClosed: false)));
        Assert.Contains("category", Assert.IsType<ValidationProblemDetails>(badRequest.Value).Errors.Keys);

        Assert.IsType<NotFoundObjectResult>(await Run(c => c.GetVariant(999_999, customerId: null, route: null, unit: null, lot: null, category: null, qualityStatus: null, includeClosed: false)));
    }
}
