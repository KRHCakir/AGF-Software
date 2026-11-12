using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Stap 4b: een inkoopfactuur echt koppelen aan de bon. Vóór deze stap bestond de koppeling alleen als verwijzing ("Factuur ontvangen":
// nummer en bedrag op de order, geen inkoopfactuur; "Verwijzing ontvangst": vrije tekst), dus na verversen hing er niets aan elkaar.
// Nu factureert elke gekoppelde regel een orderregel, komt alles terug na opnieuw laden, wordt de order definitief en vraagt een
// prijsverschil een keuze: bon aanpassen (met auditregel) of het verschil vastleggen.
public class PurchaseInvoiceOrderLinkTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    private PurchaseOrderSeed _seed;

    // De order uit de test van de eigenaar: Avocado Hass, 264 × 13,50 = 3.564,00.
    private async Task<SupplyCommitmentDto> OrderAsync(decimal quantity = 264m, decimal? price = 13.50m, int? vendorId = null)
    {
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(_seed, quantity: quantity, vendorId: vendorId);
        request.Lines[0].ExpectedNetWeightKg = null;
        request.Lines[0].UnitPrice = price;
        return await PurchaseOrderTestData.CreateOrderAsync(_factory, request);
    }

    private CreatePurchaseInvoiceDto Invoice(string number, int orderLineId, decimal quantity = 264m, decimal price = 13.50m, string decision = null, bool? acceptOver = null, int? vendorId = null) => new()
    {
        RequestId = Guid.NewGuid(), VendorId = vendorId ?? _seed.VendorId, InvoiceNumber = number, InvoiceDate = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc),
        OrderPriceDecision = decision, AcceptOverInvoicing = acceptOver,
        Lines = new() { new CreatePurchaseInvoiceLineDto { ProductVariantId = _seed.HassId, Description = "Avocado · Hass", Quantity = quantity, Unit = "box", UnitPrice = price, VatPercentage = 9m, SupplyCommitmentLineId = orderLineId } },
    };

    private async Task<ServiceResult<PurchaseInvoiceDto>> SaveAsync(CreatePurchaseInvoiceDto request)
    {
        await using var db = _factory.CreateContext();
        return await new PurchaseInvoiceService(db).CreateAsync(request);
    }

    private async Task<PurchaseOrderDetailDto> OrderDetailAsync(int id)
    {
        await using var db = _factory.CreateContext();
        return (await new PurchaseOrderService(db).GetAsync(id)).Value;
    }

    private static string Errors<T>(ServiceResult<T> result) => string.Join(" | ", result.Errors.Select(e => $"{e.Key}: {string.Join("; ", e.Value)}"));

    [Fact]
    public async Task A_linked_invoice_is_saved_comes_back_after_reloading_and_makes_the_order_definitive()
    {
        var order = await OrderAsync();
        var lineId = order.Lines[0].Id;

        // Zo was het: "Factuur ontvangen" legde alleen een verwijzing vast, zonder inkoopfactuur.
        await using (var db = _factory.CreateContext())
        {
            var before = await new PurchaseOrderService(db).AddInvoiceRefAsync(order.Id, new AddPurchaseOrderInvoiceRefDto { RequestId = Guid.NewGuid(), Kind = "Invoice", InvoiceNumber = "F-4B-1", AmountExclVat = 3564m });
            Assert.Null(before.Value.InvoiceRefs.Single().IncomingInvoiceId);
        }

        var saved = await SaveAsync(Invoice("F-4B-1", lineId));
        Assert.True(saved.Status == ServiceResultStatus.Created, Errors(saved));

        // Opnieuw laden (nieuwe context): de koppeling, de prijs op de bon en de order komen terug.
        await using (var db = _factory.CreateContext())
        {
            var invoice = (await new PurchaseInvoiceService(db).GetAsync(saved.Value.Id)).Value;
            var line = invoice.Lines.Single();
            Assert.Equal((lineId, order.Id, order.OrderNumber, "NP-88123", 13.50m, 3564.00m), (line.SupplyCommitmentLineId, line.PurchaseOrderId, line.PurchaseOrderNumber, line.SupplierReference, line.OrderUnitPrice, line.LineTotal));
            Assert.Equal(new[] { order.Id }, invoice.PurchaseOrders.Select(o => o.Id));
            Assert.Null(invoice.OrderPriceDecision);
        }
        var detail = await OrderDetailAsync(order.Id);
        Assert.Equal(("Definitive", "Invoice"), (detail.DocumentStatus, detail.FinalizedVia));
        // De eerdere verwijzing met hetzelfde nummer blijft zoals ze was (append-only); er komt geen tweede.
        Assert.Null(Assert.Single(detail.InvoiceRefs).IncomingInvoiceId);

        // Zonder eerdere verwijzing legt het koppelen er een vast, met de factuur en het bedrag van de gekoppelde regels.
        var second = await OrderAsync();
        var linked = await SaveAsync(Invoice("F-4B-1b", second.Lines[0].Id, quantity: 100m));
        var reference = Assert.Single((await OrderDetailAsync(second.Id)).InvoiceRefs);
        Assert.Equal(("F-4B-1b", linked.Value.Id, 1350.00m), (reference.InvoiceNumber, reference.IncomingInvoiceId, reference.AmountExclVat));
    }

    [Fact]
    public async Task A_price_difference_needs_a_choice_and_recording_it_keeps_the_order_price()
    {
        var order = await OrderAsync();
        var lineId = order.Lines[0].Id;

        var refused = await SaveAsync(Invoice("F-4B-2", lineId, price: 12.50m));
        Assert.Equal(ServiceResultStatus.Validation, refused.Status);
        Assert.Contains("orderPriceDecision", refused.Errors.Keys);
        Assert.Equal("Open", (await OrderDetailAsync(order.Id)).DocumentStatus);

        var saved = await SaveAsync(Invoice("F-4B-2", lineId, price: 12.50m, decision: PurchaseInvoiceOrderLinkRules.RecordDifference));
        Assert.True(saved.Status == ServiceResultStatus.Created, Errors(saved));
        var line = saved.Value.Lines.Single();
        Assert.Equal((13.50m, 3300.00m, PurchaseInvoiceOrderLinkRules.RecordDifference), (line.OrderUnitPrice, line.LineTotal, saved.Value.OrderPriceDecision));
        Assert.Equal(-264.00m, PurchaseInvoiceOrderLinkRules.Difference(line.LineTotal, line.Quantity, line.OrderUnitPrice));

        var detail = await OrderDetailAsync(order.Id);
        Assert.Equal((13.50m, "Definitive"), (detail.Lines[0].UnitPrice, detail.DocumentStatus));
        Assert.DoesNotContain(detail.Changes, c => c.Kind == "Edit");
    }

    [Fact]
    public async Task Adjusting_the_order_to_the_invoice_changes_the_price_with_an_audit_line_and_then_makes_it_definitive()
    {
        var order = await OrderAsync();
        var saved = await SaveAsync(Invoice("F-4B-3", order.Lines[0].Id, price: 12.50m, decision: PurchaseInvoiceOrderLinkRules.AdjustOrder));
        Assert.True(saved.Status == ServiceResultStatus.Created, Errors(saved));
        Assert.Equal(13.50m, saved.Value.Lines.Single().OrderUnitPrice);

        var detail = await OrderDetailAsync(order.Id);
        Assert.Equal((12.50m, "Definitive", "Invoice"), (detail.Lines[0].UnitPrice, detail.DocumentStatus, detail.FinalizedVia));
        var edit = Assert.Single(detail.Changes, c => c.Kind == "Edit");
        Assert.Equal(("n.a.v. factuur F-4B-3", PurchaseOrderEditHarness.Actor), (edit.Reason, edit.ActorReference));
        var field = Assert.Single(edit.Fields);
        Assert.Equal(("unitPrice", "13.5", "12.5"), (field.Field, field.Old, field.New));
        Assert.Equal(new[] { "Edit", "Finalize" }, detail.Changes.Select(c => c.Kind));
    }

    [Fact]
    public async Task A_definitive_order_stays_definitive_and_is_not_adjusted_through_an_invoice()
    {
        var order = await OrderAsync();
        await using (var db = _factory.CreateContext())
        {
            var finalized = await new PurchaseOrderService(db).FinalizeAsync(order.Id, new FinalizePurchaseOrderDto { RequestId = Guid.NewGuid(), ExpectedVersion = 0 });
            Assert.Equal("Definitive", finalized.Value.DocumentStatus);
        }

        var adjust = await SaveAsync(Invoice("F-4B-4", order.Lines[0].Id, price: 12.50m, decision: PurchaseInvoiceOrderLinkRules.AdjustOrder));
        Assert.Equal(ServiceResultStatus.Validation, adjust.Status);
        Assert.Contains("Correction", string.Join(" ", adjust.Errors["orderPriceDecision"]));

        var recorded = await SaveAsync(Invoice("F-4B-4", order.Lines[0].Id, price: 12.50m, decision: PurchaseInvoiceOrderLinkRules.RecordDifference));
        Assert.True(recorded.Status == ServiceResultStatus.Created, Errors(recorded));
        var detail = await OrderDetailAsync(order.Id);
        Assert.Equal((13.50m, "Definitive", "Manual"), (detail.Lines[0].UnitPrice, detail.DocumentStatus, detail.FinalizedVia));
    }

    [Fact]
    public async Task An_order_line_is_invoiced_in_parts_and_more_than_ordered_or_received_needs_a_confirmation()
    {
        var order = await OrderAsync();
        var lineId = order.Lines[0].Id;
        Assert.True((await SaveAsync(Invoice("F-4B-5a", lineId, quantity: 200m))).Status == ServiceResultStatus.Created);
        Assert.True((await SaveAsync(Invoice("F-4B-5b", lineId, quantity: 64m))).Status == ServiceResultStatus.Created);

        await using (var db = _factory.CreateContext())
        {
            var quantities = await new PurchaseOrderService(db).LineQuantitiesAsync(new List<int> { order.Id });
            Assert.Equal(new PurchaseOrderService.LineQuantities(264m, 0m, 264m), quantities[lineId]);
        }

        var over = await SaveAsync(Invoice("F-4B-5c", lineId, quantity: 1m));
        Assert.Equal(ServiceResultStatus.Validation, over.Status);
        Assert.Contains("265", string.Join(" ", over.Errors["lines[0].quantity"]));
        Assert.True((await SaveAsync(Invoice("F-4B-5c", lineId, quantity: 1m, acceptOver: true))).Status == ServiceResultStatus.Created);

        // Na ontvangst telt wat ontvangen is: 250 ontvangen, 260 factureren is te veel.
        var received = await OrderAsync();
        await PurchaseOrderTestData.ReceiveAsync(_factory, received.Id, 250m, 1000m);
        var tooMuch = await SaveAsync(Invoice("F-4B-6", received.Lines[0].Id, quantity: 260m));
        Assert.Contains("received", string.Join(" ", tooMuch.Errors["lines[0].quantity"]));
        Assert.True((await SaveAsync(Invoice("F-4B-6", received.Lines[0].Id, quantity: 250m))).Status == ServiceResultStatus.Created);
    }

    [Fact]
    public async Task An_invoice_is_only_linked_to_orders_of_its_own_supplier_in_the_article_and_unit_of_the_line()
    {
        var order = await OrderAsync();
        var lineId = order.Lines[0].Id;

        var otherSupplier = await SaveAsync(Invoice("F-4B-7", lineId, vendorId: _seed.OtherVendorId));
        Assert.Contains("another supplier", string.Join(" ", otherSupplier.Errors["lines[0].supplyCommitmentLineId"]));

        var wrongUnit = Invoice("F-4B-7", lineId);
        wrongUnit.Lines[0].Unit = "colli";
        Assert.Contains("lines[0].unit", (await SaveAsync(wrongUnit)).Errors.Keys);

        var wrongArticle = Invoice("F-4B-7", lineId);
        wrongArticle.Lines[0].ProductVariantId = _seed.KentId;
        Assert.Contains("lines[0].productVariantId", (await SaveAsync(wrongArticle)).Errors.Keys);

        var twice = Invoice("F-4B-7", lineId, quantity: 100m);
        twice.Lines.Add(new CreatePurchaseInvoiceLineDto { ProductVariantId = _seed.HassId, Description = "Avocado · Hass", Quantity = 100m, Unit = "box", UnitPrice = 13.50m, VatPercentage = 9m, SupplyCommitmentLineId = lineId });
        Assert.Contains("lines[1].supplyCommitmentLineId", (await SaveAsync(twice)).Errors.Keys);

        Assert.Contains("lines[0].supplyCommitmentLineId", (await SaveAsync(Invoice("F-4B-7", 999_999))).Errors.Keys);

        // Niets opgeslagen en de order nog open.
        await using var db = _factory.CreateContext();
        Assert.Empty(await db.IncomingInvoices.ToListAsync());
        Assert.Equal("Open", (await OrderDetailAsync(order.Id)).DocumentStatus);
    }

    [Fact]
    public async Task An_invoice_without_a_bon_works_as_before_a_retry_is_the_same_invoice_and_a_duplicate_number_is_refused()
    {
        var order = await OrderAsync();
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        var direct = new CreatePurchaseInvoiceDto
        {
            RequestId = Guid.NewGuid(), VendorId = _seed.VendorId, InvoiceNumber = "T-1", InvoiceDate = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc),
            Lines = new() { new CreatePurchaseInvoiceLineDto { Description = "Transport", Quantity = 1m, UnitPrice = 145m, VatPercentage = 21m } },
        };
        var saved = await SaveAsync(direct);
        Assert.True(saved.Status == ServiceResultStatus.Created, Errors(saved));
        Assert.Empty(saved.Value.PurchaseOrders);

        var linked = Invoice("F-4B-8", order.Lines[0].Id);
        var first = await SaveAsync(linked);
        var retry = await SaveAsync(linked);
        Assert.Equal((ServiceResultStatus.Ok, first.Value.Id), (retry.Status, retry.Value.Id));
        Assert.Single((await OrderDetailAsync(order.Id)).InvoiceRefs);

        // Dezelfde RequestId met een andere koppeling is een andere opdracht.
        var other = await OrderAsync();
        linked.Lines[0].SupplyCommitmentLineId = other.Lines[0].Id;
        Assert.Equal(ServiceResultStatus.Conflict, (await SaveAsync(linked)).Status);

        var duplicate = Invoice("F-4B-8", other.Lines[0].Id);
        Assert.Equal(ServiceResultStatus.Conflict, (await SaveAsync(duplicate)).Status);
        Assert.Equal("Open", (await OrderDetailAsync(other.Id)).DocumentStatus);
    }
}
