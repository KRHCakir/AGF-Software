using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Tests.Controllers;

public class PalletsControllerTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<int> SeedSourceLineAsync()
    {
        await using var db = _factory.CreateContext();
        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass 6", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.50m };
        var commitment = new SupplyCommitment { RequestId = Guid.NewGuid(), Vendor = new Vendor { Name = "Supplier B" }, Route = "Warehouse", Status = "Expected", Origin = "Peru" };
        var line = new SupplyCommitmentLine { SupplyCommitment = commitment, ProductVariant = variant, ExpectedQuantity = 10m, ExpectedUnit = "box" };
        db.AddRange(variant, commitment, line);
        await db.SaveChangesAsync();
        return line.Id;
    }

    private static CreatePalletDto PalletRequest() => new()
    {
        Reference = "PAL-001",
        LogisticsStatus = "Received",
        LocationContext = "Warehouse",
        ActorReference = "office-1"
    };

    private static AddPalletContentLineDto ContentRequest(int sourceLineId, Guid requestId) => new()
    {
        RequestId = requestId,
        SupplyCommitmentLineId = sourceLineId,
        LotNumber = "LOT-1",
        Quantity = 4m,
        Unit = "box",
        NetWeightKg = 16m,
        ActorReference = "warehouse-1"
    };

    private async Task<IActionResult> Run(Func<PalletsController, Task<IActionResult>> action)
    {
        await using var db = _factory.CreateContext();
        return await action(new PalletsController(new PalletService(db)));
    }

    private async Task<PalletDto> CreatePalletAsync()
    {
        var created = Assert.IsType<CreatedAtActionResult>(await Run(c => c.Create(PalletRequest())));
        return Assert.IsType<PalletDto>(created.Value);
    }

    [Fact]
    public async Task Create_Returns_201_With_Location_Then_200_On_Exact_Retry()
    {
        var created = Assert.IsType<CreatedAtActionResult>(await Run(c => c.Create(PalletRequest())));
        Assert.Equal(nameof(PalletsController.GetById), created.ActionName);
        var pallet = Assert.IsType<PalletDto>(created.Value);
        Assert.Equal(pallet.Id, created.RouteValues!["id"]);

        var retry = Assert.IsType<OkObjectResult>(await Run(c => c.Create(PalletRequest())));
        Assert.Equal(pallet.Id, Assert.IsType<PalletDto>(retry.Value).Id);
    }

    [Fact]
    public async Task Create_Returns_400_With_Field_Errors_And_409_On_Reference_Conflict()
    {
        var invalid = PalletRequest();
        invalid.Sscc = "006141411234567891";
        var badRequest = Assert.IsType<BadRequestObjectResult>(await Run(c => c.Create(invalid)));
        Assert.Contains("sscc", Assert.IsType<ValidationProblemDetails>(badRequest.Value).Errors.Keys);

        await CreatePalletAsync();
        var changed = PalletRequest();
        changed.LocationContext = "Customer";
        Assert.IsType<ConflictObjectResult>(await Run(c => c.Create(changed)));
    }

    [Fact]
    public async Task AddContentLine_Returns_201_With_Pallet_Location_Then_200_On_Retry_And_409_On_Change()
    {
        var sourceLineId = await SeedSourceLineAsync();
        var pallet = await CreatePalletAsync();
        var requestId = Guid.NewGuid();

        var created = Assert.IsType<CreatedAtActionResult>(await Run(c => c.AddContentLine(pallet.Id, ContentRequest(sourceLineId, requestId))));
        Assert.Equal(nameof(PalletsController.GetById), created.ActionName);
        Assert.Equal(pallet.Id, created.RouteValues!["id"]);
        var line = Assert.IsType<PalletContentLineDto>(created.Value);

        var retry = Assert.IsType<OkObjectResult>(await Run(c => c.AddContentLine(pallet.Id, ContentRequest(sourceLineId, requestId))));
        Assert.Equal(line.Id, Assert.IsType<PalletContentLineDto>(retry.Value).Id);

        var changed = ContentRequest(sourceLineId, requestId);
        changed.Quantity = 5m;
        Assert.IsType<ConflictObjectResult>(await Run(c => c.AddContentLine(pallet.Id, changed)));
    }

    [Fact]
    public async Task AddContentLine_Returns_400_And_404()
    {
        var sourceLineId = await SeedSourceLineAsync();
        var pallet = await CreatePalletAsync();

        var invalid = ContentRequest(sourceLineId, Guid.NewGuid());
        invalid.NetWeightKg = null;
        var badRequest = Assert.IsType<BadRequestObjectResult>(await Run(c => c.AddContentLine(pallet.Id, invalid)));
        Assert.Contains("netWeightKg", Assert.IsType<ValidationProblemDetails>(badRequest.Value).Errors.Keys);

        Assert.IsType<NotFoundObjectResult>(await Run(c => c.AddContentLine(999_999, ContentRequest(sourceLineId, Guid.NewGuid()))));
    }

    [Fact]
    public async Task GetById_Returns_200_Or_404()
    {
        var pallet = await CreatePalletAsync();

        var ok = Assert.IsType<OkObjectResult>(await Run(c => c.GetById(pallet.Id)));
        Assert.Equal(pallet.Id, Assert.IsType<PalletDto>(ok.Value).Id);
        Assert.IsType<NotFoundObjectResult>(await Run(c => c.GetById(999_999)));
    }
}
