using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Stap 4b, de basis: de rekenregels van een factuur gekoppeld aan de bon en TraceabilityGuard regel 30 (een factuurregel factureert alleen
// een orderregel van de eigen leverancier; koppeling, prijs op de bon en de keuze bij een prijsverschil liggen vast).
public class PurchaseInvoiceOrderLinkRulesTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    // Een factuur met één regel gekoppeld aan de eerste regel van de order, rechtstreeks in de database.
    private async Task<(int InvoiceId, int OtherLineId)> LinkedInvoiceAsync()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var other = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, vendorId: seed.OtherVendorId));
        await using var db = _factory.CreateContext();
        var invoice = new IncomingInvoice
        {
            VendorId = seed.VendorId, InvoiceNumber = "F-30", Status = "Draft", OrderPriceDecision = PurchaseInvoiceOrderLinkRules.RecordDifference,
            InvoiceLines = new List<IncomingInvoiceLine>
            {
                new() { ProductVariantId = seed.HassId, Description = "Avocado · Hass", Quantity = 221m, Unit = "box", UnitPrice = 12.50m, LineTotal = 2762.50m, SupplyCommitmentLineId = order.Lines[0].Id, OrderUnitPrice = 13.50m },
            },
        };
        db.IncomingInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (invoice.Id, other.Lines[0].Id);
    }

    [Fact]
    public async Task The_guard_keeps_the_link_fixed_and_refuses_a_line_of_another_suppliers_order()
    {
        var (invoiceId, otherLineId) = await LinkedInvoiceAsync();

        await using (var db = _factory.CreateContext())
        {
            var line = await db.IncomingInvoiceLines.SingleAsync(l => l.IncomingInvoiceId == invoiceId);
            line.OrderUnitPrice = 12m;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var line = await db.IncomingInvoiceLines.SingleAsync(l => l.IncomingInvoiceId == invoiceId);
            line.SupplyCommitmentLineId = otherLineId;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var invoice = await db.IncomingInvoices.SingleAsync(i => i.Id == invoiceId);
            invoice.OrderPriceDecision = PurchaseInvoiceOrderLinkRules.AdjustOrder;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            db.IncomingInvoiceLines.Add(new IncomingInvoiceLine { IncomingInvoiceId = invoiceId, Description = "x", Quantity = 1m, UnitPrice = 1m, LineTotal = 1m, SupplyCommitmentLineId = otherLineId });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            db.IncomingInvoices.Add(new IncomingInvoice { InvoiceNumber = "F-31", Status = "Draft", OrderPriceDecision = "Guess" });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        // Wat niet aan de koppeling raakt mag gewoon (bijvoorbeeld de interne notitie).
        await using (var db = _factory.CreateContext())
        {
            (await db.IncomingInvoices.SingleAsync(i => i.Id == invoiceId)).InternalNote = "Gebeld met leverancier";
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(10, 0, 0, 10)]
    [InlineData(264, 250, 200, 50)]
    [InlineData(264, 0, 300, 0)]
    public void The_open_quantity_is_received_or_else_ordered_minus_invoiced(decimal expected, decimal received, decimal invoiced, decimal open) =>
        Assert.Equal(open, PurchaseInvoiceOrderLinkRules.OpenQuantity(expected, received, invoiced));

    [Fact]
    public void Billing_status_prices_and_differences()
    {
        Assert.Equal("None", PurchaseInvoiceOrderLinkRules.BillingStatus(new[] { (264m, 0m, 0m) }, false));
        Assert.Equal("Full", PurchaseInvoiceOrderLinkRules.BillingStatus(new[] { (264m, 0m, 0m) }, true));
        Assert.Equal("Partial", PurchaseInvoiceOrderLinkRules.BillingStatus(new[] { (264m, 0m, 200m), (10m, 0m, 0m) }, false));
        Assert.Equal("Full", PurchaseInvoiceOrderLinkRules.BillingStatus(new[] { (264m, 250m, 250m) }, false));
        Assert.Equal((12.35m, true, false), (PurchaseInvoiceOrderLinkRules.InvoiceUnitPrice(13m, 5m), PurchaseInvoiceOrderLinkRules.HasPriceDifference(null, 1m, 0m), PurchaseInvoiceOrderLinkRules.HasPriceDifference(1m, 1m, 0m)));
        // Een kleine korting die op twee decimalen wegvalt is toch een verschil (264 × 13,50 met 0,01% korting is niet 3.564,00).
        Assert.True(PurchaseInvoiceOrderLinkRules.HasPriceDifference(13.50m, 13.50m, 0.01m));
        Assert.False(PurchaseInvoiceOrderLinkRules.HasPriceDifference(12.35m, 13m, 5m));
        // Bon 13,50, factuur 12,50 voor 264: −264,00; zonder prijs op de bon geen verschil in bedrag.
        Assert.Equal((-264.00m, (decimal?)null, 3564.00m), (PurchaseInvoiceOrderLinkRules.Difference(3300m, 264m, 13.50m), PurchaseInvoiceOrderLinkRules.Difference(3300m, 264m, null), PurchaseInvoiceOrderLinkRules.OrderAmount(264m, 13.50m)));
    }
}
