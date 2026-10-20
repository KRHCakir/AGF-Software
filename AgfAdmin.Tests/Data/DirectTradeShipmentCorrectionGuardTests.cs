using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static AgfAdmin.Tests.Infrastructure.DirectTradeTestFlow;

namespace AgfAdmin.Tests.Data;

// Guard-regel 12 (DirectTrade-verzendcorrectie in beide richtingen), Physical-afbakening van regel 9, onveranderlijkheid ná Corrected,
// append-only correcties en de databasechecks op verklaring en uniciteit. Elke variant sync en async.
public class DirectTradeShipmentCorrectionGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _lineId;
    private int _colliLine;

    public void Dispose() => _factory.Dispose();

    private async Task<DirectTradeShipmentDto> ArrangeShipmentAsync(int lines = 1)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _lineId, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, 100m, 10m);
        var allocations = new List<(StockReservationDto, decimal, string)>();
        for (var i = 0; i < lines; i++)
        {
            allocations.Add((await AllocateAsync(_factory, _orderId, _lineId, _seed.DirectTradeSourceLine, 10m), 40.5m, "LOT-DT-SRC"));
        }
        return await ShipAsync(_factory, _orderId, allocations.ToArray());
    }

    private static Task SaveAsync(AppDbContext db, bool async)
    {
        if (async) return db.SaveChangesAsync();
        db.SaveChanges();
        return Task.CompletedTask;
    }

    // De volledige graaf zoals de service die schrijft; slaat niet zelf op.
    private static async Task<(DirectTradeShipmentCorrection Correction, List<(StockReservation Reservation, DirectTradeShipmentCorrectionLine Line)> Lines)> CorrectInContextAsync(AppDbContext db, int shipmentId, int? onlyFirst = null)
    {
        var shipment = await db.DirectTradeShipments.Include(s => s.Lines).ThenInclude(l => l.StockReservation).SingleAsync(s => s.Id == shipmentId);
        var now = DateTime.UtcNow;
        var correction = new DirectTradeShipmentCorrection
        {
            RequestId = Guid.NewGuid(), DirectTradeShipmentId = shipment.Id, SalesOrderId = shipment.SalesOrderId, Reason = "niet geladen", ActorReference = "test",
            ShipmentDidNotTakePlaceConfirmed = true, ConfirmationCode = ReservationRules.ShipmentDidNotTakePlaceCode, ConfirmationStatement = ReservationRules.ShipmentDidNotTakePlaceStatement, CorrectedAt = now
        };
        var result = new List<(StockReservation, DirectTradeShipmentCorrectionLine)>();
        foreach (var shipmentLine in shipment.Lines.OrderBy(l => l.Id).Take(onlyFirst ?? int.MaxValue))
        {
            var reservation = shipmentLine.StockReservation;
            reservation.Status = "Corrected";
            reservation.CorrectedAt = now;
            var line = new DirectTradeShipmentCorrectionLine
            {
                DirectTradeShipmentLineId = shipmentLine.Id, StockReservation = reservation, SupplyCommitmentLineId = shipmentLine.SupplyCommitmentLineId, SalesOrderLineId = shipmentLine.SalesOrderLineId,
                Quantity = shipmentLine.Quantity, Unit = shipmentLine.Unit, NetWeightKg = shipmentLine.NetWeightKg, ExpectedReservationVersion = reservation.Version
            };
            correction.Lines.Add(line);
            result.Add((reservation, line));
        }
        db.DirectTradeShipmentCorrections.Add(correction);
        return (correction, result);
    }

    private async Task<(int Corrections, List<string> Statuses)> StateAsync()
    {
        await using var db = _factory.CreateContext();
        return (await db.DirectTradeShipmentCorrections.CountAsync(),
            await db.StockReservations.AsNoTracking().Where(r => r.Source == "DirectTrade").OrderBy(r => r.Id).Select(r => r.Status).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Complete_Graph_Is_Accepted(bool async)
    {
        var shipment = await ArrangeShipmentAsync(lines: 2);
        await using (var db = _factory.CreateContext())
        {
            await CorrectInContextAsync(db, shipment.Id);
            await SaveAsync(db, async);
        }
        var state = await StateAsync();
        Assert.Equal(1, state.Corrections);
        Assert.Equal(new[] { "Corrected", "Corrected" }, state.Statuses);
    }

    [Theory]
    [InlineData("corrected-without-line", false)]
    [InlineData("corrected-without-line", true)]
    [InlineData("partial", false)]
    [InlineData("partial", true)]
    [InlineData("quantity", false)]
    [InlineData("quantity", true)]
    [InlineData("kg", false)]
    [InlineData("kg", true)]
    [InlineData("unit", false)]
    [InlineData("unit", true)]
    [InlineData("source-line", false)]
    [InlineData("source-line", true)]
    [InlineData("order-line", false)]
    [InlineData("order-line", true)]
    [InlineData("other-order", false)]
    [InlineData("other-order", true)]
    [InlineData("not-confirmed", false)]
    [InlineData("not-confirmed", true)]
    [InlineData("wrong-code", false)]
    [InlineData("wrong-code", true)]
    [InlineData("wrong-statement", false)]
    [InlineData("wrong-statement", true)]
    [InlineData("blank-reason", false)]
    [InlineData("blank-reason", true)]
    [InlineData("no-corrected-at", false)]
    [InlineData("no-corrected-at", true)]
    [InlineData("correction-movement", false)]
    [InlineData("correction-movement", true)]
    public async Task Every_Missing_Or_Deviating_Component_Is_Rejected(string scenario, bool async)
    {
        var shipment = await ArrangeShipmentAsync(lines: 2);
        await using (var db = _factory.CreateContext())
        {
            var (correction, lines) = await CorrectInContextAsync(db, shipment.Id, scenario is "partial" or "corrected-without-line" ? 1 : null);
            var (reservation, line) = lines[0];
            switch (scenario)
            {
                case "corrected-without-line":
                    // De tweede toewijzing wordt Corrected zonder eigen correctieregel.
                    var secondId = shipment.Lines[1].StockReservationId;
                    var second = await db.StockReservations.SingleAsync(r => r.Id == secondId);
                    second.Status = "Corrected";
                    second.CorrectedAt = DateTime.UtcNow;
                    break;
                case "quantity": line.Quantity += 1m; break;
                case "kg": line.NetWeightKg += 0.001m; break;
                case "unit": line.Unit = "colli"; break;
                case "source-line": line.SupplyCommitmentLineId = _seed.DirectTradeColliSourceLine; break;
                case "order-line": line.SalesOrderLineId = _colliLine; break;
                case "other-order":
                    var other = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = _seed.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "t" };
                    db.SalesOrders.Add(other);
                    correction.SalesOrder = other;
                    break;
                case "not-confirmed": correction.ShipmentDidNotTakePlaceConfirmed = false; break;
                case "wrong-code": correction.ConfirmationCode = "OTHER"; break;
                case "wrong-statement": correction.ConfirmationStatement = "This shipment did not take place."; break;
                case "blank-reason": correction.Reason = " "; break;
                case "no-corrected-at": reservation.CorrectedAt = null; break;
                case "correction-movement":
                    db.StockMovements.Add(new StockMovement { Type = "Correction", Quantity = 10m, Unit = "box", SupplyCommitmentLineId = reservation.SupplyCommitmentLineId, StockReservation = reservation, Reason = "x", CreatedAt = DateTime.UtcNow });
                    break;
            }
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        var state = await StateAsync();
        Assert.Equal(0, state.Corrections);
        Assert.Equal(new[] { "Consumed", "Consumed" }, state.Statuses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Correction_Line_For_Another_Shipment_Line_Is_Rejected(bool async)
    {
        var shipment = await ArrangeShipmentAsync(lines: 1);
        var second = await AllocateAsync(_factory, _orderId, _lineId, _seed.DirectTradeSourceLine, 5m);
        var otherShipment = await ShipAsync(_factory, _orderId, (second, 20m, "LOT-DT-SRC"));
        await using (var db = _factory.CreateContext())
        {
            var (_, lines) = await CorrectInContextAsync(db, shipment.Id);
            lines[0].Line.DirectTradeShipmentLineId = otherShipment.Lines[0].Id;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        Assert.Equal(0, (await StateAsync()).Corrections);
    }

    // Magazijnverzending en DirectTrade-verzending hebben elk een eigen reeks: verzending 1, verzendregel 1 en correctieregel 1 bestaan in
    // beide werelden. Een magazijnreservering (of magazijnverzendregel) kan via dat gelijke nummer nooit in een DirectTrade-correctie belanden.
    [Theory]
    [InlineData("physical-reservation", false)]
    [InlineData("physical-reservation", true)]
    [InlineData("dt-reservation-in-warehouse-correction", false)]
    [InlineData("dt-reservation-in-warehouse-correction", true)]
    public async Task Warehouse_And_DirectTrade_Corrections_Cannot_Be_Crossed_Even_With_Equal_Ids(string scenario, bool async)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _lineId, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, 100m, 10m);
        int physicalId;
        await using (var db = _factory.CreateContext())
        {
            var physical = new StockReservation
            {
                RequestId = Guid.NewGuid(), SalesOrderLineId = _lineId, PalletContentLineId = _seed.ApprovedBox, SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox),
                Quantity = 10m, Unit = "box", NetWeightKg = 40m, Source = "Physical", Status = "Active"
            };
            db.StockReservations.Add(physical);
            await db.SaveChangesAsync();
            physicalId = physical.Id;
        }
        var dispatchId = await SalesTestData.ConsumeAsync(_factory, physicalId);
        var allocation = await AllocateAsync(_factory, _orderId, _lineId, _seed.DirectTradeSourceLine, 10m);
        var shipment = await ShipAsync(_factory, _orderId, (allocation, 40.5m, "LOT-DT-SRC"));
        int dispatchLineId;
        await using (var db = _factory.CreateContext())
        {
            dispatchLineId = (await db.SalesDispatchLines.AsNoTracking().SingleAsync()).Id;
        }
        Assert.Equal(dispatchId, shipment.Id);
        Assert.Equal(dispatchLineId, shipment.Lines[0].Id);

        await using (var db = _factory.CreateContext())
        {
            if (scenario == "physical-reservation")
            {
                // DirectTrade-correctie met de magazijnreservering op het gelijke (magazijn)regelnummer.
                var physical = await db.StockReservations.SingleAsync(r => r.Id == physicalId);
                physical.Status = "Corrected";
                physical.CorrectedAt = DateTime.UtcNow;
                var correction = new DirectTradeShipmentCorrection
                {
                    RequestId = Guid.NewGuid(), DirectTradeShipmentId = shipment.Id, SalesOrderId = _orderId, Reason = "x", ActorReference = "t", ShipmentDidNotTakePlaceConfirmed = true,
                    ConfirmationCode = ReservationRules.ShipmentDidNotTakePlaceCode, ConfirmationStatement = ReservationRules.ShipmentDidNotTakePlaceStatement, CorrectedAt = DateTime.UtcNow
                };
                correction.Lines.Add(new DirectTradeShipmentCorrectionLine
                {
                    DirectTradeShipmentLineId = dispatchLineId, StockReservation = physical, SupplyCommitmentLineId = physical.SupplyCommitmentLineId, SalesOrderLineId = _lineId,
                    Quantity = 10m, Unit = "box", NetWeightKg = 40m, ExpectedReservationVersion = 1
                });
                db.DirectTradeShipmentCorrections.Add(correction);
            }
            else
            {
                // Magazijncorrectie (regel 9) met de DirectTrade-toewijzing op het gelijke regelnummer.
                var dt = await db.StockReservations.SingleAsync(r => r.Id == allocation.Id);
                dt.Status = "Corrected";
                dt.CorrectedAt = DateTime.UtcNow;
                var dispatchLine = await db.SalesDispatchLines.Include(l => l.PalletContentLine).SingleAsync();
                var warehouse = new SalesDispatchCorrection
                {
                    RequestId = Guid.NewGuid(), SalesDispatchId = dispatchId, SalesOrderId = _orderId, Reason = "x", ActorReference = "t", GoodsNeverLeftWarehouseConfirmed = true, CorrectedAt = DateTime.UtcNow
                };
                var correctionLine = new SalesDispatchCorrectionLine { SalesDispatchLine = dispatchLine, StockReservation = dt, Quantity = 10m, Unit = "box", NetWeightKg = 40m, ExpectedReservationVersion = 1 };
                warehouse.Lines.Add(correctionLine);
                db.SalesDispatchCorrections.Add(warehouse);
                var restored = new PalletContentLine
                {
                    RequestId = Guid.NewGuid(), PalletId = dispatchLine.PalletContentLine.PalletId, SupplyCommitmentLineId = dispatchLine.PalletContentLine.SupplyCommitmentLineId, LotNumber = "LOT-A",
                    Quantity = 10m, Unit = "box", NetWeightKg = 40m, QualityStatus = "Quarantine", OriginCorrectionLine = correctionLine
                };
                db.PalletContentLines.Add(restored);
                db.StockMovements.Add(new StockMovement { Type = "Correction", Quantity = 10m, Unit = "box", NetWeightKg = 40m, SupplyCommitmentLineId = restored.SupplyCommitmentLineId, PalletContentLine = restored, StockReservation = dt, SalesDispatchCorrectionLine = correctionLine, Reason = "x", CreatedAt = DateTime.UtcNow });
            }
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal(("Consumed", "Consumed"), ((await verify.StockReservations.AsNoTracking().SingleAsync(r => r.Id == physicalId)).Status, (await verify.StockReservations.AsNoTracking().SingleAsync(r => r.Id == allocation.Id)).Status));
        Assert.Empty(await verify.DirectTradeShipmentCorrections.ToListAsync());
        Assert.Empty(await verify.SalesDispatchCorrections.ToListAsync());
    }

    // ---- Na Corrected: niets wijzigt meer, ook niet zolang de status Corrected blijft ----

    [Theory]
    [InlineData("quantity", false)]
    [InlineData("quantity", true)]
    [InlineData("unit", false)]
    [InlineData("unit", true)]
    [InlineData("source-line", false)]
    [InlineData("source-line", true)]
    [InlineData("order-line", false)]
    [InlineData("order-line", true)]
    [InlineData("consumed-at", false)]
    [InlineData("consumed-at", true)]
    [InlineData("corrected-at", false)]
    [InlineData("corrected-at", true)]
    [InlineData("status-consumed", false)]
    [InlineData("status-consumed", true)]
    [InlineData("status-active", false)]
    [InlineData("status-active", true)]
    [InlineData("status-released", false)]
    [InlineData("status-released", true)]
    public async Task A_Corrected_Allocation_Is_Immutable(string change, bool async)
    {
        var shipment = await ArrangeShipmentAsync();
        await using (var db = _factory.CreateContext())
        {
            await CorrectInContextAsync(db, shipment.Id);
            await SaveAsync(db, async);
        }
        var id = shipment.Lines[0].StockReservationId;
        StockReservation before;
        await using (var db = _factory.CreateContext())
        {
            before = await db.StockReservations.AsNoTracking().SingleAsync(r => r.Id == id);
        }
        await using (var db = _factory.CreateContext())
        {
            var reservation = await db.StockReservations.SingleAsync(r => r.Id == id);
            switch (change)
            {
                case "quantity": reservation.Quantity = 7m; break;
                case "unit": reservation.Unit = "colli"; break;
                case "source-line": reservation.SupplyCommitmentLineId = _seed.DirectTradeColliSourceLine; break;
                case "order-line": reservation.SalesOrderLineId = _colliLine; break;
                case "consumed-at": reservation.ConsumedAt = reservation.ConsumedAt!.Value.AddDays(-1); break;
                case "corrected-at": reservation.CorrectedAt = reservation.CorrectedAt!.Value.AddDays(-1); break;
                case "status-consumed": reservation.Status = "Consumed"; break;
                case "status-active": reservation.Status = "Active"; break;
                case "status-released": reservation.Status = "Released"; reservation.ReleasedAt = DateTime.UtcNow; reservation.ReleaseReason = "x"; break;
            }
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        await using var verify = _factory.CreateContext();
        var after = await verify.StockReservations.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal((before.Status, before.Quantity, before.Unit, before.SupplyCommitmentLineId, before.SalesOrderLineId, before.ConsumedAt, before.CorrectedAt),
            (after.Status, after.Quantity, after.Unit, after.SupplyCommitmentLineId, after.SalesOrderLineId, after.ConsumedAt, after.CorrectedAt));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrections_And_Lines_Are_Append_Only(bool async)
    {
        var shipment = await ArrangeShipmentAsync();
        await using (var db = _factory.CreateContext())
        {
            await CorrectInContextAsync(db, shipment.Id);
            await SaveAsync(db, async);
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.DirectTradeShipmentCorrections.SingleAsync()).Reason = "anders";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.DirectTradeShipmentCorrectionLines.SingleAsync()).NetWeightKg = 1m;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        await using (var db = _factory.CreateContext())
        {
            db.DirectTradeShipmentCorrections.Remove(await db.DirectTradeShipmentCorrections.SingleAsync());
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
    }

    // ---- Databasechecks buiten de guard om ----

    [Theory]
    [InlineData("FALSE", "SHIPMENT_DID_NOT_TAKE_PLACE", "Deze geregistreerde verzending heeft niet plaatsgevonden.", "x", "CK_DirectTradeShipmentCorrections_Confirmed")]
    [InlineData("TRUE", "OTHER", "Deze geregistreerde verzending heeft niet plaatsgevonden.", "x", "CK_DirectTradeShipmentCorrections_Code")]
    [InlineData("TRUE", "SHIPMENT_DID_NOT_TAKE_PLACE", "Iets anders.", "x", "CK_DirectTradeShipmentCorrections_Statement")]
    [InlineData("TRUE", "SHIPMENT_DID_NOT_TAKE_PLACE", "Deze geregistreerde verzending heeft niet plaatsgevonden.", "  ", "CK_DirectTradeShipmentCorrections_Reason_NotBlank")]
    public async Task Database_Checks_Enforce_The_Statement(string confirmed, string code, string statement, string reason, string check)
    {
        var shipment = await ArrangeShipmentAsync();
        await using var raw = _factory.CreateContext();
        var exception = await Assert.ThrowsAsync<SqliteException>(() => raw.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"DirectTradeShipmentCorrections\" (\"RequestId\", \"DirectTradeShipmentId\", \"SalesOrderId\", \"Reason\", \"ActorReference\", \"ShipmentDidNotTakePlaceConfirmed\", \"ConfirmationCode\", \"ConfirmationStatement\", \"CorrectedAt\") " +
            $"VALUES ('{Guid.NewGuid()}', {shipment.Id}, {_orderId}, '{reason}', 't', {confirmed}, '{code}', '{statement}', '2026-10-20 08:00:00')"));
        Assert.Contains(check, exception.Message);
    }

    [Fact]
    public async Task A_Shipment_Can_Be_Taken_Back_At_Most_Once_In_The_Database()
    {
        var shipment = await ArrangeShipmentAsync();
        await using var raw = _factory.CreateContext();
        string Insert() => "INSERT INTO \"DirectTradeShipmentCorrections\" (\"RequestId\", \"DirectTradeShipmentId\", \"SalesOrderId\", \"Reason\", \"ActorReference\", \"ShipmentDidNotTakePlaceConfirmed\", \"ConfirmationCode\", \"ConfirmationStatement\", \"CorrectedAt\") " +
            $"VALUES ('{Guid.NewGuid()}', {shipment.Id}, {_orderId}, 'x', 't', TRUE, 'SHIPMENT_DID_NOT_TAKE_PLACE', 'Deze geregistreerde verzending heeft niet plaatsgevonden.', '2026-10-20 08:00:00')";
        Assert.Equal(1, await raw.Database.ExecuteSqlRawAsync(Insert()));
        var exception = await Assert.ThrowsAsync<SqliteException>(() => raw.Database.ExecuteSqlRawAsync(Insert()));
        Assert.Contains("UNIQUE constraint failed", exception.Message);
    }
}
