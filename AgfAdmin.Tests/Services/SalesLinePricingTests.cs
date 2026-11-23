using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: de verkooporderregel met prijs, korting en btw zoals de inkoopregel (OrderLineMath), de totalen en de btw per tarief
// over de hele order (ook emballage), van order tot factuur op de cent gelijk, en prijs, korting en btw die na opslaan nooit veranderen (regel 36).
public sealed class SalesLinePricingTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public void Dispose() => _s.Dispose();

    [Theory]
    [InlineData(-1, 9, "discountPercentage")]
    [InlineData(100.01, 9, "discountPercentage")]
    [InlineData(10, 6, "vatPercentage")]
    public async Task A_discount_outside_0_to_100_or_an_unknown_vat_rate_is_refused(double discount, double vat, string field)
    {
        await _s.ArrangeAsync();
        var order = _s.Order((5m, "Stock"));
        order.Lines[0].UnitPrice = 10m;
        order.Lines[0].DiscountPercentage = (decimal)discount;
        order.Lines[0].VatPercentage = (decimal)vat;
        var sales = await _s.CreateAsync(order);
        Assert.Equal(ServiceResultStatus.Validation, sales.Status);
        Assert.True(sales.Errors.ContainsKey($"lines[0].{field}"));
    }

    private async Task<int> PackagingArticleAsync()
    {
        var articles = await CostArticleTestData.SeedStandardAsync(_s.Factory);
        return articles["Euro-pallet"];
    }

    private CreateSalesOrderDto PricedOrder(int packagingId)
    {
        var order = _s.Order((30m, "Stock"), (20m, "Expected"));
        order.Lines[0].UnitPrice = 13.35m;
        order.Lines[0].DiscountPercentage = 2.5m;
        order.Lines[0].VatPercentage = 9m;
        order.Lines[1].UnitPrice = 9.99m;
        order.Lines[1].VatPercentage = 21m;
        order.PackagingLines = new() { new CreateSalesOrderPackagingLineDto { CostArticleId = packagingId, Quantity = 2m, UnitPrice = 7.5m } };
        return order;
    }

    [Fact]
    public async Task A_sales_order_line_has_the_purchase_layout_price_discount_vat_and_order_totals_including_packaging()
    {
        await _s.ArrangeAsync();
        var packaging = await PackagingArticleAsync();
        var result = await _s.CreateAsync(PricedOrder(packaging));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var lines = result.Value.Lines;
        // 30 × 13,35 = 400,50 min 2,5% = 390,4875 → 390,49; 20 × 9,99 = 199,80.
        Assert.Equal(new decimal?[] { 390.49m, 199.80m }, lines.Select(l => l.LineTotal));
        Assert.Equal(new[] { ((decimal?)13.35m, (decimal?)2.5m, (decimal?)9m), (9.99m, null, 21m) }, lines.Select(l => (l.UnitPrice, l.DiscountPercentage, l.VatPercentage)));

        var totals = result.Value.Totals;
        Assert.Equal((50m, 590.29m), (totals.Quantity, totals.Amount!.Value));
        // Emballage (2 × 7,50 = 15,00) met de btw van het artikel (9%) telt mee in de btw-samenvatting. De btw is per regel op centen
        // (390,49 × 9% = 35,14; 15,00 × 9% = 1,35), zoals op de factuur.
        Assert.Equal(new[] { (9m, 405.49m, 36.49m), (21m, 199.80m, 41.96m) }, totals.Amounts.VatRates.Select(r => (r.Percentage, r.Net, r.Vat)));
        Assert.Equal((605.29m, 78.45m, 683.74m), (totals.Amounts.Net!.Value, totals.Amounts.Vat!.Value, totals.Amounts.Gross!.Value));
    }

    [Fact]
    public void The_total_quantity_is_never_summed_over_different_units()
    {
        var lines = new[] { new PurchaseLineMath.Line(30m, null, null, null, null), new PurchaseLineMath.Line(20m, null, null, null, null), new PurchaseLineMath.Line(5m, null, null, null, null) };
        var mixed = PurchaseOrderLineTotalsDto.From(lines, Array.Empty<OrderLineMath.Line>(), 1m, new[] { ("Box", 30m), ("Colli", 20m), ("Box", 5m) });
        Assert.Null(mixed.Quantity);
        Assert.Equal(new[] { ("Box", 35m), ("Colli", 20m) }, mixed.QuantitiesByUnit.Select(u => (u.Unit, u.Quantity)));

        var single = PurchaseOrderLineTotalsDto.From(lines, Array.Empty<OrderLineMath.Line>(), 1m, new[] { ("Box", 30m), ("Box", 20m), ("Box", 5m) });
        Assert.Equal(55m, single.Quantity);
    }

    [Fact]
    public async Task From_order_to_invoice_the_amounts_and_vat_per_rate_are_the_same_to_the_cent()
    {
        await _s.ArrangeAsync();
        var packaging = await PackagingArticleAsync();
        var order = (await _s.CreateAsync(PricedOrder(packaging))).Value;

        // Een factuur neemt de regels over (aantal, prijs, korting, btw) en rekent met InvoiceMath, zoals de inkoopfactuur.
        var invoiceLines = order.Lines.Select(l => (l.Quantity, l.UnitPrice!.Value, l.DiscountPercentage ?? 0m, l.VatPercentage!.Value))
            .Concat(order.PackagingLines.Select(l => ((decimal)l.Quantity, l.UnitPrice!.Value, 0m, l.VatPercentage)))
            .Select(l => { var net = InvoiceMath.LineNet(l.Item1, l.Item2, l.Item3); return (Net: net, Vat: InvoiceMath.LineVat(net, l.Item4), Rate: l.Item4); })
            .ToList();
        var invoice = InvoiceMath.Totals(invoiceLines.Select(l => (l.Net, l.Vat)));
        var amounts = order.Totals.Amounts;
        Assert.Equal((invoice.Net, invoice.Vat, invoice.Gross), (amounts.Net!.Value, amounts.Vat!.Value, amounts.Gross!.Value));
        Assert.Equal(invoiceLines.GroupBy(l => l.Rate).OrderBy(g => g.Key).Select(g => (g.Key, g.Sum(l => l.Net), g.Sum(l => l.Vat))),
            amounts.VatRates.Select(r => (r.Percentage, r.Net, r.Vat)));
    }

    [Fact]
    public async Task Price_discount_and_vat_are_part_of_the_sales_replay_and_never_change_after_saving_while_reserving_still_works()
    {
        await _s.ArrangeAsync();
        var packaging = await PackagingArticleAsync();
        var request = PricedOrder(packaging);
        var first = await _s.CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        Assert.Equal(ServiceResultStatus.Ok, (await _s.CreateAsync(request)).Status);
        request.Lines[1].DiscountPercentage = 1m;
        Assert.Equal(ServiceResultStatus.Conflict, (await _s.CreateAsync(request)).Status);

        // Reserveren raakt de regel aan (Version), niet de prijs: dat mag gewoon.
        var line = first.Value.Lines[0];
        Assert.Equal(ServiceResultStatus.Created, (await _s.ReserveAsync(first.Value.Id, line.Id, _s.StockContentId, 10m, 40m)).Status);

        await using var db = _s.Factory.CreateContext();
        var stored = await db.SalesOrderLines.SingleAsync(l => l.Id == line.Id);
        Assert.Equal((13.35m, 2.5m, 9m), (stored.UnitPrice!.Value, stored.DiscountPercentage!.Value, stored.VatPercentage!.Value));
        stored.UnitPrice = 14m;
        var error = await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        Assert.Contains("never change", error.Message);
        db.ChangeTracker.Clear();
        stored = await db.SalesOrderLines.SingleAsync(l => l.Id == line.Id);
        stored.VatPercentage = 21m;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task An_existing_sales_line_without_price_stays_readable_and_is_counted_apart()
    {
        await _s.ArrangeAsync();
        var result = await _s.CreateAsync(_s.Order((5m, "Stock")));
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null), (line.UnitPrice, line.DiscountPercentage, line.VatPercentage, line.LineTotal));
        Assert.Equal(((decimal?)null, 1, (decimal?)null), (result.Value.Totals.Amount, result.Value.Totals.LinesWithoutPrice, result.Value.Totals.Amounts.Gross));
    }
}
