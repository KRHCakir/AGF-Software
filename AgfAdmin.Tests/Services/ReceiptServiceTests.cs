using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: ontvangen in één scherm. Eén klik ontvangt het openstaande aantal van een regel (of een afwijkend aantal), zonder
// controlevragen; palletnummers zijn optioneel; een herhaling met dezelfde RequestId maakt niets dubbel.
public class ReceiptServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(PurchaseOrderSeed Seed, SupplyCommitmentDto Order)> OrderAsync(Action<CreateSupplyCommitmentDto> change = null)
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var request = PurchaseOrderTestData.OrderRequest(seed);
        change?.Invoke(request);
        return (seed, await PurchaseOrderTestData.CreateOrderAsync(_factory, request));
    }

    private async Task<ServiceResult<ReceiptOrderDto>> ViewAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        return await new ReceiptService(db).GetAsync(orderId);
    }

    private async Task<ServiceResult<ReceiveLineResultDto>> ReceiveAsync(int orderId, int lineId, ReceiveLineDto request)
    {
        await using var db = _factory.CreateContext();
        return await new ReceiptService(db).ReceiveLineAsync(orderId, lineId, request);
    }

    [Fact]
    public async Task One_Click_Receives_The_Whole_Open_Quantity_In_Quarantine_Without_Questions()
    {
        var (_, order) = await OrderAsync();
        var line = order.Lines.Single();

        var view = (await ViewAsync(order.Id)).Value;
        var expected = Assert.Single(view.Lines);
        Assert.Equal((221m, 0m, 221m, 4m, true), (expected.OrderedQuantity, expected.ReceivedQuantity, expected.OpenQuantity, expected.NetKgPerPackage, view.Receivable));
        Assert.Equal("Ordered", view.Status);

        var result = await ReceiveAsync(order.Id, line.Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = expected.OpenQuantity });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var pallet = Assert.Single(result.Value.Pallets);
        Assert.Equal((221m, 884m, "Quarantine"), (pallet.Quantity, pallet.NetWeightKg, pallet.QualityStatus));
        Assert.StartsWith("P-", pallet.Reference, StringComparison.Ordinal);
        Assert.Equal(("Received", 0m), (result.Value.Order.Status, result.Value.Order.Lines.Single().OpenQuantity));

        // Geen afwijking vastgelegd, geen vervangende regel: de ontvangst hangt aan de orderregel zelf.
        await using var db = _factory.CreateContext();
        Assert.False(await db.SupplyReceiptDeviations.AnyAsync());
        var content = await db.PalletContentLines.Include(c => c.Pallet).SingleAsync();
        Assert.Equal((line.Id, "Received", "Warehouse", "Calculated", 4m), (content.SupplyCommitmentLineId, content.Pallet.LogisticsStatus, content.Pallet.LocationContext, content.WeightMethod, content.NetKgPerPackage));
        // Feedback 22 november: de order had nog geen partij; deze ontvangst gaf haar het voorgestelde I-nummer (nooit voorraad zonder partij).
        var partij = await db.Partijen.SingleAsync();
        Assert.StartsWith("I-", partij.Number, StringComparison.Ordinal);
        Assert.Equal((partij.Id, partij.Number), (content.PartijId, content.LotNumber));
        Assert.Equal(partij.Id, (await db.SupplyCommitments.SingleAsync(c => c.Id == order.Id)).PartijId);
    }

    [Fact]
    public async Task A_Different_Quantity_Leaves_The_Rest_Open_And_A_Retry_Does_Not_Receive_Twice()
    {
        var (_, order) = await OrderAsync();
        var line = order.Lines.Single();
        var request = new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 100m };

        var first = await ReceiveAsync(order.Id, line.Id, request);
        var retry = await ReceiveAsync(order.Id, line.Id, request);
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        Assert.Equal(first.Value.Pallets.Single().PalletId, retry.Value.Pallets.Single().PalletId);

        var view = (await ViewAsync(order.Id)).Value;
        var after = view.Lines.Single();
        Assert.Equal((100m, 121m, "PartiallyReceived"), (after.ReceivedQuantity, after.OpenQuantity, view.Status));
        Assert.Single(after.Pallets);

        // Meer dan verwacht mag ook (de leverancier stuurde meer): dan staat er niets meer open.
        var more = await ReceiveAsync(order.Id, line.Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 130m });
        Assert.Equal(ServiceResultStatus.Created, more.Status);
        Assert.Equal((230m, 0m, "Received"), (more.Value.Order.Lines.Single().ReceivedQuantity, more.Value.Order.Lines.Single().OpenQuantity, more.Value.Order.Status));
    }

    [Fact]
    public async Task Pallet_Numbers_Are_Optional_And_The_Quantity_Is_Divided_Over_The_Pallets()
    {
        var (_, order) = await OrderAsync();
        var line = order.Lines.Single();

        var result = await ReceiveAsync(order.Id, line.Id, new ReceiveLineDto
        {
            RequestId = Guid.NewGuid(), Quantity = 100m, PalletCount = 3, PalletReferences = new List<string> { "LDR-1", " ", "LDR-3" },
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(new[] { 34m, 33m, 33m }, result.Value.Pallets.Select(p => p.Quantity));
        Assert.Equal(new[] { 136m, 132m, 132m }, result.Value.Pallets.Select(p => p.NetWeightKg));
        Assert.Equal("LDR-1", result.Value.Pallets[0].Reference);
        Assert.StartsWith("P-", result.Value.Pallets[1].Reference, StringComparison.Ordinal);
        Assert.Equal("LDR-3", result.Value.Pallets[2].Reference);

        // Ongeldig: meer nummers dan pallets, dubbele nummers, een pallet zonder hele verpakking.
        var invalid = await ReceiveAsync(order.Id, line.Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 2m, PalletCount = 3, PalletReferences = new List<string> { "X", "X" } });
        Assert.Equal(ServiceResultStatus.Validation, invalid.Status);
        Assert.Contains("palletReferences", invalid.Errors.Keys);
        var tooFew = await ReceiveAsync(order.Id, line.Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 2m, PalletCount = 3 });
        Assert.Contains("palletCount", tooFew.Errors.Keys);
    }

    [Fact]
    public async Task Without_Kg_On_The_Order_The_Receiver_Enters_The_Net_Kg_Otherwise_It_Follows_From_The_Order()
    {
        var (_, order) = await OrderAsync(r => r.Lines[0].ExpectedNetWeightKg = null);
        var line = order.Lines.Single();
        Assert.Null((await ViewAsync(order.Id)).Value.Lines.Single().NetKgPerPackage);

        var missing = await ReceiveAsync(order.Id, line.Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 50m });
        Assert.Contains("netWeightKg", missing.Errors.Keys);
        var weighed = await ReceiveAsync(order.Id, line.Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 50m, NetWeightKg = 201.5m, PalletCount = 2 });
        Assert.Equal(ServiceResultStatus.Created, weighed.Status);
        Assert.Equal(new[] { 100.75m, 100.75m }, weighed.Value.Pallets.Select(p => p.NetWeightKg));

        var (_, withKg) = await OrderAsync();
        var refused = await ReceiveAsync(withKg.Id, withKg.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 10m, NetWeightKg = 41m });
        Assert.Contains("netWeightKg", refused.Errors.Keys);
    }

    [Fact]
    public async Task The_Lot_Of_The_Order_Line_Is_Taken_Over_And_A_Cancelled_Order_Is_Not_Received()
    {
        var (_, order) = await OrderAsync();
        var partijId = await PartijTestData.CreateAsync(_factory, "2611301");
        await PartijTestData.LinkOrderAsync(_factory, order.Id, partijId);
        var view = (await ViewAsync(order.Id)).Value;
        Assert.Equal("2611301", view.Lines.Single().PartijNumber);

        var result = await ReceiveAsync(order.Id, order.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 10m });
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == result.Value.Pallets.Single().ContentLineId);
            Assert.Equal((partijId, "2611301"), (content.PartijId, content.LotNumber));
            var commitment = await db.SupplyCommitments.SingleAsync(c => c.Id == order.Id);
            commitment.Status = SupplyRules.StatusCancelled;
            await db.SaveChangesAsync();
        }
        var cancelled = await ReceiveAsync(order.Id, order.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 10m });
        Assert.Equal(ServiceResultStatus.Conflict, cancelled.Status);
        Assert.False((await ViewAsync(order.Id)).Value.Receivable);
    }

    [Fact]
    public async Task A_Certificate_Warning_Is_A_Line_On_The_Screen_And_Receiving_Records_Who_Saw_It()
    {
        var (seed, order) = await OrderAsync();
        await using (var db = _factory.CreateContext())
        {
            // De leverancier heeft een GGN-certificaat voor een ander product: avocado staat er niet in.
            var vendor = await db.Vendors.SingleAsync(v => v.Id == seed.VendorId);
            var relation = new Relation { Name = "Nature's Pride" };
            db.Relations.Add(relation);
            await db.SaveChangesAsync();
            vendor.RelationId = relation.Id;
            var mango = await db.Products.SingleAsync(p => p.Name == "Mango");
            db.RelationCertificates.Add(new RelationCertificate
            {
                RelationId = relation.Id, TypeCode = CertificateRules.GlobalGapIfa, Number = "4049928000001", NumberKey = "4049928000001", IsActive = true, CreatedBy = "Test", UpdatedBy = "Test",
                ValidUntil = new DateOnly(2099, 1, 1), Products = new List<RelationCertificateProduct> { new() { ProductId = mango.Id } },
            });
            await db.SaveChangesAsync();
        }

        var view = (await ViewAsync(order.Id)).Value;
        var warning = Assert.Single(view.CertificateWarnings);
        Assert.Equal("Avocado", warning.ProductName);

        // Zonder het kenmerk van de getoonde melding: 409, niets ontvangen en niets als gezien vastgelegd.
        var unseen = await ReceiveAsync(order.Id, order.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 5m });
        Assert.Equal(ServiceResultStatus.Conflict, unseen.Status);
        Assert.NotNull(view.CertificateWarningToken);
        await using (var none = _factory.CreateContext())
        {
            Assert.False(await none.CertificateConfirmations.AnyAsync());
            Assert.False(await none.PalletContentLines.AnyAsync());
        }

        var result = await ReceiveAsync(order.Id, order.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 5m, CertificateWarningToken = view.CertificateWarningToken });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        await using var check = _factory.CreateContext();
        var confirmation = await check.CertificateConfirmations.SingleAsync();
        Assert.Equal((CertificateCheck.ContextReceipt, PurchaseOrderEditHarness.Actor), (confirmation.Context, confirmation.ConfirmedBy));
    }

    [Fact]
    public async Task A_Failure_On_A_Later_Pallet_Saves_Nothing_So_A_Corrected_Retry_Does_Not_Receive_Twice()
    {
        var (_, order) = await OrderAsync();
        var line = order.Lines.Single();
        await using (var db = _factory.CreateContext())
        {
            var taken = await new PalletService(db).CreateAsync(new CreatePalletDto
            {
                RequestId = Guid.NewGuid(), Reference = "TAKEN", PalletType = "EUR", LogisticsStatus = PalletRules.StatusReceived, LocationContext = PalletRules.LocationWarehouse,
            });
            Assert.Equal(ServiceResultStatus.Created, taken.Status);
        }

        // Pallet 1 is nieuw, pallet 2 heeft een nummer dat al een andere (afwijkende) pallet heeft: de hele ontvangst faalt, ook pallet 1 blijft weg.
        var failed = await ReceiveAsync(order.Id, line.Id, new ReceiveLineDto
        {
            RequestId = Guid.NewGuid(), Quantity = 100m, PalletCount = 2, PalletReferences = new List<string> { "NEW-1", "TAKEN" },
        });
        Assert.NotEqual(ServiceResultStatus.Created, failed.Status);
        await using var check = _factory.CreateContext();
        Assert.Equal(1, await check.Pallets.CountAsync());
        Assert.False(await check.PalletContentLines.AnyAsync());
        Assert.Equal(0m, (await ViewAsync(order.Id)).Value.Lines.Single().ReceivedQuantity);
    }

    [Theory]
    [InlineData(100, 3, new[] { 34.0, 33.0, 33.0 })]
    [InlineData(10.5, 2, new[] { 5.0, 5.5 })]
    [InlineData(7, 1, new[] { 7.0 })]
    public void Split_Divides_Whole_Packages_Evenly(double quantity, int pallets, double[] expected) =>
        Assert.Equal(expected.Select(e => (decimal)e), ReceiptService.Split((decimal)quantity, pallets));

    [Fact]
    public void Split_And_Weights_Refuse_What_Cannot_Be_Divided_And_Weights_Add_Up()
    {
        Assert.Null(ReceiptService.Split(2m, 3));
        var weights = ReceiptService.WeightsFor(new[] { 34m, 33m, 33m }, 1000m / 120m, null)!.Value;
        Assert.Equal("Weighed", weights.Method);
        Assert.Equal(decimal.Round(100m * (1000m / 120m), 3, MidpointRounding.AwayFromZero), weights.Kg.Sum());
        Assert.Equal(ReceiptService.Derived(Guid.Empty, "a"), ReceiptService.Derived(Guid.Empty, "a"));
        Assert.NotEqual(ReceiptService.Derived(Guid.Empty, "a"), ReceiptService.Derived(Guid.Empty, "b"));
    }
}
