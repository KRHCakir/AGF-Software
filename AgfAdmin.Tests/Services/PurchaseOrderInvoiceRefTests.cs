using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;

namespace AgfAdmin.Tests.Services;

// Factuur- en creditnotanummers koppelen aan een inkooporder: meerdere toegestaan, dubbel geweigerd, idempotent.
public class PurchaseOrderInvoiceRefTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<ServiceResult<PurchaseOrderDetailDto>> AddRefAsync(int orderId, string number, string kind = "Invoice", Guid? requestId = null, decimal? amount = null)
    {
        await using var db = _factory.CreateContext();
        return await new PurchaseOrderService(db).AddInvoiceRefAsync(orderId, new AddPurchaseOrderInvoiceRefDto
        {
            RequestId = requestId ?? Guid.NewGuid(), Kind = kind, InvoiceNumber = number, InvoiceDate = new DateOnly(2026, 10, 25), AmountExclVat = amount
        });
    }

    private async Task<PurchaseOrderDetailDto> DetailAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        return (await new PurchaseOrderService(db).GetAsync(orderId)).Value;
    }

    [Fact]
    public async Task InvoiceRefs_MultiplePerOrder_SameNumberOnTwoOrders_DuplicateRefused()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var other = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));

        Assert.Equal(ServiceResultStatus.Created, (await AddRefAsync(order.Id, "F-100", amount: 1234.50m)).Status);
        Assert.Equal(ServiceResultStatus.Created, (await AddRefAsync(order.Id, "F-101")).Status);
        Assert.Equal(ServiceResultStatus.Created, (await AddRefAsync(other.Id, "F-100")).Status);
        // Zelfde nummer als creditnota is een ander document.
        Assert.Equal(ServiceResultStatus.Created, (await AddRefAsync(order.Id, "F-100", "CreditNote")).Status);
        // Zelfde order + nummer + soort, ook met andere hoofdletters en spaties: geweigerd.
        var duplicate = await AddRefAsync(order.Id, "  f-100 ");
        Assert.Equal(ServiceResultStatus.Conflict, duplicate.Status);

        var detail = await DetailAsync(order.Id);
        Assert.Equal(new[] { "F-100", "F-101", "F-100" }, detail.InvoiceRefs.Select(r => r.InvoiceNumber));
        Assert.Equal(1234.50m, detail.InvoiceRefs[0].AmountExclVat);
        Assert.Null(detail.InvoiceRefs[0].IncomingInvoiceId);
        Assert.Contains(detail.History, h => h.Type == "InvoiceRecorded" && h.Reference == "F-101");
    }

    [Fact]
    public async Task InvoiceRefs_Replay_IsIdempotent_DifferentContentConflicts()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var requestId = Guid.NewGuid();
        Assert.Equal(ServiceResultStatus.Created, (await AddRefAsync(order.Id, "F-1", requestId: requestId)).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await AddRefAsync(order.Id, "F-1", requestId: requestId)).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await AddRefAsync(order.Id, "F-2", requestId: requestId)).Status);
        Assert.Single((await DetailAsync(order.Id)).InvoiceRefs);

        Assert.Equal(ServiceResultStatus.Validation, (await AddRefAsync(order.Id, "   ")).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await AddRefAsync(order.Id, "F-3", "Receipt")).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await AddRefAsync(9999, "F-3")).Status);
    }

    [Fact]
    public async Task InvoiceRefs_SameNumberOtherCase_Refused_AndKindIsTrimmed()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        Assert.Equal(ServiceResultStatus.Created, (await AddRefAsync(order.Id, "F-1", " Invoice ")).Status);
        var duplicate = await AddRefAsync(order.Id, "f-1");
        Assert.Equal(ServiceResultStatus.Conflict, duplicate.Status);
        Assert.Equal("Invoice", Assert.Single((await DetailAsync(order.Id)).InvoiceRefs).Kind);
    }

    // Deterministische race zoals bij het ordernummer: beide controleren vóór opslaan, de tweede botst op de unieke index op de
    // sleutel en krijgt dezelfde 409 als de gewone controle; de eerste wint. Nooit een 500.
    [Fact]
    public async Task InvoiceRefs_ConcurrentSameNumberOtherCase_OneWins_OtherGetsDuplicateConflict()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        ServiceResult<PurchaseOrderDetailDto> inner = null;
        await using var db = _factory.CreateContext(new ConcurrentSaveInterceptor(async () => inner = await AddRefAsync(order.Id, "f-1")));
        var outer = await new PurchaseOrderService(db).AddInvoiceRefAsync(order.Id, new AddPurchaseOrderInvoiceRefDto { RequestId = Guid.NewGuid(), Kind = "Invoice", InvoiceNumber = "F-1" });

        Assert.Equal(ServiceResultStatus.Created, inner.Status);
        Assert.Equal(ServiceResultStatus.Conflict, outer.Status);
        Assert.Equal("f-1", Assert.Single((await DetailAsync(order.Id)).InvoiceRefs).InvoiceNumber);
    }
}
