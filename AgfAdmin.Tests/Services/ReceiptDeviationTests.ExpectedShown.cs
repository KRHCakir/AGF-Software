using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// "Toon verwachte kenmerken" bij blinde invoer: de ontvangst legt vast dat (en wanneer, door wie) de verwachting vooraf werd getoond;
// zichtbaar op de inkooporder en daarna onveranderlijk (guard-regel 20). Niet getoond = false zonder moment.
public sealed partial class ReceiptDeviationTests
{
    [Fact]
    public async Task Showing_The_Expected_Attributes_Is_Recorded_With_Moment_And_Employee_And_Shown_On_The_Order()
    {
        await ArrangeAsync();
        var shownAt = new DateTimeOffset(2026, 10, 31, 9, 15, 0, TimeSpan.FromHours(1));
        var request = Receive(_lineId, 10m);
        request.ExpectedShownBeforeEntry = true;
        request.ExpectedShownAt = shownAt;
        var result = await AddAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal((true, shownAt.UtcDateTime, "Kees"), (result.Value.ExpectedShownBeforeEntry, result.Value.ExpectedShownAt.Value, result.Value.ExpectedShownBy));
        await using var db = _factory.CreateContext();
        var receipt = (await new PurchaseOrderService(db).GetAsync(_orderId)).Value.Receipts.Single();
        Assert.Equal((true, shownAt.UtcDateTime, "Kees"), (receipt.ExpectedShownBeforeEntry, receipt.ExpectedShownAt.Value, receipt.ExpectedShownBy));
    }

    [Fact]
    public async Task Not_Shown_Is_Recorded_As_False_Without_Moment()
    {
        await ArrangeAsync();
        var result = await AddAsync(Receive(_lineId, 10m));
        Assert.Equal((false, (DateTime?)null, (string)null), (result.Value.ExpectedShownBeforeEntry, result.Value.ExpectedShownAt, result.Value.ExpectedShownBy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shown_Needs_A_Moment_That_Is_Not_In_The_Future(bool future)
    {
        await ArrangeAsync();
        var request = Receive(_lineId, 10m);
        request.ExpectedShownBeforeEntry = true;
        request.ExpectedShownAt = future ? DateTimeOffset.UtcNow.AddHours(1) : null;
        var result = await AddAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.True(result.Errors.ContainsKey("expectedShownAt"));
        Assert.Equal(0, (await StateAsync()).Contents);
    }

    [Fact]
    public async Task What_Was_Shown_Can_Never_Change_Afterwards()
    {
        await ArrangeAsync();
        var created = await AddAsync(Receive(_lineId, 10m));
        await using var db = _factory.CreateContext();
        var line = await db.PalletContentLines.SingleAsync(c => c.Id == created.Value.Id);
        line.ExpectedShownBeforeEntry = true;
        line.ExpectedShownAt = DateTime.UtcNow;
        line.ExpectedShownBy = "Piet";
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    // Een losgekoppelde Update markeert alle kolommen als gewijzigd: met dezelfde waarden mag dat, met andere nooit.
    [Fact]
    public async Task A_Detached_Update_Is_Compared_With_The_Stored_Values()
    {
        await ArrangeAsync();
        var created = await AddAsync(Receive(_lineId, 10m));
        PalletContentLine stored;
        await using (var read = _factory.CreateContext())
        {
            stored = await read.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == created.Value.Id);
        }
        await using (var same = _factory.CreateContext())
        {
            same.PalletContentLines.Update(stored);
            await same.SaveChangesAsync();
        }
        await using (var read = _factory.CreateContext())
        {
            stored = await read.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == created.Value.Id);
        }
        stored.ExpectedShownBeforeEntry = true;
        stored.ExpectedShownAt = DateTime.UtcNow;
        await using var changed = _factory.CreateContext();
        changed.PalletContentLines.Update(stored);
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => changed.SaveChangesAsync());
    }
}
