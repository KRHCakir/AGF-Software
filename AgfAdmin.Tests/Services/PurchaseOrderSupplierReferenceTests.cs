using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Het ordernummer van de leverancier als hoofdnummer: één sleutel (hoofdletterongevoelig, zonder spaties en streepjes) voor zoeken,
// filteren en de dubbelcontrole; hetzelfde nummer bij dezelfde leverancier is een melding en nooit een fout.
public class PurchaseOrderSupplierReferenceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(int Id, string OrderNumber)> OrderAsync(PurchaseOrderSeed seed, string reference, int? vendorId = null)
    {
        var request = PurchaseOrderTestData.OrderRequest(seed, vendorId: vendorId);
        request.SupplierReference = reference;
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, request);
        return (order.Id, order.OrderNumber);
    }

    [Theory]
    [InlineData("45-21", "4521")]
    [InlineData(" 45 21 ", "4521")]
    [InlineData("ab-12 x", "AB12X")]
    [InlineData(" - ", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Key_IgnoresCaseSpacesAndDashes(string reference, string expected) =>
        Assert.Equal(expected, PurchaseOrderRules.SupplierReferenceKey(reference));

    [Fact]
    public async Task SameVendorAndNumber_IsAWarningNotAnError_FoundOnTheKey()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var first = await OrderAsync(seed, "45-21");
        // Dezelfde leverancier en hetzelfde nummer (anders geschreven): de order wordt gewoon aangemaakt.
        var second = await OrderAsync(seed, "45 21");
        var otherVendor = await OrderAsync(seed, "4521", seed.OtherVendorId);
        await OrderAsync(seed, "45210");

        await using var db = _factory.CreateContext();
        var service = new PurchaseOrderService(db);
        var matches = await service.FindBySupplierReferenceAsync("4521", seed.VendorId);
        Assert.Equal(new[] { first.Id, second.Id }, matches.Select(m => m.Id));
        Assert.Equal(new[] { "45-21", "45 21" }, matches.Select(m => m.SupplierReference));
        Assert.Equal(first.OrderNumber, matches[0].OrderNumber);
        Assert.Equal(otherVendor.Id, Assert.Single(await service.FindBySupplierReferenceAsync("45-21", seed.OtherVendorId)).Id);
        Assert.Empty(await service.FindBySupplierReferenceAsync(" - ", seed.VendorId));
    }

    [Fact]
    public async Task List_SearchAndFilterUseTheKey_OwnNumberStillWorks_EmptyFallsBackToTheOrderNumber()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var dashed = await OrderAsync(seed, "45-21");
        var other = await OrderAsync(seed, "FO-7");
        var without = await OrderAsync(seed, "   ");

        await using var db = _factory.CreateContext();
        var service = new PurchaseOrderService(db);
        async Task<int[]> Search(string query = null, string reference = null) =>
            (await service.ListAsync(query, "all", null, 1, 50, reference)).Value.Items.Select(i => i.Id).OrderBy(id => id).ToArray();

        Assert.Equal(new[] { dashed.Id }, await Search("4521"));
        Assert.Equal(new[] { dashed.Id }, await Search("45 21"));
        Assert.Equal(new[] { dashed.Id }, await Search(dashed.OrderNumber));
        Assert.Equal(new[] { dashed.Id }, await Search(reference: "45 2"));
        Assert.Equal(new[] { other.Id }, await Search(reference: "fo7"));
        Assert.Empty(await Search(reference: dashed.OrderNumber));
        Assert.Equal(new[] { dashed.Id, other.Id, without.Id }, await Search(reference: " "));

        // Geen ordernummer van de leverancier: leeg opgeslagen, het eigen IO-nummer blijft het nummer van de order.
        var item = (await service.ListAsync(without.OrderNumber, "all", null, 1, 50)).Value.Items.Single();
        Assert.Null(item.SupplierReference);
        Assert.StartsWith("IO-", item.OrderNumber);
    }
}
