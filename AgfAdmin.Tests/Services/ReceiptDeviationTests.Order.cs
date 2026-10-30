using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Gevolgen op de inkooporder (status, regels, afwijkingen, lijst), de beschikbaarheid en de guard (regel 18).
public sealed partial class ReceiptDeviationTests
{
    // ---------------- Orderstatus en beschikbaarheid ----------------

    [Fact]
    public async Task Order_Status_And_Expected_Availability_Follow_The_Deviation()
    {
        await ArrangeAsync();
        Assert.Equal("Ordered", (await DetailAsync()).Status);
        Assert.Equal(100m, await UnregisteredAsync(_v.Peru));

        await AddAsync(Receive(_lineId, 10m, _v.SouthAfrica, confirm: true, reason: "OtherLotLoaded"));

        var detail = await DetailAsync();
        Assert.Equal("PartiallyReceived", detail.Status);
        Assert.Equal((100m, 10m), (detail.ExpectedQuantity, detail.ReceivedQuantity));
        Assert.Equal(90m, await UnregisteredAsync(_v.Peru));
        // Zuid-Afrika verwacht niets meer (alles staat al op een pallet, in quarantaine): nooit verkoopbaar vóór keuring.
        Assert.Equal(0m, await UnregisteredAsync(_v.SouthAfrica));
        var original = detail.Lines.Single(l => l.Id == _lineId);
        var replacement = detail.Lines.Single(l => l.Id != _lineId);
        Assert.Equal(new[] { replacement.Id }, original.ReplacedByLineIds);
        Assert.Equal((_lineId, "Zuid-Afrika"), (replacement.ReplacesLineId, replacement.Origin));
        var deviation = Assert.Single(detail.Deviations);
        Assert.Equal(("Peru", "Zuid-Afrika", "OtherLotLoaded"), (deviation.Expected.Origin, deviation.Received.Origin, deviation.Reason));
        Assert.Equal(new[] { "Origin" }, deviation.Kinds);
        Assert.Contains(detail.History, h => h.Type == "DeviationRecorded" && h.Quantity == 10m && h.Kinds == "Origin");

        await ReceivePlainAsync(90m);
        Assert.Equal("Received", (await DetailAsync()).Status);

        await using var db = _factory.CreateContext();
        var list = await new PurchaseOrderService(db).ListAsync(null, "all", null, 1, 50);
        Assert.True(list.Value.Items.Single(i => i.Id == _orderId).HasDeviation);
    }

    private async Task<PurchaseOrderDetailDto> DetailAsync()
    {
        await using var db = _factory.CreateContext();
        return (await new PurchaseOrderService(db).GetAsync(_orderId)).Value;
    }

    private async Task<decimal> UnregisteredAsync(int variantId)
    {
        await using var db = _factory.CreateContext();
        var drill = await new AvailabilityService(db).GetVariantAsync(variantId, new AvailabilityQuery());
        return drill.Value.SourceLines.Where(s => s.SupplyCommitmentId == _orderId).Sum(s => s.UnregisteredQuantity);
    }

    // ---------------- Guard (regel 18) ----------------

    [Fact]
    public async Task Guard_Refuses_Changing_An_Expected_Quantity_Without_A_Deviation()
    {
        await ArrangeAsync();
        await using var db = _factory.CreateContext();
        (await db.SupplyCommitmentLines.SingleAsync(l => l.Id == _lineId)).ExpectedQuantity = 90m;

        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Guard_Refuses_A_Replacement_Line_Without_A_Deviation_And_Changing_A_Deviation()
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            db.SupplyCommitmentLines.Add(new SupplyCommitmentLine { SupplyCommitmentId = _orderId, ProductVariantId = _v.SouthAfrica, ExpectedQuantity = 10m, ExpectedUnit = "colli", ReplacesLineId = _lineId });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await AddAsync(Receive(_lineId, 10m, _v.SouthAfrica, confirm: true));
        await using (var db = _factory.CreateContext())
        {
            (await db.SupplyReceiptDeviations.SingleAsync()).Reason = "Other";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.SupplyCommitmentLines.SingleAsync(l => l.ReplacesLineId == _lineId)).ProductVariantId = _v.Size16;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
    }
}
