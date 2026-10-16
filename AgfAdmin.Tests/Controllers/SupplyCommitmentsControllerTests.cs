using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Tests.Controllers;

public class SupplyCommitmentsControllerTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(int VendorId, int VariantId)> SeedAsync()
    {
        await using var db = _factory.CreateContext();
        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass 6", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.50m };
        var vendor = new Vendor { Name = "Supplier B" };
        db.AddRange(variant, vendor);
        await db.SaveChangesAsync();
        return (vendor.Id, variant.Id);
    }

    private static CreateSupplyCommitmentDto ValidRequest(int vendorId, int variantId, Guid requestId) => new()
    {
        RequestId = requestId,
        VendorId = vendorId,
        Route = "Warehouse",
        Origin = "Peru",
        ExpectedAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
        Lines = new List<CreateSupplyCommitmentLineDto>
        {
            new() { ProductVariantId = variantId, ExpectedQuantity = 40m, ExpectedUnit = "box" }
        }
    };

    private async Task<IActionResult> PostAsync(CreateSupplyCommitmentDto request)
    {
        await using var db = _factory.CreateContext();
        return await new SupplyCommitmentsController(new SupplyCommitmentService(db)).Create(request);
    }

    [Fact]
    public async Task Create_Returns_201_With_Location_Of_Get()
    {
        var (vendorId, variantId) = await SeedAsync();

        var result = await PostAsync(ValidRequest(vendorId, variantId, Guid.NewGuid()));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(SupplyCommitmentsController.GetById), created.ActionName);
        var dto = Assert.IsType<SupplyCommitmentDto>(created.Value);
        Assert.Equal(dto.Id, created.RouteValues!["id"]);
    }

    [Fact]
    public async Task Exact_Retry_Returns_200_With_Same_Commitment()
    {
        var (vendorId, variantId) = await SeedAsync();
        var requestId = Guid.NewGuid();
        var first = (SupplyCommitmentDto)Assert.IsType<CreatedAtActionResult>(await PostAsync(ValidRequest(vendorId, variantId, requestId))).Value;

        var retry = await PostAsync(ValidRequest(vendorId, variantId, requestId));

        var ok = Assert.IsType<OkObjectResult>(retry);
        Assert.Equal(first.Id, Assert.IsType<SupplyCommitmentDto>(ok.Value).Id);
    }

    [Fact]
    public async Task Reused_RequestId_With_Different_Content_Returns_409()
    {
        var (vendorId, variantId) = await SeedAsync();
        var requestId = Guid.NewGuid();
        await PostAsync(ValidRequest(vendorId, variantId, requestId));

        var changed = ValidRequest(vendorId, variantId, requestId);
        changed.Lines[0].ExpectedQuantity = 41m;

        Assert.IsType<ConflictObjectResult>(await PostAsync(changed));
    }

    [Fact]
    public async Task Validation_Error_Returns_400_With_Field_Errors()
    {
        var (vendorId, variantId) = await SeedAsync();
        var request = ValidRequest(vendorId, variantId, Guid.NewGuid());
        request.Lines[0].ExpectedUnit = "kg";

        var badRequest = Assert.IsType<BadRequestObjectResult>(await PostAsync(request));
        var problem = Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        Assert.Contains("lines[0].expectedUnit", problem.Errors.Keys);
    }

    [Fact]
    public async Task Unknown_Vendor_Returns_404()
    {
        var (_, variantId) = await SeedAsync();

        Assert.IsType<NotFoundObjectResult>(await PostAsync(ValidRequest(999_999, variantId, Guid.NewGuid())));
    }

    [Fact]
    public async Task GetById_Returns_200_Or_404()
    {
        var (vendorId, variantId) = await SeedAsync();
        var created = (SupplyCommitmentDto)Assert.IsType<CreatedAtActionResult>(await PostAsync(ValidRequest(vendorId, variantId, Guid.NewGuid()))).Value;

        await using var db = _factory.CreateContext();
        var controller = new SupplyCommitmentsController(new SupplyCommitmentService(db));

        var ok = Assert.IsType<OkObjectResult>(await controller.GetById(created.Id));
        Assert.Equal(created.Id, Assert.IsType<SupplyCommitmentDto>(ok.Value).Id);
        Assert.IsType<NotFoundObjectResult>(await controller.GetById(999_999));
    }

    [Fact]
    public async Task List_Returns_200_Or_400_For_Unknown_Filter()
    {
        var (vendorId, variantId) = await SeedAsync();
        await PostAsync(ValidRequest(vendorId, variantId, Guid.NewGuid()));

        await using var db = _factory.CreateContext();
        var controller = new SupplyCommitmentsController(new SupplyCommitmentService(db));

        var ok = Assert.IsType<OkObjectResult>(await controller.List(route: "Warehouse", status: null, vendorId: null, customerId: null));
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<SupplyCommitmentDto>>(ok.Value));
        Assert.IsType<BadRequestObjectResult>(await controller.List(route: "warehouse", status: null, vendorId: null, customerId: null));
    }
}
