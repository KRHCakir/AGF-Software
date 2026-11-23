using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: komt er iets anders, dan past de gebruiker de orderregel aan. Is de regel al deels ontvangen en verandert het
// artikel, dan splitst het systeem: het ontvangen deel blijft op de oude regel, het open deel wordt een nieuwe regel. Het restant van de oude
// regel wordt nog verwacht of vervalt; niets telt dubbel en ontvangen hoeveelheden veranderen nooit (guard-regel 39).
public class PurchaseOrderSplitTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private readonly PurchaseOrderEditHarness _h;

    public PurchaseOrderSplitTests() => _h = new PurchaseOrderEditHarness(_factory);

    public void Dispose() => _factory.Dispose();

    // De order uit de test van 18 november: 100 dozen mango uit Brazilië, 40 ontvangen; de rest komt als mango uit Peru.
    private async Task<(PurchaseOrderDetailDto Order, int BrazilId, int PeruId)> MangoAsync()
    {
        var seed = await PurchaseOrderTestData.SeedAsync(_factory);
        int brazilId;
        await using (var db = _factory.CreateContext())
        {
            var kent = await db.ProductVariants.SingleAsync(v => v.Id == seed.KentId);
            var brazil = new ProductVariant { ProductId = kent.ProductId, Name = "Kent", Origin = "Brazilië", OriginCountryCode = "BR", Packaging = "4kg Box", DefaultPrice = 8m, WeightMethod = "Calculated", DefaultNetKgPerPackage = 4m };
            db.ProductVariants.Add(brazil);
            await db.SaveChangesAsync();
            brazilId = brazil.Id;
        }
        var created = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(seed, quantity: 100m, variantId: brazilId, change: r =>
        {
            r.Lines[0].ExpectedNetWeightKg = 400m;
            r.Lines[0].UnitPrice = 10m;
            r.Lines[0].PalletCount = 1m;
            r.Lines[0].PackagesPerPallet = 100m;
        }));
        await using (var db = _factory.CreateContext())
        {
            var received = await new ReceiptService(db).ReceiveLineAsync(created.Id, created.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 40m });
            Assert.Equal(ServiceResultStatus.Created, received.Status);
        }
        return (await _h.DetailAsync(created.Id), brazilId, seed.KentId);
    }

    private static UpdatePurchaseOrderDto SplitTo(PurchaseOrderDetailDto order, int variantId, decimal quantity, string remainder)
    {
        var edit = PurchaseOrderEditHarness.EditOf(order);
        var line = edit.Lines[0];
        (line.ProductVariantId, line.ExpectedQuantity, line.ExpectedNetWeightKg, line.WeightSource, line.PalletCount, line.PackagesPerPallet, line.SplitRemainder) =
            (variantId, quantity, quantity * 4m, "Calculated", null, null, remainder);
        return edit;
    }

    [Fact]
    public async Task The_18_November_Order_Splits_And_Counts_Nothing_Twice_When_The_Rest_Is_Cancelled()
    {
        var (order, brazilId, peruId) = await MangoAsync();
        var oldLine = order.Lines.Single();

        var request = SplitTo(order, peruId, 60m, PurchaseOrderDocumentRules.RemainderCancelled);
        var saved = await _h.RunAsync(s => s.UpdateAsync(order.Id, request));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        var lines = saved.Value.Lines.OrderBy(l => l.Id).ToList();
        Assert.Equal(2, lines.Count);
        // De oude regel: mango Brazilië, precies wat ontvangen is, het restant vervallen, niets meer open.
        Assert.Equal((oldLine.Id, brazilId, 40m, 40m, 60m, 160m), (lines[0].Id, lines[0].ProductVariantId, lines[0].ExpectedQuantity, lines[0].ReceivedQuantity, lines[0].CancelledQuantity, lines[0].ExpectedNetWeightKg));
        Assert.Equal(new[] { lines[1].Id }, lines[0].SplitIntoLineIds);
        Assert.Null(lines[0].PalletInputCount);
        // De nieuwe regel: mango Peru, het open deel, dezelfde partij, nog niets ontvangen.
        Assert.Equal((peruId, 60m, 0m, oldLine.Id, oldLine.PartijId), (lines[1].ProductVariantId, lines[1].ExpectedQuantity, lines[1].ReceivedQuantity, lines[1].SplitFromLineId, lines[1].PartijId));

        // Niets telt dubbel: de order verwacht nog steeds 100 dozen, waarvan 60 open.
        await using (var db = _factory.CreateContext())
        {
            var list = (await new PurchaseOrderService(db).ListAsync(null, "all", null, 1, 50, null, default)).Value.Items.Single();
            Assert.Equal((100m, 40m, "PartiallyReceived"), (list.ExpectedQuantity, list.ReceivedQuantity, list.Status));
            var view = (await new ReceiptService(db).GetAsync(order.Id)).Value;
            Assert.Equal(new[] { 0m, 60m }, view.Lines.Select(l => l.OpenQuantity));
        }

        // Eén auditregel met de splitsing, het vervallen restant en het nieuwe aantal; een herhaling splitst niet nog eens.
        var change = saved.Value.Changes[^1];
        Assert.Contains(change.Fields, f => f.Field == "split" && f.LineId == oldLine.Id && f.Action == "added" && f.New!.StartsWith("Mango Kent: 60 box", StringComparison.Ordinal));
        Assert.Contains(change.Fields, f => f.Field == "cancelledQuantity" && f.Old == null && f.New == "60");
        Assert.Contains(change.Fields, f => f.Field == "expectedQuantity" && f.LineId == oldLine.Id && f.Old == "100" && f.New == "40");
        var replay = await _h.RunAsync(s => s.UpdateAsync(order.Id, request));
        Assert.Equal((ServiceResultStatus.Ok, 2, saved.Value.Changes.Count), (replay.Status, replay.Value.Lines.Count, replay.Value.Changes.Count));
    }

    [Fact]
    public async Task A_Cancelled_Rest_Cannot_Give_The_New_Line_More_Than_The_Open_Part()
    {
        var (order, _, peruId) = await MangoAsync();
        var refused = await _h.RunAsync(s => s.UpdateAsync(order.Id, SplitTo(order, peruId, 100m, PurchaseOrderDocumentRules.RemainderCancelled)));
        Assert.Contains("lines[0].expectedQuantity", refused.Errors.Keys);
    }

    [Fact]
    public async Task When_The_Rest_Is_Still_Expected_The_Old_Line_Stays_Open_Next_To_The_New_Line()
    {
        var (order, _, peruId) = await MangoAsync();
        var saved = await _h.RunAsync(s => s.UpdateAsync(order.Id, SplitTo(order, peruId, 30m, PurchaseOrderDocumentRules.RemainderExpected)));
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        var lines = saved.Value.Lines.OrderBy(l => l.Id).ToList();
        Assert.Equal((100m, 40m, (decimal?)null), (lines[0].ExpectedQuantity, lines[0].ReceivedQuantity, lines[0].CancelledQuantity));
        Assert.Equal(30m, lines[1].ExpectedQuantity);
        Assert.DoesNotContain(saved.Value.Changes[^1].Fields, f => f.Field == "cancelledQuantity");
    }

    [Fact]
    public async Task The_User_Must_Choose_What_Happens_With_The_Rest_And_A_Line_Without_Receipt_Is_Just_Changed()
    {
        var (order, _, peruId) = await MangoAsync();
        var missing = await _h.RunAsync(s => s.UpdateAsync(order.Id, SplitTo(order, peruId, 60m, null)));
        Assert.Equal(ServiceResultStatus.Validation, missing.Status);
        Assert.Contains("lines[0].splitRemainder", missing.Errors.Keys);
        var invalid = await _h.RunAsync(s => s.UpdateAsync(order.Id, SplitTo(order, peruId, 60m, "Later")));
        Assert.Contains("lines[0].splitRemainder", invalid.Errors.Keys);

        // Een regel zonder ontvangst krijgt gewoon het andere artikel (geen splitsing; een keuze voor het restant is dan een vergissing).
        var plain = await _h.OrderAsync();
        var edit = PurchaseOrderEditHarness.EditOf(plain);
        (edit.Lines[0].ProductVariantId, edit.Lines[0].ExpectedNetWeightKg, edit.Lines[0].WeightSource) = (_h.Seed.KentId, 884m, "Calculated");
        edit.Lines[0].ExpectedQuantity = 221m;
        var changed = await _h.RunAsync(s => s.UpdateAsync(plain.Id, edit));
        Assert.Equal(ServiceResultStatus.Ok, changed.Status);
        Assert.Single(changed.Value.Lines);
        edit = PurchaseOrderEditHarness.EditOf(changed.Value);
        edit.Lines[0].SplitRemainder = "Cancelled";
        Assert.Contains("lines[0].splitRemainder", (await _h.RunAsync(s => s.UpdateAsync(plain.Id, edit))).Errors.Keys);
    }

    [Fact]
    public async Task A_Fully_Received_Line_Is_Not_Split_And_Keeps_Its_Article()
    {
        var (order, _, peruId) = await MangoAsync();
        await using (var db = _factory.CreateContext())
        {
            var received = await new ReceiptService(db).ReceiveLineAsync(order.Id, order.Lines.Single().Id, new ReceiveLineDto { RequestId = Guid.NewGuid(), Quantity = 60m });
            Assert.Equal(ServiceResultStatus.Created, received.Status);
        }
        var full = await _h.DetailAsync(order.Id);
        foreach (var remainder in new[] { PurchaseOrderDocumentRules.RemainderCancelled, PurchaseOrderDocumentRules.RemainderExpected })
        {
            var refused = await _h.RunAsync(s => s.UpdateAsync(full.Id, SplitTo(full, peruId, 60m, remainder)));
            Assert.Equal(ServiceResultStatus.Validation, refused.Status);
            Assert.Contains("lines[0].productVariantId", refused.Errors.Keys);
        }
        Assert.Single((await _h.DetailAsync(full.Id)).Lines);
    }

    [Fact]
    public async Task Guard_Rule_39_Keeps_Received_Quantities_And_Splits_Consistent()
    {
        var (order, _, peruId) = await MangoAsync();
        var lineId = order.Lines.Single().Id;

        // Ontvangen hoeveelheden veranderen nooit (de bestaande regel blijft staan).
        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync();
            (content.Quantity, content.NetWeightKg) = (39m, 156m);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        // Een gesplitste regel zonder Edit-auditregel, of een vervallen restant zonder Edit: geweigerd.
        await using (var db = _factory.CreateContext())
        {
            db.SupplyCommitmentLines.Add(new SupplyCommitmentLine { SupplyCommitmentId = order.Id, ProductVariantId = peruId, ExpectedQuantity = 60m, ExpectedUnit = "box", SplitFromLineId = lineId });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var line = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId);
            (line.ExpectedQuantity, line.CancelledQuantity, line.PalletCount, line.PackagesPerPallet) = (40m, 60m, null, null);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }

        // Een palletregel verdwijnt nooit (de database weigert dat: de auditregel van de ontvangst houdt haar vast).
        await using (var db = _factory.CreateContext())
        {
            db.PalletContentLines.Remove(await db.PalletContentLines.SingleAsync());
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Na de splitsing: de oude regel houdt haar vervallen restant (nooit lager) en de nieuwe regel haar herkomst.
        await _h.RunAsync(s => s.UpdateAsync(order.Id, SplitTo(order, peruId, 60m, PurchaseOrderDocumentRules.RemainderCancelled)));
        await using (var db = _factory.CreateContext())
        {
            var split = await db.SupplyCommitmentLines.SingleAsync(l => l.SplitFromLineId == lineId);
            split.SplitFromLineId = null;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var line = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId);
            line.CancelledQuantity = 10m;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }
}
