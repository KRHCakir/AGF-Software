using AgfAdmin.Controllers;
using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AgfAdmin.Tests.Controllers;

public class SalesOrdersControllerTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;

    public void Dispose() => _factory.Dispose();

    private CreateSalesOrderDto OrderRequest(Guid requestId) => new()
    {
        RequestId = requestId,
        CustomerId = _seed.CustomerId,
        RequestedDeliveryDate = new DateTimeOffset(2026, 10, 23, 6, 0, 0, TimeSpan.Zero),
        ActorReference = "office-1",
        Lines = new List<CreateSalesOrderLineDto> { new() { ProductVariantId = _seed.AvocadoId, Quantity = 20m, Unit = "box" } }
    };

    private async Task<IActionResult> Orders(Func<SalesOrdersController, Task<IActionResult>> action)
    {
        await using var db = _factory.CreateContext();
        return await action(new SalesOrdersController(new SalesOrderService(db), new StockReservationService(db), new SalesDispatchService(db), new SalesDispatchCorrectionService(db), new DirectTradeAllocationService(db), new DirectTradeShipmentService(db, new FixedBusinessCalendar(new DateOnly(2026, 10, 20))), new DirectTradeShipmentCorrectionService(db), new DeliveryConfirmationService(db, new FixedBusinessCalendar(new DateOnly(2026, 10, 20)))));
    }

    private async Task<IActionResult> Reservations(Func<StockReservationsController, Task<IActionResult>> action)
    {
        await using var db = _factory.CreateContext();
        return await action(new StockReservationsController(new StockReservationService(db)));
    }

    private async Task<SalesOrderDto> CreatedOrderAsync()
    {
        _seed ??= await SalesTestData.SeedAsync(_factory);
        var created = Assert.IsType<CreatedAtActionResult>(await Orders(c => c.Create(OrderRequest(Guid.NewGuid()))));
        return Assert.IsType<SalesOrderDto>(created.Value);
    }

    [Fact]
    public async Task Create_Returns_201_Then_200_On_Retry_And_409_On_Changed_Payload()
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        var requestId = Guid.NewGuid();

        var created = Assert.IsType<CreatedAtActionResult>(await Orders(c => c.Create(OrderRequest(requestId))));
        Assert.Equal(nameof(SalesOrdersController.GetById), created.ActionName);
        var order = Assert.IsType<SalesOrderDto>(created.Value);
        Assert.Equal(order.Id, created.RouteValues!["id"]);

        var retry = Assert.IsType<OkObjectResult>(await Orders(c => c.Create(OrderRequest(requestId))));
        Assert.Equal(order.Id, Assert.IsType<SalesOrderDto>(retry.Value).Id);

        var changed = OrderRequest(requestId);
        changed.Lines[0].Quantity = 21m;
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Create(changed)));
    }

    [Fact]
    public async Task Create_Returns_400_And_404()
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        var invalid = OrderRequest(Guid.NewGuid());
        invalid.Lines.Add(new CreateSalesOrderLineDto { ProductVariantId = _seed.AvocadoId, Quantity = 1m, Unit = "box" });
        var badRequest = Assert.IsType<BadRequestObjectResult>(await Orders(c => c.Create(invalid)));
        Assert.Contains("lines[1]", Assert.IsType<ValidationProblemDetails>(badRequest.Value).Errors.Keys);

        var unknown = OrderRequest(Guid.NewGuid());
        unknown.CustomerId = 999_999;
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.Create(unknown)));
    }

    [Fact]
    public async Task Get_List_And_Reservable_Stock_Return_200_400_Or_404()
    {
        var order = await CreatedOrderAsync();

        Assert.Equal(order.Id, Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value).Id);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.GetById(999_999)));
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<SalesOrderDto>>(Assert.IsType<OkObjectResult>(await Orders(c => c.List(customerId: null, status: "Open"))).Value));
        Assert.IsType<BadRequestObjectResult>(await Orders(c => c.List(customerId: null, status: "open")));

        var stock = Assert.IsAssignableFrom<IReadOnlyList<ReservableStockLineDto>>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetReservableStock(order.Id, order.Lines[0].Id))).Value);
        Assert.Equal(2, stock.Count);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.GetReservableStock(order.Id, 999_999)));
    }

    [Fact]
    public async Task Reserve_Returns_201_200_400_404_And_409()
    {
        var order = await CreatedOrderAsync();
        var lineId = order.Lines[0].Id;
        var request = new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 10m, Unit = "box", NetWeightKg = 40m, ActorReference = "office-1"
        };

        var created = Assert.IsType<CreatedAtActionResult>(await Orders(c => c.Reserve(order.Id, lineId, request)));
        Assert.Equal(nameof(SalesOrdersController.GetById), created.ActionName);
        Assert.Equal(order.Id, created.RouteValues!["id"]);
        var reservation = Assert.IsType<StockReservationDto>(created.Value);
        Assert.Equal(reservation.Id, Assert.IsType<StockReservationDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.Reserve(order.Id, lineId, request))).Value).Id);

        var invalid = new CreateStockReservationDto { RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 1m, Unit = "box", ActorReference = "office-1" };
        Assert.Contains("netWeightKg", Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(await Orders(c => c.Reserve(order.Id, lineId, invalid))).Value).Errors.Keys);
        var unknownOrder = new CreateStockReservationDto { RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 1m, Unit = "box", NetWeightKg = 4m, ActorReference = "office-1" };
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.Reserve(999_999, lineId, unknownOrder)));
        // Dezelfde requestId onder een ander order-id is een ander commando: 409, nooit een stille replay.
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Reserve(999_999, lineId, request)));

        var blocked = new CreateStockReservationDto { RequestId = Guid.NewGuid(), PalletContentLineId = _seed.BlockedBox, Quantity = 1m, Unit = "box", NetWeightKg = 4m, ActorReference = "office-1" };
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Reserve(order.Id, lineId, blocked)));
    }

    [Fact]
    public async Task Release_Returns_200_400_404_And_409_For_Stale_Version()
    {
        var order = await CreatedOrderAsync();
        var reservation = Assert.IsType<StockReservationDto>(Assert.IsType<CreatedAtActionResult>(await Orders(c => c.Reserve(order.Id, order.Lines[0].Id, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 10m, Unit = "box", NetWeightKg = 40m, ActorReference = "office-1"
        }))).Value);
        ReleaseStockReservationDto Release(int? version, string reason = "Klant annuleert") => new()
        {
            RequestId = Guid.NewGuid(), ExpectedReservationVersion = version, ActorReference = "office-1", Reason = reason
        };

        Assert.Contains("expectedReservationVersion", Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(await Reservations(c => c.Release(reservation.Id, Release(null)))).Value).Errors.Keys);
        Assert.IsType<NotFoundObjectResult>(await Reservations(c => c.Release(999_999, Release(0))));
        Assert.IsType<ConflictObjectResult>(await Reservations(c => c.Release(reservation.Id, Release(reservation.Version + 1))));

        var ok = Assert.IsType<OkObjectResult>(await Reservations(c => c.Release(reservation.Id, Release(reservation.Version))));
        Assert.Equal(("Released", 1), (Assert.IsType<StockReservationDto>(ok.Value).Status, Assert.IsType<StockReservationDto>(ok.Value).Version));
    }

    [Fact]
    public async Task Cancel_Returns_200_Then_200_On_Replay_400_404_And_409()
    {
        var order = await CreatedOrderAsync();
        Assert.IsType<CreatedAtActionResult>(await Orders(c => c.Reserve(order.Id, order.Lines[0].Id, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 10m, Unit = "box", NetWeightKg = 40m, ActorReference = "office-1"
        })));
        CancelSalesOrderDto Cancel(int? version, string reason = "Klant heeft afgezegd", Guid? requestId = null) => new()
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = reason
        };

        Assert.Contains("reason", Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(await Orders(c => c.Cancel(order.Id, Cancel(0, reason: " ")))).Value).Errors.Keys);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.Cancel(999_999, Cancel(0))));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Cancel(order.Id, Cancel(order.Version + 1))));

        var request = Cancel(order.Version);
        var cancelled = Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.Cancel(order.Id, request))).Value);
        Assert.Equal(("Cancelled", "Klant heeft afgezegd", "office-1"), (cancelled.Status, cancelled.CancelReason, cancelled.CancelledBy));
        Assert.All(cancelled.Lines.SelectMany(l => l.Reservations), r => Assert.Equal("Released", r.Status));

        Assert.IsType<OkObjectResult>(await Orders(c => c.Cancel(order.Id, request)));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Cancel(order.Id, Cancel(order.Version, reason: "Andere reden", requestId: request.RequestId))));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Cancel(order.Id, Cancel(cancelled.Version))));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Reserve(order.Id, order.Lines[0].Id, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 1m, Unit = "box", NetWeightKg = 4m, ActorReference = "office-1"
        })));

        var events = Assert.IsAssignableFrom<IReadOnlyList<SalesOrderAuditEventDto>>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetAuditEvents(order.Id))).Value);
        var single = Assert.Single(events);
        Assert.Equal(("SalesOrderCancelled", request.RequestId, order.Id), (single.EventType, single.RequestId, single.SalesOrderId));
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.GetAuditEvents(999_999)));
        Assert.Equal("Cancelled", Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value).Status);
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<SalesOrderDto>>(Assert.IsType<OkObjectResult>(await Orders(c => c.List(customerId: null, status: "Cancelled"))).Value));
    }

    [Fact]
    public async Task Dispatch_Returns_201_Then_200_On_Replay_400_404_And_409_And_Lists_Dispatches()
    {
        var order = await CreatedOrderAsync();
        var reservation = Assert.IsType<StockReservationDto>(Assert.IsType<CreatedAtActionResult>(await Orders(c => c.Reserve(order.Id, order.Lines[0].Id, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 10m, Unit = "box", NetWeightKg = 40m, ActorReference = "office-1"
        }))).Value);
        CreateSalesDispatchDto Dispatch(int? orderVersion, int reservationVersion, Guid? requestId = null, string reference = "VRB-1") => new()
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = orderVersion, ActorReference = "office-1", Reference = reference,
            Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = reservation.Id, ExpectedReservationVersion = reservationVersion } }
        };
        var current = Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value);

        Assert.Contains("expectedOrderVersion", Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(await Orders(c => c.Dispatch(order.Id, Dispatch(null, reservation.Version)))).Value).Errors.Keys);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.Dispatch(999_999, Dispatch(0, reservation.Version))));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Dispatch(order.Id, Dispatch(current.Version + 1, reservation.Version))));

        var request = Dispatch(current.Version, reservation.Version);
        var created = Assert.IsType<CreatedAtActionResult>(await Orders(c => c.Dispatch(order.Id, request)));
        Assert.Equal(nameof(SalesOrdersController.GetById), created.ActionName);
        var dispatch = Assert.IsType<SalesDispatchDto>(created.Value);
        Assert.Equal(("VRB-1", 1), (dispatch.Reference, dispatch.Lines.Count));

        Assert.Equal(dispatch.Id, Assert.IsType<SalesDispatchDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.Dispatch(order.Id, request))).Value).Id);
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Dispatch(order.Id, Dispatch(current.Version, reservation.Version, request.RequestId, reference: "ANDERS"))));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Dispatch(order.Id, Dispatch(current.Version + 1, reservation.Version + 1))));

        var list = Assert.IsAssignableFrom<IReadOnlyList<SalesDispatchDto>>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetDispatches(order.Id))).Value);
        Assert.Equal(dispatch.Id, Assert.Single(list).Id);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.GetDispatches(999_999)));

        var after = Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value);
        var line = after.Lines[0];
        Assert.Equal((10m, 0m, 10m, false), (line.DispatchedQuantity, line.ReservedQuantity, line.OpenQuantity, line.Fulfilled));
        Assert.Equal(("Consumed", dispatch.Id), (line.Reservations.Single().Status, line.Reservations.Single().DispatchId));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.Cancel(order.Id, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = after.Version, ActorReference = "office-1", Reason = "x" })));
    }

    [Fact]
    public async Task CorrectDispatch_Returns_201_Then_200_On_Replay_400_Without_Confirmation_404_And_409()
    {
        var order = await CreatedOrderAsync();
        var reservation = Assert.IsType<StockReservationDto>(Assert.IsType<CreatedAtActionResult>(await Orders(c => c.Reserve(order.Id, order.Lines[0].Id, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 10m, Unit = "box", NetWeightKg = 40m, ActorReference = "office-1"
        }))).Value);
        var current = Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value);
        var dispatch = Assert.IsType<SalesDispatchDto>(Assert.IsType<CreatedAtActionResult>(await Orders(c => c.Dispatch(order.Id, new CreateSalesDispatchDto
        {
            RequestId = Guid.NewGuid(), ExpectedOrderVersion = current.Version, ActorReference = "office-1", Reference = "VRB-1",
            Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = reservation.Id, ExpectedReservationVersion = reservation.Version } }
        }))).Value);
        current = Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value);
        CorrectSalesDispatchDto Correct(int? version, bool? confirmed = true, Guid? requestId = null, string reason = "Verkeerd gescand") => new()
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = reason, GoodsNeverLeftWarehouse = confirmed,
            Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = reservation.Id, ExpectedReservationVersion = 1 } }
        };

        Assert.Contains("goodsNeverLeftWarehouse", Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(await Orders(c => c.CorrectDispatch(order.Id, dispatch.Id, Correct(current.Version, confirmed: false)))).Value).Errors.Keys);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.CorrectDispatch(order.Id, 999_999, Correct(current.Version))));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.CorrectDispatch(order.Id, dispatch.Id, Correct(current.Version + 1))));

        var request = Correct(current.Version);
        var created = Assert.IsType<CreatedAtActionResult>(await Orders(c => c.CorrectDispatch(order.Id, dispatch.Id, request)));
        var correction = Assert.IsType<SalesDispatchCorrectionDto>(created.Value);
        Assert.Equal((dispatch.Id, true, "Quarantine"), (correction.SalesDispatchId, correction.GoodsNeverLeftWarehouseConfirmed, correction.Lines.Single().RestoredQualityStatus));
        Assert.Equal(correction.Id, Assert.IsType<SalesDispatchCorrectionDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.CorrectDispatch(order.Id, dispatch.Id, request))).Value).Id);
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.CorrectDispatch(order.Id, dispatch.Id, Correct(current.Version, requestId: request.RequestId, reason: "Anders"))));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.CorrectDispatch(order.Id, dispatch.Id, Correct(current.Version + 1))));

        var after = Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value);
        var line = after.Lines[0];
        Assert.Equal((0m, 0m, 20m, false, "Corrected"), (line.DispatchedQuantity, line.ReservedQuantity, line.OpenQuantity, line.Fulfilled, line.Reservations.Single().Status));
        Assert.Equal(correction.Id, line.Reservations.Single().CorrectionId);
        var dispatches = Assert.IsAssignableFrom<IReadOnlyList<SalesDispatchDto>>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetDispatches(order.Id))).Value);
        Assert.Equal(correction.Id, Assert.Single(dispatches).Correction!.Id);
    }

    [Fact]
    public async Task DirectTrade_Allocation_Endpoints_Map_201_200_400_404_And_409()
    {
        var order = await CreatedOrderAsync();
        var line = order.Lines[0];

        var supply = Assert.IsAssignableFrom<IReadOnlyList<DirectTradeSupplyLineDto>>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetDirectTradeSupply(order.Id, line.Id))).Value);
        var source = Assert.Single(supply);
        Assert.Equal((_seed.DirectTradeSourceLine, 100m, 0m, 100m, true), (source.SupplyCommitmentLineId, source.ExpectedQuantity, source.AllocatedQuantity, source.RemainingQuantity, source.ExpectedAt != null));
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.GetDirectTradeSupply(order.Id, 999_999)));

        CreateDirectTradeAllocationDto Allocate(decimal quantity, bool? acknowledge = true, Guid? requestId = null, string reason = null) => new()
        {
            RequestId = requestId ?? Guid.NewGuid(), SupplyCommitmentLineId = _seed.DirectTradeSourceLine, Quantity = quantity, Unit = "box",
            AcknowledgeExpectedSupply = acknowledge, ActorReference = "office-1", Reason = reason
        };

        Assert.Contains("acknowledgeExpectedSupply", Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(await Orders(c => c.AllocateDirectTrade(order.Id, line.Id, Allocate(5m, acknowledge: false)))).Value).Errors.Keys);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.AllocateDirectTrade(order.Id, 999_999, Allocate(5m))));

        var request = Allocate(5m);
        var created = Assert.IsType<CreatedAtActionResult>(await Orders(c => c.AllocateDirectTrade(order.Id, line.Id, request)));
        Assert.Equal(nameof(SalesOrdersController.GetById), created.ActionName);
        var allocation = Assert.IsType<StockReservationDto>(created.Value);
        Assert.Equal(("DirectTrade", 5m, null, null), (allocation.Source, allocation.Quantity, allocation.PalletContentLineId, allocation.NetWeightKg));
        Assert.Equal(allocation.Id, Assert.IsType<StockReservationDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.AllocateDirectTrade(order.Id, line.Id, request))).Value).Id);
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.AllocateDirectTrade(order.Id, line.Id, Allocate(6m, requestId: request.RequestId))));
        // Over de klantvraag (20) heen: 5 toegewezen + 16.
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.AllocateDirectTrade(order.Id, line.Id, Allocate(16m))));

        var current = Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value);
        Assert.Equal((5m, 0m, 15m), (current.Lines[0].AllocatedQuantity, current.Lines[0].ReservedQuantity, current.Lines[0].OpenQuantity));

        // Vrijgeven via hetzelfde endpoint als een fysieke reservering.
        var released = Assert.IsType<StockReservationDto>(Assert.IsType<OkObjectResult>(await Reservations(c => c.Release(allocation.Id, new ReleaseStockReservationDto
        {
            RequestId = Guid.NewGuid(), ExpectedReservationVersion = allocation.Version, ActorReference = "office-1", Reason = "Toch uit magazijn"
        }))).Value);
        Assert.Equal("Released", released.Status);
    }

    [Fact]
    public async Task DirectTrade_Shipment_Endpoints_Map_201_200_400_404_And_409()
    {
        var order = await CreatedOrderAsync();
        var line = order.Lines[0];
        var allocation = Assert.IsType<StockReservationDto>(Assert.IsType<CreatedAtActionResult>(await Orders(c => c.AllocateDirectTrade(order.Id, line.Id, new CreateDirectTradeAllocationDto
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLineId = _seed.DirectTradeSourceLine, Quantity = 5m, Unit = "box", AcknowledgeExpectedSupply = true, ActorReference = "office-1"
        }))).Value);
        var current = Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value);
        CreateDirectTradeShipmentDto Ship(Guid? requestId = null, decimal kg = 20.2m, DateOnly? on = null) => new()
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = current.Version, ActorReference = "office-1", ShippedOn = on ?? new DateOnly(2026, 10, 20), Reference = "CMR-1",
            Lines = new List<CreateDirectTradeShipmentLineDto> { new() { StockReservationId = allocation.Id, ExpectedReservationVersion = allocation.Version, Quantity = 5m, Unit = "box", NetWeightKg = kg, LotNumber = "LOT-DT-SRC" } }
        };

        Assert.Contains("shippedOn", Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(await Orders(c => c.ShipDirectTrade(order.Id, Ship(on: new DateOnly(2026, 10, 21))))).Value).Errors.Keys);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.ShipDirectTrade(999_999, Ship())));
        var request = Ship();
        var created = Assert.IsType<CreatedAtActionResult>(await Orders(c => c.ShipDirectTrade(order.Id, request)));
        Assert.Equal(nameof(SalesOrdersController.GetById), created.ActionName);
        var shipment = Assert.IsType<DirectTradeShipmentDto>(created.Value);
        Assert.Equal((false, 20.2m), (shipment.DeliveryRecorded, shipment.Lines.Single().NetWeightKg));
        Assert.Equal(shipment.Id, Assert.IsType<DirectTradeShipmentDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.ShipDirectTrade(order.Id, request))).Value).Id);
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.ShipDirectTrade(order.Id, Ship(requestId: request.RequestId, kg: 20.3m))));
        var list = Assert.IsAssignableFrom<IReadOnlyList<DirectTradeShipmentDto>>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetDirectTradeShipments(order.Id))).Value);
        Assert.Equal(shipment.Id, Assert.Single(list).Id);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.GetDirectTradeShipments(999_999)));
    }

    [Fact]
    public async Task DirectTrade_Shipment_Correction_Endpoint_Maps_201_200_400_404_And_409()
    {
        var order = await CreatedOrderAsync();
        var line = order.Lines[0];
        var allocation = await DirectTradeTestFlow.AllocateAsync(_factory, order.Id, line.Id, _seed.DirectTradeSourceLine, 5m);
        var shipment = await DirectTradeTestFlow.ShipAsync(_factory, order.Id, (allocation, 20.2m, "LOT-DT-SRC"));
        var version = await DirectTradeTestFlow.OrderVersionAsync(_factory, order.Id);

        Assert.Contains("shipmentDidNotTakePlace", Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(
            await Orders(c => c.CorrectDirectTradeShipment(order.Id, shipment.Id, DirectTradeTestFlow.Correction(version, shipment, confirmed: false)))).Value).Errors.Keys);
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.CorrectDirectTradeShipment(order.Id, 999_999, DirectTradeTestFlow.Correction(version, shipment))));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.CorrectDirectTradeShipment(order.Id, shipment.Id, DirectTradeTestFlow.Correction(version + 1, shipment))));
        var request = DirectTradeTestFlow.Correction(version, shipment);
        var created = Assert.IsType<CreatedAtActionResult>(await Orders(c => c.CorrectDirectTradeShipment(order.Id, shipment.Id, request)));
        var correction = Assert.IsType<DirectTradeShipmentCorrectionDto>(created.Value);
        Assert.Equal(("SHIPMENT_DID_NOT_TAKE_PLACE", "Deze geregistreerde verzending heeft niet plaatsgevonden."), (correction.ConfirmationCode, correction.ConfirmationStatement));
        Assert.Equal(correction.Id, Assert.IsType<DirectTradeShipmentCorrectionDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.CorrectDirectTradeShipment(order.Id, shipment.Id, request))).Value).Id);
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.CorrectDirectTradeShipment(order.Id, shipment.Id, DirectTradeTestFlow.Correction(version, shipment))));
        var list = Assert.IsAssignableFrom<IReadOnlyList<DirectTradeShipmentDto>>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetDirectTradeShipments(order.Id))).Value);
        Assert.Equal(correction.Id, Assert.Single(list).Correction!.Id);
    }

    [Fact]
    public async Task Delivery_Confirmation_Endpoints_Map_201_200_400_404_And_409_Per_Kind()
    {
        var order = await CreatedOrderAsync();
        var line = order.Lines[0];
        var allocation = await DirectTradeTestFlow.AllocateAsync(_factory, order.Id, line.Id, _seed.DirectTradeSourceLine, 5m);
        var shipment = await DirectTradeTestFlow.ShipAsync(_factory, order.Id, (allocation, 20.2m, "LOT-DT-SRC"));
        var version = await DirectTradeTestFlow.OrderVersionAsync(_factory, order.Id);
        ConfirmDeliveryDto Confirm(int v, bool? full = true, Guid? requestId = null) => new()
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = v, ActorReference = "office-1", DeliveredOn = new DateOnly(2026, 10, 20), ReceiptReference = "CMR-9", DeliveredInFull = full
        };

        Assert.Contains("deliveredInFull", Assert.IsType<ValidationProblemDetails>(Assert.IsType<BadRequestObjectResult>(await Orders(c => c.ConfirmDirectTradeDelivery(order.Id, shipment.Id, Confirm(version, false)))).Value).Errors.Keys);
        // Er bestaat geen magazijnverzending met dit nummer: het magazijn-endpoint geeft 404, ook al bestaat DirectTrade-verzending 1.
        Assert.IsType<NotFoundObjectResult>(await Orders(c => c.ConfirmWarehouseDelivery(order.Id, shipment.Id, Confirm(version))));
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.ConfirmDirectTradeDelivery(order.Id, shipment.Id, Confirm(version + 1))));
        var request = Confirm(version);
        var created = Assert.IsType<CreatedAtActionResult>(await Orders(c => c.ConfirmDirectTradeDelivery(order.Id, shipment.Id, request)));
        var delivery = Assert.IsType<DeliveryConfirmationDto>(created.Value);
        Assert.Equal(("DirectTrade", "DELIVERED_IN_FULL"), (delivery.ShipmentKind, delivery.ConfirmationCode));
        Assert.Equal(delivery.Id, Assert.IsType<DeliveryConfirmationDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.ConfirmDirectTradeDelivery(order.Id, shipment.Id, request))).Value).Id);
        var fresh = await DirectTradeTestFlow.OrderVersionAsync(_factory, order.Id);
        Assert.IsType<ConflictObjectResult>(await Orders(c => c.ConfirmDirectTradeDelivery(order.Id, shipment.Id, Confirm(fresh))));
        var after = Assert.IsType<SalesOrderDto>(Assert.IsType<OkObjectResult>(await Orders(c => c.GetById(order.Id))).Value);
        Assert.Equal((5m, false), (after.Lines[0].DeliveredQuantity, after.Delivered));
    }
}

