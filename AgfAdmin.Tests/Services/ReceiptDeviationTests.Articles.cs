using AgfAdmin.DTOs;
using AgfAdmin.Services;

namespace AgfAdmin.Tests.Services;

// Artikel verwijderen na een afwijkende ontvangst: de ontvangen variant hangt aan de afwijking, de vervangende orderregel en de
// ontvangst; de bestelde variant aan de afwijking, de oorspronkelijke orderregel. Geen van beide kan nog worden verwijderd.
public sealed partial class ReceiptDeviationTests
{
    [Fact]
    public async Task Both_Variants_Of_A_Deviation_Count_It_And_Cannot_Be_Deleted()
    {
        await ArrangeAsync();
        Assert.Equal(ServiceResultStatus.Created, (await AddAsync(Receive(_lineId, 10m, _v.SouthAfrica, confirm: true, reason: "SupplierDeliveredDifferently"))).Status);

        await using var db = _factory.CreateContext();
        var service = new ArticleLifecycleService(db);
        var received = (await service.UsageAsync(_v.SouthAfrica)).Value;
        Assert.Equal(new[] { "purchaseOrderLines", "receipts", "receiptDeviations" }, received.Usages.Select(u => u.Kind));
        var ordered = (await service.UsageAsync(_v.Peru)).Value;
        Assert.Equal(new[] { "purchaseOrderLines", "receiptDeviations" }, ordered.Usages.Select(u => u.Kind));
        Assert.Equal(ServiceResultStatus.Conflict, (await service.DeleteAsync(_v.SouthAfrica, new ArticleActionDto { ActorReference = "Kees" })).Status);
        Assert.True((await service.UsageAsync(_v.Twin)).Value.CanDelete);
    }
}
