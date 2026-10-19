using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Services;

// Annuleren van een verkooporder: atomaire vrijgave van alle Active reserveringen (ook on hold), begrensde audit,
// idempotentie op de volledige payload en precies één winnaar bij gelijktijdig reserveren, vrijgeven of annuleren.
public class SalesOrderCancellationTests : IDisposable
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

    private static CancelSalesOrderDto Cancel(int? version = 0, string reason = "Klant heeft afgezegd", string actor = "office-1", Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        ExpectedOrderVersion = version,
        ActorReference = actor,
        Reason = reason
    };

    private async Task<ServiceResult<SalesOrderDto>> CancelAsync(CancelSalesOrderDto request, int? orderId = null, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new SalesOrderService(db).CancelAsync(orderId ?? _orderId, request);
    }

    private async Task<StockReservationDto> ReservedAsync(int contentLineId, decimal quantity, decimal kg, int? lineId = null, string unit = "box")
    {
        await using var db = _factory.CreateContext();
        var result = await new StockReservationService(db).ReserveAsync(_orderId, lineId ?? _boxLine, new CreateStockReservationDto
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

    private async Task<ServiceResultStatus> BlockAsync(int contentLineId)
    {
        await using var read = _factory.CreateContext();
        var line = await read.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == contentLineId);
        await using var service = _factory.CreateContext();
        return (await new PalletService(service).ChangeQualityStatusAsync(line.PalletId, line.Id, new ChangeQualityStatusDto
        {
            RequestId = Guid.NewGuid(), ExpectedContentLineVersion = line.Version, ToStatus = "Blocked", ActorReference = "qc-1", Reason = "Claim"
        })).Status;
    }

    private async Task<(SalesOrder Order, List<StockReservation> Reservations, List<SalesOrderAuditEvent> OrderEvents, List<PalletAuditEvent> ReleaseEvents)> StateAsync()
    {
        await using var db = _factory.CreateContext();
        var order = await db.SalesOrders.AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == _orderId);
        var reservations = await db.StockReservations.AsNoTracking().Where(r => r.SalesOrderLine.SalesOrderId == _orderId).OrderBy(r => r.Id).ToListAsync();
        var orderEvents = await db.SalesOrderAuditEvents.AsNoTracking().Where(e => e.SalesOrderId == _orderId).OrderBy(e => e.Id).ToListAsync();
        var releaseEvents = await db.PalletAuditEvents.AsNoTracking().Where(e => e.EventType == "ReservationReleased").OrderBy(e => e.Id).ToListAsync();
        return (order, reservations, orderEvents, releaseEvents);
    }

    // ======================== Gelukte annulering ========================

    [Fact]
    public async Task Cancel_Releases_All_Active_Reservations_Including_On_Hold_And_Leaves_Released_Ones_Unchanged()
    {
        await ArrangeAsync();
        var released = await ReservedAsync(_seed.ApprovedBox, 2m, 8m);
        Assert.Equal(ServiceResultStatus.Ok, (await ReleaseAsync(released.Id, released.Version)).Status);
        var onHold = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var fine = await ReservedAsync(_seed.SecondPalletApprovedBox, 5m, 20m);
        var colli = await ReservedAsync(_seed.ApprovedColli, 3m, 16.5m, _colliLine, "colli");
        Assert.Equal(ServiceResultStatus.Ok, await BlockAsync(_seed.ApprovedBox));
        var before = await StateAsync();
        int approvedBoxVersion, secondPalletVersion;
        await using (var read = _factory.CreateContext())
        {
            approvedBoxVersion = (await read.PalletContentLines.FindAsync(_seed.ApprovedBox))!.Version;
            secondPalletVersion = (await read.PalletContentLines.FindAsync(_seed.SecondPalletApprovedBox))!.Version;
        }
        var request = Cancel(version: before.Order.Version, reason: "  Klant heeft afgezegd  ");

        var result = await CancelAsync(request);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(("Cancelled", "Klant heeft afgezegd", "office-1"), (result.Value.Status, result.Value.CancelReason, result.Value.CancelledBy));
        Assert.NotNull(result.Value.CancelledAt);
        Assert.All(result.Value.Lines, l => Assert.Equal(0m, l.ReservedQuantity));
        Assert.All(result.Value.Lines.SelectMany(l => l.Reservations), r => Assert.False(r.OnHold));

        var after = await StateAsync();
        Assert.Equal(("Cancelled", request.RequestId, before.Order.Version + 1), (after.Order.Status, after.Order.CancelRequestId, after.Order.Version));
        Assert.Equal("Klant heeft afgezegd", after.Order.CancelReason);
        // Elke regel is aangeraakt (+1 op de versie die de eerdere reserveringen al hadden verhoogd).
        foreach (var line in after.Order.Lines)
        {
            Assert.Equal(before.Order.Lines.Single(l => l.Id == line.Id).Version + 1, line.Version);
        }
        foreach (var id in new[] { onHold.Id, fine.Id, colli.Id })
        {
            var reservation = after.Reservations.Single(r => r.Id == id);
            Assert.Equal(("Released", "Klant heeft afgezegd", 1), (reservation.Status, reservation.ReleaseReason, reservation.Version));
            Assert.NotNull(reservation.ReleasedAt);
            Assert.Null(reservation.ReleaseRequestId);
        }
        var untouched = after.Reservations.Single(r => r.Id == released.Id);
        Assert.Equal(("Released", "Klant wil andere partij", 1), (untouched.Status, untouched.ReleaseReason, untouched.Version));
        Assert.Equal(before.Reservations.Single(r => r.Id == released.Id).ReleasedAt, untouched.ReleasedAt);

        // Eén orderevent met begrensde samenvatting; per vrijgegeven reservering één palletevent dat terugverwijst.
        var orderEvent = Assert.Single(after.OrderEvents);
        Assert.Equal(("SalesOrderCancelled", "office-1", "Klant heeft afgezegd", request.RequestId), (orderEvent.EventType, orderEvent.ActorReference, orderEvent.Reason, orderEvent.RequestId));
        using var oldValue = JsonDocument.Parse(orderEvent.OldValue!);
        Assert.Equal(before.Order.Version, oldValue.RootElement.GetProperty("version").GetInt32());
        using var newValue = JsonDocument.Parse(orderEvent.NewValue!);
        Assert.Equal(3, newValue.RootElement.GetProperty("releasedReservationCount").GetInt32());
        Assert.Equal(15m, newValue.RootElement.GetProperty("releasedQuantityByUnit").GetProperty("box").GetDecimal());
        Assert.Equal(3m, newValue.RootElement.GetProperty("releasedQuantityByUnit").GetProperty("colli").GetDecimal());
        var cancelEvents = after.ReleaseEvents.Count(e => e.NewValue!.Contains(request.RequestId.ToString()!));
        Assert.Equal(3, cancelEvents);
        Assert.All(after.ReleaseEvents.Where(e => e.NewValue!.Contains("SalesOrderCancelled")), e =>
        {
            Assert.Null(e.RequestId);
            Assert.Equal("office-1", e.ActorReference);
            Assert.NotNull(e.PalletContentLineId);
            using var snapshot = JsonDocument.Parse(e.NewValue!);
            Assert.Equal(_orderId, snapshot.RootElement.GetProperty("salesOrderId").GetInt32());
            Assert.Equal(request.RequestId.ToString(), snapshot.RootElement.GetProperty("cancellationRequestId").GetString());
        });

        await using var db = _factory.CreateContext();
        Assert.Equal(0, await db.Orders.CountAsync() - 1 + await db.StockMovements.CountAsync() + await db.OrderLineAllocations.CountAsync());
        // Elke inhoudsregel met een vrijgegeven reservering is precies één keer aangeraakt.
        Assert.Equal(approvedBoxVersion + 1, (await db.PalletContentLines.FindAsync(_seed.ApprovedBox))!.Version);
        Assert.Equal(secondPalletVersion + 1, (await db.PalletContentLines.FindAsync(_seed.SecondPalletApprovedBox))!.Version);
    }

    [Fact]
    public async Task Cancel_Without_Reservations_Touches_Lines_And_Writes_One_Order_Event()
    {
        await ArrangeAsync();
        var result = await CancelAsync(Cancel());
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var state = await StateAsync();
        Assert.All(state.Order.Lines, l => Assert.Equal(1, l.Version));
        Assert.Single(state.OrderEvents);
        Assert.Empty(state.ReleaseEvents);
        using var newValue = JsonDocument.Parse(state.OrderEvents[0].NewValue!);
        Assert.Equal(0, newValue.RootElement.GetProperty("releasedReservationCount").GetInt32());
    }

    [Fact]
    public async Task A_Reason_Of_Exactly_500_Characters_Is_Stored_Unchanged_On_Order_Reservations_And_Events()
    {
        await ArrangeAsync();
        await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var reason = new string('r', 500);

        var result = await CancelAsync(Cancel(reason: reason));

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var state = await StateAsync();
        Assert.Equal(reason, state.Order.CancelReason);
        Assert.Equal(reason, Assert.Single(state.Reservations).ReleaseReason);
        Assert.Equal(reason, Assert.Single(state.OrderEvents).Reason);
        Assert.Equal(reason, Assert.Single(state.ReleaseEvents).Reason);
        Assert.Equal(ServiceResultStatus.Validation, (await CancelAsync(Cancel(reason: new string('r', 501)))).Status);
    }

    [Fact]
    public async Task Cancel_With_Many_Reservations_Keeps_The_Order_Snapshot_Bounded_And_Links_Every_Release_Event()
    {
        await ArrangeAsync(boxQuantity: 1000m);
        await using (var db = _factory.CreateContext())
        {
            var supplyLine = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox);
            // 80 kleine reserveringen: samen 40 box / 160 kg, exact de inhoud (guard-regel 6).
            for (var i = 0; i < 80; i++)
            {
                db.StockReservations.Add(new StockReservation
                {
                    RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, PalletContentLineId = _seed.ApprovedBox, SupplyCommitmentLineId = supplyLine,
                    Quantity = 0.5m, Unit = "box", NetWeightKg = 2m, Source = "Physical", Status = "Active"
                });
            }
            await db.SaveChangesAsync();
        }

        var result = await CancelAsync(Cancel());

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var state = await StateAsync();
        Assert.Equal(80, state.Reservations.Count(r => r.Status == "Released"));
        var orderEvent = Assert.Single(state.OrderEvents);
        Assert.True(orderEvent.NewValue!.Length < 500, $"snapshot length {orderEvent.NewValue.Length}");
        Assert.DoesNotContain("reservationIds", orderEvent.NewValue);
        using var newValue = JsonDocument.Parse(orderEvent.NewValue);
        Assert.Equal(80, newValue.RootElement.GetProperty("releasedReservationCount").GetInt32());
        Assert.Equal(40m, newValue.RootElement.GetProperty("releasedQuantityByUnit").GetProperty("box").GetDecimal());
        Assert.Equal(160m, newValue.RootElement.GetProperty("releasedNetWeightKg").GetDecimal());
        Assert.Equal(80, state.ReleaseEvents.Count);
        Assert.All(state.ReleaseEvents, e => Assert.Contains(orderEvent.RequestId.ToString()!, e.NewValue!));
        Assert.Equal(80, state.ReleaseEvents.Select(e => JsonDocument.Parse(e.OldValue!).RootElement.GetProperty("reservationId").GetInt32()).Distinct().Count());
    }

    // ======================== Afwijzingen ========================

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("request-id-empty", "requestId")]
    [InlineData("version-missing", "expectedOrderVersion")]
    [InlineData("reason-missing", "reason")]
    [InlineData("reason-blank", "reason")]
    [InlineData("reason-too-long", "reason")]
    [InlineData("actor-missing", "actorReference")]
    [InlineData("actor-too-long", "actorReference")]
    public async Task Cancel_Rejects_Invalid_Input_And_Changes_Nothing(string invalidCase, string key)
    {
        await ArrangeAsync();
        await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var request = invalidCase switch
        {
            "request-id-missing" => new CancelSalesOrderDto { RequestId = null, ExpectedOrderVersion = 0, ActorReference = "office-1", Reason = "Klant heeft afgezegd" },
            "request-id-empty" => Cancel(requestId: Guid.Empty),
            "version-missing" => Cancel(version: null),
            "reason-missing" => Cancel(reason: null),
            "reason-blank" => Cancel(reason: "   "),
            "reason-too-long" => Cancel(reason: new string('x', 501)),
            "actor-missing" => Cancel(actor: " "),
            _ => Cancel(actor: new string('a', 101))
        };

        var result = await CancelAsync(request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        var state = await StateAsync();
        Assert.Equal("Open", state.Order.Status);
        Assert.All(state.Reservations, r => Assert.Equal("Active", r.Status));
        Assert.Empty(state.OrderEvents);
    }

    [Fact]
    public async Task Cancel_Unknown_Order_Is_NotFound()
    {
        await ArrangeAsync();
        Assert.Equal(ServiceResultStatus.NotFound, (await CancelAsync(Cancel(), orderId: 999_999)).Status);
    }

    [Theory]
    [InlineData("stale-version")]
    [InlineData("already-cancelled")]
    [InlineData("consumed")]
    public async Task Cancel_Business_Rule_Violations_Are_Conflict_Without_Change(string scenario)
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var request = Cancel();
        switch (scenario)
        {
            case "stale-version":
                request.ExpectedOrderVersion = 1;
                break;
            case "already-cancelled":
                Assert.Equal(ServiceResultStatus.Ok, (await CancelAsync(Cancel())).Status);
                break;
            case "consumed":
                await SalesTestData.ConsumeAsync(_factory, reservation.Id);
                break;
        }
        var before = await StateAsync();

        var result = await CancelAsync(request);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        var after = await StateAsync();
        Assert.Equal(before.Order.Version, after.Order.Version);
        Assert.Equal(before.Order.Status, after.Order.Status);
        Assert.Equal(before.Reservations.Select(r => (r.Status, r.Version)), after.Reservations.Select(r => (r.Status, r.Version)));
        Assert.Equal(before.OrderEvents.Count, after.OrderEvents.Count);
        if (scenario == "consumed")
        {
            Assert.Contains("consumed", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Open", after.Order.Status);
        }
    }

    [Fact]
    public async Task New_Reservations_On_A_Cancelled_Order_Are_Conflict_And_The_Guard_Blocks_Direct_Writes()
    {
        await ArrangeAsync();
        Assert.Equal(ServiceResultStatus.Ok, (await CancelAsync(Cancel())).Status);

        var viaService = await ReserveAsync(_seed.ApprovedBox, 1m, 4m);
        Assert.Equal(ServiceResultStatus.Conflict, viaService.Status);

        await using var db = _factory.CreateContext();
        db.StockReservations.Add(new StockReservation
        {
            RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, PalletContentLineId = _seed.ApprovedBox,
            SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox),
            Quantity = 1m, Unit = "box", NetWeightKg = 4m, Source = "Physical", Status = "Active"
        });
        await Assert.ThrowsAsync<AgfAdmin.Data.TraceabilityViolationException>(() => db.SaveChangesAsync());
    }

    // ======================== Idempotentie ========================

    [Fact]
    public async Task Exact_Cancel_Retry_Returns_Current_State_Without_New_Events_Or_Version_Change()
    {
        await ArrangeAsync();
        await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var request = Cancel(reason: "  Klant heeft afgezegd ");
        Assert.Equal(ServiceResultStatus.Ok, (await CancelAsync(request)).Status);
        var before = await StateAsync();

        var retry = await CancelAsync(new CancelSalesOrderDto { RequestId = request.RequestId, ExpectedOrderVersion = 0, ActorReference = "office-1", Reason = "Klant heeft afgezegd" });

        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(("Cancelled", "office-1"), (retry.Value.Status, retry.Value.CancelledBy));
        var after = await StateAsync();
        Assert.Equal(before.Order.Version, after.Order.Version);
        Assert.Equal(before.OrderEvents.Count, after.OrderEvents.Count);
        Assert.Equal(before.ReleaseEvents.Count, after.ReleaseEvents.Count);
        Assert.Equal(before.Reservations.Select(r => r.Version), after.Reservations.Select(r => r.Version));
    }

    [Theory]
    [InlineData("other-order")]
    [InlineData("other-actor")]
    [InlineData("other-reason")]
    [InlineData("other-version")]
    public async Task Reused_Cancel_RequestId_With_Different_Payload_Is_Conflict(string change)
    {
        await ArrangeAsync();
        var request = Cancel();
        Assert.Equal(ServiceResultStatus.Ok, (await CancelAsync(request)).Status);
        var (otherOrder, _, _) = await SalesTestData.SeedOrderAsync(_factory, _seed);
        var changed = new CancelSalesOrderDto { RequestId = request.RequestId, ExpectedOrderVersion = 0, ActorReference = "office-1", Reason = "Klant heeft afgezegd" };
        var orderId = _orderId;
        switch (change)
        {
            case "other-order": orderId = otherOrder; break;
            case "other-actor": changed.ActorReference = "office-2"; break;
            case "other-reason": changed.Reason = "Andere reden"; break;
            default: changed.ExpectedOrderVersion = 1; break;
        }

        var result = await CancelAsync(changed, orderId);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains("already used", result.Message);
        await using var db = _factory.CreateContext();
        Assert.Equal("Open", (await db.SalesOrders.FindAsync(otherOrder))!.Status);
        Assert.Equal(1, await db.SalesOrderAuditEvents.CountAsync());
    }

    // ======================== Gelijktijdigheid ========================

    [Theory]
    [InlineData("reserve-saves-last")]
    [InlineData("cancel-saves-last")]
    public async Task Cancel_And_Reserve_Race_Has_Exactly_One_Winner(string order)
    {
        await ArrangeAsync();
        ServiceResultStatus innerStatus = default;
        ServiceResultStatus outerStatus;
        if (order == "reserve-saves-last")
        {
            var concurrent = new ConcurrentSaveInterceptor(async () => innerStatus = (await CancelAsync(Cancel())).Status);
            outerStatus = (await ReserveAsync(_seed.ApprovedBox, 10m, 40m, concurrent)).Status;
        }
        else
        {
            var concurrent = new ConcurrentSaveInterceptor(async () => innerStatus = (await ReserveAsync(_seed.ApprovedBox, 10m, 40m)).Status);
            outerStatus = (await CancelAsync(Cancel(), interceptors: concurrent)).Status;
        }

        Assert.Equal(ServiceResultStatus.Conflict, outerStatus);
        Assert.True(innerStatus is ServiceResultStatus.Ok or ServiceResultStatus.Created);
        var state = await StateAsync();
        // Nooit allebei: óf de order is Open met precies één Active reservering, óf geannuleerd zonder Active reserveringen.
        if (state.Order.Status == "Open")
        {
            Assert.Single(state.Reservations, r => r.Status == "Active");
            Assert.Empty(state.OrderEvents);
        }
        else
        {
            Assert.DoesNotContain(state.Reservations, r => r.Status == "Active");
        }
    }

    [Theory]
    [InlineData("release-saves-last")]
    [InlineData("cancel-saves-last")]
    public async Task Cancel_And_Release_Race_Has_Exactly_One_Winner_And_One_Release_Event(string order)
    {
        await ArrangeAsync();
        var reservation = await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        ServiceResultStatus innerStatus = default;
        ServiceResultStatus outerStatus;
        if (order == "release-saves-last")
        {
            var concurrent = new ConcurrentSaveInterceptor(async () => innerStatus = (await CancelAsync(Cancel())).Status);
            outerStatus = (await ReleaseAsync(reservation.Id, reservation.Version, concurrent)).Status;
        }
        else
        {
            var concurrent = new ConcurrentSaveInterceptor(async () => innerStatus = (await ReleaseAsync(reservation.Id, reservation.Version)).Status);
            outerStatus = (await CancelAsync(Cancel(), interceptors: concurrent)).Status;
        }

        Assert.Equal((ServiceResultStatus.Ok, ServiceResultStatus.Conflict), (innerStatus, outerStatus));
        var state = await StateAsync();
        var released = Assert.Single(state.Reservations);
        Assert.Equal(("Released", 1), (released.Status, released.Version));
        Assert.Single(state.ReleaseEvents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_Concurrent_Cancellations_Have_One_Winner_Or_Both_Return_Ok_With_The_Same_RequestId(bool sameRequestId)
    {
        await ArrangeAsync();
        await ReservedAsync(_seed.ApprovedBox, 10m, 40m);
        var first = Cancel();
        var second = sameRequestId ? new CancelSalesOrderDto { RequestId = first.RequestId, ExpectedOrderVersion = 0, ActorReference = "office-1", Reason = first.Reason } : Cancel();
        ServiceResultStatus innerStatus = default;
        var concurrent = new ConcurrentSaveInterceptor(async () => innerStatus = (await CancelAsync(second)).Status);

        var outerStatus = (await CancelAsync(first, interceptors: concurrent)).Status;

        Assert.Equal(ServiceResultStatus.Ok, innerStatus);
        Assert.Equal(sameRequestId ? ServiceResultStatus.Ok : ServiceResultStatus.Conflict, outerStatus);
        var state = await StateAsync();
        Assert.Equal(("Cancelled", 1), (state.Order.Status, state.Order.Version));
        Assert.Single(state.OrderEvents);
        Assert.Single(state.ReleaseEvents);
    }

    // ======================== Databasefouten ========================

    [Fact]
    public void Only_Concurrency_Conflicts_And_Unique_Violations_Are_Recognised()
    {
        Assert.True(DbErrors.IsConcurrencyConflict(new DbUpdateConcurrencyException("stale")));
        Assert.False(DbErrors.IsConcurrencyConflict(new DbUpdateException("other")));
        Assert.True(DbErrors.IsUniqueViolation(new DbUpdateException("save", new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: SalesOrders.CancelRequestId'.", 19))));
        Assert.False(DbErrors.IsUniqueViolation(new DbUpdateException("save", new SqliteException("SQLite Error 19: 'CHECK constraint failed: CK_SalesOrders_CancelledConsistent'.", 19))));
        Assert.False(DbErrors.IsUniqueViolation(new DbUpdateException("save", new InvalidOperationException("connection lost"))));
        Assert.False(DbErrors.IsUniqueViolation(new DbUpdateException("save")));
    }

    [Fact]
    public async Task Unexpected_Database_Errors_Are_Not_Translated_To_Conflict()
    {
        await ArrangeAsync();
        // Een vreemde fout tijdens het opslaan (geen concurrency, geen unieke sleutel) moet zichtbaar blijven.
        var failing = new ThrowingSaveInterceptor(new DbUpdateException("disk full", new InvalidOperationException("I/O error")));
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => CancelAsync(Cancel(), interceptors: failing));
        Assert.Equal("disk full", exception.Message);
        var state = await StateAsync();
        Assert.Equal("Open", state.Order.Status);
    }

    private sealed class ThrowingSaveInterceptor : SaveChangesInterceptor
    {
        private readonly Exception _exception;
        public ThrowingSaveInterceptor(Exception exception) => _exception = exception;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw _exception;
    }
}
