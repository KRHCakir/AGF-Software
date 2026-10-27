using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Inkooporders: lijst (status, zoeken, paginering), detail, opzoeken op factuurnummer en de statusregels.
public class PurchaseOrderServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // Factuurnummer rechtstreeks (koppelen via de service komt in PurchaseOrderInvoiceRefTests).
    private async Task AddRefAsync(int orderId, string number, string kind = "Invoice")
    {
        await using var db = _factory.CreateContext();
        db.SupplyCommitmentInvoiceRefs.Add(new SupplyCommitmentInvoiceRef { RequestId = Guid.NewGuid(), SupplyCommitmentId = orderId, Kind = kind, InvoiceNumber = number, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task<PurchaseOrderDetailDto> DetailAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        return (await new PurchaseOrderService(db).GetAsync(orderId)).Value;
    }

    private async Task<PurchaseOrderListDto> ListAsync(string query = null, string status = null, int? vendorId = null, int page = 1, int pageSize = 50)
    {
        await using var db = _factory.CreateContext();
        var result = await new PurchaseOrderService(db).ListAsync(query, status, vendorId, page, pageSize);
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        return result.Value;
    }

    [Fact]
    public async Task List_DefaultsToOpenOrders_NewestFirst_WithServerPaging()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var ordered = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var partial = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, quantity: 100m));
        await PurchaseOrderTestData.ReceiveAsync(_factory, partial.Id, 40m, 160m);
        var received = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, quantity: 10m));
        await PurchaseOrderTestData.ReceiveAsync(_factory, received.Id, 10m, 40m);
        var cancelled = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        await using (var db = _factory.CreateContext())
        {
            (await db.SupplyCommitments.SingleAsync(c => c.Id == cancelled.Id)).Status = "Cancelled";
            await db.SaveChangesAsync();
        }

        var open = await ListAsync();
        Assert.Equal(new[] { partial.OrderNumber, ordered.OrderNumber }, open.Items.Select(i => i.OrderNumber));
        Assert.Equal(new[] { "PartiallyReceived", "Ordered" }, open.Items.Select(i => i.Status));
        Assert.Equal(40m, open.Items[0].ReceivedQuantity);
        Assert.Equal(884m, open.Items[0].ExpectedNetWeightKg);

        var all = await ListAsync(status: "all", pageSize: 3);
        Assert.Equal(4, all.Total);
        Assert.Equal(3, all.Items.Count);
        Assert.Equal(cancelled.OrderNumber, all.Items[0].OrderNumber);
        Assert.Equal("Cancelled", all.Items[0].Status);
        Assert.Equal(ordered.OrderNumber, Assert.Single((await ListAsync(status: "all", page: 2, pageSize: 3)).Items).OrderNumber);
        Assert.Equal(received.OrderNumber, Assert.Single((await ListAsync(status: "Received")).Items).OrderNumber);

        await using var bad = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.Validation, (await new PurchaseOrderService(bad).ListAsync(null, "Besteld", null, 1, 50)).Status);
    }

    [Fact]
    public async Task List_SearchesOrderNumberSupplierReferenceVendorAndInvoiceNumber_AndFiltersVendor()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var a = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var otherRequest = PurchaseOrderTestData.OrderRequest(seed, vendorId: seed.OtherVendorId);
        otherRequest.SupplierReference = "FO-7";
        var b = await PurchaseOrderTestData.CreateOrderAsync(_factory, otherRequest);
        await AddRefAsync(b.Id, "INV-2026-4711");

        Assert.Equal(b.Id, Assert.Single((await ListAsync("inv-2026-47")).Items).Id);
        Assert.Equal(a.Id, Assert.Single((await ListAsync("io-2026-0001")).Items).Id);
        Assert.Equal(b.Id, Assert.Single((await ListAsync("fo-7")).Items).Id);
        Assert.Equal(a.Id, Assert.Single((await ListAsync("nature")).Items).Id);
        Assert.Equal(b.Id, Assert.Single((await ListAsync(vendorId: seed.OtherVendorId)).Items).Id);
        var item = (await ListAsync("4711")).Items.Single();
        Assert.Equal("INV-2026-4711", Assert.Single(item.InvoiceNumbers).InvoiceNumber);
        Assert.Equal("Received", item.InvoiceIndicator);
    }

    [Fact]
    public async Task FindByInvoiceNumber_IsCaseInsensitive_AndRespectsVendor()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        await AddRefAsync(order.Id, "INV-77");
        await using var db = _factory.CreateContext();
        var service = new PurchaseOrderService(db);

        var match = Assert.Single(await service.FindByInvoiceNumberAsync(" inv-77 ", null));
        Assert.Equal(order.OrderNumber, match.OrderNumber);
        Assert.Single(await service.FindByInvoiceNumberAsync("INV-77", seed.VendorId));
        Assert.Empty(await service.FindByInvoiceNumberAsync("INV-77", seed.OtherVendorId));
        Assert.Empty(await service.FindByInvoiceNumberAsync("INV-7", null));
        Assert.Empty(await service.FindByInvoiceNumberAsync("", null));
    }

    [Theory]
    [InlineData("Expected", 0, 10, "Ordered")]
    [InlineData("Expected", 4, 10, "PartiallyReceived")]
    [InlineData("Expected", 10, 10, "Received")]
    [InlineData("Expected", 12, 10, "Received")]
    [InlineData("Cancelled", 0, 10, "Cancelled")]
    [InlineData("Rejected", 10, 10, "Rejected")]
    [InlineData("Delivered", 0, 10, "Received")]
    public void OrderStatus_FollowsReceivedQuantities(string stored, int received, int expected, string status) =>
        Assert.Equal(status, PurchaseOrderRules.OrderStatus(stored, new[] { ((decimal)expected, (decimal)received) }));

    [Fact]
    public void InvoiceIndicator_CreditNoteExpectedUntilRecordedAfterTheRejection()
    {
        var t = new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal("None", PurchaseOrderRules.InvoiceIndicator(Array.Empty<(string, DateTime)>(), Array.Empty<(string, DateTime)>()));
        Assert.Equal("Received", PurchaseOrderRules.InvoiceIndicator(new[] { ("Invoice", t) }, new[] { ("Dispose", t) }));
        Assert.Equal("CreditNoteExpected", PurchaseOrderRules.InvoiceIndicator(new[] { ("Invoice", t) }, new[] { ("ReturnToSupplier", t.AddMinutes(1)) }));
        Assert.Equal("Received", PurchaseOrderRules.InvoiceIndicator(new[] { ("Invoice", t), ("CreditNote", t.AddMinutes(2)) }, new[] { ("ClaimDiscount", t.AddMinutes(1)) }));
        Assert.Equal("CreditNoteExpected", PurchaseOrderRules.InvoiceIndicator(new[] { ("CreditNote", t) }, new[] { ("ClaimDiscount", t.AddMinutes(1)) }));
    }
}
