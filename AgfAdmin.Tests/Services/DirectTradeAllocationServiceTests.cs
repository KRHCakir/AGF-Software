using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Services;

// Toewijzen van klantgebonden DirectTrade-aanvoer aan een orderregel: zelfde klant, variant en eenheid; alleen hoeveelheid (geen kg);
// resterend = verwacht - toegewezen; vrijgeven, annuleren, idempotentie, races en de harde scheiding van magazijnvoorraad.
public class DirectTradeAllocationServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;
    private int _colliLine;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync(decimal boxQuantity = 40m, decimal colliQuantity = 10m)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity, colliQuantity);
    }

    private static CreateDirectTradeAllocationDto Allocate(int sourceLineId, decimal quantity, string unit = "box", Guid? requestId = null, string reason = null, bool? acknowledge = true, string actor = "office-1") => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        SupplyCommitmentLineId = sourceLineId,
        Quantity = quantity,
        Unit = unit,
        AcknowledgeExpectedSupply = acknowledge,
        ActorReference = actor,
        Reason = reason
    };

    private async Task<ServiceResult<StockReservationDto>> AllocateAsync(CreateDirectTradeAllocationDto request, int? lineId = null, int? orderId = null, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new DirectTradeAllocationService(db).AllocateAsync(orderId ?? _orderId, lineId ?? _boxLine, request);
    }

    private async Task<StockReservationDto> AllocatedAsync(decimal quantity, int? sourceLineId = null, int? lineId = null, string unit = "box")
    {
        var result = await AllocateAsync(Allocate(sourceLineId ?? _seed.DirectTradeSourceLine, quantity, unit), lineId);
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    private async Task<IReadOnlyList<DirectTradeSupplyLineDto>> SupplyAsync(int? lineId = null, int? orderId = null)
    {
        await using var db = _factory.CreateContext();
        var result = await new DirectTradeAllocationService(db).GetAllocatableSupplyAsync(orderId ?? _orderId, lineId ?? _boxLine);
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        return result.Value;
    }

    private async Task<ServiceResult<StockReservationDto>> ReserveAsync(int contentLineId, decimal quantity, decimal kg, int? lineId = null, string unit = "box", params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new StockReservationService(db).ReserveAsync(_orderId, lineId ?? _boxLine, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = contentLineId, Quantity = quantity, Unit = unit, NetWeightKg = kg, ActorReference = "office-1"
        });
    }

    private async Task<StockReservationDto> ReservedAsync(int contentLineId, decimal quantity, decimal kg)
    {
        var result = await ReserveAsync(contentLineId, quantity, kg);
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    private async Task<ServiceResult<StockReservationDto>> ReleaseAsync(int reservationId, int version, Guid? requestId = null, string reason = "Klant wil later", params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new StockReservationService(db).ReleaseAsync(reservationId, new ReleaseStockReservationDto
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedReservationVersion = version, ActorReference = "office-1", Reason = reason
        });
    }

    private async Task<ServiceResult<SalesOrderDto>> CancelAsync(int version, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new SalesOrderService(db).CancelAsync(_orderId, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = "Afgezegd" });
    }

    private async Task<ServiceResult<SalesDispatchDto>> DispatchAsync(int orderVersion, params (int Id, int Version)[] lines)
    {
        await using var db = _factory.CreateContext();
        return await new SalesDispatchService(db).DispatchAsync(_orderId, new CreateSalesDispatchDto
        {
            RequestId = Guid.NewGuid(), ExpectedOrderVersion = orderVersion, ActorReference = "office-1", Reference = "VRB-DT",
            Lines = lines.Select(l => new CreateSalesDispatchLineDto { StockReservationId = l.Id, ExpectedReservationVersion = l.Version }).ToList()
        });
    }

    private async Task<SalesOrderDto> OrderAsync(int? orderId = null)
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(orderId ?? _orderId)).Value;
    }

    private SalesOrderLineDto BoxLine(SalesOrderDto order) => order.Lines.Single(l => l.Id == _boxLine);

    private async Task<(List<StockReservation> Reservations, List<SalesOrderAuditEvent> OrderEvents, int PalletEvents, int Movements, int SourceVersion, int LineVersion)> StateAsync()
    {
        await using var db = _factory.CreateContext();
        return (
            await db.StockReservations.AsNoTracking().Where(r => r.SalesOrderLine.SalesOrderId == _orderId).OrderBy(r => r.Id).ToListAsync(),
            await db.SalesOrderAuditEvents.AsNoTracking().Where(e => e.SalesOrderId == _orderId).OrderBy(e => e.Id).ToListAsync(),
            await db.PalletAuditEvents.CountAsync(),
            await db.StockMovements.CountAsync(),
            (await db.SupplyCommitmentLines.FindAsync(_seed.DirectTradeSourceLine))!.Version,
            (await db.SalesOrderLines.FindAsync(_boxLine))!.Version);
    }

    private static JsonElement Snapshot(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ======================== Gelukte toewijzing ========================

    [Fact]
    public async Task Partial_Allocations_Update_Remaining_Line_Totals_And_Audit_Without_Stock_Or_Pallet_Events()
    {
        await ArrangeAsync();
        var before = await StateAsync();

        var first = await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 10m, reason: "Klant wil rechtstreeks"));

        Assert.Equal(ServiceResultStatus.Created, first.Status);
        var dto = first.Value;
        Assert.Equal(("DirectTrade", "Active", 10m, "box"), (dto.Source, dto.Status, dto.Quantity, dto.Unit));
        Assert.Null(dto.PalletContentLineId);
        Assert.Null(dto.PalletId);
        Assert.Null(dto.PalletReference);
        Assert.Null(dto.NetWeightKg);
        Assert.False(dto.OnHold);
        Assert.Null(dto.ContentQualityStatus);
        Assert.Equal((_seed.DirectTradeSourceLine, _seed.DirectTradeCommitmentId, "Supplier B", "Peru", "Turkije", "LOT-DT-SRC"),
            (dto.SupplyCommitmentLineId, dto.SupplyCommitmentId, dto.VendorName, dto.Origin, dto.Destination, dto.LotNumber));
        Assert.Equal(new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc), dto.ExpectedAt);
        Assert.Equal((_orderId, _boxLine, 0), (dto.SalesOrderId, dto.SalesOrderLineId, dto.Version));

        var supply = Assert.Single(await SupplyAsync());
        Assert.Equal((100m, 10m, 90m, 10m, 400m, "LOT-DT-SRC", "Expected"), (supply.ExpectedQuantity, supply.AllocatedQuantity, supply.RemainingQuantity, supply.AllocatedToThisLineQuantity, supply.DeclaredExpectedNetWeightKg, supply.LotNumber, supply.CommitmentStatus));

        var second = await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 20m));
        Assert.Equal(ServiceResultStatus.Created, second.Status);

        var line = BoxLine(await OrderAsync());
        Assert.Equal((0m, 30m, 0m, 10m, false), (line.ReservedQuantity, line.AllocatedQuantity, line.DispatchedQuantity, line.OpenQuantity, line.Fulfilled));
        Assert.Equal(2, line.Reservations.Count(r => r.Source == "DirectTrade" && r.Status == "Active"));
        Assert.Equal(70m, Assert.Single(await SupplyAsync()).RemainingQuantity);

        var after = await StateAsync();
        Assert.Equal(before.PalletEvents, after.PalletEvents);
        Assert.Equal(before.Movements, after.Movements);
        Assert.Equal((2, 2), (after.SourceVersion, after.LineVersion));
        var events = after.OrderEvents.Where(e => e.EventType == "DirectTradeAllocated").ToList();
        Assert.Equal(2, events.Count);
        var snapshot = Snapshot(events[0].NewValue);
        Assert.Equal((first.Value.RequestId, "office-1", "Klant wil rechtstreeks"), (events[0].RequestId, events[0].ActorReference, events[0].Reason));
        Assert.Equal((10m, "box", true, 90m, _seed.DirectTradeSourceLine, _seed.CustomerId),
            (snapshot.GetProperty("quantity").GetDecimal(), snapshot.GetProperty("unit").GetString(), snapshot.GetProperty("expectedSupplyAcknowledgedByUser").GetBoolean(),
                snapshot.GetProperty("remainingAfter").GetDecimal(), snapshot.GetProperty("supplyCommitmentLineId").GetInt32(), snapshot.GetProperty("customerId").GetInt32()));
        Assert.Equal(70m, Snapshot(events[1].NewValue).GetProperty("remainingAfter").GetDecimal());
        Assert.All(after.Reservations, r => Assert.Null(r.PalletContentLineId));
        Assert.All(after.Reservations, r => Assert.Null(r.NetWeightKg));
    }

    [Fact]
    public async Task Allocatable_Supply_Is_Filtered_On_Customer_Variant_Unit_And_Allowlisted_Status_And_Hides_Exhausted_Lines()
    {
        await ArrangeAsync(boxQuantity: 500m, colliQuantity: 500m);

        // Regel in box: alleen de Expected box-bron van klant 1 (niet die van klant 2, niet de Received, niet de colli-regel).
        Assert.Equal(new[] { _seed.DirectTradeSourceLine }, (await SupplyAsync()).Select(s => s.SupplyCommitmentLineId));
        Assert.Equal(new[] { _seed.DirectTradeColliSourceLine }, (await SupplyAsync(_colliLine)).Select(s => s.SupplyCommitmentLineId));

        // Order van klant 2: alleen haar eigen bron.
        int otherOrder, otherLine;
        await using (var db = _factory.CreateContext())
        {
            var order = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = _seed.OtherCustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "office-1" };
            var line = new SalesOrderLine { SalesOrder = order, ProductVariantId = _seed.AvocadoId, Quantity = 10m, Unit = "box" };
            db.AddRange(order, line);
            await db.SaveChangesAsync();
            (otherOrder, otherLine) = (order.Id, line.Id);
        }
        Assert.Equal(new[] { _seed.OtherCustomerDirectTradeSourceLine }, (await SupplyAsync(otherLine, otherOrder)).Select(s => s.SupplyCommitmentLineId));

        // Volledig toegewezen: verdwijnt uit de lijst; de toezegging toont toegewezen 100 / resterend 0.
        await AllocatedAsync(100m);
        Assert.Empty(await SupplyAsync());
        await using (var db = _factory.CreateContext())
        {
            var commitment = (await new SupplyCommitmentService(db).GetAsync(_seed.DirectTradeCommitmentId)).Value;
            var box = commitment.Lines.Single(l => l.Id == _seed.DirectTradeSourceLine);
            Assert.Equal((100m, 0m), (box.DirectTradeAllocatedQuantity, box.DirectTradeRemainingQuantity));
            Assert.Equal((0m, 50m), (commitment.Lines.Single(l => l.Id == _seed.DirectTradeColliSourceLine).DirectTradeAllocatedQuantity, commitment.Lines.Single(l => l.Id == _seed.DirectTradeColliSourceLine).DirectTradeRemainingQuantity));
            // Magazijnaanvoer heeft geen toewijsbare verwachte capaciteit.
            var warehouse = (await new SupplyCommitmentService(db).ListAsync(new SupplyCommitmentFilter { Route = "Warehouse" })).Value.Single();
            Assert.All(warehouse.Lines, l => Assert.Null(l.DirectTradeAllocatedQuantity));
            Assert.All(warehouse.Lines, l => Assert.Null(l.DirectTradeRemainingQuantity));
        }
    }

    [Fact]
    public async Task Warehouse_Availability_And_Reservable_Stock_Are_Unchanged_By_Allocations()
    {
        await ArrangeAsync();
        string overviewBefore, reservableBefore;
        await using (var db = _factory.CreateContext())
        {
            overviewBefore = JsonSerializer.Serialize((await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = _seed.AvocadoId })).Value.Variants);
            reservableBefore = JsonSerializer.Serialize((await new SalesOrderService(db).GetReservableStockAsync(_orderId, _boxLine)).Value);
        }

        await AllocatedAsync(25m);

        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(overviewBefore, JsonSerializer.Serialize((await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = _seed.AvocadoId })).Value.Variants));
            Assert.Equal(reservableBefore, JsonSerializer.Serialize((await new SalesOrderService(db).GetReservableStockAsync(_orderId, _boxLine)).Value));
        }
    }

    // ======================== Regels (409) ========================

    [Theory]
    [InlineData("other-customer", "bound to customer")]
    [InlineData("unit-colli-source", "must exactly match the supply line unit")]
    [InlineData("received-status", "only Expected or PartiallyReceived")]
    [InlineData("warehouse-source", "Only DirectTrade supply")]
    [InlineData("variant-mismatch", "different product variant")]
    [InlineData("over-remaining", "still unallocated")]
    [InlineData("over-demand", "still needs only 10")]
    [InlineData("over-demand-mixed", "still needs only 5")]
    [InlineData("order-cancelled", "allocations require an open order")]
    [InlineData("request-unit-mismatch", "units are never converted")]
    public async Task Business_Rule_Violations_Are_Conflict_Without_Change(string scenario, string message)
    {
        await ArrangeAsync();
        var lineId = _boxLine;
        var request = Allocate(_seed.DirectTradeSourceLine, 10m);
        switch (scenario)
        {
            case "other-customer": request = Allocate(_seed.OtherCustomerDirectTradeSourceLine, 10m); break;
            case "unit-colli-source": request = Allocate(_seed.DirectTradeColliSourceLine, 1m); break;
            case "received-status": request = Allocate(_seed.ReceivedDirectTradeSourceLine, 1m); break;
            case "warehouse-source":
                await using (var db = _factory.CreateContext())
                {
                    request = Allocate(await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox), 1m);
                }
                break;
            case "variant-mismatch":
                await using (var db = _factory.CreateContext())
                {
                    var mango = new SalesOrderLine { SalesOrderId = _orderId, ProductVariantId = _seed.MangoId, Quantity = 10m, Unit = "box" };
                    db.Add(mango);
                    await db.SaveChangesAsync();
                    lineId = mango.Id;
                }
                break;
            case "over-remaining": request = Allocate(_seed.DirectTradeSourceLine, 100.001m); break;
            case "over-demand": await AllocatedAsync(30m); request = Allocate(_seed.DirectTradeSourceLine, 11m); break;
            case "over-demand-mixed": await ReservedAsync(_seed.ApprovedBox, 20m, 80m); await AllocatedAsync(15m); request = Allocate(_seed.DirectTradeSourceLine, 6m); break;
            case "order-cancelled": Assert.Equal(ServiceResultStatus.Ok, (await CancelAsync(0)).Status); break;
            case "request-unit-mismatch": request = Allocate(_seed.DirectTradeSourceLine, 1m, unit: "colli"); break;
        }
        var before = await StateAsync();

        var result = await AllocateAsync(request, lineId);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains(message, result.Message);
        var after = await StateAsync();
        Assert.Equal(before.Reservations.Count, after.Reservations.Count);
        Assert.Equal(before.OrderEvents.Count, after.OrderEvents.Count);
        Assert.Equal(before.SourceVersion, after.SourceVersion);
    }

    [Fact]
    public async Task Physical_Reservation_Respects_Existing_Allocations_In_The_Same_Line_Demand_Check()
    {
        await ArrangeAsync();
        await AllocatedAsync(30m);

        var tooMuch = await ReserveAsync(_seed.ApprovedBox, 15m, 60m);
        Assert.Equal(ServiceResultStatus.Conflict, tooMuch.Status);
        Assert.Contains("still needs only 10", tooMuch.Message);

        Assert.Equal(ServiceResultStatus.Created, (await ReserveAsync(_seed.ApprovedBox, 10m, 40m)).Status);
        var line = BoxLine(await OrderAsync());
        Assert.Equal((10m, 30m, 0m), (line.ReservedQuantity, line.AllocatedQuantity, line.OpenQuantity));
    }

    // ======================== Invoer (400) en 404 ========================

    [Theory]
    [InlineData("no-request-id", "requestId")]
    [InlineData("no-source", "supplyCommitmentLineId")]
    [InlineData("zero-quantity", "quantity")]
    [InlineData("four-decimals", "quantity")]
    [InlineData("bad-unit", "unit")]
    [InlineData("unit-case", "unit")]
    [InlineData("ack-false", "acknowledgeExpectedSupply")]
    [InlineData("ack-missing", "acknowledgeExpectedSupply")]
    [InlineData("no-actor", "actorReference")]
    [InlineData("reason-too-long", "reason")]
    public async Task Invalid_Input_Is_Rejected_With_The_Field_Key_And_Changes_Nothing(string invalidCase, string key)
    {
        await ArrangeAsync();
        var request = Allocate(_seed.DirectTradeSourceLine, 10m);
        switch (invalidCase)
        {
            case "no-request-id": request.RequestId = Guid.Empty; break;
            case "no-source": request.SupplyCommitmentLineId = null; break;
            case "zero-quantity": request.Quantity = 0m; break;
            case "four-decimals": request.Quantity = 1.0001m; break;
            case "bad-unit": request.Unit = "kg"; break;
            case "unit-case": request.Unit = "Box"; break;
            case "ack-false": request.AcknowledgeExpectedSupply = false; break;
            case "ack-missing": request.AcknowledgeExpectedSupply = null; break;
            case "no-actor": request.ActorReference = " "; break;
            case "reason-too-long": request.Reason = new string('x', 501); break;
        }

        var result = await AllocateAsync(request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        Assert.Empty((await StateAsync()).Reservations);
    }

    [Fact]
    public async Task Null_Body_Unknown_Line_And_Unknown_Source_Are_Handled()
    {
        await ArrangeAsync();
        Assert.Equal(ServiceResultStatus.Validation, (await AllocateAsync(null)).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 1m), lineId: 999_999)).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 1m), orderId: 999_999)).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await AllocateAsync(Allocate(999_999, 1m))).Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.NotFound, (await new DirectTradeAllocationService(db).GetAllocatableSupplyAsync(_orderId, 999_999)).Status);
    }

    // ======================== Idempotentie ========================

    [Fact]
    public async Task Exact_Retry_Returns_The_Same_Allocation_And_A_Changed_Payload_Or_Foreign_RequestId_Is_Conflict()
    {
        await ArrangeAsync();
        var requestId = Guid.NewGuid();
        var created = await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 10m, requestId: requestId, reason: "r"));
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        var before = await StateAsync();

        var retry = await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 10m, requestId: requestId, reason: "r"));
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(created.Value.Id, retry.Value.Id);
        var after = await StateAsync();
        Assert.Equal((before.Reservations.Count, before.OrderEvents.Count, before.SourceVersion), (after.Reservations.Count, after.OrderEvents.Count, after.SourceVersion));

        foreach (var changed in new[]
                 {
                     Allocate(_seed.DirectTradeSourceLine, 11m, requestId: requestId, reason: "r"),
                     Allocate(_seed.DirectTradeSourceLine, 10m, requestId: requestId, reason: "other"),
                     Allocate(_seed.DirectTradeSourceLine, 10m, requestId: requestId, reason: "r", actor: "office-2"),
                     Allocate(_seed.DirectTradeColliSourceLine, 10m, requestId: requestId, reason: "r")
                 })
        {
            Assert.Equal(ServiceResultStatus.Conflict, (await AllocateAsync(changed)).Status);
        }
        Assert.Equal(ServiceResultStatus.Conflict, (await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 10m, requestId: requestId, reason: "r"), lineId: _colliLine)).Status);

        // Een requestId van een fysieke reservering is nooit een toewijzing.
        var physical = await ReservedAsync(_seed.ApprovedBox, 5m, 20m);
        var reused = await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 5m, requestId: physical.RequestId));
        Assert.Equal(ServiceResultStatus.Conflict, reused.Status);
        Assert.Equal(2, (await StateAsync()).Reservations.Count);
    }

    // ======================== Races ========================

    [Theory]
    [InlineData("allocate")]
    [InlineData("cancel")]
    [InlineData("reserve")]
    [InlineData("release-other")]
    public async Task Concurrent_Writers_On_The_Same_Source_Or_Order_Line_Have_Exactly_One_Winner(string concurrent)
    {
        await ArrangeAsync(boxQuantity: 200m);
        var other = await AllocatedAsync(60m);
        ServiceResultStatus concurrentStatus = default;
        var interceptor = new ConcurrentSaveInterceptor(async () =>
        {
            concurrentStatus = concurrent switch
            {
                "allocate" => (await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 30m))).Status,
                "cancel" => (await CancelAsync(0)).Status,
                "reserve" => (await ReserveAsync(_seed.ApprovedBox, 5m, 20m)).Status,
                _ => (await ReleaseAsync(other.Id, other.Version)).Status
            };
        });

        // Gebaseerd op: 40 resterend op de bron (100 - 60). Beide schrijvers passen; alleen de eerste save wint.
        var result = await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 30m), interceptors: interceptor);

        Assert.Equal(concurrent is "cancel" or "release-other" ? ServiceResultStatus.Ok : ServiceResultStatus.Created, concurrentStatus);
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        var state = await StateAsync();
        var active = state.Reservations.Where(r => r.Status == "Active" && r.Source == "DirectTrade").Sum(r => r.Quantity);
        Assert.True(active <= 100m);
        Assert.Equal(concurrent switch { "allocate" => 90m, "cancel" => 0m, "reserve" => 60m, _ => 0m }, active);
    }

    [Fact]
    public async Task Two_Concurrent_Identical_Retries_Create_One_Allocation()
    {
        await ArrangeAsync();
        var requestId = Guid.NewGuid();
        ServiceResultStatus concurrentStatus = default;
        var interceptor = new ConcurrentSaveInterceptor(async () => concurrentStatus = (await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 10m, requestId: requestId))).Status);

        var result = await AllocateAsync(Allocate(_seed.DirectTradeSourceLine, 10m, requestId: requestId), interceptors: interceptor);

        Assert.Equal(ServiceResultStatus.Created, concurrentStatus);
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var state = await StateAsync();
        Assert.Single(state.Reservations);
        Assert.Single(state.OrderEvents.Where(e => e.EventType == "DirectTradeAllocated"));
        Assert.Equal(10m, BoxLine(await OrderAsync()).AllocatedQuantity);
    }

    // ======================== Vrijgeven en annuleren ========================

    [Fact]
    public async Task Release_Returns_Capacity_Writes_An_Order_Event_And_Is_Idempotent()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(10m);
        var before = await StateAsync();
        var requestId = Guid.NewGuid();

        var released = await ReleaseAsync(allocation.Id, allocation.Version, requestId);

        Assert.Equal(ServiceResultStatus.Ok, released.Status);
        Assert.Equal(("Released", "Klant wil later", 1), (released.Value.Status, released.Value.ReleaseReason, released.Value.Version));
        Assert.NotNull(released.Value.ReleasedAt);
        Assert.Equal(100m, Assert.Single(await SupplyAsync()).RemainingQuantity);
        var line = BoxLine(await OrderAsync());
        Assert.Equal((0m, 40m), (line.AllocatedQuantity, line.OpenQuantity));
        var after = await StateAsync();
        Assert.Equal(before.PalletEvents, after.PalletEvents);
        Assert.Equal(before.SourceVersion + 1, after.SourceVersion);
        var releaseEvent = Assert.Single(after.OrderEvents.Where(e => e.EventType == "DirectTradeReleased"));
        Assert.Equal((requestId, "office-1", "Klant wil later"), (releaseEvent.RequestId, releaseEvent.ActorReference, releaseEvent.Reason));
        Assert.Equal(0, Snapshot(releaseEvent.OldValue).GetProperty("version").GetInt32());

        // Replay: dezelfde vrijgave nogmaals geeft de huidige toestand; een andere reden of een tweede vrijgave is een conflict.
        Assert.Equal(ServiceResultStatus.Ok, (await ReleaseAsync(allocation.Id, allocation.Version, requestId)).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await ReleaseAsync(allocation.Id, allocation.Version, requestId, reason: "anders")).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await ReleaseAsync(allocation.Id, 1)).Status);
        Assert.Equal(after.OrderEvents.Count, (await StateAsync()).OrderEvents.Count);
    }

    [Fact]
    public async Task Stale_Version_Release_Is_Conflict_Without_Change()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(10m);
        var result = await ReleaseAsync(allocation.Id, allocation.Version + 1);
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal("Active", Assert.Single((await StateAsync()).Reservations).Status);
    }

    [Fact]
    public async Task Cancel_Releases_Physical_Reservations_And_Allocations_With_Separate_Audit()
    {
        await ArrangeAsync();
        var physical = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var allocation = await AllocatedAsync(15m);
        var palletEventsBefore = (await StateAsync()).PalletEvents;

        var cancelled = await CancelAsync((await OrderAsync()).Version);

        Assert.Equal(ServiceResultStatus.Ok, cancelled.Status);
        var state = await StateAsync();
        Assert.All(state.Reservations, r => Assert.Equal("Released", r.Status));
        Assert.Equal(palletEventsBefore + 1, state.PalletEvents);
        var allocationRelease = Assert.Single(state.OrderEvents.Where(e => e.EventType == "DirectTradeReleased"));
        Assert.Null(allocationRelease.RequestId);
        var releaseSnapshot = Snapshot(allocationRelease.NewValue);
        Assert.Equal((allocation.Id, "SalesOrderCancelled", 15m), (releaseSnapshot.GetProperty("reservationId").GetInt32(), releaseSnapshot.GetProperty("cause").GetString(), releaseSnapshot.GetProperty("quantity").GetDecimal()));
        var orderSnapshot = Snapshot(state.OrderEvents.Single(e => e.EventType == "SalesOrderCancelled").NewValue);
        Assert.Equal((1, 1, 10m, 15m), (orderSnapshot.GetProperty("releasedReservationCount").GetInt32(), orderSnapshot.GetProperty("releasedAllocationCount").GetInt32(),
            orderSnapshot.GetProperty("releasedQuantityByUnit").GetProperty("box").GetDecimal(), orderSnapshot.GetProperty("releasedAllocationQuantityByUnit").GetProperty("box").GetDecimal()));
        Assert.Equal(100m, Assert.Single(await SupplyAsync()).RemainingQuantity);
        Assert.Equal(2, state.SourceVersion);
        Assert.Equal(physical.Id, state.Reservations.Single(r => r.Source == "Physical").Id);
    }

    // ======================== Scheiding van de magazijnflow ========================

    [Fact]
    public async Task Dispatch_Refuses_An_Allocation_And_Dispatching_Physical_Stock_Never_Fulfils_Allocated_Demand()
    {
        // Rekenvoorbeeld van de smoke: order 40; toewijzen 10 + 20; 10 vrijgeven -> toegewezen 20, open 20;
        // 10 fysiek reserveren -> toegewezen 20, gereserveerd 10, open 10; nog 10 -> open 0 maar niet magazijngereed;
        // 20 fysiek verzenden -> toegewezen 20, verzonden 20, open 0, order NIET vervuld.
        await ArrangeAsync();
        var a1 = await AllocatedAsync(10m);
        await AllocatedAsync(20m);
        Assert.Equal(ServiceResultStatus.Ok, (await ReleaseAsync(a1.Id, a1.Version)).Status);
        var line = BoxLine(await OrderAsync());
        Assert.Equal((20m, 20m), (line.AllocatedQuantity, line.OpenQuantity));

        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        line = BoxLine(await OrderAsync());
        Assert.Equal((20m, 10m, 10m), (line.AllocatedQuantity, line.ReservedQuantity, line.OpenQuantity));
        var r2 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        line = BoxLine(await OrderAsync());
        Assert.Equal((20m, 20m, 0m, false), (line.AllocatedQuantity, line.ReservedQuantity, line.OpenQuantity, line.Fulfilled));

        var allocation = line.Reservations.Single(r => r.Source == "DirectTrade" && r.Status == "Active");
        var order = await OrderAsync();
        var refused = await DispatchAsync(order.Version, (allocation.Id, allocation.Version));
        Assert.Equal(ServiceResultStatus.Conflict, refused.Status);
        Assert.Contains("DirectTrade allocation", refused.Message);

        var dispatched = await DispatchAsync(order.Version, (r1.Id, r1.Version), (r2.Id, r2.Version));
        Assert.Equal(ServiceResultStatus.Created, dispatched.Status);
        order = await OrderAsync();
        line = BoxLine(order);
        Assert.Equal((20m, 0m, 20m, 0m, false, false), (line.AllocatedQuantity, line.ReservedQuantity, line.DispatchedQuantity, line.OpenQuantity, line.Fulfilled, order.Fulfilled));
        Assert.Equal("Active", line.Reservations.Single(r => r.Id == allocation.Id).Status);
        Assert.Equal(80m, Assert.Single(await SupplyAsync()).RemainingQuantity);
    }
}
