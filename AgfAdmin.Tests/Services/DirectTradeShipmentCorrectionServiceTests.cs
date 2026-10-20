using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static AgfAdmin.Tests.Infrastructure.DirectTradeTestFlow;

namespace AgfAdmin.Tests.Services;

// Terugnemen van een volledige DirectTrade-verzending die niet heeft plaatsgevonden: oorspronkelijke verzending ongewijzigd, vraag en
// broncapaciteit komen precies één keer vrij, een retry neemt niets dubbel terug, scheiding van de magazijncorrectie, races en lezers.
public class DirectTradeShipmentCorrectionServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderA;
    private int _lineA;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync(decimal demandA = 40m)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderA, _lineA, _) = await SalesTestData.SeedOrderAsync(_factory, _seed, demandA, 10m);
    }

    private async Task<(int OrderId, int LineId)> OrderAsync(decimal quantity, int? customerId = null)
    {
        await using var db = _factory.CreateContext();
        var order = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = customerId ?? _seed.CustomerId, Status = "Open", RequestedDeliveryDate = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), ActorReference = "office-1" };
        var line = new SalesOrderLine { SalesOrder = order, ProductVariantId = _seed.AvocadoId, Quantity = quantity, Unit = "box" };
        db.AddRange(order, line);
        await db.SaveChangesAsync();
        return (order.Id, line.Id);
    }

    private async Task<ServiceResult<DirectTradeShipmentCorrectionDto>> CorrectAsync(int orderId, int shipmentId, CorrectDirectTradeShipmentDto request, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new DirectTradeShipmentCorrectionService(db).CorrectAsync(orderId, shipmentId, request);
    }

    private async Task<ServiceResult<StockReservationDto>> TryAllocateAsync(int orderId, int lineId, decimal quantity, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new DirectTradeAllocationService(db).AllocateAsync(orderId, lineId, new CreateDirectTradeAllocationDto
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLineId = _seed.DirectTradeSourceLine, Quantity = quantity, Unit = "box", AcknowledgeExpectedSupply = true, ActorReference = "office-1"
        });
    }

    private async Task<decimal> RemainingAsync()
    {
        await using var db = _factory.CreateContext();
        var commitment = (await new SupplyCommitmentService(db).GetAsync(_seed.DirectTradeCommitmentId)).Value;
        return commitment.Lines.Single(l => l.Id == _seed.DirectTradeSourceLine).DirectTradeRemainingQuantity!.Value;
    }

    private async Task<SalesOrderLineDto> LineAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(orderId)).Value.Lines.First(l => l.Unit == "box");
    }

    private async Task<SalesOrderDto> OrderDtoAsync(int orderId)
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(orderId)).Value;
    }

    private async Task<(int Corrections, int CorrectionLines, int Movements, int PalletEvents, int Contents, int WarehouseCorrections, List<SalesOrderAuditEvent> Events)> StateAsync()
    {
        await using var db = _factory.CreateContext();
        return (await db.DirectTradeShipmentCorrections.CountAsync(), await db.DirectTradeShipmentCorrectionLines.CountAsync(), await db.StockMovements.CountAsync(),
            await db.PalletAuditEvents.CountAsync(), await db.PalletContentLines.CountAsync(), await db.SalesDispatchCorrections.CountAsync(),
            await db.SalesOrderAuditEvents.AsNoTracking().Where(e => e.EventType == "DirectTradeShipmentCorrected").ToListAsync());
    }

    private async Task<string> ShipmentJsonAsync()
    {
        await using var db = _factory.CreateContext();
        var shipments = await db.DirectTradeShipments.AsNoTracking().Include(s => s.Lines).OrderBy(s => s.Id).ToListAsync();
        return JsonSerializer.Serialize(shipments.Select(s => new { s.Id, s.RequestId, s.SalesOrderId, s.CustomerId, s.CustomerNameSnapshot, s.ShippedOn, s.Reference, s.ActorReference, s.RecordedAt,
            Lines = s.Lines.OrderBy(l => l.Id).Select(l => new { l.Id, l.StockReservationId, l.SalesOrderLineId, l.SupplyCommitmentLineId, l.SupplyCommitmentId, l.VendorId, l.VendorNameSnapshot, l.OriginSnapshot,
                l.DestinationSnapshot, l.AllocatedQuantity, l.Quantity, l.Unit, l.NetWeightKg, l.LotNumber, l.SourceLotNumber, l.LotDeviationReason, l.ExpectedReservationVersion }) }));
    }

    // ======================== Balans: precies één keer vrij ========================

    [Fact]
    public async Task Taking_Back_Frees_Demand_And_Capacity_Exactly_Once_And_A_Retry_Takes_Nothing_Back_Twice()
    {
        // Bron 100 verwacht. A vraagt 40, B (zelfde klant) 90.
        await ArrangeAsync();
        var (orderB, lineB) = await OrderAsync(90m);
        var allocationA = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (allocationA, 81.4m, "LOT-DT-SRC"));
        Assert.Equal(80m, await RemainingAsync());
        await AllocateAsync(_factory, orderB, lineB, _seed.DirectTradeSourceLine, 80m);
        Assert.Equal(0m, await RemainingAsync());
        Assert.Equal(ServiceResultStatus.Conflict, (await TryAllocateAsync(orderB, lineB, 1m)).Status);
        var lineBefore = await LineAsync(_orderA);
        Assert.Equal((0m, 20m, 81.4m, 20m), (lineBefore.AllocatedQuantity, lineBefore.DispatchedQuantity, lineBefore.DispatchedNetWeightKg, lineBefore.OpenQuantity));
        var shipmentBefore = await ShipmentJsonAsync();
        var before = await StateAsync();

        // Bronrest direct vóór terugnemen: 0. Na terugnemen: 20. Retry: blijft 20.
        var request = Correction(await OrderVersionAsync(_factory, _orderA), shipment);
        var corrected = await CorrectAsync(_orderA, shipment.Id, request);

        Assert.Equal(ServiceResultStatus.Created, corrected.Status);
        Assert.Equal(20m, await RemainingAsync());
        var lineAfter = await LineAsync(_orderA);
        Assert.Equal((0m, 0m, 0m, 0m, 40m, false), (lineAfter.AllocatedQuantity, lineAfter.DispatchedQuantity, lineAfter.DirectTradeShippedQuantity, lineAfter.DispatchedNetWeightKg, lineAfter.OpenQuantity, lineAfter.Fulfilled));

        var retry = await CorrectAsync(_orderA, shipment.Id, request);
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(corrected.Value.Id, retry.Value.Id);
        Assert.Equal(20m, await RemainingAsync());
        Assert.Equal(40m, (await LineAsync(_orderA)).OpenQuantity);

        var second = await CorrectAsync(_orderA, shipment.Id, Correction(await OrderVersionAsync(_factory, _orderA), shipment));
        Assert.Equal(ServiceResultStatus.Conflict, second.Status);
        Assert.Equal(20m, await RemainingAsync());

        // De vrijgekomen capaciteit is precies 20 en opnieuw toewijsbaar onder de bestaande voorwaarden.
        Assert.Equal(ServiceResultStatus.Created, (await TryAllocateAsync(orderB, lineB, 10m)).Status);
        Assert.Equal(ServiceResultStatus.Created, (await TryAllocateAsync(_orderA, _lineA, 10m)).Status);
        Assert.Equal(0m, await RemainingAsync());
        Assert.Equal(ServiceResultStatus.Conflict, (await TryAllocateAsync(orderB, lineB, 1m)).Status);

        var after = await StateAsync();
        Assert.Equal((before.Corrections + 1, before.CorrectionLines + 1, before.Movements, before.PalletEvents, before.Contents, before.WarehouseCorrections),
            (after.Corrections, after.CorrectionLines, after.Movements, after.PalletEvents, after.Contents, after.WarehouseCorrections));
        Assert.Equal(shipmentBefore, await ShipmentJsonAsync());
        Assert.Single(after.Events);
    }

    [Fact]
    public async Task The_Correction_Records_Statement_Code_And_Reason_Keeps_History_And_Reverses_Readers()
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));

        var result = await CorrectAsync(_orderA, shipment.Id, Correction(await OrderVersionAsync(_factory, _orderA), shipment, reason: "  Vervoerder heeft niet geladen  "));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var dto = result.Value;
        Assert.Equal((shipment.Id, _orderA, "Vervoerder heeft niet geladen", "office-1", true, "SHIPMENT_DID_NOT_TAKE_PLACE", "Deze geregistreerde verzending heeft niet plaatsgevonden."),
            (dto.DirectTradeShipmentId, dto.SalesOrderId, dto.Reason, dto.ActorReference, dto.ShipmentDidNotTakePlaceConfirmed, dto.ConfirmationCode, dto.ConfirmationStatement));
        var line = Assert.Single(dto.Lines);
        Assert.Equal((shipment.Lines[0].Id, a.Id, 20m, "box", 81.4m, _seed.DirectTradeSourceLine, _lineA), (line.DirectTradeShipmentLineId, line.StockReservationId, line.Quantity, line.Unit, line.NetWeightKg, line.SupplyCommitmentLineId, line.SalesOrderLineId));

        var order = await OrderDtoAsync(_orderA);
        var reservation = order.Lines.Single(l => l.Id == _lineA).Reservations.Single(r => r.Id == a.Id);
        Assert.Equal(("Corrected", dto.Id, shipment.Id, new DateOnly(2026, 10, 19), 81.4m), (reservation.Status, reservation.DirectTradeCorrectionId!.Value, reservation.DirectTradeShipmentId!.Value, reservation.ShippedOn!.Value, reservation.ShippedNetWeightKg!.Value));
        Assert.NotNull(reservation.ConsumedAt);
        Assert.NotNull(reservation.CorrectedAt);
        Assert.Null(reservation.CorrectionId);

        await using var db = _factory.CreateContext();
        var history = (await new DirectTradeShipmentService(db, new FixedBusinessCalendar(Today)).ListAsync(_orderA)).Value;
        var listed = Assert.Single(history);
        Assert.Equal(dto.Id, listed.Correction!.Id);
        Assert.Equal(20m, listed.Lines.Single().Quantity);
        var audit = Assert.Single((await StateAsync()).Events);
        var snapshot = JsonDocument.Parse(audit.NewValue).RootElement;
        Assert.Equal((1, 1, 20m, 81.4m, "SHIPMENT_DID_NOT_TAKE_PLACE", true), (snapshot.GetProperty("correctedAllocationCount").GetInt32(), snapshot.GetProperty("sourceLineCount").GetInt32(),
            snapshot.GetProperty("freedQuantityByUnit").GetProperty("box").GetDecimal(), snapshot.GetProperty("freedNetWeightKg").GetDecimal(), snapshot.GetProperty("confirmationCode").GetString(),
            snapshot.GetProperty("shipmentDidNotTakePlaceConfirmedByUser").GetBoolean()));
        Assert.False(snapshot.TryGetProperty("capacityFreedBySourceLine", out _));
    }

    [Fact]
    public async Task After_A_Full_Correction_The_Order_Can_Be_Cancelled_And_Corrected_History_Stays()
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var other = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 5m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        await using (var db = _factory.CreateContext())
        {
            var blocked = await new SalesOrderService(db).CancelAsync(_orderA, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = await OrderVersionAsync(_factory, _orderA), ActorReference = "office-1", Reason = "x" });
            Assert.Equal(ServiceResultStatus.Conflict, blocked.Status);
        }
        Assert.Equal(ServiceResultStatus.Created, (await CorrectAsync(_orderA, shipment.Id, Correction(await OrderVersionAsync(_factory, _orderA), shipment))).Status);

        await using (var db = _factory.CreateContext())
        {
            var cancelled = await new SalesOrderService(db).CancelAsync(_orderA, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = await OrderVersionAsync(_factory, _orderA), ActorReference = "office-1", Reason = "Klant zegt af" });
            Assert.Equal(ServiceResultStatus.Ok, cancelled.Status);
            var reservations = cancelled.Value.Lines.Single(l => l.Id == _lineA).Reservations;
            Assert.Equal("Corrected", reservations.Single(r => r.Id == a.Id).Status);
            Assert.Equal("Released", reservations.Single(r => r.Id == other.Id).Status);
            Assert.Single((await new DirectTradeShipmentService(db, new FixedBusinessCalendar(Today)).ListAsync(_orderA)).Value.Where(s => s.Correction != null));
        }
        Assert.Equal(100m, await RemainingAsync());
    }

    [Fact]
    public async Task Correction_Is_Allowed_Whatever_The_Commitment_Status_But_Reallocation_Follows_The_Existing_Rules()
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        await using (var db = _factory.CreateContext())
        {
            (await db.SupplyCommitments.FindAsync(_seed.DirectTradeCommitmentId))!.Status = "Received";
            await db.SaveChangesAsync();
        }

        Assert.Equal(ServiceResultStatus.Created, (await CorrectAsync(_orderA, shipment.Id, Correction(await OrderVersionAsync(_factory, _orderA), shipment))).Status);
        Assert.Equal(100m, await RemainingAsync());
        var refused = await TryAllocateAsync(_orderA, _lineA, 10m);
        Assert.Equal(ServiceResultStatus.Conflict, refused.Status);
        Assert.Contains("only Expected or PartiallyReceived", refused.Message);
    }

    [Fact]
    public async Task Warehouse_Availability_And_Reservable_Stock_Are_Unchanged()
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        string overview, reservable;
        await using (var db = _factory.CreateContext())
        {
            overview = JsonSerializer.Serialize((await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = _seed.AvocadoId })).Value.Variants);
            reservable = JsonSerializer.Serialize((await new SalesOrderService(db).GetReservableStockAsync(_orderA, _lineA)).Value);
        }
        Assert.Equal(ServiceResultStatus.Created, (await CorrectAsync(_orderA, shipment.Id, Correction(await OrderVersionAsync(_factory, _orderA), shipment))).Status);
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(overview, JsonSerializer.Serialize((await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = _seed.AvocadoId })).Value.Variants));
            Assert.Equal(reservable, JsonSerializer.Serialize((await new SalesOrderService(db).GetReservableStockAsync(_orderA, _lineA)).Value));
            Assert.Empty(await db.StockReceipts.ToListAsync());
        }
    }

    // ======================== Begrensde audit bij de maximale regelset ========================

    [Fact]
    public async Task The_Maximum_Line_Set_Corrects_Atomically_With_A_Bounded_Audit_And_One_More_Line_Is_Rejected()
    {
        await ArrangeAsync(demandA: 200m);
        var max = ReservationRules.MaxDispatchLines;
        var allocations = new List<int>();
        await using (var db = _factory.CreateContext())
        {
            // Elke toewijzing op een eigen bronregel: het aantal verschillende bronregels is maximaal.
            var sources = Enumerable.Range(0, max).Select(_ => new SupplyCommitmentLine { SupplyCommitmentId = _seed.DirectTradeCommitmentId, ProductVariantId = _seed.AvocadoId, ExpectedQuantity = 1m, ExpectedUnit = "box" }).ToList();
            db.SupplyCommitmentLines.AddRange(sources);
            await db.SaveChangesAsync();
            var reservations = sources.Select(source => new StockReservation
            {
                RequestId = Guid.NewGuid(), SalesOrderLineId = _lineA, SupplyCommitmentLineId = source.Id, Quantity = 1m, Unit = "box", Source = "DirectTrade", Status = "Active"
            }).ToList();
            db.StockReservations.AddRange(reservations);
            await db.SaveChangesAsync();
            allocations.AddRange(reservations.Select(r => r.Id));
        }
        DirectTradeShipmentDto shipment;
        await using (var db = _factory.CreateContext())
        {
            var shipped = await new DirectTradeShipmentService(db, new FixedBusinessCalendar(Today)).ShipAsync(_orderA, new CreateDirectTradeShipmentDto
            {
                RequestId = Guid.NewGuid(), ExpectedOrderVersion = await OrderVersionAsync(_factory, _orderA), ActorReference = "office-1", ShippedOn = Today,
                Lines = allocations.Select(id => new CreateDirectTradeShipmentLineDto { StockReservationId = id, ExpectedReservationVersion = 0, Quantity = 1m, Unit = "box", NetWeightKg = 4.125m, LotNumber = "LOT-BULK" }).ToList()
            });
            Assert.Equal(ServiceResultStatus.Created, shipped.Status);
            shipment = shipped.Value;
        }

        var tooMany = Correction(await OrderVersionAsync(_factory, _orderA), shipment);
        tooMany.Lines.Add(new CreateSalesDispatchLineDto { StockReservationId = 999_999, ExpectedReservationVersion = 1 });
        var rejected = await CorrectAsync(_orderA, shipment.Id, tooMany);
        Assert.Equal(ServiceResultStatus.Validation, rejected.Status);
        Assert.Contains("lines", rejected.Errors.Keys);

        var result = await CorrectAsync(_orderA, shipment.Id, Correction(await OrderVersionAsync(_factory, _orderA), shipment));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(max, result.Value.Lines.Count);
        var audit = Assert.Single((await StateAsync()).Events);
        Assert.True(audit.NewValue.Length < 2000, $"audit snapshot is {audit.NewValue.Length} characters");
        var snapshot = JsonDocument.Parse(audit.NewValue).RootElement;
        Assert.Equal((max, max, (decimal)max, max * 4.125m), (snapshot.GetProperty("correctedAllocationCount").GetInt32(), snapshot.GetProperty("sourceLineCount").GetInt32(),
            snapshot.GetProperty("freedQuantityByUnit").GetProperty("box").GetDecimal(), snapshot.GetProperty("freedNetWeightKg").GetDecimal()));
        var line = await LineAsync(_orderA);
        Assert.Equal((0m, 200m), (line.DispatchedQuantity, line.OpenQuantity));
    }

    // ======================== Invoer (400), 404 en regels (409) ========================

    [Theory]
    [InlineData("no-request-id", "requestId")]
    [InlineData("no-order-version", "expectedOrderVersion")]
    [InlineData("no-actor", "actorReference")]
    [InlineData("no-reason", "reason")]
    [InlineData("reason-too-long", "reason")]
    [InlineData("not-confirmed", "shipmentDidNotTakePlace")]
    [InlineData("confirmation-missing", "shipmentDidNotTakePlace")]
    [InlineData("no-lines", "lines")]
    [InlineData("duplicate", "lines[1]")]
    [InlineData("no-version", "lines[0].expectedReservationVersion")]
    public async Task Invalid_Input_Is_Rejected_And_Changes_Nothing(string invalidCase, string key)
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        var request = Correction(await OrderVersionAsync(_factory, _orderA), shipment);
        switch (invalidCase)
        {
            case "no-request-id": request.RequestId = Guid.Empty; break;
            case "no-order-version": request.ExpectedOrderVersion = null; break;
            case "no-actor": request.ActorReference = " "; break;
            case "no-reason": request.Reason = "  "; break;
            case "reason-too-long": request.Reason = new string('r', 501); break;
            case "not-confirmed": request.ShipmentDidNotTakePlace = false; break;
            case "confirmation-missing": request.ShipmentDidNotTakePlace = null; break;
            case "no-lines": request.Lines = new List<CreateSalesDispatchLineDto>(); break;
            case "duplicate": request.Lines.Add(request.Lines[0]); break;
            case "no-version": request.Lines[0].ExpectedReservationVersion = null; break;
        }

        var result = await CorrectAsync(_orderA, shipment.Id, request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        Assert.Equal(0, (await StateAsync()).Corrections);
    }

    [Theory]
    [InlineData("stale-order", "is at version")]
    [InlineData("stale-reservation", "is at version")]
    [InlineData("incomplete", "whole DirectTrade shipment")]
    [InlineData("extra", "whole DirectTrade shipment")]
    [InlineData("other-shipment-allocation", "whole DirectTrade shipment")]
    public async Task Business_Rule_Violations_Are_Conflict_Without_Change(string scenario, string message)
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var b = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 5m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"), (b, 20.2m, "LOT-DT-SRC"));
        var version = await OrderVersionAsync(_factory, _orderA);
        var request = Correction(version, shipment);
        switch (scenario)
        {
            case "stale-order": request.ExpectedOrderVersion = version + 1; break;
            case "stale-reservation": request.Lines[0].ExpectedReservationVersion = 0; break;
            case "incomplete": request.Lines.RemoveAt(1); break;
            case "extra":
                var c = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 1m);
                request = Correction(await OrderVersionAsync(_factory, _orderA), shipment);
                request.Lines.Add(new CreateSalesDispatchLineDto { StockReservationId = c.Id, ExpectedReservationVersion = 0 });
                break;
            case "other-shipment-allocation":
                var d = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 1m);
                await ShipAsync(_factory, _orderA, (d, 4m, "LOT-DT-SRC"));
                request = Correction(await OrderVersionAsync(_factory, _orderA), shipment);
                request.Lines[1] = new CreateSalesDispatchLineDto { StockReservationId = d.Id, ExpectedReservationVersion = 1 };
                break;
        }

        var result = await CorrectAsync(_orderA, shipment.Id, request);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains(message, result.Message);
        Assert.Equal(0, (await StateAsync()).Corrections);
    }

    [Fact]
    public async Task Unknown_Shipment_Or_A_Shipment_Of_Another_Order_Is_NotFound()
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        var (orderB, _) = await OrderAsync(10m);
        var request = Correction(await OrderVersionAsync(_factory, _orderA), shipment);
        Assert.Equal(ServiceResultStatus.NotFound, (await CorrectAsync(_orderA, 999_999, request)).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await CorrectAsync(orderB, shipment.Id, request)).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await CorrectAsync(_orderA, shipment.Id, null)).Status);
    }

    [Fact]
    public async Task Exact_Retry_Returns_The_Same_Correction_And_Any_Changed_Field_Is_Conflict()
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        var version = await OrderVersionAsync(_factory, _orderA);
        var request = Correction(version, shipment);
        Assert.Equal(ServiceResultStatus.Created, (await CorrectAsync(_orderA, shipment.Id, request)).Status);

        CorrectDirectTradeShipmentDto Changed(Action<CorrectDirectTradeShipmentDto> change)
        {
            var copy = Correction(version, shipment, request.RequestId);
            change(copy);
            return copy;
        }
        foreach (var changed in new[]
                 {
                     Changed(r => r.Reason = "andere reden"),
                     Changed(r => r.ActorReference = "office-2"),
                     Changed(r => r.ExpectedOrderVersion = version + 3),
                     Changed(r => r.Lines[0].ExpectedReservationVersion = 7),
                 })
        {
            Assert.Equal(ServiceResultStatus.Conflict, (await CorrectAsync(_orderA, shipment.Id, changed)).Status);
        }
        Assert.Equal(ServiceResultStatus.Ok, (await CorrectAsync(_orderA, shipment.Id, Correction(version, shipment, request.RequestId))).Status);
        Assert.Equal(1, (await StateAsync()).Corrections);
        Assert.Equal(100m, await RemainingAsync());
    }

    // ======================== Scheiding van de magazijncorrectie (zelfde numerieke id's) ========================

    [Fact]
    public async Task Warehouse_Reservations_And_Dispatch_Lines_Can_Never_Enter_A_DirectTrade_Correction_Even_With_Equal_Ids()
    {
        await ArrangeAsync();
        StockReservationDto physical;
        await using (var db = _factory.CreateContext())
        {
            physical = (await new StockReservationService(db).ReserveAsync(_orderA, _lineA, new CreateStockReservationDto
            {
                RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 10m, Unit = "box", NetWeightKg = 40m, ActorReference = "office-1"
            })).Value;
        }
        SalesDispatchDto dispatch;
        await using (var db = _factory.CreateContext())
        {
            dispatch = (await new SalesDispatchService(db).DispatchAsync(_orderA, new CreateSalesDispatchDto
            {
                RequestId = Guid.NewGuid(), ExpectedOrderVersion = await OrderVersionAsync(_factory, _orderA), ActorReference = "office-1",
                Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = physical.Id, ExpectedReservationVersion = physical.Version } }
            })).Value;
        }
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        // Beide reeksen beginnen op 1: hetzelfde nummer bestaat in beide tabellen.
        Assert.Equal(dispatch.Id, shipment.Id);
        Assert.Equal(dispatch.Lines.Single().Id, shipment.Lines.Single().Id);
        var version = await OrderVersionAsync(_factory, _orderA);

        // De magazijnreservering (naast of in plaats van de toewijzing) hoort nooit bij een DirectTrade-correctie.
        var withPhysical = Correction(version, shipment);
        withPhysical.Lines.Add(new CreateSalesDispatchLineDto { StockReservationId = physical.Id, ExpectedReservationVersion = 1 });
        Assert.Equal(ServiceResultStatus.Conflict, (await CorrectAsync(_orderA, shipment.Id, withPhysical)).Status);
        var onlyPhysical = Correction(version, shipment);
        onlyPhysical.Lines[0] = new CreateSalesDispatchLineDto { StockReservationId = physical.Id, ExpectedReservationVersion = 1 };
        Assert.Equal(ServiceResultStatus.Conflict, (await CorrectAsync(_orderA, shipment.Id, onlyPhysical)).Status);
        // En omgekeerd: de magazijncorrectie op hetzelfde nummer met de DirectTrade-toewijzing weigert ook.
        await using (var db = _factory.CreateContext())
        {
            var warehouse = await new SalesDispatchCorrectionService(db).CorrectAsync(_orderA, dispatch.Id, new CorrectSalesDispatchDto
            {
                RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = "x", GoodsNeverLeftWarehouse = true,
                Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = a.Id, ExpectedReservationVersion = 1 } }
            });
            Assert.Equal(ServiceResultStatus.Conflict, warehouse.Status);
        }
        Assert.Equal((0, 0), ((await StateAsync()).Corrections, (await StateAsync()).WarehouseCorrections));

        // De juiste DirectTrade-correctie op hetzelfde nummer raakt de magazijnverzending niet.
        var movementsBefore = (await StateAsync()).Movements;
        Assert.Equal(ServiceResultStatus.Created, (await CorrectAsync(_orderA, shipment.Id, Correction(version, shipment))).Status);
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal("Consumed", (await db.StockReservations.AsNoTracking().SingleAsync(r => r.Id == physical.Id)).Status);
            Assert.Equal(10m, (await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == _seed.ApprovedBox)).DispatchedQuantity);
            Assert.Empty(await db.SalesDispatchCorrections.ToListAsync());
        }
        var state = await StateAsync();
        Assert.Equal((movementsBefore, 0), (state.Movements, state.WarehouseCorrections));
        Assert.Equal((10m, 0m, 30m), ((await LineAsync(_orderA)).DispatchedQuantity, (await LineAsync(_orderA)).DirectTradeShippedQuantity, (await LineAsync(_orderA)).OpenQuantity));
    }

    // ======================== Races ========================

    [Theory]
    [InlineData("second-correction")]
    [InlineData("allocate-same-line")]
    public async Task Concurrent_Writers_On_The_Same_Order_Have_Exactly_One_Winner(string concurrent)
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        var version = await OrderVersionAsync(_factory, _orderA);
        ServiceResultStatus concurrentStatus = default;
        var interceptor = new ConcurrentSaveInterceptor(async () =>
        {
            concurrentStatus = concurrent == "second-correction"
                ? (await CorrectAsync(_orderA, shipment.Id, Correction(version, shipment))).Status
                : (await TryAllocateAsync(_orderA, _lineA, 5m)).Status;
        });

        var result = await CorrectAsync(_orderA, shipment.Id, Correction(version, shipment), interceptor);

        Assert.Equal(ServiceResultStatus.Created, concurrentStatus);
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal(concurrent == "second-correction" ? 1 : 0, (await StateAsync()).Corrections);
        Assert.Equal(concurrent == "second-correction" ? 100m : 75m, await RemainingAsync());
    }

    [Fact]
    public async Task Two_Concurrent_Identical_Retries_Create_One_Correction()
    {
        await ArrangeAsync();
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        var request = Correction(await OrderVersionAsync(_factory, _orderA), shipment);
        ServiceResultStatus concurrentStatus = default;
        var interceptor = new ConcurrentSaveInterceptor(async () => concurrentStatus = (await CorrectAsync(_orderA, shipment.Id, request)).Status);

        var result = await CorrectAsync(_orderA, shipment.Id, request, interceptor);

        Assert.Equal(ServiceResultStatus.Created, concurrentStatus);
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var state = await StateAsync();
        Assert.Equal((1, 1, 1), (state.Corrections, state.CorrectionLines, state.Events.Count));
        Assert.Equal(100m, await RemainingAsync());
    }

    [Theory]
    [InlineData("allocate-other-order")]
    [InlineData("release-other-allocation")]
    [InlineData("cancel")]
    public async Task Independent_Or_Refused_Writers_Leave_The_Correction_Valid(string concurrent)
    {
        await ArrangeAsync();
        var (orderB, lineB) = await OrderAsync(90m);
        var a = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 20m);
        var other = await AllocateAsync(_factory, _orderA, _lineA, _seed.DirectTradeSourceLine, 5m);
        var shipment = await ShipAsync(_factory, _orderA, (a, 81.4m, "LOT-DT-SRC"));
        var version = await OrderVersionAsync(_factory, _orderA);
        ServiceResultStatus concurrentStatus = default;
        var interceptor = new ConcurrentSaveInterceptor(async () =>
        {
            await using var db = _factory.CreateContext();
            concurrentStatus = concurrent switch
            {
                // Gebaseerd op de oude capaciteit (rest 75): de toewijzing elders kan nooit meer dan dat nemen.
                "allocate-other-order" => (await TryAllocateAsync(orderB, lineB, 75m)).Status,
                "release-other-allocation" => (await new StockReservationService(db).ReleaseAsync(other.Id, new ReleaseStockReservationDto { RequestId = Guid.NewGuid(), ExpectedReservationVersion = other.Version, ActorReference = "office-1", Reason = "x" })).Status,
                _ => (await new SalesOrderService(db).CancelAsync(_orderA, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = "x" })).Status
            };
        });

        var result = await CorrectAsync(_orderA, shipment.Id, Correction(version, shipment), interceptor);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(concurrent switch { "allocate-other-order" => ServiceResultStatus.Created, "release-other-allocation" => ServiceResultStatus.Ok, _ => ServiceResultStatus.Conflict }, concurrentStatus);
        Assert.Equal(concurrent switch { "allocate-other-order" => 20m, "release-other-allocation" => 100m, _ => 95m }, await RemainingAsync());
    }
}
