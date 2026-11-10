using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Emballage-artikelen (fust): standaard Pallet wegwerp, Euro-pallet en Krat met 9% btw; zelf aanmaken met prijs en btw; alleen via de
// emballage-endpoints te wijzigen, en kostensoorten niet via emballage (en andersom).
public sealed class PackagingArticleTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();
    private Dictionary<string, int> _articles = new();

    public void Dispose() => _s.Dispose();

    private async Task ArrangeAsync()
    {
        await _s.ArrangeAsync();
        _articles = await CostArticleTestData.SeedStandardAsync(_s.Factory);
    }

    [Fact]
    public async Task The_Standard_Packaging_Has_9_Percent_Vat_And_No_Price_And_Is_Listed_Apart_From_The_Costs()
    {
        await ArrangeAsync();
        await using var db = _s.Factory.CreateContext();
        var service = new CostArticleService(db);
        var packaging = await service.ListAsync(false, default, CostArticleRules.KindPackaging);
        Assert.Equal(CostArticleRules.StandardPackagingNames, packaging.Select(a => a.Name));
        Assert.All(packaging, a => Assert.Equal((true, (decimal?)9m, (decimal?)null), (a.IsStandard, a.VatPercentage, a.DefaultUnitPrice)));
        Assert.Equal(CostArticleRules.StandardNames, (await service.ListAsync(false, default, CostArticleRules.KindCost)).Select(a => a.Name));
        // De inkoopfactuur kiest uit beide soorten: een leverancier kan pallets of kratten in rekening brengen.
        Assert.Equal(9, (await service.ListAsync(false)).Count);
    }

    [Fact]
    public async Task A_Packaging_Article_Gets_A_Checked_Price_And_Vat_That_Can_Change_Only_Through_Packaging()
    {
        await ArrangeAsync();
        await using var db = _s.Factory.CreateContext();
        var service = new CostArticleService(db);
        var created = await service.CreatePackagingAsync(new SavePackagingArticleDto { RequestId = Guid.NewGuid(), Name = " Fust  CBL ", DefaultUnitPrice = 3.25m, VatPercentage = 9m });
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(("Fust CBL", "Packaging", (decimal?)3.25m), (created.Value.Name, created.Value.Kind, created.Value.DefaultUnitPrice));
        Assert.Equal(ServiceResultStatus.Conflict, (await service.CreatePackagingAsync(new SavePackagingArticleDto { RequestId = Guid.NewGuid(), Name = "krat", VatPercentage = 9m })).Status);
        var invalid = await service.CreatePackagingAsync(new SavePackagingArticleDto { RequestId = Guid.NewGuid(), Name = "Doos", DefaultUnitPrice = 1.255m });
        Assert.Equal(new[] { "defaultUnitPrice", "vatPercentage" }, invalid.Errors.Keys.OrderBy(k => k));

        var updated = await service.UpdatePackagingAsync(_articles["Euro-pallet"], new SavePackagingArticleDto { DefaultUnitPrice = 12.50m, VatPercentage = 21m });
        Assert.Equal(((decimal?)12.50m, (decimal?)21m), (updated.Value.DefaultUnitPrice, updated.Value.VatPercentage));
        // Een kostensoort is geen emballage, en emballage valt niet onder het kostenbeheer van Inkoop.
        Assert.Equal(ServiceResultStatus.NotFound, (await service.UpdatePackagingAsync(_articles["Handling"], new SavePackagingArticleDto { VatPercentage = 9m })).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await service.SetActiveAsync(_articles["Handling"], new SetCostArticleActiveDto { IsActive = false }, default, CostArticleRules.KindPackaging)).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await service.SetActiveAsync(_articles["Krat"], new SetCostArticleActiveDto { IsActive = false })).Status);
    }
}
