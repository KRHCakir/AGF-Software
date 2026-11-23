using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Feedback 22 november: kleur is een optioneel kenmerk van de partij (niet van de pallet), met een eigen lijst per product. Leeg = geen kleur
// voor dat product. De kleur staat in de partijkeuze en in Beschikbaarheid; een wijziging heeft altijd een historieregel.
public sealed class LotColorTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();
    private int _lot, _productId;

    public void Dispose() => _s.Dispose();

    private async Task ArrangeAsync()
    {
        await _s.ArrangeAsync();
        await using var db = _s.Factory.CreateContext();
        var variant = await db.ProductVariants.Include(v => v.Product).SingleAsync(v => v.Id == _s.TomatoId);
        _productId = variant.ProductId;
        var lot = new Partij { Kind = PartijRules.KindPurchase, Number = "I-2611005", NumberKey = "I2611005", CreatedBy = "test" };
        var order = new SupplyCommitment { RequestId = Guid.NewGuid(), VendorId = _s.VendorId, Route = "Warehouse", Status = "Received", Origin = "Spanje", Partij = lot };
        var line = new SupplyCommitmentLine { SupplyCommitment = order, ProductVariantId = _s.TomatoId, ExpectedQuantity = 20m, ExpectedUnit = "colli", Partij = lot };
        db.AddRange(lot, order, line, new PalletContentLine
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLine = line, Partij = lot, LotNumber = lot.Number, Quantity = 20m, Unit = "colli", NetWeightKg = 120m, QualityStatus = "Approved",
            Pallet = new Pallet { Reference = "P-KLEUR", LogisticsStatus = "Received", LocationContext = "Warehouse" },
        });
        await db.SaveChangesAsync();
        _lot = lot.Id;
    }

    private async Task<ServiceResult<T>> RunAsync<T>(Func<LotColorService, Task<ServiceResult<T>>> call)
    {
        await using var db = _s.Factory.CreateContext();
        return await call(new LotColorService(db));
    }

    [Fact]
    public async Task A_Product_Has_Its_Own_Color_List_Trimmed_Without_Doubles()
    {
        await ArrangeAsync();
        var saved = await RunAsync(s => s.UpdateProductColorsAsync(_productId, new ProductColorsDto { Colors = new() { " Groen ", "geel", "GROEN", "", "Oranje" } }));
        Assert.Equal(new[] { "Groen", "geel", "Oranje" }, saved.Value.Colors);
        Assert.Equal(ServiceResultStatus.Validation, (await RunAsync(s => s.UpdateProductColorsAsync(_productId, new ProductColorsDto { Colors = Enumerable.Range(1, 21).Select(i => $"{i}").ToList() }))).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await RunAsync(s => s.GetProductColorsAsync(9999))).Status);
        Assert.Empty((await RunAsync(s => s.UpdateProductColorsAsync(_productId, new ProductColorsDto { Colors = new() }))).Value.Colors);
    }

    [Fact]
    public async Task The_Lot_Gets_A_Color_From_The_List_With_History_And_It_Shows_In_The_Lot_Choice_And_Availability()
    {
        await ArrangeAsync();
        var noList = await RunAsync(s => s.SetPartijColorAsync(_lot, new SetPartijColorDto { Color = "Groen" }));
        Assert.Equal(ServiceResultStatus.Validation, noList.Status);
        Assert.Contains("no colors", noList.Errors["color"].Single());

        await RunAsync(s => s.UpdateProductColorsAsync(_productId, new ProductColorsDto { Colors = new() { "1", "2", "3", "4", "5", "6" } }));
        Assert.Equal(ServiceResultStatus.Validation, (await RunAsync(s => s.SetPartijColorAsync(_lot, new SetPartijColorDto { Color = "7" }))).Status);
        Assert.Equal("4", (await RunAsync(s => s.SetPartijColorAsync(_lot, new SetPartijColorDto { Color = " 4 " }))).Value.Color);
        Assert.Equal("5", (await RunAsync(s => s.SetPartijColorAsync(_lot, new SetPartijColorDto { Color = "5" }))).Value.Color);
        Assert.Null((await RunAsync(s => s.SetPartijColorAsync(_lot, new SetPartijColorDto { Color = "" }))).Value.Color);
        await RunAsync(s => s.SetPartijColorAsync(_lot, new SetPartijColorDto { Color = "5" }));

        await using var db = _s.Factory.CreateContext();
        var history = await db.PartijAuditEvents.Where(e => e.PartijId == _lot).OrderBy(e => e.Id).Select(e => ValueTuple.Create(e.EventType, e.OldValue, e.NewValue)).ToListAsync();
        Assert.Equal(new[] { ("ColorChanged", (string)null, "4"), ("ColorChanged", "4", "5"), ("ColorChanged", "5", (string)null), ("ColorChanged", (string)null, "5") }, history);
        var detail = (await new PartijService(db).GetAsync(_lot)).Value;
        Assert.Equal(("5", 6), (detail.Color, detail.AllowedColors.Count));
        var option = (await new SalesOrderService(db).GetLotOptionsAsync(_s.TomatoId, "colli")).Value.Single(o => o.PartijId == _lot);
        Assert.Equal("5", option.Color);
        var drill = (await new AvailabilityService(db).GetVariantAsync(_s.TomatoId, new AvailabilityQuery())).Value;
        Assert.Equal("5", drill.ContentLines.Single(c => c.PartijId == _lot).PartijColor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Color_Of_A_Lot_Only_Changes_With_A_History_Line(bool async)
    {
        await ArrangeAsync();
        await using var db = _s.Factory.CreateContext();
        var lot = await db.Partijen.SingleAsync(p => p.Id == _lot);
        lot.Color = "Groen";
        if (async) await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        else Assert.Throws<TraceabilityViolationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        db.Partijen.Add(new Partij { Kind = PartijRules.KindPurchase, Number = "I-KLEUR", NumberKey = "IKLEUR", CreatedBy = "test", Color = "Geel" });
        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }
}
