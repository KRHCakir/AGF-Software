using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static AgfAdmin.Tests.Infrastructure.DirectTradeTestFlow;

namespace AgfAdmin.Tests.Services;

// Intrekken van een afleverbevestiging, voor magazijn en DirectTrade: de bevestiging blijft bewaard, "actief" komt uit één definitie
// (DeliveryState) en telt nergens meer mee na intrekken, geen voorraad- of capaciteitswijziging, herbevestigen met volgnummer 2, 3, ...,
// correctie weer mogelijk zonder actieve bevestiging, idempotentie, en races op dezelfde oorspronkelijke orderversie in beide vensters
// (ná de guard via een interceptor, vóór de guard via BeforeGuardDbContext).
public class DeliveryRetractionServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;

    public void Dispose() => _factory.Dispose();

    private sealed record Shipment(string Kind, int Id, SalesDispatchDto Dispatch, DirectTradeShipmentDto DirectTrade);

    private async Task ArrangeAsync(decimal box = 40m)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _) = await SalesTestData.SeedOrderAsync(_factory, _seed, box, 10m);
    }

    private async Task<SalesDispatchDto> WarehouseDispatchAsync(decimal quantity = 10m)
    {
        int reservationId;
        await using (var db = _factory.CreateContext())
        {
            var reservation = (await new StockReservationService(db).ReserveAsync(_orderId, _boxLine, new CreateStockReservationDto
            {
                RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = quantity, Unit = "box", NetWeightKg = quantity * 4, ActorReference = "office-1"
            })).Value;
            reservationId = reservation.Id;
        }
        int dispatchId;
        await using (var db = _factory.CreateContext())
        {
            var dispatch = new SalesDispatch { RequestId = Guid.NewGuid(), SalesOrderId = _orderId, Reference = "VRB", ActorReference = "office-1", DispatchedAt = new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc) };
            await SalesTestData.ConsumeInContextAsync(db, reservationId, into: dispatch);
            db.SalesDispatches.Add(dispatch);
            await db.SaveChangesAsync();
            dispatchId = dispatch.Id;
        }
        await using var read = _factory.CreateContext();
        return (await new SalesDispatchService(read).ListAsync(_orderId)).Value.Single(d => d.Id == dispatchId);
    }

    private async Task<Shipment> ShipmentAsync(string kind)
    {
        if (kind == "warehouse")
        {
            var dispatch = await WarehouseDispatchAsync();
            return new Shipment(kind, dispatch.Id, dispatch, null);
        }
        var shipment = await ShipAsync(_factory, _orderId, (await AllocateAsync(_factory, _orderId, _boxLine, _seed.DirectTradeSourceLine, 10m), 40.5m, "LOT-DT-SRC"));
        return new Shipment(kind, shipment.Id, null, shipment);
    }

    private Task<int> VersionAsync() => OrderVersionAsync(_factory, _orderId);

    private static ConfirmDeliveryDto ConfirmBody(int version, Guid? requestId = null, DateOnly? on = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", DeliveredOn = on ?? Today, ReceiptReference = "CMR-1", DeliveredInFull = true
    };

    private static RetractDeliveryDto RetractBody(int? version, Guid? requestId = null, string reason = "Verkeerde verzending aangeklikt", bool? recordedInError = true, string actor = "office-1") => new()
    {
        RequestId = requestId ?? Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = actor, Reason = reason, RecordedInError = recordedInError
    };

    private async Task<ServiceResult<DeliveryConfirmationDto>> ConfirmAsync(Shipment s, ConfirmDeliveryDto body, AppDbContext db = null)
    {
        var owned = db == null;
        db ??= _factory.CreateContext();
        try
        {
            var service = new DeliveryConfirmationService(db, _factory.Calendar);
            return s.Kind == "warehouse" ? await service.ConfirmWarehouseAsync(_orderId, s.Id, body) : await service.ConfirmDirectTradeAsync(_orderId, s.Id, body);
        }
        finally { if (owned) await db.DisposeAsync(); }
    }

    private async Task<ServiceResult<DeliveryRetractionDto>> RetractAsync(Shipment s, int confirmationId, RetractDeliveryDto body, AppDbContext db = null, int? orderId = null)
    {
        var owned = db == null;
        db ??= _factory.CreateContext();
        try
        {
            var service = new DeliveryRetractionService(db);
            return s.Kind == "warehouse"
                ? await service.RetractWarehouseAsync(orderId ?? _orderId, s.Id, confirmationId, body)
                : await service.RetractDirectTradeAsync(orderId ?? _orderId, s.Id, confirmationId, body);
        }
        finally { if (owned) await db.DisposeAsync(); }
    }

    private async Task<ServiceResultStatus> CorrectAsync(Shipment s, int version, AppDbContext db = null, int reservationVersion = 1)
    {
        var owned = db == null;
        db ??= _factory.CreateContext();
        try
        {
            if (s.Kind == "warehouse")
            {
                return (await new SalesDispatchCorrectionService(db).CorrectAsync(_orderId, s.Id, new CorrectSalesDispatchDto
                {
                    RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = "Verkeerd gescand", GoodsNeverLeftWarehouse = true,
                    Lines = s.Dispatch.Lines.Select(l => new CreateSalesDispatchLineDto { StockReservationId = l.StockReservationId, ExpectedReservationVersion = reservationVersion }).ToList()
                })).Status;
            }
            var body = Correction(version, s.DirectTrade);
            foreach (var line in body.Lines) line.ExpectedReservationVersion = reservationVersion;
            return (await new DirectTradeShipmentCorrectionService(db).CorrectAsync(_orderId, s.Id, body)).Status;
        }
        finally { if (owned) await db.DisposeAsync(); }
    }

    private async Task<int> ConfirmedAsync(Shipment s)
    {
        var result = await ConfirmAsync(s, ConfirmBody(await VersionAsync()));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value.Id;
    }

    private async Task<SalesOrderLineDto> LineAsync()
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(_orderId)).Value.Lines.Single(l => l.Id == _boxLine);
    }

    private async Task<SalesOrderDto> OrderAsync()
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(_orderId)).Value;
    }

    private async Task<DeliveryConfirmationDto> ActiveAsync(Shipment s) => (await HistoryAsync(s)).Active;

    private async Task<(DeliveryConfirmationDto Active, List<DeliveryConfirmationDto> History, bool Recorded)> HistoryAsync(Shipment s)
    {
        await using var db = _factory.CreateContext();
        if (s.Kind == "warehouse")
        {
            var d = (await new SalesDispatchService(db).ListAsync(_orderId)).Value.Single(x => x.Id == s.Id);
            return (d.Delivery, d.DeliveryHistory, d.Delivery != null);
        }
        var t = (await new DirectTradeShipmentService(db, _factory.Calendar).ListAsync(_orderId)).Value.Single(x => x.Id == s.Id);
        return (t.Delivery, t.DeliveryHistory, t.DeliveryRecorded);
    }

    // Alles wat bevestigen of intrekken nooit mag veranderen.
    private async Task<string> StockFingerprintAsync()
    {
        await using var db = _factory.CreateContext();
        return JsonSerializer.Serialize(new
        {
            contents = await db.PalletContentLines.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Quantity, c.DispatchedQuantity, c.DispatchedNetWeightKg, c.QualityStatus, c.Version }).ToListAsync(),
            reservations = await db.StockReservations.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Status, r.Quantity, r.Version }).ToListAsync(),
            movements = await db.StockMovements.CountAsync(),
            sources = await db.SupplyCommitmentLines.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Version }).ToListAsync(),
            lines = (await OrderAsync()).Lines.Select(l => new { l.ReservedQuantity, l.AllocatedQuantity, l.DispatchedQuantity, l.DispatchedNetWeightKg, l.OpenQuantity, l.Fulfilled }),
            availability = (await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = _seed.AvocadoId })).Value.Variants,
            supply = (await new SupplyCommitmentService(db).GetAsync(_seed.DirectTradeCommitmentId)).Value.Lines.Select(l => new { l.DirectTradeAllocatedQuantity, l.DirectTradeRemainingQuantity }),
        });
    }

    // Volledige schrijfstaat voor de races: elke rij die bevestigen, intrekken of corrigeren kan raken, met beide audittabellen.
    private async Task<string> WriteStateAsync()
    {
        await using var db = _factory.CreateContext();
        return JsonSerializer.Serialize(new
        {
            deliveries = await db.ShipmentDeliveryConfirmations.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.ShipmentKind, c.SalesDispatchId, c.DirectTradeShipmentId, c.Sequence }).ToListAsync(),
            retractions = await db.ShipmentDeliveryRetractions.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.ShipmentDeliveryConfirmationId }).ToListAsync(),
            warehouseCorrections = await db.SalesDispatchCorrections.CountAsync(),
            directTradeCorrections = await db.DirectTradeShipmentCorrections.CountAsync(),
            reservations = await db.StockReservations.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Status, r.Version }).ToListAsync(),
            contents = await db.PalletContentLines.AsNoTracking().OrderBy(c => c.Id).Select(c => new { c.Id, c.Quantity, c.DispatchedQuantity, c.QualityStatus, c.Version }).ToListAsync(),
            movements = await db.StockMovements.CountAsync(),
            sources = await db.SupplyCommitmentLines.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Version }).ToListAsync(),
            orders = await db.SalesOrders.AsNoTracking().OrderBy(o => o.Id).Select(o => new { o.Id, o.Status, o.Version }).ToListAsync(),
            salesAudit = await db.SalesOrderAuditEvents.AsNoTracking().OrderBy(e => e.Id).Select(e => new { e.Id, e.EventType, e.RequestId }).ToListAsync(),
            palletAudit = await db.PalletAuditEvents.CountAsync(),
        });
    }

    private async Task<int> AuditCountAsync(string eventType)
    {
        await using var db = _factory.CreateContext();
        return await db.SalesOrderAuditEvents.CountAsync(e => e.EventType == eventType);
    }

    // Race op dezelfde oorspronkelijke orderversie: `outer` doorloopt al zijn controles; in zijn save commit `inner` volledig, ná de
    // guard (interceptor) of vóór de guard (BeforeGuardDbContext). Geeft beide uitkomsten en de staat direct na `inner`.
    private async Task<(ServiceResultStatus Outer, ServiceResultStatus Inner, string AfterInner)> RaceAsync(string window,
        Func<AppDbContext, Task<ServiceResultStatus>> outer, Func<Task<ServiceResultStatus>> inner)
    {
        ServiceResultStatus innerStatus = default;
        string afterInner = null;
        async Task Inner()
        {
            innerStatus = await inner();
            afterInner = await WriteStateAsync();
        }
        await using var db = window == "before-guard" ? _factory.CreateContextBeforeGuard(Inner) : _factory.CreateContext(new ConcurrentSaveInterceptor(Inner));
        var outerStatus = await outer(db);
        Assert.NotNull(afterInner);   // de concurrent commit heeft echt in het venster plaatsgevonden
        return (outerStatus, innerStatus, afterInner);
    }

    // ======================== Basis: intrekken bewaart de bevestiging en verandert niets aan voorraad ========================

    [Theory]
    [InlineData("warehouse")]
    [InlineData("direct-trade")]
    public async Task Retracting_Keeps_The_Confirmation_Changes_No_Stock_And_Ends_The_Delivery(string kind)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync(kind);
        var confirmationId = await ConfirmedAsync(shipment);
        string before;
        await using (var db = _factory.CreateContext()) { before = JsonSerializer.Serialize(await db.ShipmentDeliveryConfirmations.AsNoTracking().Select(c => new { c.Id, c.RequestId, c.ShipmentKind, c.SalesDispatchId, c.DirectTradeShipmentId, c.SalesOrderId, c.DeliveredOn, c.ReceiptReference, c.ActorReference, c.DeliveredInFullConfirmed, c.ConfirmationCode, c.ConfirmationStatement, c.RecordedAt, c.Sequence }).SingleAsync()); }
        var stockBefore = await StockFingerprintAsync();
        Assert.Equal(10m, (await LineAsync()).DeliveredQuantity);

        var result = await RetractAsync(shipment, confirmationId, RetractBody(await VersionAsync()));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var r = result.Value;
        Assert.Equal((confirmationId, kind == "warehouse" ? "Warehouse" : "DirectTrade", "DELIVERY_CONFIRMATION_RECORDED_IN_ERROR", "Deze afleverbevestiging is ten onrechte vastgelegd.", true, "Verkeerde verzending aangeklikt"),
            (r.ShipmentDeliveryConfirmationId, r.ShipmentKind, r.ConfirmationCode, r.ConfirmationStatement, r.RecordedInErrorConfirmed, r.Reason));
        Assert.Equal(DateTimeKind.Utc, r.RecordedAt.Kind);
        Assert.Equal(stockBefore, await StockFingerprintAsync());
        await using (var db = _factory.CreateContext())
        {
            var after = JsonSerializer.Serialize(await db.ShipmentDeliveryConfirmations.AsNoTracking().Select(c => new { c.Id, c.RequestId, c.ShipmentKind, c.SalesDispatchId, c.DirectTradeShipmentId, c.SalesOrderId, c.DeliveredOn, c.ReceiptReference, c.ActorReference, c.DeliveredInFullConfirmed, c.ConfirmationCode, c.ConfirmationStatement, c.RecordedAt, c.Sequence }).SingleAsync());
            Assert.Equal(before, after);   // de oorspronkelijke bevestiging is onveranderd bewaard
            var audit = await db.SalesOrderAuditEvents.AsNoTracking().SingleAsync(e => e.EventType == ReservationRules.DeliveryRetractedEvent);
            Assert.Equal(r.RequestId, audit.RequestId);
            Assert.True(audit.NewValue.Length < 2000);
        }
        var (active, history, recorded) = await HistoryAsync(shipment);
        Assert.Null(active);
        Assert.False(recorded);
        var only = Assert.Single(history);
        Assert.Equal((confirmationId, 1, r.Id), (only.Id, only.Sequence, only.Retraction!.Id));
    }

    // Eén definitie van actief, op elk leespad en in elke controle: een ingetrokken bevestiging telt nergens meer mee.
    [Theory]
    [InlineData("warehouse")]
    [InlineData("direct-trade")]
    public async Task A_Retracted_Confirmation_Counts_Nowhere(string kind)
    {
        await ArrangeAsync(box: 10m);
        var shipment = await ShipmentAsync(kind);
        var confirmationId = await ConfirmedAsync(shipment);
        // De testorder heeft ook een (niet verzonden) colliregel: de order is daardoor nooit volledig afgeleverd; de boxregel wel.
        var deliveredLine = await LineAsync();
        Assert.Equal((10m, true), (deliveredLine.DeliveredQuantity, deliveredLine.Delivered));
        Assert.Equal(ServiceResultStatus.Created, (await RetractAsync(shipment, confirmationId, RetractBody(await VersionAsync()))).Status);

        var order = await OrderAsync();
        var line = order.Lines.Single(l => l.Id == _boxLine);
        Assert.Equal((0m, false, false), (line.DeliveredQuantity, line.Delivered, order.Delivered));                                     // aflevertotalen en status
        var consumed = line.Reservations.Single(x => x.Status == "Consumed");
        Assert.Equal(((int?)null, (DateOnly?)null), (consumed.DeliveryConfirmationId, consumed.DeliveredOn));                             // reserverings-DTO
        Assert.Null(await ActiveAsync(shipment));                                                                                          // verzend-DTO
        Assert.Equal(ServiceResultStatus.Created, (await ConfirmAsync(shipment, ConfirmBody(await VersionAsync()))).Status);               // bevestigen ziet geen actieve
        Assert.Equal(ServiceResultStatus.Created, (await RetractAsync(shipment, (await ActiveAsync(shipment)).Id, RetractBody(await VersionAsync()))).Status);
        Assert.Equal(ServiceResultStatus.Created, await CorrectAsync(shipment, await VersionAsync()));                                    // correctiecontrole
    }

    // Herhaald bevestigen, intrekken, opnieuw bevestigen: volgnummers 1..3, nooit dubbel afgeleverd, steeds hooguit één actief.
    [Theory]
    [InlineData("warehouse")]
    [InlineData("direct-trade")]
    public async Task Repeated_Confirm_Retract_Cycles_Never_Double_The_Delivered_Quantity(string kind)
    {
        await ArrangeAsync(box: 10m);
        var shipment = await ShipmentAsync(kind);
        var stock = await StockFingerprintAsync();
        var delivered = new List<decimal>();
        for (var round = 1; round <= 3; round++)
        {
            var confirmation = await ConfirmAsync(shipment, ConfirmBody(await VersionAsync()));
            Assert.Equal((ServiceResultStatus.Created, round), (confirmation.Status, confirmation.Value.Sequence));
            delivered.Add((await LineAsync()).DeliveredQuantity);
            Assert.True((await LineAsync()).Delivered);
            Assert.Equal(ServiceResultStatus.Conflict, (await ConfirmAsync(shipment, ConfirmBody(await VersionAsync()))).Status);   // nooit twee actieve
            if (round < 3)
            {
                Assert.Equal(ServiceResultStatus.Created, (await RetractAsync(shipment, confirmation.Value.Id, RetractBody(await VersionAsync()))).Status);
                delivered.Add((await LineAsync()).DeliveredQuantity);
                Assert.False((await LineAsync()).Delivered);
            }
        }
        Assert.Equal(new[] { 10m, 0m, 10m, 0m, 10m }, delivered);
        var (active, history, _) = await HistoryAsync(shipment);
        Assert.Equal(new[] { 1, 2, 3 }, history.Select(h => h.Sequence));
        Assert.Equal(new[] { true, true, false }, history.Select(h => h.Retraction != null));
        Assert.Equal(history[2].Id, active.Id);
        Assert.Equal(stock, await StockFingerprintAsync());
        Assert.Equal((3, 2), (await AuditCountAsync(ReservationRules.DeliveryConfirmedEvent), await AuditCountAsync(ReservationRules.DeliveryRetractedEvent)));
    }

    // Een bevestiging zoals de migratie haar achterlaat (bestaande rij, Sequence = 1, nooit via de nieuwe service gemaakt): intrekbaar,
    // en daarna is bevestiging 2 toegestaan. De echte migratie wordt daarnaast op agf_smoke met bestaande historie aangetoond.
    [Theory]
    [InlineData("warehouse")]
    [InlineData("direct-trade")]
    public async Task A_Migrated_Confirmation_Keeps_Number_1_Can_Be_Retracted_And_Allows_Confirmation_2(string kind)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync(kind);
        await using (var raw = _factory.CreateContext())
        {
            var (column, value) = kind == "warehouse" ? ("SalesDispatchId", $"{shipment.Id}, NULL") : ("DirectTradeShipmentId", $"NULL, {shipment.Id}");
            await raw.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"ShipmentDeliveryConfirmations\" (\"RequestId\", \"ShipmentKind\", \"SalesDispatchId\", \"DirectTradeShipmentId\", \"SalesOrderId\", \"DeliveredOn\", \"ReceiptReference\", \"ActorReference\", \"DeliveredInFullConfirmed\", \"ConfirmationCode\", \"ConfirmationStatement\", \"RecordedAt\", \"Sequence\") " +
                $"VALUES ('{Guid.NewGuid()}', '{(kind == "warehouse" ? "Warehouse" : "DirectTrade")}', {value}, {_orderId}, '2026-10-19', NULL, 'legacy', TRUE, 'DELIVERED_IN_FULL', 'Deze verzending is volledig en zonder afwijkingen afgeleverd.', '2026-10-19 12:00:00', 1)");
            _ = column;
        }
        var legacy = await ActiveAsync(shipment);
        Assert.Equal(1, legacy.Sequence);
        Assert.Equal(10m, (await LineAsync()).DeliveredQuantity);

        Assert.Equal(ServiceResultStatus.Created, (await RetractAsync(shipment, legacy.Id, RetractBody(await VersionAsync()))).Status);
        var second = await ConfirmAsync(shipment, ConfirmBody(await VersionAsync()));

        Assert.Equal((ServiceResultStatus.Created, 2), (second.Status, second.Value.Sequence));
        var (active, history, _) = await HistoryAsync(shipment);
        Assert.Equal((second.Value.Id, 2), (active.Id, history.Count));
        Assert.Equal(10m, (await LineAsync()).DeliveredQuantity);
    }

    // ======================== Invoer, bestaan, idempotentie ========================

    [Theory]
    [InlineData("no-request-id", "requestId")]
    [InlineData("no-version", "expectedOrderVersion")]
    [InlineData("no-actor", "actorReference")]
    [InlineData("no-reason", "reason")]
    [InlineData("blank-reason", "reason")]
    [InlineData("long-reason", "reason")]
    [InlineData("not-in-error", "recordedInError")]
    [InlineData("statement-missing", "recordedInError")]
    public async Task Invalid_Input_Is_Rejected_And_Changes_Nothing(string invalidCase, string key)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync("direct-trade");
        var confirmationId = await ConfirmedAsync(shipment);
        var body = RetractBody(await VersionAsync());
        switch (invalidCase)
        {
            case "no-request-id": body.RequestId = Guid.Empty; break;
            case "no-version": body.ExpectedOrderVersion = null; break;
            case "no-actor": body.ActorReference = " "; break;
            case "no-reason": body.Reason = null; break;
            case "blank-reason": body.Reason = "   "; break;
            case "long-reason": body.Reason = new string('r', 501); break;
            case "not-in-error": body.RecordedInError = false; break;
            case "statement-missing": body.RecordedInError = null; break;
        }
        var result = await RetractAsync(shipment, confirmationId, body);
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        await using var db = _factory.CreateContext();
        Assert.Empty(await db.ShipmentDeliveryRetractions.ToListAsync());
    }

    // Het endpoint bepaalt de soort; een bevestiging van een andere verzending, soort of order is 404, ook bij gelijke nummers.
    [Fact]
    public async Task A_Confirmation_Outside_The_Chosen_Kind_Shipment_Or_Order_Is_NotFound_And_The_Other_Kind_Stays_Active()
    {
        await ArrangeAsync();
        var warehouse = await ShipmentAsync("warehouse");
        var directTrade = await ShipmentAsync("direct-trade");
        Assert.Equal(warehouse.Id, directTrade.Id);
        var warehouseConfirmation = await ConfirmedAsync(warehouse);
        var directTradeConfirmation = await ConfirmedAsync(directTrade);
        var version = await VersionAsync();

        Assert.Equal(ServiceResultStatus.NotFound, (await RetractAsync(warehouse, directTradeConfirmation, RetractBody(version))).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await RetractAsync(directTrade, warehouseConfirmation, RetractBody(version))).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await RetractAsync(warehouse with { Id = 999 }, warehouseConfirmation, RetractBody(version))).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await RetractAsync(warehouse, 999, RetractBody(version))).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await RetractAsync(warehouse, warehouseConfirmation, RetractBody(version), orderId: _orderId + 1000)).Status);

        Assert.Equal(ServiceResultStatus.Created, (await RetractAsync(warehouse, warehouseConfirmation, RetractBody(version))).Status);
        Assert.Null(await ActiveAsync(warehouse));
        Assert.Equal(directTradeConfirmation, (await ActiveAsync(directTrade)).Id);   // dezelfde nummering in de andere soort is onaangeroerd
        Assert.Equal(10m, (await LineAsync()).DeliveredQuantity);
    }

    [Fact]
    public async Task Exact_Retry_Returns_The_Same_Retraction_And_Changed_Payloads_Or_A_Second_Retraction_Conflict()
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync("warehouse");
        var confirmationId = await ConfirmedAsync(shipment);
        var version = await VersionAsync();
        var body = RetractBody(version);
        var created = await RetractAsync(shipment, confirmationId, body);
        Assert.Equal(ServiceResultStatus.Created, created.Status);

        var retry = await RetractAsync(shipment, confirmationId, body);
        Assert.Equal((ServiceResultStatus.Ok, created.Value.Id), (retry.Status, retry.Value.Id));
        foreach (var changed in new[] { RetractBody(version, body.RequestId, reason: "Andere reden"), RetractBody(version, body.RequestId, actor: "office-2"), RetractBody(version + 5, body.RequestId) })
        {
            Assert.Equal(ServiceResultStatus.Conflict, (await RetractAsync(shipment, confirmationId, changed)).Status);
        }
        var second = await RetractAsync(shipment, confirmationId, RetractBody(await VersionAsync()));
        Assert.Equal(ServiceResultStatus.Conflict, second.Status);
        Assert.Contains("already been retracted", second.Message);
        Assert.Equal(ServiceResultStatus.Conflict, (await RetractAsync(shipment, confirmationId, RetractBody(version - 1))).Status);
        await using var db = _factory.CreateContext();
        Assert.Single(await db.ShipmentDeliveryRetractions.ToListAsync());
        Assert.Equal(1, await AuditCountAsync(ReservationRules.DeliveryRetractedEvent));
    }

    // Correctie na intrekken mag weer, maar de overige correctievoorwaarden blijven gelden; daarna is bevestigen onmogelijk.
    [Theory]
    [InlineData("warehouse")]
    [InlineData("direct-trade")]
    public async Task After_Retraction_A_Correction_Is_Possible_Again_Under_Its_Own_Conditions(string kind)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync(kind);
        var confirmationId = await ConfirmedAsync(shipment);
        Assert.Equal(ServiceResultStatus.Conflict, await CorrectAsync(shipment, await VersionAsync()));
        Assert.Equal(ServiceResultStatus.Created, (await RetractAsync(shipment, confirmationId, RetractBody(await VersionAsync()))).Status);

        Assert.Equal(ServiceResultStatus.Conflict, await CorrectAsync(shipment, await VersionAsync(), reservationVersion: 0));   // verouderde reserveringsversie blijft 409
        Assert.Equal(ServiceResultStatus.Conflict, await CorrectAsync(shipment, await VersionAsync() - 1));                         // verouderde orderversie blijft 409
        Assert.Equal(ServiceResultStatus.Created, await CorrectAsync(shipment, await VersionAsync()));
        var confirmAfter = await ConfirmAsync(shipment, ConfirmBody(await VersionAsync()));
        Assert.Equal(ServiceResultStatus.Conflict, confirmAfter.Status);
        Assert.Contains("can never be confirmed", confirmAfter.Message);
    }

    // ======================== Races op dezelfde oorspronkelijke orderversie ========================

    // Twee verschillende intrekkingen van dezelfde bevestiging: één 201, één 409, één rij, één auditevent.
    [Theory]
    [InlineData("warehouse", "after-guard")]
    [InlineData("warehouse", "before-guard")]
    [InlineData("direct-trade", "after-guard")]
    [InlineData("direct-trade", "before-guard")]
    public async Task Two_Retractions_On_The_Same_Version_Have_One_Winner(string kind, string window)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync(kind);
        var confirmationId = await ConfirmedAsync(shipment);
        var version = await VersionAsync();

        var race = await RaceAsync(window,
            async db => (await RetractAsync(shipment, confirmationId, RetractBody(version, reason: "Buitenste"), db)).Status,
            async () => (await RetractAsync(shipment, confirmationId, RetractBody(version, reason: "Binnenste"))).Status);

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (race.Inner, race.Outer));
        Assert.Equal(race.AfterInner, await WriteStateAsync());
        Assert.Equal(1, await AuditCountAsync(ReservationRules.DeliveryRetractedEvent));
    }

    // Identieke retries die elkaar overlappen: één rij, de tweede is een replay (200).
    [Theory]
    [InlineData("after-guard")]
    [InlineData("before-guard")]
    public async Task Overlapping_Identical_Retractions_Create_One_And_Replay(string window)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync("direct-trade");
        var confirmationId = await ConfirmedAsync(shipment);
        var body = RetractBody(await VersionAsync());

        var race = await RaceAsync(window,
            async db => (await RetractAsync(shipment, confirmationId, body, db)).Status,
            async () => (await RetractAsync(shipment, confirmationId, body)).Status);

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Ok), (race.Inner, race.Outer));
        Assert.Equal(race.AfterInner, await WriteStateAsync());
    }

    // Intrekken tegenover een correctie op dezelfde versie: zolang de bevestiging actief is, kan de correctie niet winnen (domeinregel).
    // Volgorde 1: de correctie start eerst en wordt al bij haar controles geweigerd. Volgorde 2: de intrekking slaat op en de correctie
    // loopt in dat venster; ze ziet de nog actieve bevestiging. Een nieuw correctiecommando met de verse versie mag daarna wél.
    [Theory]
    [InlineData("warehouse", "correction-first")]
    [InlineData("warehouse", "retraction-first-after-guard")]
    [InlineData("warehouse", "retraction-first-before-guard")]
    [InlineData("direct-trade", "correction-first")]
    [InlineData("direct-trade", "retraction-first-after-guard")]
    [InlineData("direct-trade", "retraction-first-before-guard")]
    public async Task Retraction_Against_Correction_On_The_Same_Version_Always_Lets_The_Retraction_Win(string kind, string order)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync(kind);
        var confirmationId = await ConfirmedAsync(shipment);
        var version = await VersionAsync();
        ServiceResultStatus retraction, correction;
        if (order == "correction-first")
        {
            correction = await CorrectAsync(shipment, version);
            retraction = (await RetractAsync(shipment, confirmationId, RetractBody(version))).Status;
        }
        else
        {
            var race = await RaceAsync(order.EndsWith("before-guard") ? "before-guard" : "after-guard",
                async db => (await RetractAsync(shipment, confirmationId, RetractBody(version), db)).Status,
                () => CorrectAsync(shipment, version));
            (retraction, correction) = (race.Outer, race.Inner);
        }

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (retraction, correction));
        Assert.Equal(0, await AuditCountAsync(kind == "warehouse" ? ReservationRules.OrderDispatchCorrectedEvent : ReservationRules.DirectTradeShipmentCorrectedEvent));
        // Nieuw commando ná intrekken met de verse versie: correctie toegestaan.
        Assert.Equal(ServiceResultStatus.Created, await CorrectAsync(shipment, await VersionAsync()));
    }

    // Intrekken tegenover opnieuw bevestigen op dezelfde versie: herbevestigen ziet de nog actieve bevestiging en verliest altijd. Een
    // nieuw bevestigingscommando met de verse versie na het intrekken slaagt met volgnummer 2.
    [Theory]
    [InlineData("warehouse", "confirm-first")]
    [InlineData("warehouse", "retraction-first-after-guard")]
    [InlineData("warehouse", "retraction-first-before-guard")]
    [InlineData("direct-trade", "confirm-first")]
    [InlineData("direct-trade", "retraction-first-after-guard")]
    [InlineData("direct-trade", "retraction-first-before-guard")]
    public async Task Retraction_Against_Reconfirmation_On_The_Same_Version_Always_Lets_The_Retraction_Win(string kind, string order)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync(kind);
        var confirmationId = await ConfirmedAsync(shipment);
        var version = await VersionAsync();
        ServiceResultStatus retraction, confirm;
        if (order == "confirm-first")
        {
            confirm = (await ConfirmAsync(shipment, ConfirmBody(version))).Status;
            retraction = (await RetractAsync(shipment, confirmationId, RetractBody(version))).Status;
        }
        else
        {
            var race = await RaceAsync(order.EndsWith("before-guard") ? "before-guard" : "after-guard",
                async db => (await RetractAsync(shipment, confirmationId, RetractBody(version), db)).Status,
                async () => (await ConfirmAsync(shipment, ConfirmBody(version))).Status);
            (retraction, confirm) = (race.Outer, race.Inner);
        }

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (retraction, confirm));
        Assert.Equal(1, await AuditCountAsync(ReservationRules.DeliveryConfirmedEvent));
        var fresh = await ConfirmAsync(shipment, ConfirmBody(await VersionAsync()));
        Assert.Equal((ServiceResultStatus.Created, 2), (fresh.Status, fresh.Value.Sequence));
    }

    // Twee herbevestigingen na intrekken op dezelfde versie: precies één nieuwe actieve bevestiging (volgnummer 2).
    [Theory]
    [InlineData("warehouse", "after-guard")]
    [InlineData("warehouse", "before-guard")]
    [InlineData("direct-trade", "after-guard")]
    [InlineData("direct-trade", "before-guard")]
    public async Task Two_Reconfirmations_On_The_Same_Version_Have_One_Winner(string kind, string window)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync(kind);
        var confirmationId = await ConfirmedAsync(shipment);
        Assert.Equal(ServiceResultStatus.Created, (await RetractAsync(shipment, confirmationId, RetractBody(await VersionAsync()))).Status);
        var version = await VersionAsync();

        var race = await RaceAsync(window,
            async db => (await ConfirmAsync(shipment, ConfirmBody(version, on: new DateOnly(2026, 10, 19)), db)).Status,
            async () => (await ConfirmAsync(shipment, ConfirmBody(version))).Status);

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (race.Inner, race.Outer));
        Assert.Equal(race.AfterInner, await WriteStateAsync());
        var (active, history, _) = await HistoryAsync(shipment);
        Assert.Equal((2, 2, 1), (active.Sequence, history.Count, history.Count(h => h.Retraction == null)));
        Assert.Equal(10m, (await LineAsync()).DeliveredQuantity);
    }

    // Herbevestigen tegenover een correctie na intrekken, op dezelfde versie: precies één winnaar in beide volgordes en beide vensters.
    [Theory]
    [InlineData("warehouse", "confirm-wins", "after-guard")]
    [InlineData("warehouse", "confirm-wins", "before-guard")]
    [InlineData("warehouse", "correction-wins", "after-guard")]
    [InlineData("warehouse", "correction-wins", "before-guard")]
    [InlineData("direct-trade", "confirm-wins", "after-guard")]
    [InlineData("direct-trade", "confirm-wins", "before-guard")]
    [InlineData("direct-trade", "correction-wins", "after-guard")]
    [InlineData("direct-trade", "correction-wins", "before-guard")]
    public async Task Reconfirmation_Against_Correction_After_Retraction_Has_Exactly_One_Winner(string kind, string winner, string window)
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync(kind);
        var confirmationId = await ConfirmedAsync(shipment);
        Assert.Equal(ServiceResultStatus.Created, (await RetractAsync(shipment, confirmationId, RetractBody(await VersionAsync()))).Status);
        var version = await VersionAsync();
        var correctionEvent = kind == "warehouse" ? ReservationRules.OrderDispatchCorrectedEvent : ReservationRules.DirectTradeShipmentCorrectedEvent;

        var race = winner == "confirm-wins"
            ? await RaceAsync(window, db => CorrectAsync(shipment, version, db), async () => (await ConfirmAsync(shipment, ConfirmBody(version))).Status)
            : await RaceAsync(window, async db => (await ConfirmAsync(shipment, ConfirmBody(version), db)).Status, () => CorrectAsync(shipment, version));

        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Conflict), (race.Inner, race.Outer));
        Assert.Equal(race.AfterInner, await WriteStateAsync());
        Assert.Equal(winner == "confirm-wins" ? (2, 0) : (1, 1), (await AuditCountAsync(ReservationRules.DeliveryConfirmedEvent), await AuditCountAsync(correctionEvent)));
        Assert.Equal(winner == "confirm-wins" ? 10m : 0m, (await LineAsync()).DeliveredQuantity);
    }

    // Intrekken raakt de orderregels niet: tegelijk reserveren op dezelfde order slaagt ook.
    [Fact]
    public async Task Retracting_And_Reserving_On_The_Same_Order_Both_Succeed()
    {
        await ArrangeAsync(box: 100m);
        var shipment = await ShipmentAsync("warehouse");
        var confirmationId = await ConfirmedAsync(shipment);
        var version = await VersionAsync();
        var race = await RaceAsync("after-guard",
            async db => (await RetractAsync(shipment, confirmationId, RetractBody(version), db)).Status,
            async () =>
            {
                await using var db = _factory.CreateContext();
                return (await new StockReservationService(db).ReserveAsync(_orderId, _boxLine, new CreateStockReservationDto
                {
                    RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 5m, Unit = "box", NetWeightKg = 20m, ActorReference = "office-1"
                })).Status;
            });
        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Created), (race.Outer, race.Inner));
    }

    // Andere guardfouten bij intrekken worden niet als 409 afgevangen (smalle afhandeling uit PR #33 blijft).
    [Fact]
    public async Task Only_The_Recognisable_Conflict_Is_Translated_To_409()
    {
        await ArrangeAsync();
        var shipment = await ShipmentAsync("warehouse");
        var confirmationId = await ConfirmedAsync(shipment);
        var version = await VersionAsync();
        // Een schrijffout, geen gelijktijdigheid: in dezelfde context staat al een (niet opgeslagen) intrekking van dezelfde bevestiging.
        await using var db = _factory.CreateContext();
        var service = new DeliveryRetractionService(db);
        db.ShipmentDeliveryRetractions.Add(new ShipmentDeliveryRetraction
        {
            RequestId = Guid.NewGuid(), ShipmentDeliveryConfirmationId = confirmationId, ShipmentKind = "Warehouse", SalesDispatchId = shipment.Id, SalesOrderId = _orderId,
            Reason = "parallel", ActorReference = "t", RecordedInErrorConfirmed = true, ConfirmationCode = ReservationRules.RecordedInErrorCode,
            ConfirmationStatement = ReservationRules.RecordedInErrorStatement, RecordedAt = DateTime.UtcNow
        });

        // De service voegt een tweede intrekking van dezelfde bevestiging toe in dezelfde save: een schrijffout, geen gelijktijdigheid.
        var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => service.RetractWarehouseAsync(_orderId, shipment.Id, confirmationId, RetractBody(version)));
        Assert.Contains("at most once", exception.Message);
        await using var verify = _factory.CreateContext();
        Assert.Empty(await verify.ShipmentDeliveryRetractions.ToListAsync());
    }
}
