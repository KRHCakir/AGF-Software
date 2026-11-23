using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: dienstregels op een verkooporder (doorberekende handling of transport) met een Dienst-artikel en het btw-tarief van
// het artikel (standaard 21%). Ze tellen in de totalen en de btw per tarief, maar niet in voorraad, pallets of kg, en staan niet op de
// laadbon of de transportopdracht; ze veranderen nooit na opslaan (regel 38). Emballage werkt zoals altijd.
public sealed class SalesOrderServiceLineTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();
    private Dictionary<string, int> _articles = new();

    public void Dispose() => _s.Dispose();

    private async Task ArrangeAsync()
    {
        await _s.ArrangeAsync();
        _articles = await CostArticleTestData.SeedStandardAsync(_s.Factory);
    }

    private CreateSalesOrderDto WithServices(CreateSalesOrderDto order, params (string Name, decimal Quantity, decimal? Price, decimal? Discount, decimal? Vat)[] lines)
    {
        order.ServiceLines = lines.Select(l => new CreateSalesOrderServiceLineDto { CostArticleId = _articles[l.Name], Quantity = l.Quantity, UnitPrice = l.Price, DiscountPercentage = l.Discount, VatPercentage = l.Vat }).ToList();
        return order;
    }

    [Fact]
    public async Task A_service_line_counts_in_the_amounts_and_vat_per_rate_but_not_in_stock_pallets_or_kg()
    {
        await ArrangeAsync();
        var reference = (await _s.CreateAsync(_s.Order((5m, "Stock")))).Value.Lines.Single().Balance;
        var order = _s.Order((30m, "Stock"));
        order.Lines[0].UnitPrice = 10m;
        order.Lines[0].VatPercentage = 9m;
        var result = await _s.CreateAsync(WithServices(order, ("Handling", 2m, 12.50m, null, null), ("Transport", 1m, 80m, 10m, 9m)));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(new[] { ("Handling", 2m, (decimal?)12.50m, 21m, (decimal?)25m), ("Transport", 1m, 80m, 9m, 72m) },
            result.Value.ServiceLines.Select(l => (l.Name, l.Quantity, l.UnitPrice, l.VatPercentage, l.LineTotal)));

        // Alleen de 30 colli tellen in de stand en in de totalenrij (aantal, kg, pallets); de diensten alleen in de bedragen.
        var line = result.Value.Lines.Single();
        Assert.Equal((reference.Sold + 30m, reference.Free - 30m), (line.Balance.Sold, line.Balance.Free));
        Assert.Equal((30m, 300m), (result.Value.Totals.Quantity, result.Value.Totals.Amount!.Value));
        Assert.Equal(new[] { (9m, 372m, 33.48m), (21m, 25m, 5.25m) }, result.Value.Totals.Amounts.VatRates.Select(r => (r.Percentage, r.Net, r.Vat)));
        Assert.Equal(435.73m, result.Value.Totals.Amounts.Gross);
    }

    [Fact]
    public async Task Only_a_service_article_is_allowed_an_order_with_only_services_is_fine_and_the_replay_compares_the_services()
    {
        await ArrangeAsync();
        var packaging = await _s.CreateAsync(WithServices(_s.Order(), ("Euro-pallet", 1m, 7.5m, null, null)));
        Assert.Equal(ServiceResultStatus.Validation, packaging.Status);
        Assert.True(packaging.Errors.ContainsKey("serviceLines[0].costArticleId"));
        var bad = await _s.CreateAsync(WithServices(_s.Order(), ("Handling", 0m, 7.5m, 120m, 6m)));
        Assert.Equal(new[] { "serviceLines[0].discountPercentage", "serviceLines[0].quantity", "serviceLines[0].vatPercentage" }, bad.Errors.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var request = WithServices(_s.Order(), ("Transport", 1m, 95m, null, null));
        var only = await _s.CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, only.Status);
        Assert.Empty(only.Value.Lines);
        Assert.Equal(ServiceResultStatus.Ok, (await _s.CreateAsync(request)).Status);
        request.ServiceLines[0].UnitPrice = 96m;
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(request)).Status);
    }

    [Fact]
    public async Task A_replay_that_omits_or_adds_the_vat_rate_is_a_conflict()
    {
        await ArrangeAsync();
        var request = WithServices(_s.Order(), ("Transport", 1m, 95m, null, 9m));
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(request)).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await _s.CreateAsync(request)).Status);
        request.ServiceLines[0].VatPercentage = null;
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(request)).Status);

        var omitted = WithServices(_s.Order(), ("Transport", 1m, 95m, null, null));
        Assert.Equal(ServiceResultStatus.Created, (await _s.CreateAsync(omitted)).Status);
        omitted.ServiceLines[0].VatPercentage = 21m;
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(omitted)).Status);
    }

    [Fact]
    public async Task A_service_line_is_on_neither_the_loading_slip_nor_the_transport_order_and_never_changes()
    {
        await ArrangeAsync();
        var created = (await _s.CreateAsync(WithServices(_s.Order((5m, "Stock")), ("Handling", 1m, 25m, null, null)))).Value;
        await using var db = _s.Factory.CreateContext();
        Assert.Equal(ServiceResultStatus.Ok, (await new LoadingSlipService(db).ConfirmAsync(created.Id, new ConfirmSalesOrderDto())).Status);
        var slip = await db.SalesOrderDocuments.AsNoTracking().SingleAsync(d => d.SalesOrderId == created.Id);
        Assert.DoesNotContain("Handling", slip.SnapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tomaat", slip.SnapshotJson, StringComparison.OrdinalIgnoreCase);

        var order = await db.SalesOrders.Include(o => o.Lines).ThenInclude(l => l.ProductVariant).ThenInclude(v => v.Product).Include(o => o.PackagingLines)
            .Include(o => o.ServiceLines).ThenInclude(l => l.CostArticle).Include(o => o.Customer).SingleAsync(o => o.Id == created.Id);
        order.Transport = new SalesOrderTransport { Mode = SalesTransportRules.ModeOwn };
        var transport = TransportOrderBuilder.Serialize(TransportOrderBuilder.Build(order, new CompanySettings { CompanyName = "Fictief BV" }));
        Assert.DoesNotContain("Handling", transport, StringComparison.OrdinalIgnoreCase);
        db.ChangeTracker.Clear();

        var stored = await db.SalesOrderServiceLines.SingleAsync(l => l.SalesOrderId == created.Id);
        stored.UnitPrice = 30m;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.SalesOrderServiceLines.Remove(await db.SalesOrderServiceLines.SingleAsync(l => l.SalesOrderId == created.Id));
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }
}
