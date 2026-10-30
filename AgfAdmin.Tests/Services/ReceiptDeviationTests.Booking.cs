using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Boeken via PalletService: vragen, weigeren, Ja, replay, gelijktijdigheid, hoeveelheden en klantgebonden aanvoer.
public sealed partial class ReceiptDeviationTests
{
    private static AddPalletContentLineDto Receive(int lineId, decimal quantity, int? variant = null, bool? confirm = null, Guid? requestId = null,
        string reason = null, string note = null, string unit = "colli") => new()
    {
        RequestId = requestId ?? Guid.NewGuid(), SupplyCommitmentLineId = lineId, LotNumber = "LOT-ZA-1", Quantity = quantity, Unit = unit,
        WeightMethod = "Calculated", NetKgPerPackage = 4m, ActorReference = "Kees", ReceivedProductVariantId = variant, ConfirmDeviation = confirm,
        DeviationReason = reason, DeviationReasonNote = note
    };

    private async Task<ServiceResult<PalletContentLineDto>> AddAsync(AddPalletContentLineDto request, int? palletId = null, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        await using var db = interceptors.Length == 0 ? _factory.CreateContext() : _factory.CreateContext(interceptors);
        return await new PalletService(db).AddContentLineAsync(palletId ?? _palletId, request);
    }

    private async Task ReceivePlainAsync(decimal quantity)
    {
        var result = await AddAsync(Receive(_lineId, quantity));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
    }

    private static ReceiptDeviationConflictDto Body(ServiceResult<PalletContentLineDto> result) => Assert.IsType<ReceiptDeviationConflictDto>(result.ConflictBody);

    // ---------------- Exacte match ----------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Exact_Variant_Books_Without_A_Question(bool sendVariant)
    {
        await ArrangeAsync();
        var result = await AddAsync(Receive(_lineId, 10m, sendVariant ? _v.Peru : null));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var (lines, _, deviations) = await StateAsync();
        Assert.Equal(_lineId, result.Value.SupplyCommitmentLineId);
        Assert.Equal(100m, Assert.Single(lines).ExpectedQuantity);
        Assert.Empty(deviations);
    }

    // ---------------- Verwisselscenario's: bevestiging vereist, Nee verandert niets ----------------

    public static TheoryData<string, string[]> DeviationCases => new()
    {
        { "SouthAfrica", new[] { "Origin" } },
        { "Size16", new[] { "Size" } },
        { "Crate", new[] { "Packaging" } },
        { "SouthAfrica16", new[] { "Origin", "Size" } },
        { "Twin", new[] { "Other" } }
    };

    [Theory]
    [MemberData(nameof(DeviationCases))]
    public async Task Any_Difference_Requires_Confirmation_And_No_Changes_Nothing(string variant, string[] kinds)
    {
        await ArrangeAsync();
        var before = await StateAsync();

        var result = await AddAsync(Receive(_lineId, 10m, VariantByName(variant), confirm: false));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        var body = Body(result);
        Assert.Equal(ReceiptDeviationRules.CodeConfirmationRequired, body.Code);
        Assert.Equal(kinds, body.Deviation.Kinds);
        Assert.Equal("Deviation", body.Deviation.Status);
        Assert.Equal(_v.Peru, body.Deviation.Expected.ProductVariantId);
        Assert.Equal((100m, 10m, 90m, 0m), (body.Deviation.OriginalExpectedQuantity, body.Deviation.ReducedQuantity, body.Deviation.OriginalExpectedQuantityAfter, body.Deviation.ExcessQuantity));
        var after = await StateAsync();
        Assert.Equal(before.Contents, after.Contents);
        Assert.Equal(before.Lines.Select(l => (l.Id, l.ExpectedQuantity, l.Version)), after.Lines.Select(l => (l.Id, l.ExpectedQuantity, l.Version)));
        Assert.Empty(after.Deviations);
    }

    [Theory]
    [InlineData("Fuerte")]
    [InlineData("Mango")]
    [InlineData("AlmostSameName")]
    public async Task Another_Article_Is_Never_A_Deviation_Even_When_Confirmed(string variant)
    {
        await ArrangeAsync();

        var result = await AddAsync(Receive(_lineId, 10m, VariantByName(variant), confirm: true, reason: "SupplierDeliveredDifferently"));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal(ReceiptDeviationRules.CodeOtherArticle, Body(result).Code);
        var (lines, contents, deviations) = await StateAsync();
        Assert.Single(lines);
        Assert.Equal(0, contents);
        Assert.Empty(deviations);
    }

    // ---------------- Ja, klopt ----------------

    [Fact]
    public async Task Confirmed_Deviation_Adjusts_The_Order_In_One_Save()
    {
        await ArrangeAsync();

        var result = await AddAsync(Receive(_lineId, 10m, _v.SouthAfrica, confirm: true, reason: "SupplierDeliveredDifferently"));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var (lines, contents, deviations) = await StateAsync();
        Assert.Equal(2, lines.Count);
        var original = lines[0];
        var replacement = lines[1];
        // Peru van 100 naar 90 colli; kg evenredig (zelfde kg per colli), pallets opnieuw verdeeld: 1 × 48 + 42.
        Assert.Equal((90m, 360m, 378m, "Entered"), (original.ExpectedQuantity, original.ExpectedNetWeightKg, original.ExpectedGrossWeightKg, original.WeightSource));
        Assert.Equal((1m, 48m, 42m), (original.PalletCount, original.PackagesPerPallet, original.LoosePackages));
        Assert.Equal(1, original.Version);
        // Zuid-Afrika 10 colli erbij, met het werkelijke kg en de partij, verwijzend naar de oorspronkelijke regel.
        Assert.Equal((_v.SouthAfrica, 10m, "colli", 40m, "Entered", _lineId, "LOT-ZA-1"),
            (replacement.ProductVariantId, replacement.ExpectedQuantity, replacement.ExpectedUnit, replacement.ExpectedNetWeightKg, replacement.WeightSource, replacement.ReplacesLineId, replacement.LotNumber));
        Assert.Equal(1, contents);
        Assert.Equal(replacement.Id, result.Value.SupplyCommitmentLineId);
        Assert.Equal(_v.SouthAfrica, result.Value.ProductVariantId);

        var deviation = Assert.Single(deviations);
        Assert.Equal((_orderId, _lineId, replacement.Id, result.Value.Id, "Origin", 10m, 10m, "colli", "SupplierDeliveredDifferently", "Kees"),
            (deviation.SupplyCommitmentId, deviation.OriginalLineId, deviation.NewLineId, deviation.PalletContentLineId, deviation.Kinds, deviation.Quantity, deviation.ReducedQuantity, deviation.Unit, deviation.Reason, deviation.ActorReference));
        Assert.Contains("\"origin\":\"Peru\"", deviation.ExpectedVariantSnapshot);
        Assert.Contains("\"origin\":\"Zuid-Afrika\"", deviation.ReceivedVariantSnapshot);
        Assert.Contains("\"expectedQuantity\":100", deviation.OriginalLineBefore);
        Assert.Contains("\"expectedQuantity\":90", deviation.OriginalLineAfter);

        await using var db = _factory.CreateContext();
        var audit = await db.PalletAuditEvents.AsNoTracking().SingleAsync(e => e.PalletContentLineId == result.Value.Id);
        Assert.Contains($"\"replacesSupplyCommitmentLineId\":{_lineId}", audit.NewValue);
    }

    [Fact]
    public async Task Other_Reason_Needs_An_Explanation()
    {
        await ArrangeAsync();

        var result = await AddAsync(Receive(_lineId, 10m, _v.SouthAfrica, confirm: true, reason: "Other"));

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains("deviationReasonNote", result.Errors.Keys);
        Assert.Empty((await StateAsync()).Deviations);
    }

    [Fact]
    public async Task Unit_Is_Never_Converted_For_A_Deviation()
    {
        await ArrangeAsync();

        var result = await AddAsync(Receive(_lineId, 10m, _v.SouthAfrica, confirm: true, unit: "box"));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("never converted", result.Message);
        Assert.Empty((await StateAsync()).Deviations);
    }

    // ---------------- Replay en gelijktijdigheid ----------------

    [Fact]
    public async Task Replay_With_The_Same_RequestId_Never_Creates_A_Second_Deviation()
    {
        await ArrangeAsync();
        var request = Receive(_lineId, 10m, _v.SouthAfrica, confirm: true);

        var first = await AddAsync(request);
        var second = await AddAsync(request);

        Assert.Equal(ServiceResultStatus.Created, first.Status);
        Assert.Equal(ServiceResultStatus.Ok, second.Status);
        Assert.Equal(first.Value.Id, second.Value.Id);
        var (lines, contents, deviations) = await StateAsync();
        Assert.Equal((2, 1, 1), (lines.Count, contents, deviations.Count));
        Assert.Equal(90m, lines[0].ExpectedQuantity);

        // Dezelfde sleutel met een andere variant is een andere aanvraag.
        var other = Receive(_lineId, 10m, _v.Size16, confirm: true, requestId: request.RequestId);
        Assert.Equal(ServiceResultStatus.Conflict, (await AddAsync(other)).Status);
    }

    [Fact]
    public async Task Two_Concurrent_Deviations_On_The_Same_Line_Never_Both_Book()
    {
        await ArrangeAsync();
        var concurrent = new ConcurrentSaveInterceptor(async () =>
        {
            var winner = await AddAsync(Receive(_lineId, 20m, _v.Size16, confirm: true), await NewPalletAsync());
            Assert.Equal(ServiceResultStatus.Created, winner.Status);
        });

        var loser = await AddAsync(Receive(_lineId, 10m, _v.SouthAfrica, confirm: true), null, concurrent);

        Assert.Equal(ServiceResultStatus.Conflict, loser.Status);
        var (lines, contents, deviations) = await StateAsync();
        Assert.Equal(80m, lines[0].ExpectedQuantity);
        Assert.Equal((2, 1), (lines.Count, contents));
        Assert.Equal(_v.Size16, Assert.Single(deviations).ReceivedProductVariantId);
    }

    // ---------------- Hoeveelheden ----------------

    [Fact]
    public async Task Earlier_Partial_Receipt_Keeps_The_Original_Line_Above_What_Was_Received()
    {
        await ArrangeAsync();
        await ReceivePlainAsync(40m);

        var result = await AddAsync(Receive(_lineId, 10m, _v.SouthAfrica, confirm: true));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var (lines, _, _) = await StateAsync();
        Assert.Equal((90m, 10m), (lines[0].ExpectedQuantity, lines[1].ExpectedQuantity));
    }

    [Theory]
    [InlineData(95, 10, 5, 100 - 5, 5)]  // nog 5 verwacht, 10 ontvangen: 5 meer geleverd
    [InlineData(100, 10, 0, 100, 10)]    // alles al binnen: de oorspronkelijke regel blijft staan
    public async Task Deviation_Larger_Than_What_Is_Still_Expected_Never_Goes_Below_Received(int receivedBefore, int quantity, int reduced, int originalAfter, int excess)
    {
        await ArrangeAsync();
        await ReceivePlainAsync(receivedBefore);

        var preview = await AddAsync(Receive(_lineId, quantity, _v.SouthAfrica));
        Assert.Equal((receivedBefore, reduced, excess), ((int)Body(preview).Deviation.OriginalReceivedQuantity, (int)Body(preview).Deviation.ReducedQuantity, (int)Body(preview).Deviation.ExcessQuantity));

        var result = await AddAsync(Receive(_lineId, quantity, _v.SouthAfrica, confirm: true));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var (lines, _, deviations) = await StateAsync();
        Assert.Equal((originalAfter, quantity), ((int)lines[0].ExpectedQuantity, (int)lines[1].ExpectedQuantity));
        Assert.Equal(reduced, (int)Assert.Single(deviations).ReducedQuantity);
        Assert.True(lines[0].ExpectedQuantity >= receivedBefore);
    }

    [Fact]
    public async Task Whole_Line_Received_As_Another_Variant_Leaves_An_Empty_Original_Without_Kg_Or_Pallets()
    {
        await ArrangeAsync();

        var result = await AddAsync(Receive(_lineId, 100m, _v.SouthAfrica, confirm: true));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var original = (await StateAsync()).Lines[0];
        Assert.Equal((0m, (decimal?)null, (decimal?)null, (string)null, (decimal?)null), (original.ExpectedQuantity, original.ExpectedNetWeightKg, original.ExpectedGrossWeightKg, original.WeightSource, original.PalletCount));
    }

    // ---------------- Klantgebonden aanvoer (DirectTrade) ----------------

    [Theory]
    [InlineData(45, true)]   // 50 verwacht, 45 toegewezen: 10 eraf zou de toewijzing raken
    [InlineData(40, false)]  // 40 toegewezen: het niet-toegewezen deel (10) mag afwijken
    public async Task Assigned_DirectTrade_Supply_Is_Never_Touched(int assigned, bool blocked)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, colliLine) = await SalesTestData.SeedOrderAsync(_factory, seed, 5m, 50m);
        await DirectTradeTestFlow.AllocateAsync(_factory, orderId, colliLine, seed.DirectTradeColliSourceLine, assigned, "colli");
        int southAfrica, palletId;
        await using (var db = _factory.CreateContext())
        {
            var hass = await db.ProductVariants.AsNoTracking().SingleAsync(v => v.Id == seed.AvocadoId);
            var variant = new ProductVariant { ProductId = hass.ProductId, Name = hass.Name, Origin = "Zuid-Afrika", Packaging = hass.Packaging, DefaultPrice = 1m };
            var pallet = new Pallet { Reference = "P-DT-DEV", LogisticsStatus = "Received", LocationContext = "Crossdock" };
            db.AddRange(variant, pallet);
            await db.SaveChangesAsync();
            (southAfrica, palletId) = (variant.Id, pallet.Id);
        }

        var result = await AddAsync(Receive(seed.DirectTradeColliSourceLine, 10m, southAfrica, confirm: true), palletId);

        if (blocked)
        {
            Assert.Equal(ServiceResultStatus.Conflict, result.Status);
            Assert.Equal(ReceiptDeviationRules.CodeBlockedAssigned, Body(result).Code);
            Assert.Equal(assigned, Body(result).Deviation.AssignedQuantity);
        }
        else
        {
            Assert.Equal(ServiceResultStatus.Created, result.Status);
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal(blocked ? 50m : 40m, (await verify.SupplyCommitmentLines.AsNoTracking().SingleAsync(l => l.Id == seed.DirectTradeColliSourceLine)).ExpectedQuantity);
        Assert.Equal(blocked ? 0 : 1, await verify.SupplyReceiptDeviations.CountAsync());
    }
}
