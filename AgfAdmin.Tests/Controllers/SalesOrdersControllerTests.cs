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
        return await action(new SalesOrdersController(new SalesOrderService(db), new StockReservationService(db)));
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
}
