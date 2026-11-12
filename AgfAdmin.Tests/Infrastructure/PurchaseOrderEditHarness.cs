using AgfAdmin.DTOs;
using AgfAdmin.Security;
using AgfAdmin.Services;

namespace AgfAdmin.Tests.Infrastructure;

// Gedeelde stukken voor de tests van inkooporder aanpassen en definitief maken (stap 4a).
public sealed class PurchaseOrderEditHarness(SqliteAppDbContextFactory factory)
{
    public static readonly string Actor = CurrentUser.Default.Actor(100);
    public PurchaseOrderSeed Seed { get; private set; }

    public async Task<PurchaseOrderDetailDto> OrderAsync()
    {
        Seed ??= await PurchaseOrderTestData.SeedAsync(factory);
        var created = await PurchaseOrderTestData.CreateOrderAsync(factory, PurchaseOrderTestData.OrderRequest(Seed));
        return await DetailAsync(created.Id);
    }

    public async Task<PurchaseOrderDetailDto> DetailAsync(int id)
    {
        await using var db = factory.CreateContext();
        return (await new PurchaseOrderService(db).GetAsync(id)).Value;
    }

    public async Task<ServiceResult<PurchaseOrderDetailDto>> RunAsync(Func<PurchaseOrderService, Task<ServiceResult<PurchaseOrderDetailDto>>> call)
    {
        await using var db = factory.CreateContext();
        return await call(new PurchaseOrderService(db));
    }

    // Het formulier zoals het opent: alle opgeslagen waarden, elke regel met haar id.
    public static UpdatePurchaseOrderDto EditOf(PurchaseOrderDetailDto d) => new()
    {
        RequestId = Guid.NewGuid(), ExpectedVersion = d.EditVersion, VendorId = d.VendorId, Origin = d.Origin, ExpectedAt = d.ExpectedAt == null ? null : new DateTimeOffset(d.ExpectedAt.Value),
        SupplierReference = d.SupplierReference, OrderedOn = d.OrderedOn, OrderChannel = d.OrderChannel, Note = d.Note, PartijId = d.PartijId, SupplierLotReference = d.SupplierLotReference,
        Lines = d.Lines.Select(l => new UpdatePurchaseOrderLineDto
        {
            Id = l.Id, ProductVariantId = l.ProductVariantId, ExpectedQuantity = l.ExpectedQuantity, ExpectedUnit = l.Unit, ExpectedNetWeightKg = l.ExpectedNetWeightKg,
            ExpectedGrossWeightKg = l.ExpectedGrossWeightKg, WeightSource = l.WeightSource, LotNumber = l.LotNumber, PalletCount = l.PalletInputCount,
            PackagesPerPallet = l.PalletInputPackagesPerPallet, LoosePackages = l.PalletInputLoose, UnitPrice = l.UnitPrice,
        }).ToList(),
    };

    public static string CodeOf(ServiceResult<PurchaseOrderDetailDto> result) => (string)result.ConflictBody?.GetType().GetProperty("code")?.GetValue(result.ConflictBody);
}
