using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

public class StockReservationServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;
    private int _colliLine;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync(decimal boxQuantity = 100m, decimal colliQuantity = 10m)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity, colliQuantity);
    }

    private static CreateStockReservationDto Reserve(int contentLineId, decimal quantity, decimal kg, string unit = "box", Guid? requestId = null, string reason = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        PalletContentLineId = contentLineId,
        Quantity = quantity,
        Unit = unit,
        NetWeightKg = kg,
        ActorReference = "office-1",
        Reason = reason
    };

    private async Task<ServiceResult<StockReservationDto>> ReserveAsync(CreateStockReservationDto request, int? lineId = null, int? orderId = null, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new StockReservationService(db).ReserveAsync(orderId ?? _orderId, lineId ?? _boxLine, request);
    }

    private static ReleaseStockReservationDto Release(int version, string reason = "Klant annuleert", Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        ExpectedReservationVersion = version,
        ActorReference = "office-1",
        Reason = reason
    };

    private async Task<ServiceResult<StockReservationDto>> ReleaseAsync(int reservationId, ReleaseStockReservationDto request, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new StockReservationService(db).ReleaseAsync(reservationId, request);
    }

    private async Task<(int Active, decimal Quantity, decimal Kg, int Events)> StateAsync(int contentLineId)
    {
        await using var db = _factory.CreateContext();
        var active = await db.StockReservations.AsNoTracking()
            .Where(r => r.PalletContentLineId == contentLineId && r.Status == "Active" && r.SalesOrderLineId != null).ToListAsync();
        var events = await db.PalletAuditEvents.CountAsync(e => e.PalletContentLineId == contentLineId && e.EventType.StartsWith("Reservation"));
        return (active.Count, active.Sum(r => r.Quantity), active.Sum(r => r.NetWeightKg ?? 0m), events);
    }

    private async Task<int> VersionOfContentAsync(int id) { await using var db = _factory.CreateContext(); return (await db.PalletContentLines.FindAsync(id))!.Version; }
    private async Task<int> VersionOfSalesLineAsync(int id) { await using var db = _factory.CreateContext(); return (await db.SalesOrderLines.FindAsync(id))!.Version; }
    private async Task<int> VersionOfPalletAsync(int id) { await using var db = _factory.CreateContext(); return (await db.Pallets.FindAsync(id))!.Version; }

    // ======================== Reserveren ========================

    [Fact]
    public async Task Reserve_Creates_Traceable_Active_Physical_Reservation_With_Audit_And_Version_Touches()
    {
        await ArrangeAsync();
        var request = Reserve(_seed.ApprovedBox, 10m, 40.5m, reason: "  Klant T, ochtendlevering ");

        var result = await ReserveAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var dto = result.Value;
        Assert.Equal(("Active", "Physical", 10m, "box", 40.5m, 0), (dto.Status, dto.Source, dto.Quantity, dto.Unit, dto.NetWeightKg, dto.Version));
        Assert.Equal((_orderId, _boxLine, _seed.ApprovedBox, _seed.WarehousePalletId), (dto.SalesOrderId, dto.SalesOrderLineId, dto.PalletContentLineId, dto.PalletId));
        Assert.Equal(("P-WH", "COLD-01", "LOT-A"), (dto.PalletReference, dto.WarehouseLocation, dto.LotNumber));

        await using var db = _factory.CreateContext();
        var stored = await db.StockReservations.SingleAsync();
        Assert.Equal(request.RequestId, stored.RequestId);
        Assert.Null(stored.OrderLineId);
        Assert.Equal(await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox), stored.SupplyCommitmentLineId);

        var audit = await db.PalletAuditEvents.SingleAsync(e => e.EventType == "ReservationCreated");
        Assert.Equal((_seed.WarehousePalletId, _seed.ApprovedBox, "office-1", "Klant T, ochtendlevering", request.RequestId), (audit.PalletId!.Value, audit.PalletContentLineId!.Value, audit.ActorReference, audit.Reason, audit.RequestId!.Value));
        using var snapshot = JsonDocument.Parse(audit.NewValue!);
        Assert.Equal(10m, snapshot.RootElement.GetProperty("quantity").GetDecimal());
        Assert.Equal(40.5m, snapshot.RootElement.GetProperty("netWeightKg").GetDecimal());
        Assert.Equal(_boxLine, snapshot.RootElement.GetProperty("salesOrderLineId").GetInt32());

        Assert.Equal(1, await VersionOfContentAsync(_seed.ApprovedBox));
        Assert.Equal(1, await VersionOfSalesLineAsync(_boxLine));
        Assert.Equal(1, await VersionOfPalletAsync(_seed.WarehousePalletId));
    }

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("request-id-empty", "requestId")]
    [InlineData("content-missing", "palletContentLineId")]
    [InlineData("quantity-missing", "quantity")]
    [InlineData("quantity-zero", "quantity")]
    [InlineData("quantity-four-decimals", "quantity")]
    [InlineData("unit-missing", "unit")]
    [InlineData("unit-kg", "unit")]
    [InlineData("weight-missing", "netWeightKg")]
    [InlineData("weight-zero", "netWeightKg")]
    [InlineData("weight-four-decimals", "netWeightKg")]
    [InlineData("actor-missing", "actorReference")]
    [InlineData("reason-too-long", "reason")]
    public async Task Reserve_Rejects_Invalid_Input(string invalidCase, string key)
    {
        await ArrangeAsync();
        var request = Reserve(_seed.ApprovedBox, 10m, 40m);
        switch (invalidCase)
        {
            case "request-id-missing": request.RequestId = null; break;
            case "request-id-empty": request.RequestId = Guid.Empty; break;
            case "content-missing": request.PalletContentLineId = null; break;
            case "quantity-missing": request.Quantity = null; break;
            case "quantity-zero": request.Quantity = 0m; break;
            case "quantity-four-decimals": request.Quantity = 1.0001m; break;
            case "unit-missing": request.Unit = null; break;
            case "unit-kg": request.Unit = "kg"; break;
            case "weight-missing": request.NetWeightKg = null; break;
            case "weight-zero": request.NetWeightKg = 0m; break;
            case "weight-four-decimals": request.NetWeightKg = 1.0001m; break;
            case "actor-missing": request.ActorReference = null; break;
            case "reason-too-long": request.Reason = new string('x', 501); break;
        }

        var result = await ReserveAsync(request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        Assert.Equal((0, 0m, 0m, 0), await StateAsync(_seed.ApprovedBox));
    }

    [Theory]
    [InlineData("order")]
    [InlineData("line-of-other-order")]
    [InlineData("content")]
    public async Task Reserve_Unknown_Order_Line_Or_Content_Is_NotFound(string missing)
    {
        await ArrangeAsync();
        var (_, otherBox, _) = await SalesTestData.SeedOrderAsync(_factory, _seed);
        var result = missing switch
        {
            "order" => await ReserveAsync(Reserve(_seed.ApprovedBox, 1m, 4m), orderId: 999_999),
            "line-of-other-order" => await ReserveAsync(Reserve(_seed.ApprovedBox, 1m, 4m), lineId: otherBox),
            _ => await ReserveAsync(Reserve(999_999, 1m, 4m)),
        };
        Assert.Equal(ServiceResultStatus.NotFound, result.Status);
    }

    [Theory]
    [InlineData("quarantine")]
    [InlineData("blocked")]
    [InlineData("rejected")]
    [InlineData("in-transit")]
    [InlineData("crossdock")]
    [InlineData("direct-trade")]
    [InlineData("unit-differs-from-content")]
    [InlineData("unit-differs-from-order-line")]
    [InlineData("variant-differs")]
    [InlineData("over-content-quantity")]
    [InlineData("over-content-weight")]
    [InlineData("over-order-line-open-quantity")]
    [InlineData("stranded-weight")]
    [InlineData("stranded-quantity")]
    [InlineData("order-not-open")]
    public async Task Reserve_Business_Rule_Violations_Are_Conflict_Without_Change(string scenario)
    {
        await ArrangeAsync(boxQuantity: 100m);
        int lineId = _boxLine;
        CreateStockReservationDto request = scenario switch
        {
            "quarantine" => Reserve(_seed.QuarantineBox, 1m, 4m),
            "blocked" => Reserve(_seed.BlockedBox, 1m, 4m),
            "rejected" => Reserve(_seed.RejectedBox, 1m, 4m),
            "in-transit" => Reserve(_seed.InTransitBox, 1m, 4m),
            "crossdock" => Reserve(_seed.CrossdockBox, 1m, 4m),
            "direct-trade" => Reserve(_seed.DirectTradeBox, 1m, 4m),
            "unit-differs-from-content" => Reserve(_seed.ApprovedColli, 1m, 5m, unit: "box"),
            "unit-differs-from-order-line" => Reserve(_seed.ApprovedColli, 1m, 5m, unit: "colli"),
            "variant-differs" => Reserve(_seed.ApprovedBox, 1m, 4m),
            "over-content-quantity" => Reserve(_seed.ApprovedBox, 40.001m, 150m),
            "over-content-weight" => Reserve(_seed.ApprovedBox, 39m, 160.001m),
            "over-order-line-open-quantity" => Reserve(_seed.ApprovedBox, 11m, 44m),
            "stranded-weight" => Reserve(_seed.ApprovedBox, 40m, 159m),
            "stranded-quantity" => Reserve(_seed.ApprovedBox, 39m, 160m),
            _ => Reserve(_seed.ApprovedBox, 1m, 4m),
        };
        if (scenario == "over-order-line-open-quantity")
        {
            var (_, smallBox, _) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity: 10m);
            await using var db = _factory.CreateContext();
            lineId = smallBox;
            _orderId = (await db.SalesOrderLines.FindAsync(smallBox))!.SalesOrderId;
        }
        if (scenario == "variant-differs")
        {
            await using var db = _factory.CreateContext();
            var mangoLine = new SalesOrderLine { SalesOrderId = _orderId, ProductVariantId = _seed.MangoId, Quantity = 5m, Unit = "box" };
            db.SalesOrderLines.Add(mangoLine);
            await db.SaveChangesAsync();
            lineId = mangoLine.Id;
        }
        if (scenario == "order-not-open")
        {
            await using var db = _factory.CreateContext();
            (await db.SalesOrders.FindAsync(_orderId))!.Status = "Cancelled";
            await db.SaveChangesAsync();
        }

        var result = await ReserveAsync(request, lineId: lineId);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.StockReservations.CountAsync());
        Assert.Equal(0, await verify.PalletAuditEvents.CountAsync(e => e.EventType.StartsWith("Reservation")));
    }

    [Fact]
    public async Task Reserving_The_Full_Remainder_And_Multiple_Up_To_The_Limit_Is_Allowed()
    {
        await ArrangeAsync();
        Assert.Equal(ServiceResultStatus.Created, (await ReserveAsync(Reserve(_seed.ApprovedBox, 15m, 60m))).Status);
        Assert.Equal(ServiceResultStatus.Created, (await ReserveAsync(Reserve(_seed.ApprovedBox, 15m, 60m))).Status);
        Assert.Equal(ServiceResultStatus.Created, (await ReserveAsync(Reserve(_seed.ApprovedBox, 10m, 40m))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await ReserveAsync(Reserve(_seed.ApprovedBox, 0.001m, 0.001m))).Status);
        Assert.Equal((3, 40m, 160m, 3), await StateAsync(_seed.ApprovedBox));
    }

    [Fact]
    public async Task Exact_Reserve_Retry_Returns_Same_Reservation_Without_New_Event()
    {
        await ArrangeAsync();
        var request = Reserve(_seed.ApprovedBox, 10m, 40m, reason: "Ochtend");
        var first = await ReserveAsync(request);
        var retry = Reserve(_seed.ApprovedBox, 10.000m, 40.0m, requestId: request.RequestId, reason: " Ochtend ");
        retry.ActorReference = " office-1 ";

        var result = await ReserveAsync(retry);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(first.Value.Id, result.Value.Id);
        Assert.Equal((1, 10m, 40m, 1), await StateAsync(_seed.ApprovedBox));
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("weight")]
    [InlineData("content-line")]
    [InlineData("order-line")]
    [InlineData("other-order-same-line")]
    [InlineData("actor")]
    [InlineData("reason")]
    public async Task Reused_Reserve_RequestId_With_Different_Payload_Is_Conflict(string change)
    {
        await ArrangeAsync();
        var request = Reserve(_seed.ApprovedBox, 10m, 40m, reason: "Ochtend");
        await ReserveAsync(request);
        var retry = Reserve(_seed.ApprovedBox, 10m, 40m, requestId: request.RequestId, reason: "Ochtend");
        int? lineId = null;
        int? orderId = null;
        switch (change)
        {
            case "other-order-same-line": lineId = _boxLine; orderId = (await SalesTestData.SeedOrderAsync(_factory, _seed)).OrderId; break;
            case "quantity": retry.Quantity = 11m; break;
            case "weight": retry.NetWeightKg = 41m; break;
            case "content-line": retry.PalletContentLineId = _seed.SecondPalletApprovedBox; break;
            case "order-line": lineId = (await SalesTestData.SeedOrderAsync(_factory, _seed)).BoxLineId; break;
            case "actor": retry.ActorReference = "office-2"; break;
            case "reason": retry.Reason = "Middag"; break;
        }

        var result = lineId is null
            ? await ReserveAsync(retry)
            : await ReserveAsync(retry, lineId: lineId, orderId: orderId ?? await OrderOfLineAsync(lineId.Value));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal((1, 10m, 40m, 1), await StateAsync(_seed.ApprovedBox));
    }

    private async Task<int> OrderOfLineAsync(int lineId)
    {
        await using var db = _factory.CreateContext();
        return (await db.SalesOrderLines.FindAsync(lineId))!.SalesOrderId;
    }

    [Fact]
    public async Task Reserve_Has_No_Legacy_Inventory_Or_Movement_Side_Effects()
    {
        await ArrangeAsync();
        await ReserveAsync(Reserve(_seed.ApprovedBox, 10m, 40m));

        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.Inventory.CountAsync());
        Assert.Equal(0, await verify.OrderLineAllocations.CountAsync());
        Assert.Equal(1, await verify.OrderLines.CountAsync());
    }

    // ======================== Vrijgeven ========================

    private async Task<StockReservationDto> ReservedAsync(int contentLineId = 0, decimal quantity = 10m, decimal kg = 40m)
    {
        var result = await ReserveAsync(Reserve(contentLineId == 0 ? _seed.ApprovedBox : contentLineId, quantity, kg));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    [Fact]
    public async Task Release_Marks_Released_Increments_Version_And_Writes_One_Event()
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync();
        var contentVersion = await VersionOfContentAsync(_seed.ApprovedBox);
        var request = Release(reservation.Version, "  Klant annuleert  ");

        var result = await ReleaseAsync(reservation.Id, request);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(("Released", 1, "Klant annuleert"), (result.Value.Status, result.Value.Version, result.Value.ReleaseReason));
        Assert.NotNull(result.Value.ReleasedAt);
        await using var db = _factory.CreateContext();
        var stored = await db.StockReservations.SingleAsync();
        Assert.Equal((request.RequestId, "Released", 1), (stored.ReleaseRequestId!.Value, stored.Status, stored.Version));
        var audit = await db.PalletAuditEvents.SingleAsync(e => e.EventType == "ReservationReleased");
        Assert.Equal((_seed.ApprovedBox, "office-1", "Klant annuleert", request.RequestId), (audit.PalletContentLineId!.Value, audit.ActorReference, audit.Reason, audit.RequestId!.Value));
        Assert.Equal(contentVersion + 1, await VersionOfContentAsync(_seed.ApprovedBox));
        Assert.Equal(ServiceResultStatus.Created, (await ReserveAsync(Reserve(_seed.ApprovedBox, 40m, 160m))).Status);
    }

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("version-missing", "expectedReservationVersion")]
    [InlineData("reason-missing", "reason")]
    [InlineData("reason-blank", "reason")]
    [InlineData("reason-too-long", "reason")]
    [InlineData("actor-missing", "actorReference")]
    public async Task Release_Rejects_Invalid_Input(string invalidCase, string key)
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync();
        var request = Release(reservation.Version);
        switch (invalidCase)
        {
            case "request-id-missing": request.RequestId = null; break;
            case "version-missing": request.ExpectedReservationVersion = null; break;
            case "reason-missing": request.Reason = null; break;
            case "reason-blank": request.Reason = "   "; break;
            case "reason-too-long": request.Reason = new string('x', 501); break;
            case "actor-missing": request.ActorReference = null; break;
        }

        var result = await ReleaseAsync(reservation.Id, request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        Assert.Equal((1, 10m, 40m, 1), await StateAsync(_seed.ApprovedBox));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task Stale_Release_Version_Is_Conflict_Without_Change_Or_Event(int offset)
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync();

        var result = await ReleaseAsync(reservation.Id, Release(reservation.Version + offset));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        await using var db = _factory.CreateContext();
        var stored = await db.StockReservations.SingleAsync();
        Assert.Equal(("Active", 0), (stored.Status, stored.Version));
        Assert.Equal((1, 10m, 40m, 1), await StateAsync(_seed.ApprovedBox));
    }

    [Fact]
    public async Task Exact_Release_Retry_Returns_Current_State_Without_New_Event()
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync();
        var request = Release(reservation.Version);
        await ReleaseAsync(reservation.Id, request);

        var retry = await ReleaseAsync(reservation.Id, new ReleaseStockReservationDto
        {
            RequestId = request.RequestId,
            ExpectedReservationVersion = request.ExpectedReservationVersion,
            ActorReference = " office-1 ",
            Reason = " Klant annuleert "
        });

        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(("Released", 1), (retry.Value.Status, retry.Value.Version));
        Assert.Equal((0, 0m, 0m, 2), await StateAsync(_seed.ApprovedBox));
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("actor")]
    [InlineData("version")]
    public async Task Reused_Release_RequestId_With_Different_Payload_Is_Conflict(string change)
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync();
        var request = Release(reservation.Version);
        await ReleaseAsync(reservation.Id, request);
        var retry = Release(reservation.Version, requestId: request.RequestId);
        switch (change)
        {
            case "reason": retry.Reason = "Andere reden"; break;
            case "actor": retry.ActorReference = "office-2"; break;
            case "version": retry.ExpectedReservationVersion = 1; break;
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await ReleaseAsync(reservation.Id, retry)).Status);
        Assert.Equal((0, 0m, 0m, 2), await StateAsync(_seed.ApprovedBox));
    }

    [Theory]
    [InlineData("already-released")]
    [InlineData("consumed")]
    [InlineData("legacy")]
    public async Task Release_Of_Non_Active_Or_Legacy_Reservation_Is_Conflict(string scenario)
    {
        await ArrangeAsync();
        int reservationId;
        int version;
        if (scenario == "legacy")
        {
            await using var db = _factory.CreateContext();
            var legacy = new StockReservation
            {
                OrderLineId = _seed.LegacyOrderLineId,
                SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox),
                Quantity = 1m,
                Source = "Expected",
                Status = "Active"
            };
            db.StockReservations.Add(legacy);
            await db.SaveChangesAsync();
            (reservationId, version) = (legacy.Id, 0);
        }
        else
        {
            var reservation = await ReservedAsync();
            if (scenario == "already-released")
            {
                await ReleaseAsync(reservation.Id, Release(reservation.Version));
            }
            else
            {
                await using var db = _factory.CreateContext();
                (await db.StockReservations.FindAsync(reservation.Id))!.Status = "Consumed";
                await db.SaveChangesAsync();
            }
            (reservationId, version) = (reservation.Id, 1);
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await ReleaseAsync(reservationId, Release(version))).Status);
    }

    [Fact]
    public async Task Release_Of_Unknown_Reservation_Is_NotFound()
    {
        await ArrangeAsync();
        Assert.Equal(ServiceResultStatus.NotFound, (await ReleaseAsync(999_999, Release(0))).Status);
    }

    // ======================== Concurrency (deterministisch) ========================

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_Concurrent_Reservations_On_One_Content_Line_Never_Exceed_It(bool reverse)
    {
        await ArrangeAsync();
        var a = Reserve(_seed.ApprovedBox, 25m, 100m);
        var b = Reserve(_seed.ApprovedBox, 25m, 100m);
        var (outer, inner) = reverse ? (b, a) : (a, b);
        ServiceResultStatus innerStatus = default;

        var concurrent = new ConcurrentSaveInterceptor(async () => innerStatus = (await ReserveAsync(inner)).Status);
        var outerStatus = (await ReserveAsync(outer, interceptors: concurrent)).Status;

        Assert.Equal(ServiceResultStatus.Created, innerStatus);
        Assert.Equal(ServiceResultStatus.Conflict, outerStatus);
        var state = await StateAsync(_seed.ApprovedBox);
        Assert.Equal((1, 25m, 100m, 1), state);
    }

    [Fact]
    public async Task Two_Concurrent_Reservations_For_One_Order_Line_Never_Exceed_Its_Open_Quantity()
    {
        await ArrangeAsync(boxQuantity: 30m);
        ServiceResultStatus innerStatus = default;
        var concurrent = new ConcurrentSaveInterceptor(async () => innerStatus = (await ReserveAsync(Reserve(_seed.SecondPalletApprovedBox, 20m, 80m))).Status);

        var outerStatus = (await ReserveAsync(Reserve(_seed.ApprovedBox, 20m, 80m), interceptors: concurrent)).Status;

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (innerStatus, outerStatus));
        await using var db = _factory.CreateContext();
        Assert.Equal(20m, (await db.StockReservations.Where(r => r.SalesOrderLineId == _boxLine && r.Status == "Active").ToListAsync()).Sum(r => r.Quantity));
    }

    private async Task<ServiceResultStatus> BlockAsync(int contentLineId)
    {
        await using var read = _factory.CreateContext();
        var line = await read.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == contentLineId);
        await using var service = _factory.CreateContext();
        return (await new PalletService(service).ChangeQualityStatusAsync(line.PalletId, line.Id, new ChangeQualityStatusDto
        {
            RequestId = Guid.NewGuid(),
            ExpectedContentLineVersion = line.Version,
            ToStatus = "Blocked",
            ActorReference = "qc-1",
            Reason = "Claim"
        })).Status;
    }

    [Theory]
    [InlineData("reserve-saves-last")]
    [InlineData("block-saves-last")]
    public async Task Reserve_And_Block_Race_Has_Exactly_One_Winner_And_Never_Reserves_Blocked_Content(string order)
    {
        await ArrangeAsync();
        ServiceResultStatus reserve, block;
        if (order == "reserve-saves-last")
        {
            ServiceResultStatus inner = default;
            var concurrent = new ConcurrentSaveInterceptor(async () => inner = await BlockAsync(_seed.ApprovedBox));
            reserve = (await ReserveAsync(Reserve(_seed.ApprovedBox, 10m, 40m), interceptors: concurrent)).Status;
            block = inner;
        }
        else
        {
            ServiceResultStatus inner = default;
            await using var db = _factory.CreateContext();
            var line = await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == _seed.ApprovedBox);
            var concurrent = new ConcurrentSaveInterceptor(async () => inner = (await ReserveAsync(Reserve(_seed.ApprovedBox, 10m, 40m))).Status);
            await using var service = _factory.CreateContext(concurrent);
            block = (await new PalletService(service).ChangeQualityStatusAsync(line.PalletId, line.Id, new ChangeQualityStatusDto
            {
                RequestId = Guid.NewGuid(), ExpectedContentLineVersion = line.Version, ToStatus = "Blocked", ActorReference = "qc-1", Reason = "Claim"
            })).Status;
            reserve = inner;
        }

        Assert.Equal(1, new[] { reserve, block }.Count(s => s is ServiceResultStatus.Created or ServiceResultStatus.Ok));
        Assert.Equal(1, new[] { reserve, block }.Count(s => s == ServiceResultStatus.Conflict));
        await using var verify = _factory.CreateContext();
        var content = await verify.PalletContentLines.FindAsync(_seed.ApprovedBox);
        var active = await verify.StockReservations.CountAsync(r => r.Status == "Active");
        Assert.True(content!.QualityStatus == "Approved" ? active == 1 : active == 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_Concurrent_Releases_With_Different_RequestIds_Have_Exactly_One_Winner(bool reverse)
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync();
        var a = Release(reservation.Version, "Reden A");
        var b = Release(reservation.Version, "Reden B");
        var (outer, inner) = reverse ? (b, a) : (a, b);
        ServiceResultStatus innerStatus = default;

        var concurrent = new ConcurrentSaveInterceptor(async () => innerStatus = (await ReleaseAsync(reservation.Id, inner)).Status);
        var outerStatus = (await ReleaseAsync(reservation.Id, outer, concurrent)).Status;

        Assert.Equal((ServiceResultStatus.Ok, ServiceResultStatus.Conflict), (innerStatus, outerStatus));
        await using var db = _factory.CreateContext();
        var stored = await db.StockReservations.SingleAsync();
        Assert.Equal(("Released", 1, inner.Reason), (stored.Status, stored.Version, stored.ReleaseReason));
        Assert.Equal(1, await db.PalletAuditEvents.CountAsync(e => e.EventType == "ReservationReleased"));
    }

    [Fact]
    public async Task Two_Concurrent_Releases_With_The_Same_RequestId_Both_Return_Ok_With_One_Event()
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync();
        var request = Release(reservation.Version);
        ServiceResultStatus innerStatus = default;

        var concurrent = new ConcurrentSaveInterceptor(async () => innerStatus = (await ReleaseAsync(reservation.Id, request)).Status);
        var outerStatus = (await ReleaseAsync(reservation.Id, request, concurrent)).Status;

        Assert.Equal((ServiceResultStatus.Ok, ServiceResultStatus.Ok), (innerStatus, outerStatus));
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.PalletAuditEvents.CountAsync(e => e.EventType == "ReservationReleased"));
    }

    // Dispatch (PR #26) zet een reservering op Consumed en verhoogt de versie; hier rechtstreeks gesimuleerd.
    private async Task ConsumeDirectlyAsync(int reservationId)
    {
        await using var db = _factory.CreateContext();
        (await db.StockReservations.FindAsync(reservationId))!.Status = "Consumed";
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("release-saves-last")]
    [InlineData("dispatch-saves-last")]
    public async Task Release_Versus_Later_Dispatch_Consumption_Has_Exactly_One_Winner(string order)
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync();
        if (order == "release-saves-last")
        {
            var concurrent = new ConcurrentSaveInterceptor(() => ConsumeDirectlyAsync(reservation.Id));
            Assert.Equal(ServiceResultStatus.Conflict, (await ReleaseAsync(reservation.Id, Release(reservation.Version), concurrent)).Status);
        }
        else
        {
            ServiceResultStatus released = default;
            await using var db = _factory.CreateContext(new ConcurrentSaveInterceptor(async () => released = (await ReleaseAsync(reservation.Id, Release(reservation.Version))).Status));
            (await db.StockReservations.FindAsync(reservation.Id))!.Status = "Consumed";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
            Assert.Equal(ServiceResultStatus.Ok, released);
        }

        await using var verify = _factory.CreateContext();
        var stored = await verify.StockReservations.SingleAsync();
        Assert.Equal(1, stored.Version);
        Assert.Equal(order == "release-saves-last" ? "Consumed" : "Released", stored.Status);
        // Een afgeboekte (Consumed) reservering kan daarna nooit meer worden vrijgegeven.
        if (stored.Status == "Consumed")
        {
            Assert.Equal(ServiceResultStatus.Conflict, (await ReleaseAsync(reservation.Id, Release(stored.Version))).Status);
        }
    }

    // ======================== B6: pallets met actieve verkoopreserveringen ========================

    private async Task<ServiceResultStatus> DispatchAsync(int palletId, string location = "InTransit", params IInterceptor[] interceptors)
    {
        await using var read = _factory.CreateContext();
        var version = (await read.Pallets.FindAsync(palletId))!.Version;
        await using var db = _factory.CreateContext(interceptors);
        return (await new PalletService(db).ChangeLogisticsStatusAsync(palletId, new ChangeLogisticsStatusDto
        {
            RequestId = Guid.NewGuid(), ExpectedPalletVersion = version, ToStatus = "Dispatched", LocationContext = location, ActorReference = "warehouse-1"
        })).Status;
    }

    private async Task<ServiceResultStatus> MoveAsync(int palletId, string location)
    {
        await using var read = _factory.CreateContext();
        var version = (await read.Pallets.FindAsync(palletId))!.Version;
        await using var db = _factory.CreateContext();
        return (await new PalletService(db).ChangeLocationAsync(palletId, new ChangePalletLocationDto
        {
            RequestId = Guid.NewGuid(), ExpectedPalletVersion = version, LocationContext = location, ActorReference = "warehouse-1"
        })).Status;
    }

    [Fact]
    public async Task Pallet_With_Active_Sales_Reservation_Cannot_Be_Dispatched_Or_Moved_Out_Of_Warehouse()
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync(_seed.SecondPalletApprovedBox, 5m, 20m);
        var palletVersion = await VersionOfPalletAsync(_seed.SecondWarehousePalletId);

        Assert.Equal(ServiceResultStatus.Conflict, await DispatchAsync(_seed.SecondWarehousePalletId));
        Assert.Equal(ServiceResultStatus.Conflict, await MoveAsync(_seed.SecondWarehousePalletId, "Crossdock"));
        Assert.Equal(palletVersion, await VersionOfPalletAsync(_seed.SecondWarehousePalletId));

        Assert.Equal(ServiceResultStatus.Ok, (await ReleaseAsync(reservation.Id, Release(reservation.Version))).Status);
        Assert.Equal(ServiceResultStatus.Ok, await DispatchAsync(_seed.SecondWarehousePalletId));
    }

    [Theory]
    [InlineData("released")]
    [InlineData("consumed")]
    [InlineData("legacy-order-line")]
    public async Task Pallet_Block_Only_Applies_To_Active_Physical_Sales_Reservations(string scenario)
    {
        await ArrangeAsync();
        if (scenario == "legacy-order-line")
        {
            await using var db = _factory.CreateContext();
            db.StockReservations.Add(new StockReservation
            {
                OrderLineId = _seed.LegacyOrderLineId,
                SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.SecondPalletApprovedBox),
                PalletContentLineId = _seed.SecondPalletApprovedBox,
                Quantity = 5m,
                Unit = "box",
                NetWeightKg = 20m,
                Source = "Physical",
                Status = "Active"
            });
            await db.SaveChangesAsync();
        }
        else
        {
            var reservation = await ReservedAsync(_seed.SecondPalletApprovedBox, 5m, 20m);
            if (scenario == "released") await ReleaseAsync(reservation.Id, Release(reservation.Version));
            else await ConsumeDirectlyAsync(reservation.Id);
        }

        Assert.Equal(ServiceResultStatus.Ok, await DispatchAsync(_seed.SecondWarehousePalletId));
    }

    [Theory]
    [InlineData("reserve-saves-last")]
    [InlineData("dispatch-saves-last")]
    public async Task Reserve_And_Dispatch_Race_Has_Exactly_One_Winner(string order)
    {
        await ArrangeAsync();
        ServiceResultStatus reserve, dispatch;
        if (order == "reserve-saves-last")
        {
            ServiceResultStatus inner = default;
            var concurrent = new ConcurrentSaveInterceptor(async () => inner = await DispatchAsync(_seed.SecondWarehousePalletId));
            reserve = (await ReserveAsync(Reserve(_seed.SecondPalletApprovedBox, 5m, 20m), interceptors: concurrent)).Status;
            dispatch = inner;
        }
        else
        {
            ServiceResultStatus inner = default;
            var concurrent = new ConcurrentSaveInterceptor(async () => inner = (await ReserveAsync(Reserve(_seed.SecondPalletApprovedBox, 5m, 20m))).Status);
            dispatch = await DispatchAsync(_seed.SecondWarehousePalletId, interceptors: concurrent);
            reserve = inner;
        }

        Assert.Equal(1, new[] { reserve, dispatch }.Count(s => s is ServiceResultStatus.Created or ServiceResultStatus.Ok));
        Assert.Equal(1, new[] { reserve, dispatch }.Count(s => s == ServiceResultStatus.Conflict));
        await using var verify = _factory.CreateContext();
        var pallet = await verify.Pallets.FindAsync(_seed.SecondWarehousePalletId);
        var active = await verify.StockReservations.CountAsync(r => r.Status == "Active");
        Assert.True(pallet!.LogisticsStatus == "Dispatched" ? active == 0 : active == 1);
    }
}
