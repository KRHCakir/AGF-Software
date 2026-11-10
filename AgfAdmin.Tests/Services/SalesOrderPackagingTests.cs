using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Emballage als regel op een verkooporder naast de goederen (of alleen emballage): heel aantal, prijs per stuk (0,00 mag) en de btw van
// het artikel, zonder invloed op voorraad, reserveren, tekort, beschikbaarheid of de opschoonpagina; replay; guard-regel 27.
public sealed class SalesOrderPackagingTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();
    private Dictionary<string, int> _articles = new();

    public void Dispose() => _s.Dispose();

    private async Task ArrangeAsync()
    {
        await _s.ArrangeAsync();
        _articles = await CostArticleTestData.SeedStandardAsync(_s.Factory);
    }

    private CreateSalesOrderDto WithPackaging(CreateSalesOrderDto order, params (string Name, decimal Quantity, decimal? Price)[] lines)
    {
        order.PackagingLines = lines.Select(l => new CreateSalesOrderPackagingLineDto { CostArticleId = _articles[l.Name], Quantity = l.Quantity, UnitPrice = l.Price }).ToList();
        return order;
    }

    [Fact]
    public async Task An_Order_Carries_Packaging_Next_To_The_Goods_And_Stock_Availability_And_Cleanup_Do_Not_See_It()
    {
        await ArrangeAsync();
        var reference = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value.Lines.Single().Balance;
        var result = await _s.CreateAsync(WithPackaging(_s.Order((30m, "Stock")), ("Pallet wegwerp", 1m, 7.50m), ("Euro-pallet", 2m, 0m)));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(new[] { ("Pallet wegwerp", 1, (decimal?)7.50m, 9m, (decimal?)7.50m), ("Euro-pallet", 2, 0m, 9m, 0m) },
            result.Value.PackagingLines.Select(l => (l.Name, l.Quantity, l.UnitPrice, l.VatPercentage, l.LineTotal)));
        var line = Assert.Single(result.Value.Lines);

        // Alleen de 30 colli tellen: verkocht en vrij zijn wat de goederenregel alleen ook zou geven.
        Assert.Equal((reference.Sold + 30m, reference.Free - 30m, reference.Stock), (line.Balance.Sold, line.Balance.Free, line.Balance.Stock));
        Assert.Empty(result.Value.ShortageWarnings);

        // Reserveren gaat per goederenregel; daarna staat er niets meer open, ook al heeft de order emballage.
        Assert.Equal(ServiceResultStatus.Created, (await _s.ReserveAsync(result.Value.Id, line.Id, _s.StockContentId, 30m, 120m)).Status);
        await using var db = _s.Factory.CreateContext();
        var order = (await new SalesOrderService(db).GetAsync(result.Value.Id)).Value;
        Assert.Equal((30m, 0m, 2), (order.Lines.Single().ReservedQuantity, order.Lines.Single().OpenQuantity, order.PackagingLines.Count));

        var overview = (await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery())).Value;
        var cleanup = await new ArticleService(db).SizeClassCleanupAsync();
        var names = CostArticleRules.StandardPackagingNames;
        Assert.DoesNotContain(overview.Variants, v => names.Contains(v.ProductName) || names.Contains(v.VariantName));
        Assert.DoesNotContain(cleanup, item => names.Contains(item.Description));
        Assert.Equal(0, await db.ProductVariants.CountAsync(v => names.Contains(v.Name)));
    }

    [Fact]
    public async Task An_Order_With_Only_Packaging_Is_Allowed_Is_No_Goods_Order_And_Is_Found_By_The_Article()
    {
        await ArrangeAsync();
        var result = await _s.CreateAsync(WithPackaging(_s.Order(), ("Euro-pallet", 10m, null)));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Empty(result.Value.Lines);
        Assert.Equal((false, false, false, false), (result.Value.Fulfilled, result.Value.Delivered, result.Value.Shortage, result.Value.AwaitingStock));
        var packaging = Assert.Single(result.Value.PackagingLines);
        Assert.Equal((10, (decimal?)null, (decimal?)null), (packaging.Quantity, packaging.UnitPrice, packaging.LineTotal));

        var none = await _s.CreateAsync(_s.Order());
        Assert.Equal(ServiceResultStatus.Validation, none.Status);
        Assert.Contains("lines", none.Errors.Keys);

        await using var db = _s.Factory.CreateContext();
        var found = (await new SalesOrderService(db).ListAsync(new SalesOrderFilter { Q = "euro-pal" })).Value;
        Assert.Equal(new[] { result.Value.Id }, found.Select(o => o.Id));
    }

    [Theory]
    [InlineData("Euro-pallet", 1.5, 1.0, "packagingLines[0].quantity")]
    [InlineData("Euro-pallet", 0.0, 1.0, "packagingLines[0].quantity")]
    [InlineData("Euro-pallet", 2.0, -1.0, "packagingLines[0].unitPrice")]
    [InlineData("Euro-pallet", 2.0, 1.255, "packagingLines[0].unitPrice")]
    [InlineData("Handling", 2.0, 1.0, "packagingLines[0].costArticleId")]
    public async Task Invalid_Packaging_Is_Refused_And_Nothing_Is_Saved(string name, double quantity, double price, string field)
    {
        await ArrangeAsync();
        var result = await _s.CreateAsync(WithPackaging(_s.Order((5m, "Stock")), (name, (decimal)quantity, (decimal)price)));
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(field, result.Errors.Keys);
        await using var db = _s.Factory.CreateContext();
        Assert.Equal(0, await db.SalesOrders.CountAsync());
    }

    [Fact]
    public async Task A_Retry_Returns_The_Same_Order_Even_After_The_Article_Went_Inactive_And_Other_Packaging_Is_409()
    {
        await ArrangeAsync();
        var request = WithPackaging(_s.Order((10m, "Stock")), ("Krat", 4m, 1.10m));
        var first = await _s.CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        await using (var db = _s.Factory.CreateContext())
        {
            await new CostArticleService(db).SetActiveAsync(_articles["Krat"], new SetCostArticleActiveDto { IsActive = false }, default, CostArticleRules.KindPackaging);
        }
        var again = await _s.CreateAsync(request);
        Assert.Equal((ServiceResultStatus.Ok, first.Value.Id), (again.Status, again.Value.Id));

        request.PackagingLines[0].UnitPrice = 1.20m;
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(request)).Status);
        request.RequestId = Guid.NewGuid();
        var inactive = await _s.CreateAsync(request);
        Assert.Contains("inactive", Assert.Single(inactive.Errors["packagingLines[0].costArticleId"]));
    }

    [Fact]
    public async Task Rule_27_A_Packaging_Line_Never_Changes_Needs_A_Packaging_Article_And_A_Kind_Never_Changes()
    {
        await ArrangeAsync();
        var order = (await _s.CreateAsync(WithPackaging(_s.Order((5m, "Stock")), ("Pallet wegwerp", 1m, 7.50m)))).Value;
        await using var db = _s.Factory.CreateContext();

        var line = await db.SalesOrderPackagingLines.SingleAsync();
        line.UnitPrice = 8m;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.SalesOrderPackagingLines.Remove(await db.SalesOrderPackagingLines.SingleAsync());
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.SalesOrderPackagingLines.Add(new SalesOrderPackagingLine { SalesOrderId = order.Id, CostArticleId = _articles["Handling"], Quantity = 1, VatPercentage = 9m });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var krat = await db.CostArticles.SingleAsync(c => c.Id == _articles["Krat"]);
        (krat.Kind, krat.VatPercentage) = (CostArticleRules.KindCost, null);
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }
}
