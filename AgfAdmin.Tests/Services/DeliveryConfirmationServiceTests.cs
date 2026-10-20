using AgfAdmin.Data;
using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static AgfAdmin.Tests.Infrastructure.DirectTradeTestFlow;

namespace AgfAdmin.Tests.Services;

// Afleverbevestiging voor magazijn- en DirectTrade-verzendingen: niets aan voorraad of capaciteit verandert, aflevering per regel en
// eenheid, uitsluiting met correcties (ook gelijktijdig), gelijke id's in beide reeksen, datumgrenzen in Europe/Amsterdam, idempotentie.
public class DeliveryConfirmationServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;
    private int _colliLine;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync(decimal box = 40m, decimal colli = 10m)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, box, colli);
    }

    private async Task<StockReservationDto> ReserveAsync(int contentLineId, decimal quantity, decimal kg, int? lineId = null, string unit = "box", params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        var result = await new StockReservationService(db).ReserveAsync(_orderId, lineId ?? _boxLine, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = contentLineId, Quantity = quantity, Unit = unit, NetWeightKg = kg, ActorReference = "office-1"
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    // Magazijnverzending met een vast tijdstip (19 oktober 2026, 12:00 Amsterdam-tijd), zodat de tests niet van de systeemklok afhangen.
    // Zelfde volledige graaf als de dispatch-service (Consumed, Dispatch-mutatie, verzendregel, afgeboekt), via de gedeelde testhelper.
    private async Task<SalesDispatchDto> DispatchAsync(params StockReservationDto[] reservations)
    {
        int dispatchId;
        await using (var db = _factory.CreateContext())
        {
            var dispatch = new SalesDispatch { RequestId = Guid.NewGuid(), SalesOrderId = _orderId, Reference = "VRB", ActorReference = "office-1", DispatchedAt = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc) };
            foreach (var reservation in reservations)
            {
                await SalesTestData.ConsumeInContextAsync(db, reservation.Id, into: dispatch);
            }
            db.SalesDispatches.Add(dispatch);
            await db.SaveChangesAsync();
            dispatchId = dispatch.Id;
        }
        await using var read = _factory.CreateContext();
        return (await new SalesDispatchService(read).ListAsync(_orderId)).Value.Single(d => d.Id == dispatchId);
    }

    private async Task<SalesDispatchDto> WarehouseDispatchAsync(decimal quantity = 10m, decimal kg = 40m) => await DispatchAsync(await ReserveAsync(_seed.ApprovedBox, quantity, kg));

    private async Task<DirectTradeShipmentDto> DirectTradeShipmentAsync(decimal quantity = 10m, decimal kg = 40.5m) =>
        await ShipAsync(_factory, _orderId, (await AllocateAsync(_factory, _orderId, _boxLine, _seed.DirectTradeSourceLine, quantity), kg, "LOT-DT-SRC"));

    private ConfirmDeliveryDto Confirm(int? version, DateOnly? on = null, Guid? requestId = null, string reference = "CMR-OK-1", bool? full = true, string actor = "office-1") => new()
    {
        RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = actor, DeliveredOn = on ?? Today, ReceiptReference = reference, DeliveredInFull = full
    };

    private async Task<ServiceResult<DeliveryConfirmationDto>> ConfirmWarehouseAsync(int dispatchId, ConfirmDeliveryDto request, IBusinessCalendar calendar = null, params IInterceptor[] interceptors)
    {
        var effective = calendar ?? _factory.Calendar;
        await using var db = _factory.CreateContext(effective, interceptors);
        return await new DeliveryConfirmationService(db, effective).ConfirmWarehouseAsync(_orderId, dispatchId, request);
    }

    private async Task<ServiceResult<DeliveryConfirmationDto>> ConfirmDirectTradeAsync(int shipmentId, ConfirmDeliveryDto request, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new DeliveryConfirmationService(db, _factory.Calendar).ConfirmDirectTradeAsync(_orderId, shipmentId, request);
    }

    private async Task<ServiceResult<SalesDispatchCorrectionDto>> CorrectWarehouseAsync(SalesDispatchDto dispatch, int version, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new SalesDispatchCorrectionService(db).CorrectAsync(_orderId, dispatch.Id, new CorrectSalesDispatchDto
        {
            RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = "Verkeerd gescand", GoodsNeverLeftWarehouse = true,
            Lines = dispatch.Lines.Select(l => new CreateSalesDispatchLineDto { StockReservationId = l.StockReservationId, ExpectedReservationVersion = 1 }).ToList()
        });
    }

    private async Task<ServiceResult<DirectTradeShipmentCorrectionDto>> TakeBackAsync(DirectTradeShipmentDto shipment, int version, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new DirectTradeShipmentCorrectionService(db).CorrectAsync(_orderId, shipment.Id, Correction(version, shipment));
    }

    private async Task<SalesOrderDto> OrderAsync()
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(_orderId)).Value;
    }

    // Alles wat aflevering nooit mag veranderen: voorraad, mutaties, reserveringen, capaciteit, verzonden totalen, pallets, beschikbaarheid.
    private async Task<string> StockFingerprintAsync()
    {
        await using var db = _factory.CreateContext();
        var contents = await db.PalletContentLines.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Quantity, c.DispatchedQuantity, c.DispatchedNetWeightKg, c.QualityStatus, c.Version }).ToListAsync();
        var pallets = await db.Pallets.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.LogisticsStatus, p.LocationContext, p.Version }).ToListAsync();
        var reservations = await db.StockReservations.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Status, r.Quantity, r.Version, r.SupplyCommitmentLineId }).ToListAsync();
        var movements = await db.StockMovements.CountAsync();
        var sources = await db.SupplyCommitmentLines.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Version }).ToListAsync();
        var order = await OrderAsync();
        var lines = order.Lines.Select(l => new { l.Id, l.ReservedQuantity, l.AllocatedQuantity, l.DispatchedQuantity, l.DispatchedNetWeightKg, l.OpenQuantity, l.Fulfilled });
        var availability = (await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = _seed.AvocadoId })).Value.Variants;
        var supply = (await new SupplyCommitmentService(db).GetAsync(_seed.DirectTradeCommitmentId)).Value.Lines.Select(l => new { l.Id, l.DirectTradeAllocatedQuantity, l.DirectTradeRemainingQuantity });
        return JsonSerializer.Serialize(new { contents, pallets, reservations, movements, sources, lines, availability, supply });
    }

    // ======================== Bevestigen verandert niets aan voorraad of capaciteit ========================

    [Fact]
    public async Task Confirming_A_Warehouse_Dispatch_Records_The_Delivery_And_Changes_No_Stock()
    {
        await ArrangeAsync();
        var dispatch = await WarehouseDispatchAsync();
        var before = await StockFingerprintAsync();

        var result = await ConfirmWarehouseAsync(dispatch.Id, Confirm(await OrderVersionAsync(_factory, _orderId), new DateOnly(2026, 10, 20)));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var dto = result.Value;
        Assert.Equal(("Warehouse", (int?)dispatch.Id, (int?)null, new DateOnly(2026, 10, 20), "CMR-OK-1", true, "DELIVERED_IN_FULL", "Deze verzending is volledig en zonder afwijkingen afgeleverd."),
            (dto.ShipmentKind, dto.SalesDispatchId, dto.DirectTradeShipmentId, dto.DeliveredOn, dto.ReceiptReference, dto.DeliveredInFullConfirmed, dto.ConfirmationCode, dto.ConfirmationStatement));
        Assert.Equal(DateTimeKind.Utc, dto.RecordedAt.Kind);
        Assert.Equal(before, await StockFingerprintAsync());

        var line = (await OrderAsync()).Lines.Single(l => l.Id == _boxLine);
        Assert.Equal((10m, 10m, false), (line.DispatchedQuantity, line.DeliveredQuantity, line.Delivered));
        var reservation = line.Reservations.Single(r => r.Source == "Physical");
        Assert.Equal((dto.Id, new DateOnly(2026, 10, 20)), (reservation.DeliveryConfirmationId!.Value, reservation.DeliveredOn!.Value));
        await using var db = _factory.CreateContext();
        var listed = Assert.Single((await new SalesDispatchService(db).ListAsync(_orderId)).Value);
        Assert.Equal(dto.Id, listed.Delivery!.Id);
        var audit = await db.SalesOrderAuditEvents.AsNoTracking().SingleAsync(e => e.EventType == "ShipmentDeliveryConfirmed");
        Assert.Equal(dto.RequestId, audit.RequestId);
        Assert.True(audit.NewValue.Length < 2000);
    }

    [Fact]
    public async Task Confirming_A_DirectTrade_Shipment_Records_The_Delivery_And_Changes_No_Capacity()
    {
        await ArrangeAsync();
        var shipment = await DirectTradeShipmentAsync();
        var before = await StockFingerprintAsync();

        var result = await ConfirmDirectTradeAsync(shipment.Id, Confirm(await OrderVersionAsync(_factory, _orderId), new DateOnly(2026, 10, 19), reference: null));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(("DirectTrade", (int?)null, (int?)shipment.Id, (string)null), (result.Value.ShipmentKind, result.Value.SalesDispatchId, result.Value.DirectTradeShipmentId, result.Value.ReceiptReference));
        Assert.Equal(before, await StockFingerprintAsync());
        await using var db = _factory.CreateContext();
        var listed = Assert.Single((await new DirectTradeShipmentService(db, _factory.Calendar).ListAsync(_orderId)).Value);
        Assert.True(listed.DeliveryRecorded);
        Assert.Equal(result.Value.Id, listed.Delivery!.Id);
        Assert.Equal(10m, (await OrderAsync()).Lines.Single(l => l.Id == _boxLine).DeliveredQuantity);
    }

    // ======================== Aflevering per orderregel en eenheid ========================

    [Fact]
    public async Task Delivery_Counts_Per_Line_And_Unit_Across_Partial_Warehouse_And_DirectTrade_Shipments()
    {
        await ArrangeAsync(box: 40m, colli: 10m);
        var first = await WarehouseDispatchAsync(10m, 40m);
        var second = await WarehouseDispatchAsync(15m, 60m);
        var dt = await DirectTradeShipmentAsync(15m, 60.75m);
        var colli = await DispatchAsync(await ReserveAsync(_seed.ApprovedColli, 10m, 55m, _colliLine, "colli"));
        var order = await OrderAsync();
        Assert.True(order.Fulfilled);
        Assert.False(order.Delivered);

        async Task<(decimal Box, bool BoxDelivered, decimal Colli, bool ColliDelivered, bool Order)> StateAsync()
        {
            var o = await OrderAsync();
            var box = o.Lines.Single(l => l.Id == _boxLine);
            var col = o.Lines.Single(l => l.Id == _colliLine);
            return (box.DeliveredQuantity, box.Delivered, col.DeliveredQuantity, col.Delivered, o.Delivered);
        }

        Assert.Equal(ServiceResultStatus.Created, (await ConfirmWarehouseAsync(first.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);
        Assert.Equal((10m, false, 0m, false, false), await StateAsync());
        Assert.Equal(ServiceResultStatus.Created, (await ConfirmWarehouseAsync(second.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);
        Assert.Equal((25m, false, 0m, false, false), await StateAsync());
        Assert.Equal(ServiceResultStatus.Created, (await ConfirmDirectTradeAsync(dt.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);
        // De boxregel is volledig afgeleverd; de colliregel niet: de order niet. Box en colli worden nooit opgeteld.
        Assert.Equal((40m, true, 0m, false, false), await StateAsync());
        Assert.Equal(ServiceResultStatus.Created, (await ConfirmWarehouseAsync(colli.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);
        Assert.Equal((40m, true, 10m, true, true), await StateAsync());
    }

    [Fact]
    public async Task A_Taken_Back_Or_Corrected_Shipment_Never_Counts_And_Cannot_Be_Confirmed()
    {
        await ArrangeAsync();
        var dispatch = await WarehouseDispatchAsync();
        var shipment = await DirectTradeShipmentAsync();
        Assert.Equal(ServiceResultStatus.Created, (await CorrectWarehouseAsync(dispatch, await OrderVersionAsync(_factory, _orderId))).Status);
        Assert.Equal(ServiceResultStatus.Created, (await TakeBackAsync(shipment, await OrderVersionAsync(_factory, _orderId))).Status);

        var warehouse = await ConfirmWarehouseAsync(dispatch.Id, Confirm(await OrderVersionAsync(_factory, _orderId)));
        var directTrade = await ConfirmDirectTradeAsync(shipment.Id, Confirm(await OrderVersionAsync(_factory, _orderId)));

        Assert.Equal((ServiceResultStatus.Conflict, ServiceResultStatus.Conflict), (warehouse.Status, directTrade.Status));
        Assert.Contains("can never be confirmed", warehouse.Message);
        Assert.Contains("can never be confirmed", directTrade.Message);
        Assert.Equal(0m, (await OrderAsync()).Lines.Single(l => l.Id == _boxLine).DeliveredQuantity);
    }

    [Fact]
    public async Task A_Confirmed_Delivery_Blocks_Both_Administrative_Corrections()
    {
        await ArrangeAsync();
        var dispatch = await WarehouseDispatchAsync();
        var shipment = await DirectTradeShipmentAsync();
        Assert.Equal(ServiceResultStatus.Created, (await ConfirmWarehouseAsync(dispatch.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);
        Assert.Equal(ServiceResultStatus.Created, (await ConfirmDirectTradeAsync(shipment.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);

        var warehouse = await CorrectWarehouseAsync(dispatch, await OrderVersionAsync(_factory, _orderId));
        var takeBack = await TakeBackAsync(shipment, await OrderVersionAsync(_factory, _orderId));

        Assert.Equal((ServiceResultStatus.Conflict, ServiceResultStatus.Conflict), (warehouse.Status, takeBack.Status));
        Assert.Contains("confirmed as delivered", warehouse.Message);
        Assert.Contains("confirmed as delivered", takeBack.Message);
    }

    // ======================== Gelijke id's: het endpoint bepaalt de soort ========================

    private async Task<(SalesDispatchDto Dispatch, DirectTradeShipmentDto Shipment)> EqualIdsAsync()
    {
        await ArrangeAsync();
        var dispatch = await WarehouseDispatchAsync();
        var shipment = await DirectTradeShipmentAsync();
        Assert.Equal(dispatch.Id, shipment.Id);
        return (dispatch, shipment);
    }

    [Fact]
    public async Task Confirming_Warehouse_Dispatch_1_Leaves_DirectTrade_Shipment_1_Completely_Untouched()
    {
        var (dispatch, shipment) = await EqualIdsAsync();
        Assert.Equal(ServiceResultStatus.Created, (await ConfirmWarehouseAsync(dispatch.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);

        await using (var db = _factory.CreateContext())
        {
            Assert.Null((await new DirectTradeShipmentService(db, _factory.Calendar).ListAsync(_orderId)).Value.Single().Delivery);
        }
        var line = (await OrderAsync()).Lines.Single(l => l.Id == _boxLine);
        Assert.Equal(10m, line.DeliveredQuantity);
        Assert.Null(line.Reservations.Single(r => r.Source == "DirectTrade").DeliveryConfirmationId);
        // De DirectTrade-verzending met hetzelfde nummer is nog gewoon terug te nemen.
        Assert.Equal(ServiceResultStatus.Created, (await TakeBackAsync(shipment, await OrderVersionAsync(_factory, _orderId))).Status);
    }

    [Fact]
    public async Task Confirming_DirectTrade_Shipment_1_Leaves_Warehouse_Dispatch_1_Completely_Untouched()
    {
        var (dispatch, shipment) = await EqualIdsAsync();
        Assert.Equal(ServiceResultStatus.Created, (await ConfirmDirectTradeAsync(shipment.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);

        await using (var db = _factory.CreateContext())
        {
            Assert.Null((await new SalesDispatchService(db).ListAsync(_orderId)).Value.Single().Delivery);
        }
        var line = (await OrderAsync()).Lines.Single(l => l.Id == _boxLine);
        Assert.Null(line.Reservations.Single(r => r.Source == "Physical").DeliveryConfirmationId);
        Assert.Equal(ServiceResultStatus.Created, (await CorrectWarehouseAsync(dispatch, await OrderVersionAsync(_factory, _orderId))).Status);
    }

    [Fact]
    public async Task A_Missing_Shipment_Within_The_Chosen_Kind_Is_NotFound_Even_When_The_Other_Kind_Has_That_Number()
    {
        await ArrangeAsync();
        await WarehouseDispatchAsync();
        var second = await WarehouseDispatchAsync(5m, 20m);
        var shipment = await DirectTradeShipmentAsync();
        Assert.Equal((2, 1), (second.Id, shipment.Id));
        var version = await OrderVersionAsync(_factory, _orderId);

        Assert.Equal(ServiceResultStatus.NotFound, (await ConfirmDirectTradeAsync(2, Confirm(version))).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await ConfirmWarehouseAsync(999, Confirm(version))).Status);
        await using var db = _factory.CreateContext();
        Assert.Empty(await db.ShipmentDeliveryConfirmations.ToListAsync());
    }

    // ======================== Invoer, datum en idempotentie ========================

    [Theory]
    [InlineData("no-request-id", "requestId")]
    [InlineData("no-order-version", "expectedOrderVersion")]
    [InlineData("no-actor", "actorReference")]
    [InlineData("not-full", "deliveredInFull")]
    [InlineData("full-missing", "deliveredInFull")]
    [InlineData("no-date", "deliveredOn")]
    [InlineData("future-date", "deliveredOn")]
    [InlineData("before-shipping", "deliveredOn")]
    [InlineData("reference-too-long", "receiptReference")]
    public async Task Invalid_Input_Is_Rejected_And_Changes_Nothing(string invalidCase, string key)
    {
        await ArrangeAsync();
        var shipment = await DirectTradeShipmentAsync();   // verzonden op 2026-10-19
        var request = Confirm(await OrderVersionAsync(_factory, _orderId));
        switch (invalidCase)
        {
            case "no-request-id": request.RequestId = Guid.Empty; break;
            case "no-order-version": request.ExpectedOrderVersion = null; break;
            case "no-actor": request.ActorReference = " "; break;
            case "not-full": request.DeliveredInFull = false; break;
            case "full-missing": request.DeliveredInFull = null; break;
            case "no-date": request.DeliveredOn = null; break;
            case "future-date": request.DeliveredOn = Today.AddDays(1); break;
            case "before-shipping": request.DeliveredOn = new DateOnly(2026, 10, 18); break;
            case "reference-too-long": request.ReceiptReference = new string('r', 101); break;
        }

        var result = await ConfirmDirectTradeAsync(shipment.Id, request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        await using var db = _factory.CreateContext();
        Assert.Empty(await db.ShipmentDeliveryConfirmations.ToListAsync());
    }

    // Een magazijnverzending om 00:30 Amsterdam-tijd op 21 oktober is in UTC nog 20 oktober: de verzenddatum is 21 oktober.
    [Theory]
    [InlineData("2026-10-20T21:30:00Z", "2026-10-20", true)]   // 23:30 CEST op 20 oktober
    [InlineData("2026-10-20T22:30:00Z", "2026-10-20", false)]  // 00:30 CEST op 21 oktober: 20 oktober ligt vóór de verzenddatum
    [InlineData("2026-10-20T22:30:00Z", "2026-10-21", true)]
    public async Task The_Warehouse_Shipping_Date_Is_The_Amsterdam_Calendar_Date(string dispatchedAtUtc, string deliveredOn, bool accepted)
    {
        await ArrangeAsync();
        var reservation = await ReserveAsync(_seed.ApprovedBox, 10m, 40m);
        int dispatchId;
        await using (var db = _factory.CreateContext())
        {
            var dispatch = new SalesDispatch { RequestId = Guid.NewGuid(), SalesOrderId = _orderId, ActorReference = "t", DispatchedAt = DateTime.Parse(dispatchedAtUtc).ToUniversalTime() };
            await SalesTestData.ConsumeInContextAsync(db, reservation.Id, into: dispatch);
            db.SalesDispatches.Add(dispatch);
            await db.SaveChangesAsync();
            dispatchId = dispatch.Id;
        }
        var calendar = new FixedBusinessCalendar(new DateOnly(2026, 10, 22));

        var result = await ConfirmWarehouseAsync(dispatchId, Confirm(await OrderVersionAsync(_factory, _orderId), DateOnly.Parse(deliveredOn)), calendar);

        Assert.Equal(accepted ? ServiceResultStatus.Created : ServiceResultStatus.Validation, result.Status);
    }

    [Theory]
    [InlineData("2026-10-20T21:59:59Z", "2026-10-21", false)] // 23:59:59 CEST: morgen is nog niet vandaag
    [InlineData("2026-10-20T22:00:00Z", "2026-10-21", true)]  // 00:00 CEST op 21 oktober
    [InlineData("2026-12-31T22:59:59Z", "2027-01-01", false)]
    [InlineData("2026-12-31T23:00:00Z", "2027-01-01", true)]
    public async Task Today_For_The_Delivery_Date_Is_Europe_Amsterdam_Around_Midnight(string utcNow, string deliveredOn, bool accepted)
    {
        await ArrangeAsync();
        var dispatch = await WarehouseDispatchAsync();
        var calendar = new AmsterdamBusinessCalendar(new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));

        var result = await ConfirmWarehouseAsync(dispatch.Id, Confirm(await OrderVersionAsync(_factory, _orderId), DateOnly.Parse(deliveredOn)), calendar);

        Assert.Equal(accepted ? ServiceResultStatus.Created : ServiceResultStatus.Validation, result.Status);
        if (!accepted) Assert.Contains("Europe/Amsterdam", result.Errors["deliveredOn"].Single());
    }

    [Fact]
    public async Task Exact_Retry_Returns_The_Same_Confirmation_And_Any_Changed_Field_Or_A_Second_Confirmation_Is_Conflict()
    {
        await ArrangeAsync();
        var dispatch = await WarehouseDispatchAsync();
        var version = await OrderVersionAsync(_factory, _orderId);
        var request = Confirm(version);
        var created = await ConfirmWarehouseAsync(dispatch.Id, request);
        Assert.Equal(ServiceResultStatus.Created, created.Status);

        var retry = await ConfirmWarehouseAsync(dispatch.Id, request);
        Assert.Equal((ServiceResultStatus.Ok, created.Value.Id), (retry.Status, retry.Value.Id));
        foreach (var changed in new[]
                 {
                     Confirm(version, new DateOnly(2026, 10, 19), request.RequestId),
                     Confirm(version, requestId: request.RequestId, reference: "ANDERS"),
                     Confirm(version, requestId: request.RequestId, actor: "office-2"),
                     Confirm(version + 3, requestId: request.RequestId),
                 })
        {
            Assert.Equal(ServiceResultStatus.Conflict, (await ConfirmWarehouseAsync(dispatch.Id, changed)).Status);
        }
        // Dezelfde requestId via het andere endpoint is nooit een replay.
        var shipment = await DirectTradeShipmentAsync();
        Assert.Equal(ServiceResultStatus.Conflict, (await ConfirmDirectTradeAsync(shipment.Id, Confirm(await OrderVersionAsync(_factory, _orderId), requestId: request.RequestId))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await ConfirmWarehouseAsync(dispatch.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);
        await using var db = _factory.CreateContext();
        Assert.Single(await db.ShipmentDeliveryConfirmations.ToListAsync());
    }

    // ======================== Races ========================

    [Theory]
    [InlineData("warehouse", "confirm-first")]
    [InlineData("warehouse", "correct-first")]
    [InlineData("direct-trade", "confirm-first")]
    [InlineData("direct-trade", "correct-first")]
    public async Task Confirming_Against_A_Correction_Has_Exactly_One_Winner_In_Both_Orders(string kind, string winner)
    {
        await ArrangeAsync();
        var dispatch = kind == "warehouse" ? await WarehouseDispatchAsync() : null;
        var shipment = kind == "direct-trade" ? await DirectTradeShipmentAsync() : null;
        var version = await OrderVersionAsync(_factory, _orderId);
        Task<ServiceResultStatus> Confirming(params IInterceptor[] i) => kind == "warehouse"
            ? ConfirmWarehouseAsync(dispatch!.Id, Confirm(version), null, i).ContinueWith(t => t.Result.Status)
            : ConfirmDirectTradeAsync(shipment!.Id, Confirm(version), i).ContinueWith(t => t.Result.Status);
        Task<ServiceResultStatus> Correcting(params IInterceptor[] i) => kind == "warehouse"
            ? CorrectWarehouseAsync(dispatch!, version, i).ContinueWith(t => t.Result.Status)
            : TakeBackAsync(shipment!, version, i).ContinueWith(t => t.Result.Status);

        ServiceResultStatus inner = default, outer;
        if (winner == "confirm-first")
        {
            outer = await Correcting(new ConcurrentSaveInterceptor(async () => inner = await Confirming()));
        }
        else
        {
            outer = await Confirming(new ConcurrentSaveInterceptor(async () => inner = await Correcting()));
        }

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (inner, outer));
        await using var db = _factory.CreateContext();
        var deliveries = await db.ShipmentDeliveryConfirmations.CountAsync();
        var corrections = await db.SalesDispatchCorrections.CountAsync() + await db.DirectTradeShipmentCorrections.CountAsync();
        Assert.Equal(winner == "confirm-first" ? (1, 0) : (0, 1), (deliveries, corrections));
        // Een nieuwe beslissing van de verliezer stuit op de domeinregel.
        var fresh = await OrderVersionAsync(_factory, _orderId);
        var retry = winner == "confirm-first"
            ? (kind == "warehouse" ? (await CorrectWarehouseAsync(dispatch!, fresh)).Status : (await TakeBackAsync(shipment!, fresh)).Status)
            : (kind == "warehouse" ? (await ConfirmWarehouseAsync(dispatch!.Id, Confirm(fresh))).Status : (await ConfirmDirectTradeAsync(shipment!.Id, Confirm(fresh))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, retry);
    }

    [Fact]
    public async Task Two_Confirmations_Of_The_Same_Shipment_Have_One_Winner_And_Identical_Retries_Create_One()
    {
        await ArrangeAsync();
        var dispatch = await WarehouseDispatchAsync();
        var version = await OrderVersionAsync(_factory, _orderId);
        ServiceResultStatus inner = default;
        var outer = await ConfirmWarehouseAsync(dispatch.Id, Confirm(version), null, new ConcurrentSaveInterceptor(async () => inner = (await ConfirmWarehouseAsync(dispatch.Id, Confirm(version))).Status));
        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (inner, outer.Status));

        await using (var db = _factory.CreateContext()) { Assert.Single(await db.ShipmentDeliveryConfirmations.ToListAsync()); }

        await ArrangeIdenticalAsync();
    }

    private async Task ArrangeIdenticalAsync()
    {
        var shipment = await DirectTradeShipmentAsync();
        var request = Confirm(await OrderVersionAsync(_factory, _orderId));
        ServiceResultStatus inner = default;
        var outer = await ConfirmDirectTradeAsync(shipment.Id, request, new ConcurrentSaveInterceptor(async () => inner = (await ConfirmDirectTradeAsync(shipment.Id, request)).Status));
        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Ok), (inner, outer.Status));
        await using var db = _factory.CreateContext();
        Assert.Single(await db.ShipmentDeliveryConfirmations.Where(c => c.DirectTradeShipmentId == shipment.Id).ToListAsync());
    }

    // Reserveren en toewijzen raken de orderversie niet; aflevering verandert geen vraag of voorraad: beide mogen slagen.
    [Theory]
    [InlineData("reserve")]
    [InlineData("allocate")]
    public async Task Confirming_And_Reserving_Or_Allocating_On_The_Same_Order_Both_Succeed(string concurrent)
    {
        await ArrangeAsync(box: 100m);
        var dispatch = await WarehouseDispatchAsync();
        ServiceResultStatus inner = default;
        var interceptor = new ConcurrentSaveInterceptor(async () =>
        {
            if (concurrent == "reserve")
            {
                await using var db = _factory.CreateContext();
                inner = (await new StockReservationService(db).ReserveAsync(_orderId, _boxLine, new CreateStockReservationDto
                {
                    RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 5m, Unit = "box", NetWeightKg = 20m, ActorReference = "office-1"
                })).Status;
            }
            else
            {
                await using var db = _factory.CreateContext();
                inner = (await new DirectTradeAllocationService(db).AllocateAsync(_orderId, _boxLine, new CreateDirectTradeAllocationDto
                {
                    RequestId = Guid.NewGuid(), SupplyCommitmentLineId = _seed.DirectTradeSourceLine, Quantity = 5m, Unit = "box", AcknowledgeExpectedSupply = true, ActorReference = "office-1"
                })).Status;
            }
        });

        var result = await ConfirmWarehouseAsync(dispatch.Id, Confirm(await OrderVersionAsync(_factory, _orderId)), null, interceptor);

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Created), (inner, result.Status));
        var line = (await OrderAsync()).Lines.Single(l => l.Id == _boxLine);
        Assert.Equal((10m, 5m), (line.DeliveredQuantity, concurrent == "reserve" ? line.ReservedQuantity : line.AllocatedQuantity));
    }

    // Domeinregel: een order met een geldige (Consumed) verzending kan niet worden geannuleerd, vóór en na de bevestiging.
    [Fact]
    public async Task Cancelling_An_Order_With_A_Valid_Shipment_Is_Refused_Before_And_After_Confirming()
    {
        await ArrangeAsync();
        var dispatch = await WarehouseDispatchAsync();
        async Task<ServiceResultStatus> CancelAsync()
        {
            await using var db = _factory.CreateContext();
            return (await new SalesOrderService(db).CancelAsync(_orderId, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = await OrderVersionAsync(_factory, _orderId), ActorReference = "o", Reason = "x" })).Status;
        }
        Assert.Equal(ServiceResultStatus.Conflict, await CancelAsync());
        Assert.Equal(ServiceResultStatus.Created, (await ConfirmWarehouseAsync(dispatch.Id, Confirm(await OrderVersionAsync(_factory, _orderId)))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, await CancelAsync());
    }

    // ======================== Concurrente commit ná de servicecontroles en vóór de guard ========================

    // Volledige schrijfstaat: elke rij die een bevestiging of correctie kan raken, inclusief beide audittabellen.
    private async Task<string> WriteStateAsync()
    {
        await using var db = _factory.CreateContext();
        return JsonSerializer.Serialize(new
        {
            deliveries = await db.ShipmentDeliveryConfirmations.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.ShipmentKind, c.SalesDispatchId, c.DirectTradeShipmentId }).ToListAsync(),
            warehouseCorrections = await db.SalesDispatchCorrections.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.SalesDispatchId }).ToListAsync(),
            warehouseCorrectionLines = await db.SalesDispatchCorrectionLines.CountAsync(),
            directTradeCorrections = await db.DirectTradeShipmentCorrections.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.DirectTradeShipmentId }).ToListAsync(),
            directTradeCorrectionLines = await db.DirectTradeShipmentCorrectionLines.CountAsync(),
            reservations = await db.StockReservations.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Status, r.Version }).ToListAsync(),
            contents = await db.PalletContentLines.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Quantity, c.DispatchedQuantity, c.QualityStatus, c.Version }).ToListAsync(),
            movements = await db.StockMovements.AsNoTracking().OrderBy(m => m.Id).Select(m => new { m.Id, m.Type }).ToListAsync(),
            sources = await db.SupplyCommitmentLines.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Version }).ToListAsync(),
            orders = await db.SalesOrders.AsNoTracking().OrderBy(o => o.Id).Select(o => new { o.Id, o.Status, o.Version }).ToListAsync(),
            orderLines = await db.SalesOrderLines.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Version }).ToListAsync(),
            salesAudit = await db.SalesOrderAuditEvents.AsNoTracking().OrderBy(e => e.Id).Select(e => new { e.Id, e.EventType, e.RequestId }).ToListAsync(),
            palletAudit = await db.PalletAuditEvents.AsNoTracking().OrderBy(e => e.Id).Select(e => new { e.Id, e.EventType }).ToListAsync(),
        });
    }

    private async Task<int> AuditCountAsync(string eventType)
    {
        await using var db = _factory.CreateContext();
        return await db.SalesOrderAuditEvents.CountAsync(e => e.EventType == eventType);
    }

    // De verliezer start eerst en doorloopt al zijn controles; in zijn SaveChangesAsync, vóór de guard, commit de winnaar volledig.
    // Daarna bereikt de verliezer de guard, die de opgeslagen tegenpartij ziet: 409, en niets van de verliezer belandt in de database.
    [Theory]
    [InlineData("warehouse", "correction-wins")]
    [InlineData("warehouse", "confirmation-wins")]
    [InlineData("direct-trade", "correction-wins")]
    [InlineData("direct-trade", "confirmation-wins")]
    public async Task A_Concurrent_Commit_Between_Service_Checks_And_Guard_Gives_One_Success_And_One_Conflict(string kind, string winner)
    {
        await ArrangeAsync();
        var dispatch = kind == "warehouse" ? await WarehouseDispatchAsync() : null;
        var shipment = kind == "direct-trade" ? await DirectTradeShipmentAsync() : null;
        var version = await OrderVersionAsync(_factory, _orderId);
        var correctionEvent = kind == "warehouse" ? ReservationRules.OrderDispatchCorrectedEvent : ReservationRules.DirectTradeShipmentCorrectedEvent;

        ServiceResultStatus winnerStatus = default;
        string afterWinner = null;
        async Task WinnerAsync()
        {
            winnerStatus = winner == "correction-wins"
                ? (kind == "warehouse" ? (await CorrectWarehouseAsync(dispatch!, version)).Status : (await TakeBackAsync(shipment!, version)).Status)
                : (kind == "warehouse" ? (await ConfirmWarehouseAsync(dispatch!.Id, Confirm(version))).Status : (await ConfirmDirectTradeAsync(shipment!.Id, Confirm(version))).Status);
            afterWinner = await WriteStateAsync();
        }

        ServiceResultStatus loserStatus;
        string loserMessage;
        await using (var db = _factory.CreateContextBeforeGuard(WinnerAsync))
        {
            if (winner == "correction-wins")
            {
                var service = new DeliveryConfirmationService(db, _factory.Calendar);
                var result = kind == "warehouse" ? await service.ConfirmWarehouseAsync(_orderId, dispatch!.Id, Confirm(version)) : await service.ConfirmDirectTradeAsync(_orderId, shipment!.Id, Confirm(version));
                (loserStatus, loserMessage) = (result.Status, result.Message);
            }
            else if (kind == "warehouse")
            {
                var result = await new SalesDispatchCorrectionService(db).CorrectAsync(_orderId, dispatch!.Id, new CorrectSalesDispatchDto
                {
                    RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = "Verkeerd gescand", GoodsNeverLeftWarehouse = true,
                    Lines = dispatch.Lines.Select(l => new CreateSalesDispatchLineDto { StockReservationId = l.StockReservationId, ExpectedReservationVersion = 1 }).ToList()
                });
                (loserStatus, loserMessage) = (result.Status, result.Message);
            }
            else
            {
                var result = await new DirectTradeShipmentCorrectionService(db).CorrectAsync(_orderId, shipment!.Id, Correction(version, shipment));
                (loserStatus, loserMessage) = (result.Status, result.Message);
            }
        }

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (winnerStatus, loserStatus));
        Assert.Contains(winner == "correction-wins" ? "corrected concurrently" : "delivery was confirmed concurrently", loserMessage);
        // Geen gedeeltelijke wijziging en geen extra audit van de verliezer: de staat is exact die direct na de winnaar.
        Assert.Equal(afterWinner, await WriteStateAsync());
        Assert.Equal(winner == "correction-wins" ? (0, 1) : (1, 0), (await AuditCountAsync(ReservationRules.DeliveryConfirmedEvent), await AuditCountAsync(correctionEvent)));
        await using var verify = _factory.CreateContext();
        var deliveries = await verify.ShipmentDeliveryConfirmations.CountAsync();
        var corrections = await verify.SalesDispatchCorrections.CountAsync() + await verify.DirectTradeShipmentCorrections.CountAsync();
        Assert.Equal(winner == "correction-wins" ? (0, 1) : (1, 0), (deliveries, corrections));
    }

    // Alleen het herkenbare aflevering/correctie-conflict wordt 409. Een andere guardfout blijft een fout van de schrijver: hier
    // laat de service een datum door die de guard (andere kalender) als "na vandaag" weigert. Die fout moet doorkomen, niet als 409.
    [Fact]
    public async Task Other_Guard_Violations_Are_Not_Translated_To_Conflict()
    {
        await ArrangeAsync();
        var dispatch = await WarehouseDispatchAsync();
        var version = await OrderVersionAsync(_factory, _orderId);
        await using var db = _factory.CreateContext(new FixedBusinessCalendar(new DateOnly(2026, 10, 20)));
        var service = new DeliveryConfirmationService(db, new FixedBusinessCalendar(new DateOnly(2026, 10, 21)));

        var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => service.ConfirmWarehouseAsync(_orderId, dispatch.Id, Confirm(version, new DateOnly(2026, 10, 21))));

        Assert.Contains("after today", exception.Message);
        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.ShipmentDeliveryConfirmations.CountAsync());
        Assert.Equal(0, await AuditCountAsync(ReservationRules.DeliveryConfirmedEvent));
    }
}

