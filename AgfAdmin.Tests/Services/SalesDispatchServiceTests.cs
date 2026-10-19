using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Services;

// Verzenden met afboeking: verbruik van hele reserveringen, grootboek (mutaties + afgeboekt) in één transactie,
// saldi die nooit dubbel aftrekken, idempotentie en precies één winnaar bij races.
public class SalesDispatchServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;
    private int _colliLine;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync(decimal boxQuantity = 30m, decimal colliQuantity = 10m)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity, colliQuantity);
    }

    private static CreateSalesDispatchDto Dispatch(int orderVersion, params (int Id, int Version)[] lines) => new()
    {
        RequestId = Guid.NewGuid(),
        ExpectedOrderVersion = orderVersion,
        ActorReference = "office-1",
        Reference = "VRB-2026-001",
        Lines = lines.Select(l => new CreateSalesDispatchLineDto { StockReservationId = l.Id, ExpectedReservationVersion = l.Version }).ToList()
    };

    private async Task<ServiceResult<SalesDispatchDto>> DispatchAsync(CreateSalesDispatchDto request, int? orderId = null, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new SalesDispatchService(db).DispatchAsync(orderId ?? _orderId, request);
    }

    private async Task<StockReservationDto> ReservedAsync(int contentLineId, decimal quantity, decimal kg, int? lineId = null, string unit = "box", int? orderId = null)
    {
        await using var db = _factory.CreateContext();
        var result = await new StockReservationService(db).ReserveAsync(orderId ?? _orderId, lineId ?? _boxLine, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = contentLineId, Quantity = quantity, Unit = unit, NetWeightKg = kg, ActorReference = "office-1"
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    private async Task<ServiceResult<StockReservationDto>> ReserveAsync(int contentLineId, decimal quantity, decimal kg, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new StockReservationService(db).ReserveAsync(_orderId, _boxLine, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(), PalletContentLineId = contentLineId, Quantity = quantity, Unit = "box", NetWeightKg = kg, ActorReference = "office-1"
        });
    }

    private async Task<ServiceResult<StockReservationDto>> ReleaseAsync(int reservationId, int version, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new StockReservationService(db).ReleaseAsync(reservationId, new ReleaseStockReservationDto
        {
            RequestId = Guid.NewGuid(), ExpectedReservationVersion = version, ActorReference = "office-1", Reason = "Klant wil andere partij"
        });
    }

    private async Task<ServiceResult<SalesOrderDto>> CancelAsync(int version, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new SalesOrderService(db).CancelAsync(_orderId, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = "Afgezegd" });
    }

    private async Task<ServiceResultStatus> BlockAsync(int contentLineId, params IInterceptor[] interceptors)
    {
        await using var read = _factory.CreateContext();
        var line = await read.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == contentLineId);
        await using var service = _factory.CreateContext(interceptors);
        return (await new PalletService(service).ChangeQualityStatusAsync(line.PalletId, line.Id, new ChangeQualityStatusDto
        {
            RequestId = Guid.NewGuid(), ExpectedContentLineVersion = line.Version, ToStatus = "Blocked", ActorReference = "qc-1", Reason = "Claim"
        })).Status;
    }

    private async Task<SalesOrderDto> OrderAsync()
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(_orderId)).Value;
    }

    private async Task<IReadOnlyList<ReservableStockLineDto>> ReservableAsync()
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetReservableStockAsync(_orderId, _boxLine)).Value;
    }

    private async Task<(PalletContentLine Content, List<StockMovement> Movements, List<StockReservation> Reservations, int Dispatches, int DispatchLines, List<PalletAuditEvent> Consumed, List<SalesOrderAuditEvent> OrderEvents)> StateAsync(int contentLineId)
    {
        await using var db = _factory.CreateContext();
        return (
            await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == contentLineId),
            await db.StockMovements.AsNoTracking().OrderBy(m => m.Id).ToListAsync(),
            await db.StockReservations.AsNoTracking().Where(r => r.SalesOrderLineId != null).OrderBy(r => r.Id).ToListAsync(),
            await db.SalesDispatches.CountAsync(),
            await db.SalesDispatchLines.CountAsync(),
            await db.PalletAuditEvents.AsNoTracking().Where(e => e.EventType == "ReservationConsumed").OrderBy(e => e.Id).ToListAsync(),
            await db.SalesOrderAuditEvents.AsNoTracking().Where(e => e.EventType == "SalesOrderDispatched").OrderBy(e => e.Id).ToListAsync());
    }

    // ======================== Gelukte verzending en saldi ========================

    [Fact]
    public async Task Dispatch_Consumes_Reservations_Writes_Ledger_And_Audit_And_Leaves_Pallet_Status_Untouched()
    {
        await ArrangeAsync();
        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var r2 = await ReservedAsync(_seed.ApprovedColli, 3m, 16.5m, _colliLine, "colli");
        var order = await OrderAsync();
        var request = Dispatch(order.Version, (r1.Id, r1.Version), (r2.Id, r2.Version));

        var result = await DispatchAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var dto = result.Value;
        Assert.Equal((_orderId, "VRB-2026-001", "office-1", 2), (dto.SalesOrderId, dto.Reference, dto.ActorReference, dto.Lines.Count));
        var boxLine = dto.Lines.Single(l => l.StockReservationId == r1.Id);
        Assert.Equal((10m, "box", 40m, "P-WH", "LOT-A", "Avocado", "Hass"), (boxLine.Quantity, boxLine.Unit, boxLine.NetWeightKg, boxLine.PalletReference, boxLine.LotNumber, boxLine.ProductName, boxLine.ProductVariantName));

        var (content, movements, reservations, dispatches, dispatchLines, consumedEvents, orderEvents) = await StateAsync(_seed.ApprovedBox);
        // Ontvangst ongewijzigd; afgeboekt verhoogd; grootboek = afgeboekt.
        Assert.Equal((40m, 160m, 10m, 40m), (content.Quantity, content.NetWeightKg, content.DispatchedQuantity, content.DispatchedNetWeightKg));
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m => Assert.Equal("Dispatch", m.Type));
        var boxMovement = movements.Single(m => m.StockReservationId == r1.Id);
        Assert.Equal((10m, "box", 40m, _seed.ApprovedBox, "VRB-2026-001"), (boxMovement.Quantity, boxMovement.Unit, boxMovement.NetWeightKg, boxMovement.PalletContentLineId, boxMovement.Reason));
        Assert.All(reservations, r => Assert.Equal(("Consumed", 1), (r.Status, r.Version)));
        Assert.All(reservations, r => Assert.NotNull(r.ConsumedAt));
        Assert.Equal((1, 2), (dispatches, dispatchLines));
        Assert.Equal(2, consumedEvents.Count);
        Assert.All(consumedEvents, e =>
        {
            Assert.Null(e.RequestId);
            Assert.Contains(request.RequestId.ToString()!, e.NewValue);
            Assert.NotNull(e.PalletContentLineId);
        });
        var orderEvent = Assert.Single(orderEvents);
        Assert.Equal(request.RequestId, orderEvent.RequestId);
        using var snapshot = JsonDocument.Parse(orderEvent.NewValue!);
        Assert.Equal(2, snapshot.RootElement.GetProperty("consumedReservationCount").GetInt32());
        Assert.Equal(10m, snapshot.RootElement.GetProperty("dispatchedQuantityByUnit").GetProperty("box").GetDecimal());
        Assert.Equal(56.5m, snapshot.RootElement.GetProperty("dispatchedNetWeightKg").GetDecimal());

        await using var db = _factory.CreateContext();
        var pallet = await db.Pallets.FindAsync(_seed.WarehousePalletId);
        Assert.Equal(("Received", "Warehouse", 3), (pallet!.LogisticsStatus, pallet.LocationContext, pallet.Version)); // 2 reserveringen + verzending
        Assert.Equal(1, (await db.SalesOrders.FindAsync(_orderId))!.Version);
        Assert.Equal(2, (await db.SalesOrderLines.FindAsync(_boxLine))!.Version); // reservering + verzending
        // Legacy blijft onaangeroerd: alleen de gezaaide legacy-order, geen Inventory, en uitsluitend Dispatch-mutaties.
        Assert.Equal((1, 0), (await db.Orders.CountAsync(), await db.Inventory.CountAsync()));
        Assert.Equal(2, await db.StockMovements.CountAsync(m => m.Type == "Dispatch"));
    }

    [Fact]
    public async Task Balances_Partial_Shipment_Then_Another_Reservation_Never_Returns_Shipped_Stock()
    {
        // Voorbeeld uit het plan: inhoud 40 box / 160 kg, regel 30 box.
        await ArrangeAsync(boxQuantity: 30m);
        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var stock = (await ReservableAsync()).Single(s => s.PalletContentLineId == _seed.ApprovedBox);
        Assert.Equal((40m, 0m, 40m, 10m, 30m, 120m), (stock.Quantity, stock.DispatchedQuantity, stock.OnHandQuantity, stock.ReservedQuantity, stock.AvailableQuantity, stock.AvailableNetWeightKg));
        var line = (await OrderAsync()).Lines.Single(l => l.Id == _boxLine);
        Assert.Equal((10m, 0m, 20m, false), (line.ReservedQuantity, line.DispatchedQuantity, line.OpenQuantity, line.Fulfilled));

        Assert.Equal(ServiceResultStatus.Created, (await DispatchAsync(Dispatch((await OrderAsync()).Version, (r1.Id, r1.Version)))).Status);

        stock = (await ReservableAsync()).Single(s => s.PalletContentLineId == _seed.ApprovedBox);
        Assert.Equal((40m, 10m, 30m, 0m, 30m, 120m), (stock.Quantity, stock.DispatchedQuantity, stock.OnHandQuantity, stock.ReservedQuantity, stock.AvailableQuantity, stock.AvailableNetWeightKg));
        line = (await OrderAsync()).Lines.Single(l => l.Id == _boxLine);
        Assert.Equal((0m, 10m, 40m, 20m, false), (line.ReservedQuantity, line.DispatchedQuantity, line.DispatchedNetWeightKg, line.OpenQuantity, line.Fulfilled));

        var r2 = await ReservedAsync(_seed.ApprovedBox, 15m, 60m);
        stock = (await ReservableAsync()).Single(s => s.PalletContentLineId == _seed.ApprovedBox);
        Assert.Equal((30m, 15m, 15m, 60m), (stock.OnHandQuantity, stock.ReservedQuantity, stock.AvailableQuantity, stock.AvailableNetWeightKg));
        line = (await OrderAsync()).Lines.Single(l => l.Id == _boxLine);
        Assert.Equal((15m, 10m, 5m), (line.ReservedQuantity, line.DispatchedQuantity, line.OpenQuantity));
        // De regel vraagt nog 5: een reservering van 6 past niet, ook al is er voorraad.
        Assert.Equal(ServiceResultStatus.Conflict, (await ReserveAsync(_seed.SecondPalletApprovedBox, 6m, 24m)).Status);

        Assert.Equal(ServiceResultStatus.Created, (await DispatchAsync(Dispatch((await OrderAsync()).Version, (r2.Id, r2.Version)))).Status);
        stock = (await ReservableAsync()).Single(s => s.PalletContentLineId == _seed.ApprovedBox);
        Assert.Equal((25m, 100m, 15m, 60m, 0m, 15m, 60m), (stock.DispatchedQuantity, stock.DispatchedNetWeightKg, stock.OnHandQuantity, stock.OnHandNetWeightKg, stock.ReservedQuantity, stock.AvailableQuantity, stock.AvailableNetWeightKg));
        line = (await OrderAsync()).Lines.Single(l => l.Id == _boxLine);
        Assert.Equal((0m, 25m, 100m, 5m, false), (line.ReservedQuantity, line.DispatchedQuantity, line.DispatchedNetWeightKg, line.OpenQuantity, line.Fulfilled));

        // Availability: vóór = voorraad (15), gereserveerd 0, na 15; ontvangst telt nog steeds als geregistreerd.
        await using var db = _factory.CreateContext();
        var overview = (await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = _seed.AvocadoId, Route = "Warehouse", Unit = "box" })).Value;
        var supply = overview.Variants.Single().WarehouseSupply.Single(s => s.Unit == "box");
        Assert.Equal(15m + 20m, supply.AvailableBeforeReservations.Quantity); // P-WH 15 over + P-WH2 20
        Assert.Equal(0m, supply.Reserved.Quantity);
        Assert.Equal(35m, supply.AvailableAfterReservations.Quantity);
        var drill = (await new AvailabilityService(db).GetVariantAsync(_seed.AvocadoId, new AvailabilityQuery { Route = "Warehouse", Unit = "box" })).Value;
        var contentLine = drill.ContentLines.Single(c => c.PalletContentLineId == _seed.ApprovedBox);
        Assert.Equal((40m, 25m, 15m, 15m, false), (contentLine.Quantity, contentLine.DispatchedQuantity, contentLine.OnHandQuantity, contentLine.AvailableQuantity, contentLine.Depleted));
        // Geregistreerd tegen de bronregel blijft de som van de ontvangsten (LOT-A 40 + Q 5 + X 6 + R 2 + C 20 + T 7 + D 3 = 83), ongeacht verzendingen.
        Assert.Equal(83m, drill.SourceLines.Single(s => s.SupplyCommitmentLineId == contentLine.SupplyCommitmentLineId).RegisteredQuantity);
    }

    [Fact]
    public async Task Fully_Dispatched_Line_And_Order_Are_Fulfilled_And_Content_Becomes_Depleted_Without_Closing_The_Pallet()
    {
        await ArrangeAsync(boxQuantity: 40m, colliQuantity: 10m);
        var box = await ReservedAsync(_seed.ApprovedBox, 40m, 160m);
        var colli = await ReservedAsync(_seed.ApprovedColli, 10m, 55m, _colliLine, "colli");
        var partial = await DispatchAsync(Dispatch((await OrderAsync()).Version, (box.Id, box.Version)));
        Assert.Equal(ServiceResultStatus.Created, partial.Status);

        var order = await OrderAsync();
        Assert.Equal((true, false, false), (order.Lines.Single(l => l.Id == _boxLine).Fulfilled, order.Lines.Single(l => l.Id == _colliLine).Fulfilled, order.Fulfilled));
        Assert.Equal(0m, order.Lines.Single(l => l.Id == _boxLine).OpenQuantity);

        Assert.Equal(ServiceResultStatus.Created, (await DispatchAsync(Dispatch(order.Version, (colli.Id, colli.Version)))).Status);
        order = await OrderAsync();
        Assert.True(order.Fulfilled);
        Assert.All(order.Lines, l => Assert.True(l.Fulfilled && l.OpenQuantity == 0m && l.ReservedQuantity == 0m));
        Assert.Equal("Open", order.Status);

        await using var db = _factory.CreateContext();
        var pallet = (await new PalletService(db).GetAsync(_seed.WarehousePalletId)).Value;
        var lotA = pallet.ContentLines.Single(c => c.Id == _seed.ApprovedBox);
        Assert.Equal((40m, 40m, 0m, 0m, true), (lotA.Quantity, lotA.DispatchedQuantity, lotA.OnHandQuantity, lotA.OnHandNetWeightKg, lotA.Depleted));
        // Gedeelde/mixpallet: andere regels (Quarantine, Blocked, Rejected) staan er nog, dus de pallet is niet uitgeput en blijft Received.
        Assert.False(pallet.Depleted);
        Assert.Equal(("Received", "Warehouse"), (pallet.LogisticsStatus, pallet.LocationContext));
        Assert.Empty(await ReservableAsync().ContinueWith(t => t.Result.Where(s => s.PalletContentLineId == _seed.ApprovedBox)));

        // Beschikbaarheid: uitgeputte inhoud telt nergens meer mee, maar de ontvangst blijft geregistreerd.
        var drill = (await new AvailabilityService(db).GetVariantAsync(_seed.AvocadoId, new AvailabilityQuery { Route = "Warehouse", Unit = "box" })).Value;
        var line = drill.ContentLines.Single(c => c.PalletContentLineId == _seed.ApprovedBox);
        Assert.Equal((0m, 0m, true, false), (line.OnHandQuantity, line.AvailableQuantity, line.Depleted, line.SellableNow));
        var source = drill.SourceLines.Single(s => s.SupplyCommitmentLineId == line.SupplyCommitmentLineId);
        Assert.Equal(100m - source.RegisteredQuantity, source.UnregisteredQuantity);
    }

    [Fact]
    public async Task A_Pallet_Whose_Only_Content_Is_Fully_Dispatched_Is_Depleted_But_Stays_Received()
    {
        await ArrangeAsync(boxQuantity: 20m);
        var r = await ReservedAsync(_seed.SecondPalletApprovedBox, 20m, 80m);
        Assert.Equal(ServiceResultStatus.Created, (await DispatchAsync(Dispatch((await OrderAsync()).Version, (r.Id, r.Version)))).Status);
        await using var db = _factory.CreateContext();
        var pallet = (await new PalletService(db).GetAsync(_seed.SecondWarehousePalletId)).Value;
        Assert.True(pallet.Depleted);
        Assert.Equal(0m, pallet.ContentNetKg);
        Assert.Equal(("Received", "Warehouse"), (pallet.LogisticsStatus, pallet.LocationContext));
    }

    // ======================== Afwijzingen ========================

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("version-missing", "expectedOrderVersion")]
    [InlineData("actor-missing", "actorReference")]
    [InlineData("reference-too-long", "reference")]
    [InlineData("lines-missing", "lines")]
    [InlineData("lines-empty", "lines")]
    [InlineData("line-reservation-missing", "lines[0].stockReservationId")]
    [InlineData("line-version-missing", "lines[0].expectedReservationVersion")]
    [InlineData("line-duplicate", "lines[1]")]
    public async Task Dispatch_Rejects_Invalid_Input_And_Changes_Nothing(string invalidCase, string key)
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var request = Dispatch(0, (r.Id, r.Version));
        switch (invalidCase)
        {
            case "request-id-missing": request.RequestId = null; break;
            case "version-missing": request.ExpectedOrderVersion = null; break;
            case "actor-missing": request.ActorReference = " "; break;
            case "reference-too-long": request.Reference = new string('x', 101); break;
            case "lines-missing": request.Lines = null; break;
            case "lines-empty": request.Lines = new List<CreateSalesDispatchLineDto>(); break;
            case "line-reservation-missing": request.Lines[0].StockReservationId = null; break;
            case "line-version-missing": request.Lines[0].ExpectedReservationVersion = null; break;
            default: request.Lines.Add(new CreateSalesDispatchLineDto { StockReservationId = r.Id, ExpectedReservationVersion = r.Version }); break;
        }

        var result = await DispatchAsync(request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        var (content, movements, reservations, dispatches, _, _, _) = await StateAsync(_seed.ApprovedBox);
        Assert.Equal((0m, 0, 0), (content.DispatchedQuantity, movements.Count, dispatches));
        Assert.All(reservations, x => Assert.Equal("Active", x.Status));
    }

    [Fact]
    public async Task Dispatch_Unknown_Order_Is_NotFound()
    {
        await ArrangeAsync();
        Assert.Equal(ServiceResultStatus.NotFound, (await DispatchAsync(Dispatch(0, (1, 0)), orderId: 999_999)).Status);
    }

    [Theory]
    [InlineData("stale-order-version")]
    [InlineData("stale-reservation-version")]
    [InlineData("foreign-reservation")]
    [InlineData("released")]
    [InlineData("on-hold")]
    [InlineData("already-consumed")]
    [InlineData("order-cancelled")]
    public async Task Dispatch_Business_Rule_Violations_Are_Conflict_Without_Change(string scenario)
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var request = Dispatch((await OrderAsync()).Version, (r.Id, r.Version));
        switch (scenario)
        {
            case "stale-order-version":
                request.ExpectedOrderVersion = 99;
                break;
            case "stale-reservation-version":
                request.Lines[0].ExpectedReservationVersion = 5;
                break;
            case "foreign-reservation":
            {
                var (otherOrder, otherLine, _) = await SalesTestData.SeedOrderAsync(_factory, _seed);
                var foreign = await ReservedAsync(_seed.SecondPalletApprovedBox, 1m, 4m, otherLine, orderId: otherOrder);
                request.Lines[0] = new CreateSalesDispatchLineDto { StockReservationId = foreign.Id, ExpectedReservationVersion = foreign.Version };
                break;
            }
            case "released":
                Assert.Equal(ServiceResultStatus.Ok, (await ReleaseAsync(r.Id, r.Version)).Status);
                request.Lines[0].ExpectedReservationVersion = 1;
                break;
            case "on-hold":
                Assert.Equal(ServiceResultStatus.Ok, await BlockAsync(_seed.ApprovedBox));
                break;
            case "already-consumed":
                Assert.Equal(ServiceResultStatus.Created, (await DispatchAsync(Dispatch(request.ExpectedOrderVersion!.Value, (r.Id, r.Version)))).Status);
                request.ExpectedOrderVersion = (await OrderAsync()).Version;
                request.Lines[0].ExpectedReservationVersion = 1;
                break;
            case "order-cancelled":
                Assert.Equal(ServiceResultStatus.Ok, (await ReleaseAsync(r.Id, r.Version)).Status);
                Assert.Equal(ServiceResultStatus.Ok, (await CancelAsync((await OrderAsync()).Version)).Status);
                request.ExpectedOrderVersion = (await OrderAsync()).Version;
                request.Lines[0].ExpectedReservationVersion = 1;
                break;
        }
        var before = await StateAsync(_seed.ApprovedBox);

        var result = await DispatchAsync(request);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        var after = await StateAsync(_seed.ApprovedBox);
        Assert.Equal(before.Content.DispatchedQuantity, after.Content.DispatchedQuantity);
        Assert.Equal(before.Movements.Count, after.Movements.Count);
        Assert.Equal(before.Dispatches, after.Dispatches);
        Assert.Equal(before.Reservations.Select(x => (x.Status, x.Version)), after.Reservations.Select(x => (x.Status, x.Version)));
        if (scenario == "on-hold") Assert.Contains("on hold", result.Message);
        if (scenario == "already-consumed") Assert.Contains("already consumed", result.Message);
    }

    [Fact]
    public async Task Dispatch_Refuses_To_Leave_Quantity_And_Kg_Out_Of_Balance_And_Never_Adjusts_Kg()
    {
        await ArrangeAsync(boxQuantity: 100m);
        // 40 box / 160 kg: 30 box / 100 kg en 10 box / 60 kg verbruiken samen alles (ok). Afzonderlijk laat elk een rest met de andere grootheid nog > 0.
        var a = await ReservedAsync(_seed.ApprovedBox, 30m, 100m);
        var b = await ReservedAsync(_seed.ApprovedBox, 10m, 60m);
        var orderVersion = (await OrderAsync()).Version;

        // Alleen a: rest 10 box en 60 kg -> allebei > 0, dus toegestaan.
        var onlyA = await DispatchAsync(Dispatch(orderVersion, (a.Id, a.Version)));
        Assert.Equal(ServiceResultStatus.Created, onlyA.Status);
        var content = (await StateAsync(_seed.ApprovedBox)).Content;
        Assert.Equal((30m, 100m), (content.DispatchedQuantity, content.DispatchedNetWeightKg));

        // Een reservering die de rest-hoeveelheid tot 0 brengt maar niet de kg kan via de API niet bestaan (gestrande rest);
        // simuleer dat hier via b met een afwijkende kg in de reservering en controleer dat verzenden weigert zonder kg aan te passen.
        await using (var db = _factory.CreateContext())
        {
            var stored = await db.StockReservations.FindAsync(b.Id);
            stored!.NetWeightKg = 50m; // 10 box / 50 kg: hoeveelheid naar 0, kg blijft 10 over
            await db.SaveChangesAsync();
        }
        var result = await DispatchAsync(Dispatch((await OrderAsync()).Version, (b.Id, b.Version + 1)));
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("reach zero together", result.Message);
        content = (await StateAsync(_seed.ApprovedBox)).Content;
        Assert.Equal((30m, 100m), (content.DispatchedQuantity, content.DispatchedNetWeightKg));
    }

    // ======================== Idempotentie ========================

    [Fact]
    public async Task Exact_Replay_Returns_The_Same_Dispatch_Without_New_Consumption_Or_Movements()
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var request = Dispatch((await OrderAsync()).Version, (r.Id, r.Version));
        var first = await DispatchAsync(request);
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        var before = await StateAsync(_seed.ApprovedBox);

        var retry = await DispatchAsync(request);

        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        var after = await StateAsync(_seed.ApprovedBox);
        Assert.Equal((before.Content.DispatchedQuantity, before.Movements.Count, before.Dispatches, before.DispatchLines, before.Consumed.Count, before.OrderEvents.Count),
            (after.Content.DispatchedQuantity, after.Movements.Count, after.Dispatches, after.DispatchLines, after.Consumed.Count, after.OrderEvents.Count));
        Assert.Equal(before.Reservations.Select(x => x.Version), after.Reservations.Select(x => x.Version));
    }

    [Theory]
    [InlineData("other-order")]
    [InlineData("other-actor")]
    [InlineData("other-reference")]
    [InlineData("other-order-version")]
    [InlineData("other-reservations")]
    [InlineData("other-reservation-version")]
    public async Task Reused_Dispatch_RequestId_With_Different_Payload_Is_Conflict(string change)
    {
        await ArrangeAsync();
        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var r2 = await ReservedAsync(_seed.SecondPalletApprovedBox, 5m, 20m);
        var request = Dispatch((await OrderAsync()).Version, (r1.Id, r1.Version));
        Assert.Equal(ServiceResultStatus.Created, (await DispatchAsync(request)).Status);
        var orderId = _orderId;
        switch (change)
        {
            case "other-order": (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, _seed); break;
            case "other-actor": request.ActorReference = "office-2"; break;
            case "other-reference": request.Reference = "VRB-OTHER"; break;
            case "other-order-version": request.ExpectedOrderVersion += 1; break;
            case "other-reservations": request.Lines.Add(new CreateSalesDispatchLineDto { StockReservationId = r2.Id, ExpectedReservationVersion = r2.Version }); break;
            default: request.Lines[0].ExpectedReservationVersion += 1; break;
        }

        var result = await DispatchAsync(request, orderId);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("already used", result.Message);
        Assert.Equal(1, (await StateAsync(_seed.ApprovedBox)).Dispatches);
    }

    // ======================== Gelijktijdigheid ========================

    [Theory]
    [InlineData("dispatch-saves-last")]
    [InlineData("other-saves-last")]
    public async Task Dispatch_And_Cancel_Race_Has_Exactly_One_Winner(string order)
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var version = (await OrderAsync()).Version;
        ServiceResultStatus inner = default, outer;
        if (order == "dispatch-saves-last")
        {
            outer = (await DispatchAsync(Dispatch(version, (r.Id, r.Version)), interceptors: new ConcurrentSaveInterceptor(async () => inner = (await CancelAsync(version)).Status))).Status;
        }
        else
        {
            outer = (await CancelAsync(version, new ConcurrentSaveInterceptor(async () => inner = (await DispatchAsync(Dispatch(version, (r.Id, r.Version)))).Status))).Status;
        }
        Assert.Equal(ServiceResultStatus.Conflict, outer);
        Assert.True(inner is ServiceResultStatus.Ok or ServiceResultStatus.Created);
        var state = await StateAsync(_seed.ApprovedBox);
        var reservation = Assert.Single(state.Reservations);
        Assert.True(reservation.Status is "Consumed" or "Released");
        Assert.Equal(reservation.Status == "Consumed" ? 1 : 0, state.Movements.Count);
    }

    [Theory]
    [InlineData("dispatch-saves-last")]
    [InlineData("release-saves-last")]
    public async Task Dispatch_And_Release_Race_Has_Exactly_One_Winner(string order)
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var version = (await OrderAsync()).Version;
        ServiceResultStatus inner = default, outer;
        if (order == "dispatch-saves-last")
        {
            outer = (await DispatchAsync(Dispatch(version, (r.Id, r.Version)), interceptors: new ConcurrentSaveInterceptor(async () => inner = (await ReleaseAsync(r.Id, r.Version)).Status))).Status;
        }
        else
        {
            outer = (await ReleaseAsync(r.Id, r.Version, new ConcurrentSaveInterceptor(async () => inner = (await DispatchAsync(Dispatch(version, (r.Id, r.Version)))).Status))).Status;
        }
        Assert.Equal(ServiceResultStatus.Conflict, outer);
        Assert.True(inner is ServiceResultStatus.Ok or ServiceResultStatus.Created);
        var state = await StateAsync(_seed.ApprovedBox);
        Assert.Equal(1, Assert.Single(state.Reservations).Version);
    }

    [Theory]
    [InlineData("dispatch-saves-last")]
    [InlineData("block-saves-last")]
    public async Task Dispatch_And_Quality_Block_Race_Has_Exactly_One_Winner_And_Never_Dispatches_Blocked_Content(string order)
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var version = (await OrderAsync()).Version;
        ServiceResultStatus inner = default, outer;
        if (order == "dispatch-saves-last")
        {
            outer = (await DispatchAsync(Dispatch(version, (r.Id, r.Version)), interceptors: new ConcurrentSaveInterceptor(async () => inner = await BlockAsync(_seed.ApprovedBox)))).Status;
        }
        else
        {
            outer = await BlockAsync(_seed.ApprovedBox, new ConcurrentSaveInterceptor(async () => inner = (await DispatchAsync(Dispatch(version, (r.Id, r.Version)))).Status));
        }
        Assert.Equal(ServiceResultStatus.Conflict, outer);
        Assert.True(inner is ServiceResultStatus.Ok or ServiceResultStatus.Created);
        var state = await StateAsync(_seed.ApprovedBox);
        // Óf verzonden op Approved inhoud, óf geblokkeerd met de reservering nog Active: nooit verzonden op Blocked.
        Assert.True((state.Content.QualityStatus == "Approved" && state.Reservations.Single().Status == "Consumed")
            || (state.Content.QualityStatus == "Blocked" && state.Reservations.Single().Status == "Active" && state.Movements.Count == 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_Concurrent_Dispatches_Of_The_Same_Reservation_Consume_It_Once(bool sameRequestId)
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var version = (await OrderAsync()).Version;
        var first = Dispatch(version, (r.Id, r.Version));
        var second = sameRequestId
            ? new CreateSalesDispatchDto { RequestId = first.RequestId, ExpectedOrderVersion = version, ActorReference = "office-1", Reference = "VRB-2026-001", Lines = first.Lines.Select(l => new CreateSalesDispatchLineDto { StockReservationId = l.StockReservationId, ExpectedReservationVersion = l.ExpectedReservationVersion }).ToList() }
            : Dispatch(version, (r.Id, r.Version));
        ServiceResultStatus inner = default;

        var outer = (await DispatchAsync(first, interceptors: new ConcurrentSaveInterceptor(async () => inner = (await DispatchAsync(second)).Status))).Status;

        Assert.Equal(ServiceResultStatus.Created, inner);
        Assert.Equal(sameRequestId ? ServiceResultStatus.Ok : ServiceResultStatus.Conflict, outer);
        var state = await StateAsync(_seed.ApprovedBox);
        Assert.Equal((10m, 1, 1, 1), (state.Content.DispatchedQuantity, state.Movements.Count, state.Dispatches, state.DispatchLines));
    }

    [Fact]
    public async Task Dispatch_And_A_Reserve_That_Would_Exceed_On_Hand_Have_Exactly_One_Winner()
    {
        // Inhoud 40 box: reservering A 30 box wordt verzonden; gelijktijdig wil B 15 box reserveren op dezelfde inhoud (past alleen vóór de verzending niet, erna wel: voorraad 10).
        await ArrangeAsync(boxQuantity: 100m);
        var a = await ReservedAsync(_seed.ApprovedBox, 30m, 120m);
        var version = (await OrderAsync()).Version;
        ServiceResultStatus reserve = default;
        var outer = (await DispatchAsync(Dispatch(version, (a.Id, a.Version)), interceptors: new ConcurrentSaveInterceptor(async () => reserve = (await ReserveAsync(_seed.ApprovedBox, 10m, 40m)).Status))).Status;
        Assert.Equal(ServiceResultStatus.Created, reserve);
        Assert.Equal(ServiceResultStatus.Conflict, outer); // de inhoudsregel is intussen aangeraakt: herladen en opnieuw beslissen
        var retry = await DispatchAsync(Dispatch((await OrderAsync()).Version, (a.Id, a.Version)));
        Assert.Equal(ServiceResultStatus.Created, retry.Status);
        var state = await StateAsync(_seed.ApprovedBox);
        Assert.Equal(30m, state.Content.DispatchedQuantity);
        Assert.Equal(10m, state.Reservations.Where(x => x.Status == "Active").Sum(x => x.Quantity));
        // Guard-regel 6 na de verzending: Active 10 <= voorraad 10.
        Assert.Equal(ServiceResultStatus.Conflict, (await ReserveAsync(_seed.ApprovedBox, 1m, 4m)).Status);
    }

    [Fact]
    public async Task Cancellation_Is_Refused_After_A_Dispatch_And_Release_Of_Consumed_Is_Refused()
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        Assert.Equal(ServiceResultStatus.Created, (await DispatchAsync(Dispatch((await OrderAsync()).Version, (r.Id, r.Version)))).Status);
        var cancel = await CancelAsync((await OrderAsync()).Version);
        Assert.Equal(ServiceResultStatus.Conflict, cancel.Status);
        Assert.Contains("consumed", cancel.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ServiceResultStatus.Conflict, (await ReleaseAsync(r.Id, 1)).Status);
    }

    [Fact]
    public async Task List_Returns_Dispatches_Of_The_Order_Oldest_First()
    {
        await ArrangeAsync();
        var r1 = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var r2 = await ReservedAsync(_seed.SecondPalletApprovedBox, 5m, 20m);
        var d1 = await DispatchAsync(Dispatch((await OrderAsync()).Version, (r1.Id, r1.Version)));
        var d2 = await DispatchAsync(Dispatch((await OrderAsync()).Version, (r2.Id, r2.Version)));
        await using var db = _factory.CreateContext();
        var list = (await new SalesDispatchService(db).ListAsync(_orderId)).Value;
        Assert.Equal(new[] { d1.Value.Id, d2.Value.Id }, list.Select(d => d.Id));
        Assert.Equal(ServiceResultStatus.NotFound, (await new SalesDispatchService(db).ListAsync(999_999)).Status);
        var order = (await new SalesOrderService(db).GetAsync(_orderId)).Value;
        var consumed = order.Lines.Single(l => l.Id == _boxLine).Reservations.Single(x => x.Id == r1.Id);
        Assert.Equal((d1.Value.Id, "VRB-2026-001"), (consumed.DispatchId, consumed.DispatchReference));
        Assert.NotNull(consumed.ConsumedAt);
    }

    [Fact]
    public async Task Unexpected_Database_Errors_Are_Not_Translated_To_Conflict()
    {
        await ArrangeAsync();
        var r = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var failing = new ThrowingSaveInterceptor(new DbUpdateException("disk full", new InvalidOperationException("I/O error")));
        var version = (await OrderAsync()).Version;
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => DispatchAsync(Dispatch(version, (r.Id, r.Version)), interceptors: failing));
        Assert.Equal("disk full", exception.Message);
        Assert.Equal(0, (await StateAsync(_seed.ApprovedBox)).Dispatches);
    }

    private sealed class ThrowingSaveInterceptor : SaveChangesInterceptor
    {
        private readonly Exception _exception;
        public ThrowingSaveInterceptor(Exception exception) => _exception = exception;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw _exception;
    }
}
