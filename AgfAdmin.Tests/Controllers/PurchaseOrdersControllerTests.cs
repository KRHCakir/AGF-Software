using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Tests.Controllers;

public class PurchaseOrdersControllerTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<int> SeedOrderAsync()
    {
        await using var db = _factory.CreateContext();
        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12m };
        var vendor = new Vendor { Name = "Nature's Pride" };
        db.AddRange(variant, vendor);
        await db.SaveChangesAsync();
        var created = await new SupplyCommitmentService(db).CreateAsync(new CreateSupplyCommitmentDto
        {
            RequestId = Guid.NewGuid(), VendorId = vendor.Id, Route = "Warehouse", Origin = "Peru", ExpectedAt = DateTimeOffset.UtcNow, SupplierReference = "NP-1",
            Lines = new List<CreateSupplyCommitmentLineDto> { new() { ProductVariantId = variant.Id, ExpectedQuantity = 10m, ExpectedUnit = "box" } }
        });
        return created.Value.Id;
    }

    private PurchaseOrdersController Controller(AgfAdmin.Data.AppDbContext db) => new(new PurchaseOrderService(db));

    [Fact]
    public async Task ListGetAndLookup_FindTheOrder_UnknownStatusIs400()
    {
        var id = await SeedOrderAsync();
        await using var db = _factory.CreateContext();
        db.SupplyCommitmentInvoiceRefs.Add(new SupplyCommitmentInvoiceRef { RequestId = Guid.NewGuid(), SupplyCommitmentId = id, Kind = "Invoice", InvoiceNumber = "F-1" });
        await db.SaveChangesAsync();
        var controller = Controller(db);

        var list = Assert.IsType<PurchaseOrderListDto>(Assert.IsType<OkObjectResult>(await controller.List("F-1", null, null)).Value);
        Assert.Equal("IO-2026-0001", Assert.Single(list.Items).OrderNumber);
        var matches = Assert.IsAssignableFrom<IReadOnlyList<PurchaseOrderInvoiceMatchDto>>(Assert.IsType<OkObjectResult>(await controller.ByInvoiceNumber("f-1", null)).Value);
        Assert.Equal(id, Assert.Single(matches).Id);
        Assert.Equal("NP-1", Assert.IsType<PurchaseOrderDetailDto>(Assert.IsType<OkObjectResult>(await controller.GetById(id)).Value).SupplierReference);
        Assert.IsType<BadRequestObjectResult>(await controller.List(null, "nope", null));
        Assert.IsType<NotFoundObjectResult>(await controller.GetById(id + 1));
    }
}
