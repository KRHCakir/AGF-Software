using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Controllers;

// Gewichtsinstellingen van een productvariant: methode en standaard kg per verpakking; raakt geen inhoudsregels.
public class ProductsControllerTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<int> SeedVariantAsync()
    {
        await using var db = _factory.CreateContext();
        var variant = new ProductVariant { Product = new Product { Name = "Citroen" }, Name = "Primofiori", Origin = "Spanje", Packaging = "10kg Doos", DefaultPrice = 8m };
        db.Add(variant);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    [Fact]
    public async Task New_Variants_Are_Weighed_By_Default_And_Settings_Can_Switch_To_Calculated()
    {
        var id = await SeedVariantAsync();
        await using (var verify = _factory.CreateContext())
        {
            var fresh = await verify.ProductVariants.SingleAsync(v => v.Id == id);
            Assert.Equal("Weighed", fresh.WeightMethod);
            Assert.Null(fresh.DefaultNetKgPerPackage);
        }

        await using var db = _factory.CreateContext();
        var result = await new ProductsController(db).UpdateWeightSettings(id, new UpdateWeightSettingsDto { WeightMethod = "Calculated", DefaultNetKgPerPackage = 10m });
        Assert.IsType<OkObjectResult>(result);

        await using var after = _factory.CreateContext();
        var saved = await after.ProductVariants.SingleAsync(v => v.Id == id);
        Assert.Equal("Calculated", saved.WeightMethod);
        Assert.Equal(10m, saved.DefaultNetKgPerPackage);
    }

    [Theory]
    [InlineData("Estimated", null)]
    [InlineData("Calculated", 0.0)]
    [InlineData("Weighed", 4.0001)]
    [InlineData(null, null)]
    public async Task Invalid_Weight_Settings_Are_Rejected_And_Nothing_Changes(string method, double? kg)
    {
        var id = await SeedVariantAsync();
        await using var db = _factory.CreateContext();
        var result = await new ProductsController(db).UpdateWeightSettings(id, new UpdateWeightSettingsDto { WeightMethod = method, DefaultNetKgPerPackage = kg == null ? null : (decimal)kg.Value });
        Assert.IsType<BadRequestObjectResult>(result);

        await using var verify = _factory.CreateContext();
        var unchanged = await verify.ProductVariants.SingleAsync(v => v.Id == id);
        Assert.Equal("Weighed", unchanged.WeightMethod);
        Assert.Null(unchanged.DefaultNetKgPerPackage);
    }

    [Fact]
    public async Task Unknown_Variant_Gives_NotFound()
    {
        await using var db = _factory.CreateContext();
        Assert.IsType<NotFoundResult>(await new ProductsController(db).UpdateWeightSettings(999, new UpdateWeightSettingsDto { WeightMethod = "Weighed" }));
    }
}
