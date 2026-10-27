using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Afkeuren en terugsturen van ontvangen inkoop: deel (afsplitsing), alles (order Afgekeurd), grenzen, creditnota en herhaling.
public class PurchaseOrderRejectionTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<ServiceResult<PurchaseOrderDetailDto>> RejectAsync(int orderId, int contentId, decimal quantity, decimal? kg = null, string decision = "ReturnToSupplier",
        Guid? requestId = null, int? version = null, string reason = "Quality", string explanation = null)
    {
        await using var db = _factory.CreateContext();
        var currentVersion = version ?? (await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == contentId)).Version;
        return await new PurchaseOrderService(db).RejectAsync(orderId, new RejectPurchaseOrderDto
        {
            RequestId = requestId ?? Guid.NewGuid(),
            Reason = reason,
            Explanation = explanation,
            Decision = decision,
            ActorReference = "Jan",
            Lines = new List<RejectPurchaseOrderLineDto> { new() { PalletContentLineId = contentId, ExpectedContentLineVersion = currentVersion, Quantity = quantity, NetWeightKg = kg } }
        });
    }

    private async Task<ServiceResult<PurchaseOrderDetailDto>> AddRefAsync(int orderId, string number, string kind = "Invoice")
    {
        await using var db = _factory.CreateContext();
        return await new PurchaseOrderService(db).AddInvoiceRefAsync(orderId, new AddPurchaseOrderInvoiceRefDto { RequestId = Guid.NewGuid(), Kind = kind, InvoiceNumber = number });
    }

    private async Task<PurchaseOrderDetailDto> DetailAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        return (await new PurchaseOrderService(db).GetAsync(orderId)).Value;
    }

    private async Task<PurchaseOrderListDto> ListAsync(string status = null)
    {
        await using var db = _factory.CreateContext();
        return (await new PurchaseOrderService(db).ListAsync(null, status, null, 1, 50)).Value;
    }

    [Fact]
    public async Task Reject_Partial_SplitsRejectedLine_KeepsReceipt_AndExpectsCreditNoteUntilRecorded()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var content = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 221m, 884m);
        await AddRefAsync(order.Id, "F-500");

        var result = await RejectAsync(order.Id, content, 48m);
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var detail = result.Value;
        Assert.Equal("Received", detail.Status);
        Assert.Equal(221m, detail.ReceivedQuantity);
        Assert.Equal(48m, detail.RejectedQuantity);
        Assert.Equal(192m, detail.RejectedNetWeightKg); // evenredig: 884 × 48 / 221
        Assert.Equal("CreditNoteExpected", detail.InvoiceIndicator);

        var original = detail.Receipts.Single(r => r.PalletContentLineId == content);
        Assert.Equal(221m, original.Quantity);           // historische ontvangst blijft
        Assert.Equal(173m, original.OnHandQuantity);
        Assert.Equal(692m, original.OnHandNetWeightKg);
        Assert.Equal("Approved", original.QualityStatus);
        var split = detail.Receipts.Single(r => r.Origin == "RejectionSplit");
        Assert.Equal(48m, split.Quantity);
        Assert.Equal("Rejected", split.QualityStatus);
        Assert.False(split.Rejectable);
        Assert.True(Assert.Single(detail.Rejections).Lines.Single().SplitOff);

        // Beschikbaarheid: 173 goedgekeurd verkoopbaar, 48 afgekeurd, niets dubbel geregistreerd tegen de bron.
        await using (var db = _factory.CreateContext())
        {
            var overview = (await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = seed.HassId })).Value;
            var supply = overview.Variants.Single().WarehouseSupply.Single();
            Assert.Equal(173m, supply.AvailableAfterReservations.Quantity);
            Assert.Equal(48m, supply.Warehouse.Rejected.Quantity);
            Assert.Equal(0m, supply.ExpectedUnregistered.Quantity);
            Assert.DoesNotContain(supply.Warnings, w => w.Code == "OVER_REGISTERED");
            Assert.Equal(884m, (await db.Pallets.Include(p => p.ContentLines).SelectMany(p => p.ContentLines).Where(c => c.Id == content).ToListAsync()).Sum(c => c.NetWeightKg));
        }

        Assert.Equal(ServiceResultStatus.Created, (await AddRefAsync(order.Id, "CN-12", "CreditNote")).Status);
        var after = await DetailAsync(order.Id);
        Assert.Equal("Received", after.InvoiceIndicator);
        Assert.Contains(after.History, h => h.Type == "Rejected" && h.Decision == "ReturnToSupplier" && h.Quantity == 48m);
        Assert.Contains(after.History, h => h.Type == "CreditNoteRecorded" && h.Reference == "CN-12");

        // Een nieuwe afkeuring na de creditnota maakt opnieuw een creditnota verwacht.
        var again = await RejectAsync(order.Id, content, 10m, decision: "ClaimDiscount");
        Assert.Equal("CreditNoteExpected", again.Value.InvoiceIndicator);
    }

    [Fact]
    public async Task Reject_All_TurnsLineRejected_AndOrderRejected_NotExpectedAnymore()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, quantity: 100m));
        var content = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 50m, 200m, quality: "Quarantine");

        var result = await RejectAsync(order.Id, content, 50m, decision: "Dispose");
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal("Rejected", result.Value.Status);
        Assert.Equal("Rejected", result.Value.StoredStatus);
        // Vernietigen: geen creditnota verwacht.
        Assert.Equal("None", result.Value.InvoiceIndicator);
        var line = Assert.Single(result.Value.Receipts);
        Assert.Equal("Rejected", line.QualityStatus);
        Assert.Equal("Receipt", line.Origin);
        Assert.False(Assert.Single(Assert.Single(result.Value.Rejections).Lines).SplitOff);

        await using var db = _factory.CreateContext();
        var audit = await db.PalletAuditEvents.Where(e => e.PalletContentLineId == content && e.EventType == "QualityStatusChanged").SingleAsync();
        Assert.Equal("Jan", audit.ActorReference);
        Assert.Contains("\"qualityStatus\":\"Rejected\"", audit.NewValue);
        // De resterende 50 verwachte dozen tellen niet meer als verwacht, en 50 afgekeurd is nooit verkoopbaar.
        var supply = (await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = seed.HassId })).Value.Variants.Single().WarehouseSupply.Single();
        Assert.Equal(0m, supply.ExpectedUnregistered.Quantity);
        Assert.Equal(0m, supply.AvailableAfterReservations.Quantity);
        Assert.Equal(50m, supply.Warehouse.Rejected.Quantity);
        Assert.Contains((await ListAsync(status: "Rejected")).Items, i => i.Id == order.Id);
        Assert.DoesNotContain((await ListAsync()).Items, i => i.Id == order.Id);

        // Een afgekeurde order wordt niet verder ontvangen.
        var pallet = await new PalletService(db).CreateAsync(new CreatePalletDto { RequestId = Guid.NewGuid(), LogisticsStatus = "Received", LocationContext = "Warehouse" });
        var source = await db.SupplyCommitmentLines.FirstAsync(l => l.SupplyCommitmentId == order.Id);
        var add = await new PalletService(db).AddContentLineAsync(pallet.Value.Id, new AddPalletContentLineDto
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLineId = source.Id, LotNumber = "LOT-2", Quantity = 5m, Unit = "box", NetWeightKg = 20m
        });
        Assert.Equal(ServiceResultStatus.Conflict, add.Status);
    }

    [Fact]
    public async Task Reject_AllOfTwoReceipts_InSteps_MakesOrderRejected_PartOnlyKeepsReceived()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, quantity: 60m));
        var first = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 30m, 120m);
        var second = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 30m, 120m, lot: "LOT-2");

        Assert.Equal("Received", (await RejectAsync(order.Id, first, 30m)).Value.Status);
        var partial = await RejectAsync(order.Id, second, 29m);
        Assert.Equal("Received", partial.Value.Status);
        Assert.Equal(59m, partial.Value.RejectedQuantity);
        var last = await RejectAsync(order.Id, second, 1m);
        Assert.Equal("Rejected", last.Value.Status);
        Assert.Equal("Rejected", last.Value.StoredStatus);
    }

    [Fact]
    public async Task Reject_MoreThanOnHandOrFree_IsRefused_AndDispatchedPartIsNotRejectable()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, quantity: 40m));
        var content = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 40m, 160m);

        var tooMany = await RejectAsync(order.Id, content, 41m);
        Assert.Equal(ServiceResultStatus.Validation, tooMany.Status);
        Assert.Contains("lines[0].quantity", tooMany.Errors.Keys);

        // 10 gereserveerd voor een verkooporder: alleen 30 is vrij; 30 afkeuren mag (splitsing), de reservering blijft geldig.
        await using (var db = _factory.CreateContext())
        {
            var customer = new Customer { Name = "Klant" };
            var salesOrder = new SalesOrder { RequestId = Guid.NewGuid(), Customer = customer, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "office" };
            var salesLine = new SalesOrderLine { SalesOrder = salesOrder, ProductVariantId = seed.HassId, Quantity = 10m, Unit = "box" };
            var source = await db.PalletContentLines.Where(c => c.Id == content).Select(c => c.SupplyCommitmentLineId).SingleAsync();
            db.AddRange(customer, salesOrder, salesLine, new StockReservation
            {
                RequestId = Guid.NewGuid(), SalesOrderLine = salesLine, PalletContentLineId = content, SupplyCommitmentLineId = source,
                Quantity = 10m, Unit = "box", NetWeightKg = 40m, Source = "Physical", Status = "Active"
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(ServiceResultStatus.Validation, (await RejectAsync(order.Id, content, 31m)).Status);
        // Gevraagd kg zou de reservering zonder kg laten.
        Assert.Equal(ServiceResultStatus.Validation, (await RejectAsync(order.Id, content, 30m, kg: 125m)).Status);
        var ok = await RejectAsync(order.Id, content, 30m);
        Assert.Equal(ServiceResultStatus.Created, ok.Status);
        var original = ok.Value.Receipts.Single(r => r.PalletContentLineId == content);
        Assert.Equal(10m, original.OnHandQuantity);
        Assert.Equal(40m, original.OnHandNetWeightKg);
        Assert.False(original.Rejectable); // de rest is gereserveerd
        Assert.Equal("Received", ok.Value.Status);
    }

    [Fact]
    public async Task Reject_CalculatedWeight_UsesQuantityTimesKgPerPackage()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, quantity: 20m, variantId: seed.KentId));
        var content = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 20m, 80m);

        Assert.Equal(ServiceResultStatus.Validation, (await RejectAsync(order.Id, content, 5m, kg: 21m)).Status);
        var ok = await RejectAsync(order.Id, content, 5m);
        Assert.Equal(20m, ok.Value.Receipts.Single(r => r.Origin == "RejectionSplit").NetWeightKg);
        Assert.Equal("Calculated", ok.Value.Receipts.Single(r => r.Origin == "RejectionSplit").WeightMethod);
    }

    [Fact]
    public async Task Reject_Replay_IsIdempotent_StaleOrWrongStateConflicts()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        var content = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 100m, 400m);
        var requestId = Guid.NewGuid();

        Assert.Equal(ServiceResultStatus.Created, (await RejectAsync(order.Id, content, 10m, requestId: requestId, version: 0)).Status);
        // Zelfde opdracht (ook met de oude versie): één afkeuring, huidige order terug.
        Assert.Equal(ServiceResultStatus.Ok, (await RejectAsync(order.Id, content, 10m, requestId: requestId, version: 0)).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await RejectAsync(order.Id, content, 11m, requestId: requestId, version: 0)).Status);
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(1, await db.SupplyRejections.CountAsync());
            Assert.Equal(2, await db.PalletContentLines.CountAsync());
        }
        // Verouderde versie: opnieuw laden en beslissen.
        Assert.Equal(ServiceResultStatus.Conflict, (await RejectAsync(order.Id, content, 5m, version: 0)).Status);

        var inTransit = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 5m, 20m, palletStatus: "InTransit");
        Assert.Equal(ServiceResultStatus.Conflict, (await RejectAsync(order.Id, inTransit, 5m)).Status);
        var rejected = await PurchaseOrderTestData.ReceiveAsync(_factory, order.Id, 5m, 20m, quality: "Rejected");
        Assert.Equal(ServiceResultStatus.Conflict, (await RejectAsync(order.Id, rejected, 5m)).Status);
        var otherOrder = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed));
        Assert.Equal(ServiceResultStatus.NotFound, (await RejectAsync(otherOrder.Id, content, 5m)).Status);
        var other = await RejectAsync(order.Id, content, 5m, reason: "Other");
        Assert.Equal(ServiceResultStatus.Validation, other.Status);
        Assert.Contains("explanation", other.Errors.Keys);
        Assert.Equal(ServiceResultStatus.Created, (await RejectAsync(order.Id, content, 5m, reason: "Other", explanation: "Schimmel in de dozen")).Status);
    }

    [Fact]
    public void DefaultRejectedKg_ProportionalCalculatedOrRemainder()
    {
        Assert.Equal(192m, PurchaseOrderRules.DefaultRejectedKg(48m, 221m, 884m, "Weighed", null));
        Assert.Equal(13.333m, PurchaseOrderRules.DefaultRejectedKg(1m, 3m, 40m, "Weighed", null));
        Assert.Equal(20m, PurchaseOrderRules.DefaultRejectedKg(5m, 20m, 80m, "Calculated", 4m));
        Assert.Equal(80.5m, PurchaseOrderRules.DefaultRejectedKg(20m, 20m, 80.5m, "Calculated", 4m));
    }
}
