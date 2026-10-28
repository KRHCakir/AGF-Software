using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Guard regel 17 (afkeuringsgrootboek) en voorraad = ontvangst - afgeboekt - afgesplitst bij elke lezer.
public class RejectionLedgerGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record Seed(int OrderId, int VariantId, int ContentId);

    // Order met 100 doos Hass ontvangen en goedgekeurd op een Received + Warehouse-pallet.
    private async Task<Seed> SeedAsync()
    {
        await using var db = _factory.CreateContext();
        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12m };
        var order = new SupplyCommitment { RequestId = Guid.NewGuid(), Vendor = new Vendor { Name = "Nature's Pride" }, Route = "Warehouse", Status = "Expected", Origin = "Peru" };
        var line = new SupplyCommitmentLine { SupplyCommitment = order, ProductVariant = variant, ExpectedQuantity = 100m, ExpectedUnit = "box" };
        var content = new PalletContentLine
        {
            RequestId = Guid.NewGuid(), Pallet = new Pallet { Reference = "P-REJ", LogisticsStatus = "Received", LocationContext = "Warehouse" },
            SupplyCommitmentLine = line, LotNumber = "LOT-1", Quantity = 100m, Unit = "box", NetWeightKg = 400m, QualityStatus = "Approved"
        };
        db.AddRange(variant, order, line, content);
        await db.SaveChangesAsync();
        return new Seed(order.Id, variant.Id, content.Id);
    }

    private static SupplyRejection Rejection(int orderId) =>
        new() { RequestId = Guid.NewGuid(), SupplyCommitmentId = orderId, Reason = "Quality", Decision = "ReturnToSupplier", ActorReference = "Jan" };

    // Geldige deelafkeuring zoals de service die schrijft: afgesplitst omhoog, afkeuringsregel en een Rejected kopie met herkomst.
    private static (SupplyRejectionLine Line, PalletContentLine Split) SplitOff(AppDbContext db, PalletContentLine content, SupplyRejection rejection, decimal quantity, decimal kg)
    {
        content.SplitOffQuantity += quantity;
        content.SplitOffNetWeightKg += kg;
        var line = new SupplyRejectionLine { PalletContentLine = content, SupplyCommitmentLineId = content.SupplyCommitmentLineId, Quantity = quantity, Unit = content.Unit, NetWeightKg = kg, SplitOff = true };
        rejection.Lines.Add(line);
        var split = new PalletContentLine
        {
            RequestId = Guid.NewGuid(), PalletId = content.PalletId, SupplyCommitmentLineId = content.SupplyCommitmentLineId, LotNumber = content.LotNumber,
            Quantity = quantity, Unit = content.Unit, NetWeightKg = kg, QualityStatus = "Rejected", OriginRejectionLine = line
        };
        db.PalletContentLines.Add(split);
        return (line, split);
    }

    [Fact]
    public async Task ValidSplit_IsAccepted_AndEveryReaderUsesOnHandMinusSplitOff()
    {
        var seed = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            db.SupplyRejections.Add(rejection);
            SplitOff(db, content, rejection, 30m, 120m);
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        var pallet = (await new PalletService(verify).GetAsync((await verify.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId)).PalletId)).Value;
        var original = pallet.ContentLines.Single(c => c.Id == seed.ContentId);
        Assert.Equal(100m, original.Quantity);
        Assert.Equal(70m, original.OnHandQuantity);
        Assert.Equal(280m, original.OnHandNetWeightKg);
        Assert.Equal(30m, original.SplitOffQuantity);
        var split = pallet.ContentLines.Single(c => c.Id != seed.ContentId);
        Assert.Equal("RejectionSplit", split.Origin);
        Assert.Equal("Rejected", split.QualityStatus);
        // Palletgewicht en ontvangst blijven gelijk: niets dubbel geteld.
        Assert.Equal(400m, pallet.ContentNetKg);
        Assert.Equal(400m, pallet.ReceivedNetKg);

        var supply = (await new AvailabilityService(verify).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = seed.VariantId })).Value.Variants.Single().WarehouseSupply.Single();
        Assert.Equal(70m, supply.Warehouse.Approved.Quantity);
        Assert.Equal(30m, supply.Warehouse.Rejected.Quantity);
        Assert.Equal(0m, supply.ExpectedUnregistered.Quantity);
        Assert.DoesNotContain(supply.Warnings, w => w.Code == "OVER_REGISTERED");
        var drill = (await new AvailabilityService(verify).GetVariantAsync(seed.VariantId, new AvailabilityQuery())).Value;
        Assert.Equal(100m, drill.SourceLines.Single().RegisteredQuantity);
        Assert.Equal("RejectionSplit", drill.ContentLines.Single(c => c.PalletContentLineId != seed.ContentId).Origin);
    }

    [Fact]
    public async Task SplitOffWithoutRejection_OrRejectionWithoutSplitOff_IsRefused()
    {
        var seed = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            (await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId)).SplitOffQuantity = 10m;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            db.SupplyRejections.Add(rejection);
            SplitOff(db, content, rejection, 10m, 40m);
            content.SplitOffQuantity -= 10m; // regel en kopie zonder verhoging op de oorspronkelijke regel
            content.SplitOffNetWeightKg -= 40m;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            db.SupplyRejections.Add(rejection);
            var (_, split) = SplitOff(db, content, rejection, 10m, 40m);
            split.Quantity = 11m; // kopie wijkt af van de afkeuringsregel
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            db.SupplyRejections.Add(rejection);
            var (_, split) = SplitOff(db, content, rejection, 10m, 40m);
            split.QualityStatus = "Approved"; // afgesplitste inhoud is altijd Rejected
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task WholeRejection_NeedsStatusRejected_AndExactOnHand()
    {
        var seed = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            rejection.Lines.Add(new SupplyRejectionLine { PalletContentLine = content, SupplyCommitmentLineId = content.SupplyCommitmentLineId, Quantity = 100m, Unit = "box", NetWeightKg = 400m });
            db.SupplyRejections.Add(rejection);
            content.QualityReason = "x"; // gewijzigd, maar niet naar Rejected
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            rejection.Lines.Add(new SupplyRejectionLine { PalletContentLine = content, SupplyCommitmentLineId = content.SupplyCommitmentLineId, Quantity = 99m, Unit = "box", NetWeightKg = 400m });
            db.SupplyRejections.Add(rejection);
            content.QualityStatus = "Rejected";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            rejection.Lines.Add(new SupplyRejectionLine { PalletContentLine = content, SupplyCommitmentLineId = content.SupplyCommitmentLineId, Quantity = 100m, Unit = "box", NetWeightKg = 400m });
            db.SupplyRejections.Add(rejection);
            content.QualityStatus = "Rejected";
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SplitOffNeverDecreases_OriginIsFixed_AndRecordsAreAppendOnly()
    {
        var seed = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            db.SupplyRejections.Add(rejection);
            SplitOff(db, content, rejection, 10m, 40m);
            db.SupplyCommitmentInvoiceRefs.Add(new SupplyCommitmentInvoiceRef { RequestId = Guid.NewGuid(), SupplyCommitmentId = seed.OrderId, Kind = "Invoice", InvoiceNumber = "F-9" });
            await db.SaveChangesAsync();
        }

        async Task Refused(Func<AppDbContext, Task> change)
        {
            await using var db = _factory.CreateContext();
            await change(db);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await Refused(async db => (await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId)).SplitOffQuantity = 5m);
        await Refused(async db => (await db.PalletContentLines.SingleAsync(c => c.OriginRejectionLineId != null)).OriginRejectionLineId = null);
        await Refused(async db => (await db.SupplyRejectionLines.SingleAsync()).Quantity = 9m);
        await Refused(async db => db.SupplyRejections.Remove(await db.SupplyRejections.SingleAsync()));
        await Refused(async db => (await db.SupplyCommitmentInvoiceRefs.SingleAsync()).InvoiceNumber = "F-10");
    }

    [Fact]
    public async Task SplitOff_NeverMoreThanOnHand_AndNeverBelowActiveReservations()
    {
        var seed = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            db.SupplyRejections.Add(rejection);
            SplitOff(db, content, rejection, 101m, 404m); // meer dan de 100 op voorraad
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }

        // 80 doos / 320 kg actief gereserveerd voor een verkooporder: 30 afsplitsen laat er maar 70 over.
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var order = new SalesOrder { RequestId = Guid.NewGuid(), Customer = new Customer { Name = "Klant" }, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "office" };
            var line = new SalesOrderLine { SalesOrder = order, ProductVariantId = seed.VariantId, Quantity = 80m, Unit = "box" };
            db.AddRange(order, line, new StockReservation
            {
                RequestId = Guid.NewGuid(), SalesOrderLine = line, PalletContentLineId = content.Id, SupplyCommitmentLineId = content.SupplyCommitmentLineId,
                Quantity = 80m, Unit = "box", NetWeightKg = 320m, Source = "Physical", Status = "Active"
            });
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            db.SupplyRejections.Add(rejection);
            SplitOff(db, content, rejection, 30m, 120m);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            db.SupplyRejections.Add(rejection);
            SplitOff(db, content, rejection, 20m, 80m);
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("ReturnToSupplier", "Blocked")]
    [InlineData("ClaimDiscount", "Rejected")]
    public async Task SplitStatus_MustFollowDecision(string decision, string status)
    {
        var seed = await SeedAsync();
        await using var db = _factory.CreateContext();
        var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
        var rejection = Rejection(seed.OrderId);
        rejection.Decision = decision;
        db.SupplyRejections.Add(rejection);
        var (_, split) = SplitOff(db, content, rejection, 30m, 120m);
        split.QualityStatus = status;
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ClaimDiscount_Split_IsBlocked_AndWholeLineGoesBlocked()
    {
        var seed = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
            var rejection = Rejection(seed.OrderId);
            rejection.Decision = "ClaimDiscount";
            db.SupplyRejections.Add(rejection);
            var (_, split) = SplitOff(db, content, rejection, 30m, 120m);
            split.QualityStatus = "Blocked";
            await db.SaveChangesAsync();
        }

        await using var whole = _factory.CreateContext();
        var rest = await whole.PalletContentLines.SingleAsync(c => c.Id == seed.ContentId);
        var discount = Rejection(seed.OrderId);
        discount.Decision = "ClaimDiscount";
        discount.Lines.Add(new SupplyRejectionLine { PalletContentLine = rest, SupplyCommitmentLineId = rest.SupplyCommitmentLineId, Quantity = 70m, Unit = rest.Unit, NetWeightKg = 280m });
        whole.SupplyRejections.Add(discount);
        rest.QualityStatus = "Rejected";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => whole.SaveChangesAsync());
        rest.QualityStatus = "Blocked";
        await whole.SaveChangesAsync();
    }
}
