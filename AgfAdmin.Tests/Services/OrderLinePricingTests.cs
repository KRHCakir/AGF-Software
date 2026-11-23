using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: de orderregel met prijs, korting en btw, gelijk voor inkoop en verkoop (de verkoopregel: SalesLinePricingTests).
// Bedrag = aantal × prijs min korting op centen, btw per tarief per regel op centen en opgeteld (zoals de factuur), bestaande regels blijven
// zoals ze zijn opgeslagen, en korting en btw van een inkoopregel veranderen alleen met een Edit (regel 29).
public sealed class OrderLinePricingTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly PurchaseOrderEditHarness _h;

    public OrderLinePricingTests() => _h = new PurchaseOrderEditHarness(_factory);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void The_amount_is_quantity_times_price_minus_discount_on_cents_and_without_discount_exactly_quantity_times_price()
    {
        Assert.Equal(1250m, OrderLineMath.LineAmount(100m, 12.5m, null));
        Assert.Equal(1250m, OrderLineMath.LineAmount(100m, 12.5m, 0m));
        Assert.Equal(1125m, OrderLineMath.LineAmount(100m, 12.5m, 10m));
        // 3 × 13,345 = 40,035 → 40,04 zonder korting; met 2,5% korting 39,034125 → 39,03.
        Assert.Equal(40.04m, OrderLineMath.LineAmount(3m, 13.345m, null));
        Assert.Equal(39.03m, OrderLineMath.LineAmount(3m, 13.345m, 2.5m));
        Assert.Equal(0m, OrderLineMath.LineAmount(7m, 4.99m, 100m));
        Assert.Null(OrderLineMath.LineAmount(7m, null, 10m));
        // De inkoopregel rekent hetzelfde (de oude aanroep zonder korting verandert niet).
        Assert.Equal(PurchaseLineMath.LineTotal(3m, 13.345m), OrderLineMath.LineAmount(3m, 13.345m, null));
        Assert.Equal(39.03m, PurchaseLineMath.LineTotal(3m, 13.345m, 2.5m));
    }

    [Fact]
    public void Vat_is_per_rate_rounded_per_line_and_totals_name_lines_without_price_or_vat()
    {
        var summary = OrderLineMath.Summarize(new[]
        {
            new OrderLineMath.Line(10.05m, 9m),   // 0,9045 → 0,90
            new OrderLineMath.Line(10.05m, 9m),   // 0,90
            new OrderLineMath.Line(33.33m, 21m),  // 6,9993 → 7,00
            new OrderLineMath.Line(5m, 0m),
            new OrderLineMath.Line(null, 9m),     // zonder prijs
            new OrderLineMath.Line(12m, null),    // opgeslagen vóór deze ronde: geen btw-tarief
        });
        Assert.Equal(new[] { (0m, 5m, 0m), (9m, 20.10m, 1.80m), (21m, 33.33m, 7.00m) }, summary.VatRates.Select(r => (r.Percentage, r.Net, r.Vat)));
        Assert.Equal((70.43m, 8.80m, 79.23m), (summary.Net!.Value, summary.Vat!.Value, summary.Gross!.Value));
        Assert.Equal((1, 1), (summary.LinesWithoutPrice, summary.LinesWithoutVat));

        var empty = OrderLineMath.Summarize(new[] { new OrderLineMath.Line(null, 9m) });
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null, 0), (empty.Net, empty.Vat, empty.Gross, empty.VatRates.Count));
    }

    [Theory]
    [InlineData(-1, 9, "discountPercentage")]
    [InlineData(100.01, 9, "discountPercentage")]
    [InlineData(10.005, 9, "discountPercentage")]
    [InlineData(10, 6, "vatPercentage")]
    public async Task A_discount_outside_0_to_100_or_an_unknown_vat_rate_is_refused(double discount, double vat, string field)
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(seed);
        request.Lines[0].UnitPrice = 10m;
        request.Lines[0].DiscountPercentage = (decimal)discount;
        request.Lines[0].VatPercentage = (decimal)vat;
        await using var db = _factory.CreateContext();
        var purchase = await new SupplyCommitmentService(db).CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, purchase.Status);
        Assert.True(purchase.Errors.ContainsKey($"lines[0].{field}"));
    }

    [Fact]
    public async Task A_purchase_order_line_carries_discount_and_vat_into_its_amount_totals_per_rate_and_replay()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(seed);
        request.Lines[0].UnitPrice = 2.35m;
        request.Lines[0].DiscountPercentage = 5m;
        request.Lines[0].VatPercentage = 9m;
        request.Lines.Add(new CreateSupplyCommitmentLineDto { ProductVariantId = seed.KentId, ExpectedQuantity = 10m, ExpectedUnit = "box", UnitPrice = 4m, VatPercentage = 21m });
        ServiceResult<SupplyCommitmentDto> created;
        await using (var db = _factory.CreateContext())
        {
            created = await new SupplyCommitmentService(db).CreateAsync(request);
        }
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        // 221 × 2,35 = 519,35; min 5% = 493,3825 → 493,38.
        Assert.Equal(new decimal?[] { 493.38m, 40m }, created.Value.Lines.Select(l => l.LineTotal));

        var detail = await _h.DetailAsync(created.Value.Id);
        Assert.Equal(new[] { (5m, 9m), ((decimal?)null, (decimal?)21m) }.Select(x => ((decimal?)x.Item1, x.Item2)), detail.Lines.Select(l => (l.DiscountPercentage, l.VatPercentage)));
        var amounts = detail.Totals.Amounts;
        Assert.Equal(533.38m, detail.Totals.Amount);
        Assert.Equal(new[] { (9m, 493.38m, 44.40m), (21m, 40m, 8.40m) }, amounts.VatRates.Select(r => (r.Percentage, r.Net, r.Vat)));
        Assert.Equal((533.38m, 52.80m, 586.18m, 0, 0), (amounts.Net!.Value, amounts.Vat!.Value, amounts.Gross!.Value, amounts.LinesWithoutPrice, amounts.LinesWithoutVat));

        // Korting en btw horen bij de replay: dezelfde aanvraag is dezelfde order, een andere korting is een conflict.
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new SupplyCommitmentService(db).CreateAsync(request)).Status);
        }
        request.Lines[0].DiscountPercentage = 6m;
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Conflict, (await new SupplyCommitmentService(db).CreateAsync(request)).Status);
        }
    }

    [Fact]
    public async Task An_existing_purchase_line_stays_as_stored_and_discount_and_vat_change_only_with_an_edit_record()
    {
        var order = await _h.OrderAsync();
        var line = order.Lines.Single();
        Assert.Equal(((decimal?)null, (decimal?)null), (line.DiscountPercentage, line.VatPercentage));
        Assert.Equal((1, (decimal?)null), (order.Totals.Amounts.LinesWithoutPrice, order.Totals.Amounts.Net));

        var edit = PurchaseOrderEditHarness.EditOf(order);
        edit.Lines[0].UnitPrice = 2m;
        edit.Lines[0].DiscountPercentage = 10m;
        edit.Lines[0].VatPercentage = 9m;
        var saved = await _h.RunAsync(s => s.UpdateAsync(order.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        var change = Assert.Single(saved.Value.Changes);
        Assert.Contains(change.Fields, f => f.Field == "discountPercentage" && f.LineId == line.Id && f.Old == null && f.New == "10");
        Assert.Contains(change.Fields, f => f.Field == "vatPercentage" && f.LineId == line.Id && f.Old == null && f.New == "9");
        Assert.Equal(397.80m, saved.Value.Lines.Single().LineTotal); // 221 × 2 = 442, min 10%

        // Buiten het aanpassen om (zonder auditregel): regel 29 weigert het.
        await using var db = _factory.CreateContext();
        var stored = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == line.Id);
        stored.VatPercentage = 21m;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        stored = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == line.Id);
        stored.DiscountPercentage = null;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }
}
