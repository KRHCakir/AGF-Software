using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Verkoopfactuur 1, lezen: per levering geleverd, gefactureerd en rest; de werklijst Nog te factureren per klant; een factuur met btw per
// tarief en de waarschuwing voor een concept dat langer dan 7 dagen ligt. De concepten staan hier rechtstreeks in de database.
public sealed class SalesInvoiceReadTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public SalesInvoiceReadTests() => _factory.Calendar = new FixedBusinessCalendar(DateOnly.FromDateTime(DateTime.UtcNow));

    public void Dispose() => _factory.Dispose();

    private async Task<int> ConceptAsync(SalesInvoiceTestData.Arranged a, decimal goods, bool withService = true)
    {
        await using var db = _factory.CreateContext();
        var invoice = new Invoice
        {
            Kind = SalesInvoiceRules.KindDelivery, Status = SalesInvoiceRules.StatusConcept, CustomerId = a.Seed.CustomerId, RequestId = Guid.NewGuid(), RequestFingerprint = "test", CreatedBy = "Jan",
            CustomCustomerName = "Customer T", InvoiceDate = DateTime.UtcNow,
            InvoiceLines = new List<InvoiceLine>
            {
                new() { LineKind = SalesInvoiceRules.LineProduct, SalesDeliveryId = a.FirstDelivery, SalesOrderLineId = a.BoxLine, ProductVariantId = a.Seed.AvocadoId,
                    Description = "Avocado Hass, 4kg Box", Quantity = goods, Unit = "box", UnitPrice = 12.50m, DiscountPercentage = 10m, VATPercentage = 9m, SortOrder = 0 }
            }
        };
        if (withService)
        {
            invoice.InvoiceLines.Add(new InvoiceLine { LineKind = SalesInvoiceRules.LineService, SalesOrderServiceLineId = a.ServiceLine, Description = "Handling",
                Quantity = 1m, Unit = SalesInvoiceRules.PieceUnit, UnitPrice = 25m, VATPercentage = 21m, RevenueTypeId = 1, SortOrder = 1 });
        }
        SalesInvoiceRules.RecalculateTotals(invoice);
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    [Fact]
    public async Task Each_Delivery_Shows_Delivered_Invoiced_And_Rest()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory, shipSecond: false);
        await using (var db = _factory.CreateContext())
        {
            var deliveries = (await new SalesInvoiceService(db).OrderInvoicingAsync(a.OrderId)).Value;
            Assert.Equal(new[] { 1, 2 }, deliveries.Select(d => d.Sequence));
            var first = deliveries[0];
            Assert.Equal((30m, 0m, 30m), (first.Lines.Single().Delivered, first.Lines.Single().Invoiced, first.Lines.Single().Rest));
            Assert.True(first.HasRest);
            Assert.NotNull(first.DeliveredAt);
            Assert.Empty(deliveries[1].Lines);
            Assert.False(deliveries[1].HasRest);
        }

        var concept = await ConceptAsync(a, 20m);
        await using (var db = _factory.CreateContext())
        {
            var first = (await new SalesInvoiceService(db).OrderInvoicingAsync(a.OrderId)).Value[0];
            Assert.Equal((20m, 10m), (first.Lines.Single().Invoiced, first.Lines.Single().Rest));
            Assert.Equal(concept, first.OpenConceptId);
            Assert.Equal(SalesInvoiceRules.StatusConcept, first.Invoices.Single().Status);
            Assert.Equal(ServiceResultStatus.NotFound, (await new SalesInvoiceService(db).OrderInvoicingAsync(999)).Status);
        }
    }

    [Fact]
    public async Task The_Worklist_Groups_Deliveries_With_A_Rest_Per_Customer()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        await using (var db = _factory.CreateContext())
        {
            var customer = Assert.Single(await new SalesInvoiceService(db).ToInvoiceAsync());
            Assert.Equal((a.Seed.CustomerId, "Customer T"), (customer.CustomerId, customer.CustomerName));
            Assert.Equal(new[] { a.FirstDelivery, a.SecondDelivery }, customer.Deliveries.Select(d => d.SalesDeliveryId).OrderBy(id => id));
            Assert.All(customer.Deliveries, d => Assert.Equal(0, d.DaysSinceDelivery));
        }
        await ConceptAsync(a, 30m);
        await using (var db = _factory.CreateContext())
        {
            // De eerste levering is helemaal op het concept gezet; de tweede heeft nog 15 doos (en de emballage van de order).
            var customer = Assert.Single(await new SalesInvoiceService(db).ToInvoiceAsync());
            Assert.Contains(customer.Deliveries, d => d.SalesDeliveryId == a.SecondDelivery && d.Lines.Single().Rest == 15m);
        }
    }

    [Fact]
    public async Task An_Invoice_Shows_Its_Lines_Its_Deliveries_And_Vat_Per_Rate()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var id = await ConceptAsync(a, 30m);
        await using var db = _factory.CreateContext();
        var invoice = (await new SalesInvoiceService(db).GetAsync(id)).Value;

        Assert.Equal((SalesInvoiceRules.KindDelivery, SalesInvoiceRules.StatusConcept), (invoice.Kind, invoice.Status));
        Assert.Null(invoice.InvoiceNumber);
        Assert.Equal(new[] { "Avocado Hass, 4kg Box", "Handling" }, invoice.Lines.Select(l => l.Description));
        Assert.Equal(30m, invoice.Lines[0].MaxQuantity);
        Assert.Equal("Omzet diensten", invoice.Lines[1].RevenueTypeName);
        Assert.Equal(1, invoice.Deliveries.Single().Sequence);
        Assert.Equal(new[] { (9m, 337.50m, 30.38m), (21m, 25.00m, 5.25m) }, invoice.Totals.VatRates.Select(r => (r.Percentage, r.Net, r.Vat)));
        Assert.Equal((362.50m, 35.63m, 398.13m), (invoice.Totals.Net.Value, invoice.Totals.Vat.Value, invoice.Totals.Gross.Value));
        Assert.False(invoice.Stale);
        Assert.Equal(ServiceResultStatus.NotFound, (await new SalesInvoiceService(db).GetAsync(id + 100)).Status);
    }

    [Fact]
    public async Task A_Concept_Older_Than_Seven_Days_Gets_A_Warning()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var id = await ConceptAsync(a, 30m, withService: false);
        _factory.Calendar = new FixedBusinessCalendar(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(SalesInvoiceRules.StaleConceptDays + 1));
        await using var db = _factory.CreateContext();
        Assert.True((await new SalesInvoiceService(db).GetAsync(id)).Value.Stale);
    }

    [Fact]
    public async Task Old_Invoices_Are_Not_Sales_Invoices_Of_The_New_Model()
    {
        await using var db = _factory.CreateContext();
        var old = new Invoice { InvoiceNumber = "FAC-20261020-1234", Status = "Nieuw", CustomCustomerName = "Oud", InvoiceDate = DateTime.UtcNow };
        db.Invoices.Add(old);
        await db.SaveChangesAsync();
        Assert.Equal(ServiceResultStatus.NotFound, (await new SalesInvoiceService(db).GetAsync(old.Id)).Status);
    }
}
