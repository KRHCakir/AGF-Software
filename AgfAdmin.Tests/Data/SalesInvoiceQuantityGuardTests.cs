using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Regel 45 (verkoopfactuur 1): nooit meer gefactureerd dan geleverd, per levering en orderregel en per dienst- of emballageregel; een
// vervallen concept en een uit het concept gehaalde regel tellen niet mee; de klant van de levering is de klant van de factuur.
public sealed class SalesInvoiceQuantityGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static Invoice Concept(int customerId, params InvoiceLine[] lines) => new()
    {
        Kind = SalesInvoiceRules.KindDelivery, Status = SalesInvoiceRules.StatusConcept, CustomerId = customerId, RequestId = Guid.NewGuid(), CreatedBy = "test",
        InvoiceLines = lines.ToList()
    };

    private static InvoiceLine Goods(SalesInvoiceTestData.Arranged a, int deliveryId, decimal quantity) => new()
    {
        LineKind = SalesInvoiceRules.LineProduct, SalesDeliveryId = deliveryId, SalesOrderLineId = a.BoxLine, ProductVariantId = a.Seed.AvocadoId,
        Description = "Avocado Hass", Quantity = quantity, Unit = "box", UnitPrice = 12.50m, VATPercentage = 9m
    };

    private async Task<int> SaveAsync(Invoice invoice)
    {
        await using var db = _factory.CreateContext();
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private async Task RefusedAsync<TException>(Invoice invoice) where TException : Exception
    {
        await using var db = _factory.CreateContext();
        db.Invoices.Add(invoice);
        await Assert.ThrowsAsync<TException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Up_To_The_Delivered_Quantity_Per_Delivery_And_Never_More_Over_All_Invoices()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        await SaveAsync(Concept(a.Seed.CustomerId, Goods(a, a.FirstDelivery, 20m)));
        await SaveAsync(Concept(a.Seed.CustomerId, Goods(a, a.FirstDelivery, 10m), Goods(a, a.SecondDelivery, 15m)));
        await RefusedAsync<InvoiceQuantityConflictException>(Concept(a.Seed.CustomerId, Goods(a, a.FirstDelivery, 0.5m)));
        await RefusedAsync<InvoiceQuantityConflictException>(Concept(a.Seed.CustomerId, Goods(a, a.SecondDelivery, 1m)));
    }

    [Fact]
    public async Task What_Is_Not_Shipped_Yet_Is_Not_Delivered()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory, shipSecond: false);
        await RefusedAsync<InvoiceQuantityConflictException>(Concept(a.Seed.CustomerId, Goods(a, a.SecondDelivery, 1m)));
    }

    [Fact]
    public async Task A_Discarded_Concept_And_A_Removed_Line_No_Longer_Count()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var first = await SaveAsync(Concept(a.Seed.CustomerId, Goods(a, a.FirstDelivery, 30m)));
        await RefusedAsync<InvoiceQuantityConflictException>(Concept(a.Seed.CustomerId, Goods(a, a.FirstDelivery, 30m)));
        await using (var db = _factory.CreateContext())
        {
            var invoice = await db.Invoices.SingleAsync(i => i.Id == first);
            invoice.Status = SalesInvoiceRules.StatusDiscarded;
            invoice.DiscardedAt = DateTime.UtcNow;
            invoice.DiscardedBy = "test";
            await db.SaveChangesAsync();
        }
        var second = await SaveAsync(Concept(a.Seed.CustomerId, Goods(a, a.FirstDelivery, 30m)));
        await using (var db = _factory.CreateContext())
        {
            (await db.InvoiceLines.SingleAsync(l => l.InvoiceId == second)).RemovedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await SaveAsync(Concept(a.Seed.CustomerId, Goods(a, a.FirstDelivery, 30m)));
    }

    [Fact]
    public async Task Raising_A_Line_Above_The_Delivery_Is_Refused()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        var id = await SaveAsync(Concept(a.Seed.CustomerId, Goods(a, a.FirstDelivery, 30m)));
        await using var db = _factory.CreateContext();
        (await db.InvoiceLines.SingleAsync(l => l.InvoiceId == id)).Quantity = 31m;
        await Assert.ThrowsAsync<InvoiceQuantityConflictException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Service_And_Packaging_Never_More_Than_On_The_Order()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        InvoiceLine Service(decimal quantity) => new() { LineKind = SalesInvoiceRules.LineService, SalesOrderServiceLineId = a.ServiceLine, Description = "Handling", Quantity = quantity, Unit = "piece", VATPercentage = 21m };
        InvoiceLine Packaging(decimal quantity) => new() { LineKind = SalesInvoiceRules.LinePackaging, SalesOrderPackagingLineId = a.PackagingLine, Description = "Pallet", Quantity = quantity, Unit = "piece", VATPercentage = 21m };
        await SaveAsync(Concept(a.Seed.CustomerId, Service(1m), Packaging(1m)));
        await RefusedAsync<InvoiceQuantityConflictException>(Concept(a.Seed.CustomerId, Service(0.5m)));
        await SaveAsync(Concept(a.Seed.CustomerId, Packaging(1m)));
        await RefusedAsync<InvoiceQuantityConflictException>(Concept(a.Seed.CustomerId, Packaging(1m)));
    }

    [Fact]
    public async Task A_Delivery_Of_Another_Customer_Or_On_A_Direct_Invoice_Is_Refused()
    {
        var a = await SalesInvoiceTestData.ArrangeAsync(_factory);
        await RefusedAsync<TraceabilityViolationException>(Concept(a.Seed.OtherCustomerId, Goods(a, a.FirstDelivery, 1m)));
        var direct = Concept(a.Seed.CustomerId, Goods(a, a.FirstDelivery, 1m));
        direct.Kind = SalesInvoiceRules.KindDirect;
        await RefusedAsync<TraceabilityViolationException>(direct);
    }
}
