using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Eenduidige reserveringstotalen:
//   reserved       = Active Physical verkoopreserveringen op inhoud die nu Approved is en Received + Warehouse.
//   reservedOnHold = Active Physical verkoopreserveringen op inhoud die niet meer Approved is.
//   availableAfterReservations = availableBeforeReservations - reserved (onHold wordt nooit nogmaals afgetrokken).
public class ReservationAvailabilityTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // ======================== Pure berekening ========================

    private static SourceLineFact Source(int id, string unit = "box", string route = "Warehouse", int? customerId = null) => new(
        id, id * 10, route, customerId, customerId == null ? null : "Customer", "Expected",
        new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc), 1, "Avocado", "Hass", null, unit, 100m, null);

    private int _nextId = 100;

    private ContentLineFact Content(SourceLineFact source, decimal quantity, decimal kg, string quality = "Approved", decimal reservedQuantity = 0m, decimal reservedKg = 0m,
        string logistics = "Received", string location = "Warehouse") => new(
            _nextId++, 7, "PAL-7", logistics, location, null, source.SupplyCommitmentLineId, source.Route, source.CustomerId, source.CustomerName,
            1, "Avocado", "Hass", "LOT-1", source.Unit, quantity, kg, quality, reservedQuantity, reservedKg);

    private static WarehouseSupplyDto Box(List<VariantAvailabilityDto> overview, string unit = "box") =>
        Assert.Single(Assert.Single(overview).WarehouseSupply, e => e.Unit == unit);

    private static List<VariantAvailabilityDto> Overview(IEnumerable<SourceLineFact> sources, IEnumerable<ContentLineFact> contents) =>
        AvailabilityCalculator.BuildOverview(sources.ToList(), contents.ToList(), new AvailabilityQuery());

    private static void AssertQuantity(AvailabilityQuantityDto actual, decimal quantity, decimal kg)
    {
        Assert.Equal(quantity, actual.Quantity);
        Assert.Equal(kg, actual.NetWeightKg);
    }

    [Fact]
    public void Reserved_Is_Subtracted_From_Available_Before_Reservations()
    {
        var source = Source(1);
        var entry = Box(Overview(new[] { source }, new[] { Content(source, 40m, 160m, reservedQuantity: 10m, reservedKg: 40.5m) }));

        AssertQuantity(entry.AvailableBeforeReservations, 40m, 160m);
        Assert.Equal((10m, 40.5m, true), (entry.Reserved.Quantity, entry.Reserved.NetWeightKg, entry.Reserved.Applied));
        AssertQuantity(entry.ReservedOnHold, 0m, 0m);
        AssertQuantity(entry.AvailableAfterReservations, 30m, 119.5m);
    }

    [Theory]
    [InlineData("Blocked")]
    [InlineData("Rejected")]
    [InlineData("Quarantine")]
    public void Reservation_On_Content_That_Is_No_Longer_Approved_Is_On_Hold_And_Never_Subtracted_Again(string quality)
    {
        var source = Source(1);
        var line = Content(source, 40m, 160m, quality, reservedQuantity: 10m, reservedKg: 40m);

        var entry = Box(Overview(new[] { source }, new[] { line }));

        AssertQuantity(entry.AvailableBeforeReservations, 0m, 0m);
        Assert.Equal((0m, 0m), (entry.Reserved.Quantity, entry.Reserved.NetWeightKg));
        AssertQuantity(entry.ReservedOnHold, 10m, 40m);
        AssertQuantity(entry.AvailableAfterReservations, 0m, 0m);
        var warning = Assert.Single(entry.Warnings);
        Assert.Equal(("RESERVATION_ON_NON_APPROVED_CONTENT", line.PalletContentLineId, 10m), (warning.Code, warning.PalletContentLineId, warning.Quantity));
    }

    [Fact]
    public void Mixed_Approved_And_Blocked_Lines_Keep_Reserved_And_OnHold_Separate()
    {
        var source = Source(1);
        var entry = Box(Overview(new[] { source }, new[]
        {
            Content(source, 40m, 160m, reservedQuantity: 10m, reservedKg: 40m),
            Content(source, 10m, 40m, "Blocked", reservedQuantity: 10m, reservedKg: 40m)
        }));

        AssertQuantity(entry.AvailableBeforeReservations, 40m, 160m);
        Assert.Equal((10m, 40m), (entry.Reserved.Quantity, entry.Reserved.NetWeightKg));
        AssertQuantity(entry.ReservedOnHold, 10m, 40m);
        AssertQuantity(entry.AvailableAfterReservations, 30m, 120m);
        AssertQuantity(entry.Warehouse.Blocked, 10m, 40m);
    }

    [Fact]
    public void Units_Are_Never_Mixed_In_Reservation_Totals()
    {
        var box = Source(1, "box");
        var colli = Source(2, "colli");
        var overview = Overview(new[] { box, colli }, new[]
        {
            Content(box, 40m, 160m, reservedQuantity: 10m, reservedKg: 40m),
            Content(colli, 10m, 55m, reservedQuantity: 2m, reservedKg: 11m)
        });

        Assert.Equal((10m, 40m), (Box(overview, "box").Reserved.Quantity, Box(overview, "box").Reserved.NetWeightKg));
        Assert.Equal((2m, 11m), (Box(overview, "colli").Reserved.Quantity, Box(overview, "colli").Reserved.NetWeightKg));
        AssertQuantity(Box(overview, "colli").AvailableAfterReservations, 8m, 44m);
    }

    [Fact]
    public void Corrupt_Over_Reservation_Never_Makes_Availability_Negative_And_Is_Flagged()
    {
        var source = Source(1);
        var line = Content(source, 40m, 160m, reservedQuantity: 45m, reservedKg: 170m);

        var entry = Box(Overview(new[] { source }, new[] { line }));

        AssertQuantity(entry.AvailableAfterReservations, 0m, 0m);
        Assert.Contains(entry.Warnings, w => w.Code == "RESERVATION_EXCEEDS_CONTENT" && w.PalletContentLineId == line.PalletContentLineId);
    }

    [Fact]
    public void Without_Reservations_Reserved_Is_Zero_And_Applied()
    {
        var source = Source(1);
        var entry = Box(Overview(new[] { source }, new[] { Content(source, 40m, 160m) }));

        Assert.Equal((0m, 0m, true), (entry.Reserved.Quantity, entry.Reserved.NetWeightKg, entry.Reserved.Applied));
        AssertQuantity(entry.AvailableAfterReservations, 40m, 160m);
    }

    [Fact]
    public void DrillDown_Shows_Reserved_OnHold_And_Available_Per_Content_Line()
    {
        var source = Source(1);
        var partial = Content(source, 40m, 160m, reservedQuantity: 10m, reservedKg: 40m);
        var full = Content(source, 20m, 80m, reservedQuantity: 20m, reservedKg: 80m);
        var blocked = Content(source, 10m, 40m, "Blocked", reservedQuantity: 4m, reservedKg: 16m);

        var (_, lines) = AvailabilityCalculator.BuildDrillDown(new[] { source }, new[] { partial, full, blocked }, new AvailabilityQuery { ProductVariantId = 1 });

        var p = lines.Single(l => l.PalletContentLineId == partial.PalletContentLineId);
        Assert.Equal((10m, 40m, false, 30m, 120m, true), (p.ReservedQuantity, p.ReservedNetWeightKg, p.OnHold, p.AvailableQuantity, p.AvailableNetWeightKg, p.SellableNow));
        var f = lines.Single(l => l.PalletContentLineId == full.PalletContentLineId);
        Assert.Equal((0m, 0m, false), (f.AvailableQuantity, f.AvailableNetWeightKg, f.SellableNow));
        var b = lines.Single(l => l.PalletContentLineId == blocked.PalletContentLineId);
        Assert.Equal((4m, true, 0m, false), (b.ReservedQuantity, b.OnHold, b.AvailableQuantity, b.SellableNow));
    }

    [Fact]
    public void DirectTrade_Supply_Is_Unchanged_By_Reservations()
    {
        var direct = Source(1, route: "DirectTrade", customerId: 7);
        var variant = Assert.Single(Overview(new[] { direct }, new[] { Content(direct, 9m, 36m, location: "Crossdock") }));

        Assert.Empty(variant.WarehouseSupply);
        Assert.Null(typeof(DirectTradeSupplyDto).GetProperty("AvailableAfterReservations"));
        Assert.Null(typeof(DirectTradeSupplyDto).GetProperty("ReservedOnHold"));
        Assert.Equal(9m, Assert.Single(variant.DirectTradeSupply).ReceivedOutsideWarehouse.Approved.Quantity);
    }

    // ======================== Via de echte commando's (SQLite) ========================

    private async Task<WarehouseSupplyDto> AvocadoBoxAsync(SalesSeed seed)
    {
        await using var db = _factory.CreateContext();
        var overview = (await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = seed.AvocadoId, Route = "Warehouse" })).Value;
        Assert.True(overview.Basis.ReservationsApplied);
        return Assert.Single(overview.Variants.Single().WarehouseSupply, e => e.Unit == "box");
    }

    private async Task<ServiceResultStatus> ChangeQualityAsync(int contentLineId, string to, string reason)
    {
        await using var read = _factory.CreateContext();
        var line = await read.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == contentLineId);
        await using var db = _factory.CreateContext();
        return (await new PalletService(db).ChangeQualityStatusAsync(line.PalletId, line.Id, new ChangeQualityStatusDto
        {
            RequestId = Guid.NewGuid(), ExpectedContentLineVersion = line.Version, ToStatus = to, ActorReference = "qc-1", Reason = reason
        })).Status;
    }

    [Fact]
    public async Task Reserve_Then_Block_Then_Reject_Then_Release_Keeps_Totals_Unambiguous()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, boxLine, _) = await SalesTestData.SeedOrderAsync(_factory, seed, boxQuantity: 100m);

        // Vooraf: Approved box in het magazijn = LOT-A 40/160 + LOT-C 20/80.
        var start = await AvocadoBoxAsync(seed);
        AssertQuantity(start.AvailableBeforeReservations, 60m, 240m);
        AssertQuantity(start.AvailableAfterReservations, 60m, 240m);

        StockReservationDto reservation;
        await using (var db = _factory.CreateContext())
        {
            reservation = (await new StockReservationService(db).ReserveAsync(orderId, boxLine, new CreateStockReservationDto
            {
                RequestId = Guid.NewGuid(), PalletContentLineId = seed.ApprovedBox, Quantity = 10m, Unit = "box", NetWeightKg = 40.125m, ActorReference = "office-1"
            })).Value;
        }
        var reserved = await AvocadoBoxAsync(seed);
        Assert.Equal((10m, 40.125m), (reserved.Reserved.Quantity, reserved.Reserved.NetWeightKg));
        AssertQuantity(reserved.ReservedOnHold, 0m, 0m);
        AssertQuantity(reserved.AvailableAfterReservations, 50m, 199.875m);

        Assert.Equal(ServiceResultStatus.Ok, await ChangeQualityAsync(seed.ApprovedBox, "Blocked", "Klantclaim"));
        var blocked = await AvocadoBoxAsync(seed);
        AssertQuantity(blocked.AvailableBeforeReservations, 20m, 80m);
        Assert.Equal((0m, 0m), (blocked.Reserved.Quantity, blocked.Reserved.NetWeightKg));
        AssertQuantity(blocked.ReservedOnHold, 10m, 40.125m);
        AssertQuantity(blocked.AvailableAfterReservations, 20m, 80m);
        Assert.Contains(blocked.Warnings, w => w.Code == "RESERVATION_ON_NON_APPROVED_CONTENT");

        Assert.Equal(ServiceResultStatus.Ok, await ChangeQualityAsync(seed.ApprovedBox, "Rejected", "Schimmel"));
        var rejected = await AvocadoBoxAsync(seed);
        AssertQuantity(rejected.ReservedOnHold, 10m, 40.125m);
        AssertQuantity(rejected.AvailableAfterReservations, 20m, 80m);

        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new StockReservationService(db).ReleaseAsync(reservation.Id, new ReleaseStockReservationDto
            {
                RequestId = Guid.NewGuid(), ExpectedReservationVersion = reservation.Version, ActorReference = "office-1", Reason = "Partij afgekeurd"
            })).Status);
        }
        var released = await AvocadoBoxAsync(seed);
        AssertQuantity(released.ReservedOnHold, 0m, 0m);
        Assert.Equal((0m, 0m), (released.Reserved.Quantity, released.Reserved.NetWeightKg));
        AssertQuantity(released.AvailableAfterReservations, 20m, 80m);
        Assert.DoesNotContain(released.Warnings, w => w.Code == "RESERVATION_ON_NON_APPROVED_CONTENT");
    }

    [Fact]
    public async Task Legacy_OrderLine_Reservations_Never_Count_In_The_New_Totals()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(new AgfAdmin.Models.StockReservation
            {
                OrderLineId = seed.LegacyOrderLineId,
                SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, seed.ApprovedBox),
                PalletContentLineId = seed.ApprovedBox,
                Quantity = 30m,
                Unit = "box",
                NetWeightKg = 120m,
                Source = "Physical",
                Status = "Active"
            });
            await db.SaveChangesAsync();
        }

        var entry = await AvocadoBoxAsync(seed);

        Assert.Equal((0m, 0m), (entry.Reserved.Quantity, entry.Reserved.NetWeightKg));
        AssertQuantity(entry.ReservedOnHold, 0m, 0m);
        AssertQuantity(entry.AvailableAfterReservations, 60m, 240m);
    }

    [Fact]
    public async Task DrillDown_Via_Service_Shows_Per_Line_Reservations()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, boxLine, _) = await SalesTestData.SeedOrderAsync(_factory, seed, boxQuantity: 100m);
        await using (var db = _factory.CreateContext())
        {
            await new StockReservationService(db).ReserveAsync(orderId, boxLine, new CreateStockReservationDto
            {
                RequestId = Guid.NewGuid(), PalletContentLineId = seed.SecondPalletApprovedBox, Quantity = 20m, Unit = "box", NetWeightKg = 80m, ActorReference = "office-1"
            });
        }

        await using var read = _factory.CreateContext();
        var drill = (await new AvailabilityService(read).GetVariantAsync(seed.AvocadoId, new AvailabilityQuery { Route = "Warehouse", Unit = "box" })).Value;

        Assert.True(drill.Basis.ReservationsApplied);
        var full = drill.ContentLines.Single(l => l.PalletContentLineId == seed.SecondPalletApprovedBox);
        Assert.Equal((20m, 80m, 0m, 0m, false), (full.ReservedQuantity, full.ReservedNetWeightKg, full.AvailableQuantity, full.AvailableNetWeightKg, full.SellableNow));
        var free = drill.ContentLines.Single(l => l.PalletContentLineId == seed.ApprovedBox);
        Assert.Equal((0m, 40m, true), (free.ReservedQuantity, free.AvailableQuantity, free.SellableNow));
    }
}
