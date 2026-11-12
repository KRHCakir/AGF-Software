using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Stap 4b, terugvinden: de order toont haar facturen (bedrag en verschil met de bon) en per regel wat gefactureerd en open is; de lijst
// zegt Gefactureerd / Deels / Niet en filtert daarop; zoeken op factuurnummer vindt de order en zoeken op het ordernummer de factuur;
// het factuurformulier krijgt de orders van de leverancier met wat er per regel nog openstaat.
public class PurchaseInvoiceOrderReadTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();
    private PurchaseOrderSeed _seed;

    private async Task<SupplyCommitmentDto> OrderAsync(string reference, decimal quantity = 264m, decimal? price = 13.50m, int? vendorId = null)
    {
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(_seed, quantity: quantity, vendorId: vendorId);
        request.SupplierReference = reference;
        request.Lines[0].ExpectedNetWeightKg = null;
        request.Lines[0].UnitPrice = price;
        return await PurchaseOrderTestData.CreateOrderAsync(_factory, request);
    }

    private async Task<PurchaseInvoiceDto> InvoiceAsync(string number, int orderLineId, decimal quantity, decimal price = 13.50m, string decision = null)
    {
        await using var db = _factory.CreateContext();
        var result = await new PurchaseInvoiceService(db).CreateAsync(new CreatePurchaseInvoiceDto
        {
            RequestId = Guid.NewGuid(), VendorId = _seed.VendorId, InvoiceNumber = number, InvoiceDate = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), OrderPriceDecision = decision,
            Lines = new() { new CreatePurchaseInvoiceLineDto { ProductVariantId = _seed.HassId, Description = "Avocado · Hass", Quantity = quantity, Unit = "box", UnitPrice = price, VatPercentage = 9m, SupplyCommitmentLineId = orderLineId } },
        });
        Assert.True(result.Status == ServiceResultStatus.Created, string.Join(" | ", result.Errors.SelectMany(e => e.Value)));
        return result.Value;
    }

    private async Task<T> WithOrdersAsync<T>(Func<PurchaseOrderService, Task<T>> call)
    {
        await using var db = _factory.CreateContext();
        return await call(new PurchaseOrderService(db));
    }

    [Fact]
    public async Task The_order_shows_its_invoices_with_the_difference_and_per_line_what_is_invoiced_and_open()
    {
        var order = await OrderAsync("NP-1");
        var lineId = order.Lines[0].Id;
        var notYet = await WithOrdersAsync(s => s.GetAsync(order.Id));
        Assert.Equal(("None", 0m, 264m), (notYet.Value.BillingStatus, notYet.Value.Lines[0].InvoicedQuantity, notYet.Value.Lines[0].OpenToInvoiceQuantity));

        var first = await InvoiceAsync("F-R-1", lineId, 200m, 12.50m, PurchaseInvoiceOrderLinkRules.RecordDifference);
        var partial = (await WithOrdersAsync(s => s.GetAsync(order.Id))).Value;
        Assert.Equal(("Partial", 200m, 64m), (partial.BillingStatus, partial.Lines[0].InvoicedQuantity, partial.Lines[0].OpenToInvoiceQuantity));
        var linked = Assert.Single(partial.Invoices);
        Assert.Equal((first.Id, "F-R-1", 2500.00m, -200.00m, PurchaseInvoiceOrderLinkRules.RecordDifference), (linked.Id, linked.InvoiceNumber, linked.AmountExclVat, linked.Difference, linked.Decision));

        await InvoiceAsync("F-R-2", lineId, 64m);
        var full = (await WithOrdersAsync(s => s.GetAsync(order.Id))).Value;
        Assert.Equal(("Full", 0m), (full.BillingStatus, full.Lines[0].OpenToInvoiceQuantity));
        Assert.Equal(new[] { "F-R-1", "F-R-2" }, full.Invoices.Select(i => i.InvoiceNumber));
        Assert.Equal(0m, full.Invoices[1].Difference);
    }

    [Fact]
    public async Task The_list_says_invoiced_partly_or_not_filters_on_it_and_search_works_both_ways()
    {
        var none = await OrderAsync("NP-NONE");
        var part = await OrderAsync("NP-PART");
        var full = await OrderAsync("NP-FULL");
        await InvoiceAsync("F-PART", part.Lines[0].Id, 100m);
        await InvoiceAsync("F-FULL", full.Lines[0].Id, 264m);

        var all = (await WithOrdersAsync(s => s.ListAsync(null, "all", null, 1, 50))).Value.Items.ToDictionary(i => i.Id, i => i.BillingStatus);
        Assert.Equal(("None", "Partial", "Full"), (all[none.Id], all[part.Id], all[full.Id]));
        var partial = (await WithOrdersAsync(s => s.ListAsync(null, "all", null, 1, 50, billing: "partial"))).Value.Items;
        Assert.Equal(new[] { part.Id }, partial.Select(i => i.Id));
        Assert.Equal(ServiceResultStatus.Validation, (await WithOrdersAsync(s => s.ListAsync(null, "all", null, 1, 50, billing: "half"))).Status);

        // Factuurnummer → order, en ordernummer (van de leverancier of IO) → factuur.
        var byInvoice = (await WithOrdersAsync(s => s.ListAsync("f-full", "all", null, 1, 50))).Value.Items;
        Assert.Equal(new[] { full.Id }, byInvoice.Select(i => i.Id));
        await using var db = _factory.CreateContext();
        var invoices = new PurchaseInvoiceService(db);
        Assert.Equal(new[] { "F-PART" }, (await invoices.ListAsync(default, "np-part")).Select(i => i.InvoiceNumber));
        Assert.Equal(new[] { "F-FULL" }, (await invoices.ListAsync(default, full.OrderNumber)).Select(i => i.InvoiceNumber));
        Assert.Equal(2, (await invoices.ListAsync()).Count);
    }

    [Fact]
    public async Task The_invoice_form_gets_the_suppliers_orders_with_what_is_still_open_per_line()
    {
        var open = await OrderAsync("NP-OPEN");
        var done = await OrderAsync("NP-DONE");
        var other = await OrderAsync("OTHER-1", vendorId: _seed.OtherVendorId);
        var cancelled = await OrderAsync("NP-CANCEL");
        await InvoiceAsync("F-DONE", done.Lines[0].Id, 264m);
        await InvoiceAsync("F-OPEN", open.Lines[0].Id, 64m);
        await using (var db = _factory.CreateContext())
        {
            var order = await db.SupplyCommitments.FindAsync(cancelled.Id);
            order!.Status = SupplyRules.StatusCancelled;
            await db.SaveChangesAsync();
        }

        var candidates = await WithOrdersAsync(s => s.InvoiceCandidatesAsync(_seed.VendorId, null, null));
        var candidate = Assert.Single(candidates);
        Assert.Equal((open.Id, "NP-OPEN", "Definitive", false), (candidate.Id, candidate.SupplierReference, candidate.DocumentStatus, candidate.PriceAdjustable));
        var line = Assert.Single(candidate.Lines);
        Assert.Equal((264m, 0m, 64m, 200m, 13.50m, "box", "Avocado", "Hass"), (line.ExpectedQuantity, line.ReceivedQuantity, line.InvoicedQuantity, line.OpenQuantity, line.UnitPrice, line.Unit, line.ProductName, line.ProductVariantName));
        Assert.Equal(new[] { "F-OPEN" }, candidate.InvoiceNumbers);

        // Zoeken vindt ook een volledig gefactureerde order (op factuurnummer of ordernummer); een order van een ander of geannuleerd nooit.
        Assert.Equal(new[] { done.Id }, (await WithOrdersAsync(s => s.InvoiceCandidatesAsync(_seed.VendorId, "F-DONE", null))).Select(c => c.Id));
        Assert.Empty(await WithOrdersAsync(s => s.InvoiceCandidatesAsync(_seed.VendorId, "OTHER-1", null)));
        Assert.Empty(await WithOrdersAsync(s => s.InvoiceCandidatesAsync(_seed.VendorId, "NP-CANCEL", null)));
        Assert.Equal(new[] { done.Id }, (await WithOrdersAsync(s => s.InvoiceCandidatesAsync(_seed.VendorId, null, done.Id))).Select(c => c.Id));
        Assert.Empty(await WithOrdersAsync(s => s.InvoiceCandidatesAsync(_seed.VendorId, null, other.Id)));

        // Een nieuwe open magazijnorder kan bij een verschil worden aangepast.
        await OrderAsync("NP-FRESH");
        Assert.True((await WithOrdersAsync(s => s.InvoiceCandidatesAsync(_seed.VendorId, "NP-FRESH", null))).Single().PriceAdjustable);
    }

    [Fact]
    public async Task An_order_with_only_an_old_invoice_reference_is_not_offered_without_a_search_term()
    {
        var legacy = await OrderAsync("NP-LEGACY");
        await using (var db = _factory.CreateContext())
        {
            db.SupplyCommitmentInvoiceRefs.Add(new SupplyCommitmentInvoiceRef
            {
                RequestId = Guid.NewGuid(), SupplyCommitmentId = legacy.Id, Kind = PurchaseOrderRules.KindInvoice, InvoiceNumber = "F-OLD", InvoiceNumberKey = "F-OLD",
                ActorReference = "test", CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        Assert.Empty(await WithOrdersAsync(s => s.InvoiceCandidatesAsync(_seed.VendorId, null, null)));
        Assert.Equal(new[] { legacy.Id }, (await WithOrdersAsync(s => s.InvoiceCandidatesAsync(_seed.VendorId, "NP-LEGACY", null))).Select(c => c.Id));
    }
}
