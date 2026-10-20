using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static AgfAdmin.Tests.Infrastructure.DirectTradeTestFlow;

namespace AgfAdmin.Tests.Data;

// Guard-regel 13 voor rechtstreekse EF-schrijvers (sync en async): soort en verwijzing, verklaring, order, datumgrenzen in Europe/Amsterdam
// met dezelfde kalender als de services, uitsluiting met beide correcties in beide richtingen, append-only en de databasechecks.
public class DeliveryConfirmationGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;

    public void Dispose() => _factory.Dispose();

    // Magazijnverzending (vast tijdstip) en DirectTrade-verzending (ShippedOn 19 oktober) op dezelfde order.
    private async Task<(int DispatchId, int ShipmentId, int PhysicalReservationId, int AllocationId)> ArrangeAsync(DateTime? dispatchedAtUtc = null)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _) = await SalesTestData.SeedOrderAsync(_factory, _seed, 100m, 10m);
        int reservationId, dispatchId;
        await using (var db = _factory.CreateContext())
        {
            var physical = new StockReservation
            {
                RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, PalletContentLineId = _seed.ApprovedBox, SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox),
                Quantity = 10m, Unit = "box", NetWeightKg = 40m, Source = "Physical", Status = "Active"
            };
            db.StockReservations.Add(physical);
            await db.SaveChangesAsync();
            reservationId = physical.Id;
        }
        await using (var db = _factory.CreateContext())
        {
            var dispatch = new SalesDispatch { RequestId = Guid.NewGuid(), SalesOrderId = _orderId, ActorReference = "t", DispatchedAt = dispatchedAtUtc ?? new DateTime(2026, 10, 19, 10, 0, 0, DateTimeKind.Utc) };
            await SalesTestData.ConsumeInContextAsync(db, reservationId, into: dispatch);
            db.SalesDispatches.Add(dispatch);
            await db.SaveChangesAsync();
            dispatchId = dispatch.Id;
        }
        var allocation = await AllocateAsync(_factory, _orderId, _boxLine, _seed.DirectTradeSourceLine, 10m);
        var shipment = await ShipAsync(_factory, _orderId, (allocation, 40.5m, "LOT-DT-SRC"));
        return (dispatchId, shipment.Id, reservationId, allocation.Id);
    }

    private ShipmentDeliveryConfirmation Delivery(string kind, int? dispatchId, int? shipmentId, DateOnly? on = null, int? orderId = null) => new()
    {
        RequestId = Guid.NewGuid(), ShipmentKind = kind, SalesDispatchId = dispatchId, DirectTradeShipmentId = shipmentId, SalesOrderId = orderId ?? _orderId,
        DeliveredOn = on ?? new DateOnly(2026, 10, 20), ActorReference = "t", DeliveredInFullConfirmed = true,
        ConfirmationCode = ReservationRules.DeliveredInFullCode, ConfirmationStatement = ReservationRules.DeliveredInFullStatement, RecordedAt = DateTime.UtcNow
    };

    private static Task SaveAsync(AppDbContext db, bool async)
    {
        if (async) return db.SaveChangesAsync();
        db.SaveChanges();
        return Task.CompletedTask;
    }

    private async Task<int> DeliveriesAsync()
    {
        await using var db = _factory.CreateContext();
        return await db.ShipmentDeliveryConfirmations.CountAsync();
    }

    [Theory]
    [InlineData("Warehouse", false)]
    [InlineData("Warehouse", true)]
    [InlineData("DirectTrade", false)]
    [InlineData("DirectTrade", true)]
    public async Task A_Valid_Confirmation_Of_Either_Kind_Is_Accepted(string kind, bool async)
    {
        var (dispatchId, shipmentId, _, _) = await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            db.ShipmentDeliveryConfirmations.Add(kind == "Warehouse" ? Delivery(kind, dispatchId, null) : Delivery(kind, null, shipmentId));
            await SaveAsync(db, async);
        }
        Assert.Equal(1, await DeliveriesAsync());
    }

    [Theory]
    [InlineData("warehouse-kind-with-shipment", false)]
    [InlineData("warehouse-kind-with-shipment", true)]
    [InlineData("direct-trade-kind-with-dispatch", false)]
    [InlineData("direct-trade-kind-with-dispatch", true)]
    [InlineData("both", false)]
    [InlineData("both", true)]
    [InlineData("not-confirmed", false)]
    [InlineData("not-confirmed", true)]
    [InlineData("wrong-code", false)]
    [InlineData("wrong-code", true)]
    [InlineData("wrong-statement", false)]
    [InlineData("wrong-statement", true)]
    [InlineData("other-order", false)]
    [InlineData("other-order", true)]
    [InlineData("future", false)]
    [InlineData("future", true)]
    [InlineData("before-dt-shipping", false)]
    [InlineData("before-dt-shipping", true)]
    [InlineData("before-warehouse-dispatch", false)]
    [InlineData("before-warehouse-dispatch", true)]
    public async Task Invalid_Confirmations_Are_Rejected(string scenario, bool async)
    {
        var (dispatchId, shipmentId, _, _) = await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            int? otherOrder = null;
            if (scenario == "other-order")
            {
                var order = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = _seed.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "t" };
                db.SalesOrders.Add(order);
                await db.SaveChangesAsync();
                otherOrder = order.Id;
            }
            var delivery = scenario switch
            {
                "warehouse-kind-with-shipment" => Delivery("Warehouse", null, shipmentId),
                "direct-trade-kind-with-dispatch" => Delivery("DirectTrade", dispatchId, null),
                "both" => Delivery("Warehouse", dispatchId, shipmentId),
                "other-order" => Delivery("Warehouse", dispatchId, null, orderId: otherOrder),
                "future" => Delivery("Warehouse", dispatchId, null, new DateOnly(2026, 10, 21)),
                "before-dt-shipping" => Delivery("DirectTrade", null, shipmentId, new DateOnly(2026, 10, 18)),
                "before-warehouse-dispatch" => Delivery("Warehouse", dispatchId, null, new DateOnly(2026, 10, 18)),
                _ => Delivery("Warehouse", dispatchId, null)
            };
            if (scenario == "not-confirmed") delivery.DeliveredInFullConfirmed = false;
            if (scenario == "wrong-code") delivery.ConfirmationCode = "OTHER";
            if (scenario == "wrong-statement") delivery.ConfirmationStatement = "Delivered.";
            db.ShipmentDeliveryConfirmations.Add(delivery);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        Assert.Equal(0, await DeliveriesAsync());
    }

    // Middernacht in Europe/Amsterdam: een magazijnverzending om 00:30 CEST op 21 oktober (22:30 UTC op 20 oktober) heeft verzenddatum
    // 21 oktober. Dezelfde kalender als de service bepaalt de grens.
    [Theory]
    [InlineData("2026-10-20T21:30:00Z", "2026-10-20", true, false)]
    [InlineData("2026-10-20T21:30:00Z", "2026-10-20", true, true)]
    [InlineData("2026-10-20T22:30:00Z", "2026-10-20", false, false)]
    [InlineData("2026-10-20T22:30:00Z", "2026-10-20", false, true)]
    [InlineData("2026-10-20T22:30:00Z", "2026-10-21", true, false)]
    [InlineData("2026-10-20T22:30:00Z", "2026-10-21", true, true)]
    public async Task The_Warehouse_Shipping_Date_Bound_Uses_The_Amsterdam_Calendar_Date(string dispatchedAtUtc, string deliveredOn, bool accepted, bool async)
    {
        _factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 10, 22));
        var (dispatchId, _, _, _) = await ArrangeAsync(DateTime.Parse(dispatchedAtUtc).ToUniversalTime());
        await using var db = _factory.CreateContext();
        db.ShipmentDeliveryConfirmations.Add(Delivery("Warehouse", dispatchId, null, DateOnly.Parse(deliveredOn)));
        if (accepted) await SaveAsync(db, async);
        else await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
    }

    // "Vandaag" in de guard: een rechtstreekse schrijver om 00:30 Amsterdam-tijd (22:30 UTC) mag de nieuwe kalenderdag gebruiken, om
    // 23:30 Amsterdam-tijd nog niet. Geldt voor afleverbevestigingen (beide soorten) en voor ShippedOn van nieuwe DirectTrade-verzendingen.
    [Theory]
    [InlineData("Warehouse", "2026-10-20T21:30:00Z", false, false)]
    [InlineData("Warehouse", "2026-10-20T21:30:00Z", false, true)]
    [InlineData("Warehouse", "2026-10-20T22:30:00Z", true, false)]
    [InlineData("Warehouse", "2026-10-20T22:30:00Z", true, true)]
    [InlineData("DirectTrade", "2026-10-20T21:30:00Z", false, false)]
    [InlineData("DirectTrade", "2026-10-20T21:30:00Z", false, true)]
    [InlineData("DirectTrade", "2026-10-20T22:30:00Z", true, false)]
    [InlineData("DirectTrade", "2026-10-20T22:30:00Z", true, true)]
    public async Task Today_In_The_Guard_Is_Europe_Amsterdam_Around_Midnight(string kind, string utcNow, bool accepted, bool async)
    {
        var (dispatchId, shipmentId, _, _) = await ArrangeAsync();
        var calendar = new AmsterdamBusinessCalendar(new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));
        await using var db = _factory.CreateContext(calendar);
        db.ShipmentDeliveryConfirmations.Add(kind == "Warehouse" ? Delivery(kind, dispatchId, null, new DateOnly(2026, 10, 21)) : Delivery(kind, null, shipmentId, new DateOnly(2026, 10, 21)));
        if (accepted) await SaveAsync(db, async);
        else
        {
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            Assert.Contains("Europe/Amsterdam", exception.Message);
        }
    }

    [Theory]
    [InlineData("2026-10-20T21:30:00Z", false, false)]
    [InlineData("2026-10-20T21:30:00Z", false, true)]
    [InlineData("2026-10-20T22:30:00Z", true, false)]
    [InlineData("2026-10-20T22:30:00Z", true, true)]
    public async Task A_New_DirectTrade_Shipment_Written_Directly_Cannot_Be_Shipped_After_Today(string utcNow, bool accepted, bool async)
    {
        await ArrangeAsync();
        var allocation = await AllocateAsync(_factory, _orderId, _boxLine, _seed.DirectTradeSourceLine, 5m);
        var calendar = new AmsterdamBusinessCalendar(new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));
        await using var db = _factory.CreateContext(calendar);
        var reservation = await db.StockReservations.SingleAsync(r => r.Id == allocation.Id);
        var commitment = await db.SupplyCommitments.Include(c => c.Vendor).SingleAsync(c => c.Id == _seed.DirectTradeCommitmentId);
        var source = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == _seed.DirectTradeSourceLine);
        reservation.Status = "Consumed";
        reservation.ConsumedAt = DateTime.UtcNow;
        var shipment = new DirectTradeShipment
        {
            RequestId = Guid.NewGuid(), SalesOrderId = _orderId, CustomerId = _seed.CustomerId, CustomerNameSnapshot = "Customer T", ShippedOn = new DateOnly(2026, 10, 21), ActorReference = "t", RecordedAt = DateTime.UtcNow
        };
        shipment.Lines.Add(new DirectTradeShipmentLine
        {
            StockReservation = reservation, SalesOrderLineId = _boxLine, SupplyCommitmentLineId = source.Id, SupplyCommitmentId = commitment.Id, VendorId = commitment.VendorId,
            VendorNameSnapshot = commitment.Vendor.Name, OriginSnapshot = commitment.Origin, DestinationSnapshot = commitment.Destination,
            AllocatedQuantity = 5m, Quantity = 5m, Unit = "box", NetWeightKg = 20m, LotNumber = source.LotNumber, SourceLotNumber = source.LotNumber, ExpectedReservationVersion = reservation.Version
        });
        db.DirectTradeShipments.Add(shipment);
        if (accepted) await SaveAsync(db, async);
        else await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
    }

    // ---- Uitsluiting met correcties, in beide richtingen ----

    [Theory]
    [InlineData("Warehouse", false)]
    [InlineData("Warehouse", true)]
    [InlineData("DirectTrade", false)]
    [InlineData("DirectTrade", true)]
    public async Task A_Corrected_Shipment_Cannot_Be_Confirmed(string kind, bool async)
    {
        var (dispatchId, shipmentId, _, _) = await ArrangeAsync();
        if (kind == "Warehouse") await SalesTestData.CorrectAsync(_factory, dispatchId);
        else
        {
            await using var dbc = _factory.CreateContext();
            var shipment = (await new DirectTradeShipmentService(dbc, _factory.Calendar).ListAsync(_orderId)).Value.Single();
            Assert.Equal(ServiceResultStatus.Created, (await new DirectTradeShipmentCorrectionService(dbc).CorrectAsync(_orderId, shipmentId, Correction(await OrderVersionAsync(_factory, _orderId), shipment))).Status);
        }
        await using var db = _factory.CreateContext();
        db.ShipmentDeliveryConfirmations.Add(kind == "Warehouse" ? Delivery(kind, dispatchId, null) : Delivery(kind, null, shipmentId));
        // Opgeslagen correctie: het herkenbare conflict dat services als 409 mogen behandelen.
        var exception = await Assert.ThrowsAsync<DeliveryCorrectionConflictException>(() => SaveAsync(db, async));
        Assert.Contains("can never be confirmed as delivered", exception.Message);
    }

    [Theory]
    [InlineData("Warehouse", "stored", false)]
    [InlineData("Warehouse", "stored", true)]
    [InlineData("Warehouse", "same-save", false)]
    [InlineData("Warehouse", "same-save", true)]
    [InlineData("DirectTrade", "stored", false)]
    [InlineData("DirectTrade", "stored", true)]
    [InlineData("DirectTrade", "same-save", false)]
    [InlineData("DirectTrade", "same-save", true)]
    public async Task A_Confirmed_Shipment_Can_Never_Get_A_Correction(string kind, string when, bool async)
    {
        var (dispatchId, shipmentId, _, _) = await ArrangeAsync();
        var delivery = kind == "Warehouse" ? Delivery(kind, dispatchId, null) : Delivery(kind, null, shipmentId);
        if (when == "stored")
        {
            await using var db0 = _factory.CreateContext();
            db0.ShipmentDeliveryConfirmations.Add(delivery);
            await db0.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            if (when == "same-save") db.ShipmentDeliveryConfirmations.Add(delivery);
            if (kind == "Warehouse") await SalesTestData.CorrectInContextAsync(db, dispatchId);
            else
            {
                var shipment = await db.DirectTradeShipments.Include(s => s.Lines).ThenInclude(l => l.StockReservation).SingleAsync(s => s.Id == shipmentId);
                var now = DateTime.UtcNow;
                var correction = new DirectTradeShipmentCorrection
                {
                    RequestId = Guid.NewGuid(), DirectTradeShipmentId = shipmentId, SalesOrderId = _orderId, Reason = "x", ActorReference = "t", ShipmentDidNotTakePlaceConfirmed = true,
                    ConfirmationCode = ReservationRules.ShipmentDidNotTakePlaceCode, ConfirmationStatement = ReservationRules.ShipmentDidNotTakePlaceStatement, CorrectedAt = now
                };
                foreach (var line in shipment.Lines)
                {
                    line.StockReservation.Status = "Corrected";
                    line.StockReservation.CorrectedAt = now;
                    correction.Lines.Add(new DirectTradeShipmentCorrectionLine
                    {
                        DirectTradeShipmentLineId = line.Id, StockReservation = line.StockReservation, SupplyCommitmentLineId = line.SupplyCommitmentLineId, SalesOrderLineId = line.SalesOrderLineId,
                        Quantity = line.Quantity, Unit = line.Unit, NetWeightKg = line.NetWeightKg, ExpectedReservationVersion = line.StockReservation.Version
                    });
                }
                db.DirectTradeShipmentCorrections.Add(correction);
            }
            // Opgeslagen bevestiging = herkenbaar conflict; bevestiging en correctie in dezelfde save = fout van de schrijver (exact basistype).
            if (when == "stored")
            {
                var conflict = await Assert.ThrowsAsync<DeliveryCorrectionConflictException>(() => SaveAsync(db, async));
                Assert.Contains("is confirmed as delivered", conflict.Message);
            }
            else
            {
                await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            }
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal((when == "stored" ? 1 : 0, 0, 0), (await verify.ShipmentDeliveryConfirmations.CountAsync(), await verify.SalesDispatchCorrections.CountAsync(), await verify.DirectTradeShipmentCorrections.CountAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmations_Are_Append_Only(bool async)
    {
        var (dispatchId, _, _, _) = await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            db.ShipmentDeliveryConfirmations.Add(Delivery("Warehouse", dispatchId, null));
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.ShipmentDeliveryConfirmations.SingleAsync()).DeliveredOn = new DateOnly(2026, 10, 19);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        await using (var db = _factory.CreateContext())
        {
            db.ShipmentDeliveryConfirmations.Remove(await db.ShipmentDeliveryConfirmations.SingleAsync());
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
    }

    // ---- Databasechecks buiten de guard om ----

    private async Task<int> RawAsync(string kind, string dispatchId, string shipmentId, string confirmed = "TRUE", string code = "DELIVERED_IN_FULL",
        string statement = "Deze verzending is volledig en zonder afwijkingen afgeleverd.", string reference = "NULL")
    {
        await using var raw = _factory.CreateContext();
        return await raw.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"ShipmentDeliveryConfirmations\" (\"RequestId\", \"ShipmentKind\", \"SalesDispatchId\", \"DirectTradeShipmentId\", \"SalesOrderId\", \"DeliveredOn\", \"ReceiptReference\", \"ActorReference\", \"DeliveredInFullConfirmed\", \"ConfirmationCode\", \"ConfirmationStatement\", \"RecordedAt\") " +
            $"VALUES ('{Guid.NewGuid()}', '{kind}', {dispatchId}, {shipmentId}, {_orderId}, '2026-10-20', {reference}, 't', {confirmed}, '{code}', '{statement}', '2026-10-20 08:00:00')");
    }

    [Fact]
    public async Task Database_Checks_Enforce_Kind_Statement_And_One_Confirmation_Per_Shipment()
    {
        var (dispatchId, shipmentId, _, _) = await ArrangeAsync();
        async Task Rejected(Func<Task<int>> insert, string check) => Assert.Contains(check, (await Assert.ThrowsAsync<SqliteException>(insert)).Message);

        await Rejected(() => RawAsync("Warehouse", "NULL", shipmentId.ToString()), "CK_ShipmentDeliveryConfirmations_OneShipment");
        await Rejected(() => RawAsync("DirectTrade", dispatchId.ToString(), "NULL"), "CK_ShipmentDeliveryConfirmations_OneShipment");
        await Rejected(() => RawAsync("Warehouse", dispatchId.ToString(), shipmentId.ToString()), "CK_ShipmentDeliveryConfirmations_OneShipment");
        await Rejected(() => RawAsync("Warehouse", dispatchId.ToString(), "NULL", confirmed: "FALSE"), "CK_ShipmentDeliveryConfirmations_Confirmed");
        await Rejected(() => RawAsync("Warehouse", dispatchId.ToString(), "NULL", code: "OTHER"), "CK_ShipmentDeliveryConfirmations_Code");
        await Rejected(() => RawAsync("Warehouse", dispatchId.ToString(), "NULL", statement: "Iets anders."), "CK_ShipmentDeliveryConfirmations_Statement");
        await Rejected(() => RawAsync("Warehouse", dispatchId.ToString(), "NULL", reference: "'  '"), "CK_ShipmentDeliveryConfirmations_Reference_NullOrNotBlank");

        Assert.Equal(1, await RawAsync("Warehouse", dispatchId.ToString(), "NULL"));
        Assert.Equal(1, await RawAsync("DirectTrade", "NULL", shipmentId.ToString()));
        await Rejected(() => RawAsync("Warehouse", dispatchId.ToString(), "NULL"), "UNIQUE constraint failed");
        await Rejected(() => RawAsync("DirectTrade", "NULL", shipmentId.ToString()), "UNIQUE constraint failed");
    }
}
