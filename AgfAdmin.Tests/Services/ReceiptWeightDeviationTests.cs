using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static AgfAdmin.Services.ReceiptDeviationRules;

namespace AgfAdmin.Tests.Services;

// Gewichtscontrole bij ontvangst: besteld Avocado Hass Peru 18, 10 colli à 4 kg (40 kg netto). Meer dan 2% verschil in netto kg per colli
// is een afwijking (soort Weight), ook bij dezelfde variant; bevestigen werkt het verwachte gewicht van de regel bij zonder nieuwe regel.
// Daarnaast: de herkomst op de orderkop is optioneel en de herkomst van de order volgt uit de regels.
public sealed partial class ReceiptWeightDeviationTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private int _peru;
    private int _chile;
    private int _bare;
    private int _vendorId;
    private int _orderId;
    private int _lineId;
    private int _palletId;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync(decimal? expectedNetKg = 40m, decimal? expectedGrossKg = null, string headerOrigin = "Peru")
    {
        await using (var db = _factory.CreateContext())
        {
            var avocado = new Product { Name = "Avocado" };
            ProductVariant Hass(string origin, string code, decimal? kg = 4m) => new()
            {
                Product = avocado, Name = "Hass", Origin = origin, OriginCountryCode = code, SizeCategory = "18", Packaging = "4 kg doos", PackagingUnit = "colli",
                TradeClass = "I", DefaultPrice = 1m, WeightMethod = "Calculated", DefaultNetKgPerPackage = kg
            };
            var peru = Hass("Peru", "PE");
            var chile = Hass("Chili", "CL");
            var bare = new ProductVariant { Product = new Product { Name = "Mango" }, Name = "Palmer", Origin = "Brazilië", OriginCountryCode = "BR", PackagingUnit = "colli", DefaultPrice = 1m };
            var vendor = new Vendor { Name = "Nature's Pride" };
            db.AddRange(peru, chile, bare, vendor);
            await db.SaveChangesAsync();
            (_peru, _chile, _bare, _vendorId) = (peru.Id, chile.Id, bare.Id, vendor.Id);
        }
        var order = await CreateOrderAsync(headerOrigin, new CreateSupplyCommitmentLineDto
        {
            ProductVariantId = _peru, ExpectedQuantity = 10m, ExpectedUnit = "colli", ExpectedNetWeightKg = expectedNetKg, ExpectedGrossWeightKg = expectedGrossKg,
            WeightSource = expectedNetKg == null && expectedGrossKg == null ? null : "Entered", LotNumber = "TEST-001"
        });
        _orderId = order.Id;
        _lineId = order.Lines.Single().Id;
        await using var palletDb = _factory.CreateContext();
        _palletId = await new PalletService(palletDb).CreatePalletForTestAsync();
    }

    private async Task<SupplyCommitmentDto> CreateOrderAsync(string origin, params CreateSupplyCommitmentLineDto[] lines)
    {
        await using var db = _factory.CreateContext();
        var result = await new SupplyCommitmentService(db).CreateAsync(new CreateSupplyCommitmentDto
        {
            RequestId = Guid.NewGuid(), VendorId = _vendorId, Route = "Warehouse", Origin = origin, ExpectedAt = PurchaseOrderTestData.ExpectedAt, Lines = lines.ToList()
        });
        Assert.True(result.Status == ServiceResultStatus.Created, string.Join("; ", result.Errors.SelectMany(e => e.Value)));
        return result.Value;
    }

    // Etiket (berekend): kg per colli × aantal; gewogen: het totaal.
    private static AddPalletContentLineDto Receive(int lineId, decimal quantity, decimal kgPerColli, bool weighed = false, int? variant = null, bool? confirm = null, Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(), SupplyCommitmentLineId = lineId, LotNumber = "TEST-001", Quantity = quantity, Unit = "colli",
        WeightMethod = weighed ? "Weighed" : "Calculated", NetKgPerPackage = weighed ? null : kgPerColli, NetWeightKg = weighed ? quantity * kgPerColli : null,
        ActorReference = "Kees", ReceivedProductVariantId = variant, ConfirmDeviation = confirm
    };

    private async Task<ServiceResult<PalletContentLineDto>> AddAsync(AddPalletContentLineDto request)
    {
        await using var db = _factory.CreateContext();
        return await new PalletService(db).AddContentLineAsync(_palletId, request);
    }

    private async Task<(List<SupplyCommitmentLine> Lines, List<SupplyReceiptDeviation> Deviations, int Contents)> StateAsync()
    {
        await using var db = _factory.CreateContext();
        return (await db.SupplyCommitmentLines.AsNoTracking().Where(l => l.SupplyCommitmentId == _orderId).OrderBy(l => l.Id).ToListAsync(),
            await db.SupplyReceiptDeviations.AsNoTracking().ToListAsync(), await db.PalletContentLines.CountAsync());
    }

    // ---------------- Regels ----------------

    [Theory]
    [InlineData(40.796, false)] // +1,99%
    [InlineData(40.800, false)] // precies +2%: nog binnen
    [InlineData(40.804, true)]  // +2,01%
    [InlineData(39.204, false)] // -1,99%
    [InlineData(39.196, true)]  // -2,01%
    [InlineData(60.000, true)]  // 6 kg per colli
    public void The_tolerance_is_two_percent_per_package(decimal receivedNetKg, bool deviation)
    {
        Assert.Equal(0.02m, WeightTolerance);
        Assert.Equal(deviation, IsWeightDeviation(4m, 10m, receivedNetKg));
    }

    [Fact]
    public void Expected_kg_per_package_comes_from_the_line_then_the_article_and_otherwise_there_is_no_check()
    {
        Assert.Equal(4m, ExpectedKgPerPackage(10m, 40m, 6m));
        Assert.Equal(6m, ExpectedKgPerPackage(10m, null, 6m));
        Assert.Null(ExpectedKgPerPackage(10m, null, null));
        Assert.False(IsWeightDeviation(null, 10m, 60m));
    }

    [Fact]
    public void Reweighing_never_invents_gross_kg()
    {
        var line = new LineShape(10m, 40m, 42m, LineWeightRules.Calculated, null, null, null);
        Assert.Equal(new LineShape(10m, 60m, null, LineWeightRules.Entered, null, null, null), Reweigh(line, 6m));
        Assert.Equal(70m, Reweigh(line with { GrossKg = 70m }, 6m).GrossKg);
        Assert.Null(Reweigh(line with { GrossKg = null }, 6m).GrossKg);
    }

    // ---------------- Boeken ----------------

    [Theory]
    [InlineData(4.05, false)]
    [InlineData(3.95, true)]
    public async Task Within_tolerance_books_without_a_question(decimal kgPerColli, bool weighed)
    {
        await ArrangeAsync();

        var result = await AddAsync(Receive(_lineId, 10m, kgPerColli, weighed));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Empty((await StateAsync()).Deviations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Another_weight_asks_first_and_No_changes_nothing(bool weighed)
    {
        await ArrangeAsync();

        var result = await AddAsync(Receive(_lineId, 10m, 6m, weighed));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        var body = Assert.IsType<ReceiptDeviationConflictDto>(result.ConflictBody);
        Assert.Equal(CodeConfirmationRequired, body.Code);
        Assert.Equal(new[] { KindWeight }, body.Deviation.Kinds);
        Assert.Equal((4m, 6m, 40m, 60m, 40m, 60m), (body.Deviation.ExpectedKgPerPackage, body.Deviation.ReceivedKgPerPackage, body.Deviation.ExpectedNetKg,
            body.Deviation.ReceivedNetKg, body.Deviation.OriginalExpectedNetKg, body.Deviation.OriginalExpectedNetKgAfter));
        Assert.Equal((0m, 10m), (body.Deviation.ReducedQuantity, body.Deviation.OriginalExpectedQuantityAfter));
        var (lines, deviations, contents) = await StateAsync();
        Assert.Equal((1, 40m, 0, 0), (lines.Count, lines[0].ExpectedNetWeightKg, deviations.Count, contents));
    }

    [Fact]
    public async Task Yes_updates_the_expected_weight_of_the_same_line_without_a_new_line()
    {
        await ArrangeAsync(expectedGrossKg: 42m);
        var request = Receive(_lineId, 10m, 6m, confirm: true);

        var result = await AddAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(_lineId, result.Value.SupplyCommitmentLineId);
        var (lines, deviations, contents) = await StateAsync();
        var line = Assert.Single(lines);
        // Aantal blijft 10; 10 × 6 kg = 60 kg (opgegeven); bruto 42 past niet meer onder 60 netto en wordt leeg, nooit verzonnen.
        Assert.Equal((10m, 60m, (decimal?)null, "Entered"), (line.ExpectedQuantity, line.ExpectedNetWeightKg, line.ExpectedGrossWeightKg, line.WeightSource));
        var deviation = Assert.Single(deviations);
        Assert.Equal(((int?)null, "Weight", 0m, 4m, 6m, _peru, _peru), (deviation.NewLineId, deviation.Kinds, deviation.ReducedQuantity, deviation.ExpectedKgPerPackage,
            deviation.ReceivedKgPerPackage, deviation.ExpectedProductVariantId, deviation.ReceivedProductVariantId));
        Assert.Contains("\"netKg\":40", deviation.OriginalLineBefore);
        Assert.Contains("\"netKg\":60", deviation.OriginalLineAfter);
        Assert.Equal(1, contents);

        // Herhaling van dezelfde aanvraag (ook met de variant expliciet erbij) maakt nooit een tweede afwijking.
        Assert.Equal(ServiceResultStatus.Ok, (await AddAsync(request)).Status);
        var withVariant = Receive(_lineId, 10m, 6m, variant: _peru, confirm: true, requestId: request.RequestId);
        Assert.Equal(ServiceResultStatus.Ok, (await AddAsync(withVariant)).Status);
        Assert.Single((await StateAsync()).Deviations);
    }

    [Fact]
    public async Task Another_variant_and_another_weight_together_use_the_replacement_line_with_the_received_weight()
    {
        await ArrangeAsync();

        var asked = await AddAsync(Receive(_lineId, 10m, 6m, variant: _chile));
        Assert.Equal(new[] { KindOrigin, KindWeight }, Assert.IsType<ReceiptDeviationConflictDto>(asked.ConflictBody).Deviation.Kinds);

        var result = await AddAsync(Receive(_lineId, 10m, 6m, variant: _chile, confirm: true));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var (lines, deviations, _) = await StateAsync();
        Assert.Equal((0m, (decimal?)null), (lines[0].ExpectedQuantity, lines[0].ExpectedNetWeightKg));
        Assert.Equal((_chile, 10m, 60m), (lines[1].ProductVariantId, lines[1].ExpectedQuantity, lines[1].ExpectedNetWeightKg));
        Assert.Equal(("Origin,Weight", lines[1].Id), (deviations.Single().Kinds, deviations.Single().NewLineId));
    }

    [Fact]
    public async Task A_partial_delivery_at_the_ordered_weight_is_no_weight_deviation()
    {
        await ArrangeAsync();

        Assert.Equal(ServiceResultStatus.Created, (await AddAsync(Receive(_lineId, 4m, 4m))).Status);
        Assert.Equal(ServiceResultStatus.Created, (await AddAsync(Receive(_lineId, 8m, 4m))).Status); // samen meer dan besteld: bestaande regels
        Assert.Empty((await StateAsync()).Deviations);
    }

    [Fact]
    public async Task Without_any_expected_weight_there_is_no_weight_check()
    {
        await ArrangeAsync(expectedNetKg: null);
        var mango = await CreateOrderAsync("", new CreateSupplyCommitmentLineDto { ProductVariantId = _bare, ExpectedQuantity = 10m, ExpectedUnit = "colli" });

        var result = await AddAsync(Receive(mango.Lines.Single().Id, 10m, 9m, weighed: true));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        // Zonder kg op de regel geldt de artikelstandaard (4 kg): 9 kg per colli bij de Hass-regel is dus wel een afwijking.
        Assert.Equal(ServiceResultStatus.Conflict, (await AddAsync(Receive(_lineId, 10m, 9m, weighed: true))).Status);
    }

    [Fact]
    public async Task The_preview_endpoint_reports_a_weight_deviation_for_the_same_variant()
    {
        await ArrangeAsync();
        await using var db = _factory.CreateContext();
        var service = new ReceiptDeviationService(db);

        Assert.Equal("Exact", (await service.PreviewAsync(_lineId, _peru, 10m, 40m)).Value.Status);
        var weight = (await service.PreviewAsync(_lineId, _peru, 10m, 60m)).Value;
        Assert.Equal(("Deviation", KindWeight), (weight.Status, Assert.Single(weight.Kinds)));
        Assert.Equal(ServiceResultStatus.Validation, (await service.PreviewAsync(_lineId, _peru, 10m, -1m)).Status);
    }

    [Fact]
    public async Task Guard_refuses_a_deviation_without_replacement_line_for_another_variant()
    {
        await ArrangeAsync();
        await using var db = _factory.CreateContext();
        var pallet = await db.Pallets.SingleAsync();
        var content = new PalletContentLine
        {
            RequestId = Guid.NewGuid(), PalletId = pallet.Id, SupplyCommitmentLineId = _lineId, LotNumber = "X", Quantity = 10m, Unit = "colli", NetWeightKg = 60m, QualityStatus = "Quarantine"
        };
        db.Add(content);
        db.Add(new SupplyReceiptDeviation
        {
            RequestId = content.RequestId, SupplyCommitmentId = _orderId, OriginalLineId = _lineId, PalletContentLine = content, Kinds = "Origin",
            ExpectedProductVariantId = _peru, ExpectedVariantSnapshot = "{}", ReceivedProductVariantId = _chile, ReceivedVariantSnapshot = "{}",
            Quantity = 10m, Unit = "colli", OriginalLineBefore = "{}", OriginalLineAfter = "{}"
        });

        await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
    }
}
