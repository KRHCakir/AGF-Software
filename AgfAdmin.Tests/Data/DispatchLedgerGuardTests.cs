using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Guard-regel 8 (afboekgrootboek in beide richtingen) en regel 3 voor verzendingen en Dispatch-mutaties, sync en async.
// Andere mutatietypen (Receipt, Waste, Correction) blijven buiten regel 3.
public class DispatchLedgerGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _boxLine;
    private int _colliLine;
    private int _reservationId;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync()
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity: 100m);
        await using var db = _factory.CreateContext();
        var reservation = new StockReservation
        {
            RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, PalletContentLineId = _seed.ApprovedBox,
            SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox),
            Quantity = 10m, Unit = "box", NetWeightKg = 40m, Source = "Physical", Status = "Active"
        };
        db.StockReservations.Add(reservation);
        await db.SaveChangesAsync();
        _reservationId = reservation.Id;
    }

    private static Task SaveAsync(AppDbContext db, bool async)
    {
        if (async) return db.SaveChangesAsync();
        db.SaveChanges();
        return Task.CompletedTask;
    }

    private async Task AssertViolationAsync(bool async, Func<AppDbContext, Task> arrange, string fragment)
    {
        await using var db = _factory.CreateContext();
        await arrange(db);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => SaveAsync(db, async));
        Assert.True(exception is TraceabilityViolationException, $"expected a traceability violation containing '{fragment}' but got {exception.GetType().Name}: {exception.Message}");
        Assert.Contains(fragment, exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Complete_Dispatch_Ledger_Entry_Is_Accepted(bool async)
    {
        await ArrangeAsync();
        await using var db = _factory.CreateContext();
        await SalesTestData.ConsumeInContextAsync(db, _reservationId);
        await SaveAsync(db, async);
        await using var verify = _factory.CreateContext();
        Assert.Equal(("Consumed", 10m, 40m, 1), (
            (await verify.StockReservations.FindAsync(_reservationId))!.Status,
            (await verify.PalletContentLines.FindAsync(_seed.ApprovedBox))!.DispatchedQuantity,
            (await verify.PalletContentLines.FindAsync(_seed.ApprovedBox))!.DispatchedNetWeightKg,
            await verify.StockMovements.CountAsync(m => m.Type == "Dispatch")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consumed_Without_Movement_Dispatch_Line_Or_Increment_Is_Rejected(bool async)
    {
        await ArrangeAsync();
        // Alleen de status.
        await AssertViolationAsync(async, async db =>
        {
            var r = await db.StockReservations.FindAsync(_reservationId);
            r!.Status = "Consumed";
            r.ConsumedAt = DateTime.UtcNow;
        }, "exactly one dispatch stock movement");
        // Status + mutatie + verhoging, maar zonder verzending en verzendregel (losgekoppeld van de change tracker).
        await AssertViolationAsync(async, async db =>
        {
            var dispatch = await SalesTestData.ConsumeInContextAsync(db, _reservationId);
            var line = dispatch.Lines.Single();
            db.Entry(line).State = EntityState.Detached;
            db.Entry(dispatch).State = EntityState.Detached;
        }, "without a sales dispatch line");
        // Status + mutatie + verzendregel, maar zonder ConsumedAt.
        await AssertViolationAsync(async, async db =>
        {
            await SalesTestData.ConsumeInContextAsync(db, _reservationId);
            (await db.StockReservations.FindAsync(_reservationId))!.ConsumedAt = null;
        }, "without a consumption time");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Increment_Without_Movement_And_Movement_Without_Increment_Are_Rejected(bool async)
    {
        await ArrangeAsync();
        await AssertViolationAsync(async, async db =>
        {
            (await db.PalletContentLines.FindAsync(_seed.ApprovedBox))!.DispatchedQuantity = 5m;
        }, "increase without matching dispatch stock movements");
        // Mutatie blijft, verhoging teruggedraaid of afwijkend: beide richtingen melden een mismatch ("matching").
        await AssertViolationAsync(async, async db =>
        {
            await SalesTestData.ConsumeInContextAsync(db, _reservationId);
            var content = await db.PalletContentLines.FindAsync(_seed.ApprovedBox);
            content!.DispatchedQuantity = 0m;
            content.DispatchedNetWeightKg = 0m;
        }, "without a matching increase");
        await AssertViolationAsync(async, async db =>
        {
            await SalesTestData.ConsumeInContextAsync(db, _reservationId);
            (await db.PalletContentLines.FindAsync(_seed.ApprovedBox))!.DispatchedNetWeightKg += 1m;
        }, "matching");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Movement_That_Differs_From_Its_Reservation_Is_Rejected(bool async)
    {
        await ArrangeAsync();
        await AssertViolationAsync(async, async db =>
        {
            await SalesTestData.ConsumeInContextAsync(db, _reservationId);
            var movement = db.ChangeTracker.Entries<StockMovement>().Single().Entity;
            movement.NetWeightKg = 39m;
            (await db.PalletContentLines.FindAsync(_seed.ApprovedBox))!.DispatchedNetWeightKg = 39m;
        }, "copy quantity, unit, net weight and content line");
        await AssertViolationAsync(async, async db =>
        {
            await SalesTestData.ConsumeInContextAsync(db, _reservationId);
            db.StockMovements.Add(new StockMovement
            {
                Type = "Dispatch", Quantity = 1m, Unit = "box", NetWeightKg = 4m,
                SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox), PalletContentLineId = _seed.ApprovedBox
            });
        }, "must reference the consumed reservation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatched_Totals_Never_Decrease_And_New_Content_Starts_At_Zero(bool async)
    {
        await ArrangeAsync();
        await SalesTestData.ConsumeAsync(_factory, _reservationId);
        await AssertViolationAsync(async, async db =>
        {
            (await db.PalletContentLines.FindAsync(_seed.ApprovedBox))!.DispatchedQuantity = 9m;
        }, "can never decrease");
        await AssertViolationAsync(async, async db =>
        {
            var source = await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == _seed.ApprovedBox);
            db.PalletContentLines.Add(new PalletContentLine
            {
                RequestId = Guid.NewGuid(), PalletId = source.PalletId, SupplyCommitmentLineId = source.SupplyCommitmentLineId,
                LotNumber = "LOT-NEW", Quantity = 5m, Unit = "box", NetWeightKg = 20m, QualityStatus = "Quarantine", DispatchedQuantity = 1m, DispatchedNetWeightKg = 4m
            });
        }, "starts with nothing dispatched");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatches_Dispatch_Lines_And_Dispatch_Movements_Are_Append_Only_But_Other_Movement_Types_Are_Not_Blocked(bool async)
    {
        await ArrangeAsync();
        var dispatchId = await SalesTestData.ConsumeAsync(_factory, _reservationId);
        await AssertViolationAsync(async, async db => { (await db.SalesDispatches.FindAsync(dispatchId))!.Reference = "gewijzigd"; }, "Sales dispatches are append-only");
        await AssertViolationAsync(async, async db => { db.SalesDispatchLines.Remove(await db.SalesDispatchLines.SingleAsync()); }, "Sales dispatches are append-only");
        await AssertViolationAsync(async, async db => { (await db.StockMovements.SingleAsync(m => m.Type == "Dispatch")).Reason = "gewijzigd"; }, "Dispatch stock movements are append-only");
        await AssertViolationAsync(async, async db => { db.StockMovements.Remove(await db.StockMovements.SingleAsync(m => m.Type == "Dispatch")); }, "Dispatch stock movements are append-only");

        // Een Correction-mutatie mag wel worden toegevoegd en gewijzigd: regel 3 geldt alleen voor Dispatch.
        await using (var db = _factory.CreateContext())
        {
            db.StockMovements.Add(new StockMovement { Type = "Correction", Quantity = 1m, Unit = "box", SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox), PalletContentLineId = _seed.ApprovedBox, Reason = "telling" });
            await SaveAsync(db, async);
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.StockMovements.SingleAsync(m => m.Type == "Correction")).Reason = "hertelling";
            await SaveAsync(db, async);
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal("hertelling", (await verify.StockMovements.SingleAsync(m => m.Type == "Correction")).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rule_6_Uses_On_Hand_After_Dispatch(bool async)
    {
        await ArrangeAsync();
        await SalesTestData.ConsumeAsync(_factory, _reservationId); // voorraad 30 box / 120 kg
        await using (var db = _factory.CreateContext())
        {
            var supply = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox);
            db.StockReservations.Add(new StockReservation { RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, PalletContentLineId = _seed.ApprovedBox, SupplyCommitmentLineId = supply, Quantity = 30m, Unit = "box", NetWeightKg = 120m, Source = "Physical", Status = "Active" });
            await SaveAsync(db, async);
        }
        await AssertViolationAsync(async, async db =>
        {
            var supply = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox);
            db.StockReservations.Add(new StockReservation { RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, PalletContentLineId = _seed.ApprovedBox, SupplyCommitmentLineId = supply, Quantity = 1m, Unit = "box", NetWeightKg = 4m, Source = "Physical", Status = "Active" });
        }, "on-hand quantity or net weight");
    }

    [Fact]
    public async Task Database_Enforces_Consumed_Time_Dispatched_Bounds_And_One_Dispatch_Movement_Per_Reservation()
    {
        await ArrangeAsync();
        await using var db = _factory.CreateContext();
        // Rechtstreeks SQL om de guard te omzeilen en alleen de database-checks te toetsen.
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"StockReservations\" SET \"Status\" = 'Consumed' WHERE \"Id\" = {0}", _reservationId));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"PalletContentLines\" SET \"DispatchedQuantity\" = 41 WHERE \"Id\" = {0}", _seed.ApprovedBox));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"PalletContentLines\" SET \"DispatchedNetWeightKg\" = -1 WHERE \"Id\" = {0}", _seed.ApprovedBox));
        var supply = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"StockMovements\" (\"Type\", \"Quantity\", \"Unit\", \"NetWeightKg\", \"SupplyCommitmentLineId\", \"PalletContentLineId\", \"StockReservationId\", \"CreatedAt\") VALUES ('Dispatch', 10, 'box', 40, {0}, {1}, {2}, {3})", supply, _seed.ApprovedBox, _reservationId, DateTime.UtcNow);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("INSERT INTO \"StockMovements\" (\"Type\", \"Quantity\", \"Unit\", \"NetWeightKg\", \"SupplyCommitmentLineId\", \"PalletContentLineId\", \"StockReservationId\", \"CreatedAt\") VALUES ('Dispatch', 10, 'box', 40, {0}, {1}, {2}, {3})", supply, _seed.ApprovedBox, _reservationId, DateTime.UtcNow));
        // Een niet-Dispatch-mutatie op dezelfde reservering mag wel (gedeeltelijke unieke index).
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"StockMovements\" (\"Type\", \"Quantity\", \"Unit\", \"NetWeightKg\", \"SupplyCommitmentLineId\", \"PalletContentLineId\", \"StockReservationId\", \"CreatedAt\") VALUES ('Reservation', 10, 'box', 40, {0}, {1}, {2}, {3})", supply, _seed.ApprovedBox, _reservationId, DateTime.UtcNow);
    }

    // ---- Regel 8, verzendregelkant ----

    private async Task<int> SecondReservationAsync(int contentLineId, decimal quantity = 5m, decimal kg = 20m, string unit = "box")
    {
        await using var db = _factory.CreateContext();
        var reservation = new StockReservation
        {
            RequestId = Guid.NewGuid(), SalesOrderLineId = unit == "colli" ? _colliLine : _boxLine, PalletContentLineId = contentLineId,
            SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, contentLineId),
            Quantity = quantity, Unit = unit, NetWeightKg = kg, Source = "Physical", Status = "Active"
        };
        db.StockReservations.Add(reservation);
        await db.SaveChangesAsync();
        return reservation.Id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatch_Line_Without_Consumption_Or_New_Movement_Is_Rejected(bool async)
    {
        await ArrangeAsync();
        // Alleen een verzendregel (navigatie): de reservering blijft Active, geen mutatie.
        await AssertViolationAsync(async, async db =>
        {
            var reservation = await db.StockReservations.Include(r => r.PalletContentLine).SingleAsync(r => r.Id == _reservationId);
            var dispatch = new SalesDispatch { RequestId = Guid.NewGuid(), SalesOrderId = reservation.SalesOrderLine == null ? (await db.SalesOrderLines.FindAsync(_boxLine))!.SalesOrderId : reservation.SalesOrderLine.SalesOrderId, ActorReference = "test", DispatchedAt = DateTime.UtcNow };
            var movement = new StockMovement { Type = "Dispatch", Quantity = 10m, Unit = "box", NetWeightKg = 40m, SupplyCommitmentLineId = reservation.SupplyCommitmentLineId, PalletContentLine = reservation.PalletContentLine, StockReservation = reservation };
            dispatch.Lines.Add(new SalesDispatchLine { StockReservation = reservation, SalesOrderLineId = _boxLine, PalletContentLine = reservation.PalletContentLine, Quantity = 10m, Unit = "box", NetWeightKg = 40m, StockMovement = movement });
            db.SalesDispatches.Add(dispatch);
            // Bewust geen Consumed, geen verhoging: de mutatie is er wel, dus eerst struikelt de mutatie-controle.
            db.StockMovements.Add(movement);
        }, "requires its reservation to become Consumed");
        // Alleen FK's: verzendregel naar een bestaande verzending en een al opgeslagen mutatie, zonder verbruik.
        var storedDispatch = await SalesTestData.ConsumeAsync(_factory, _reservationId);
        var other = await SecondReservationAsync(_seed.SecondPalletApprovedBox);
        await AssertViolationAsync(async, async db =>
        {
            var storedMovement = await db.StockMovements.AsNoTracking().SingleAsync(m => m.Type == "Dispatch");
            db.SalesDispatchLines.Add(new SalesDispatchLine
            {
                SalesDispatchId = storedDispatch, StockReservationId = other, SalesOrderLineId = _boxLine, PalletContentLineId = _seed.SecondPalletApprovedBox,
                Quantity = 5m, Unit = "box", NetWeightKg = 20m, StockMovementId = storedMovement.Id
            });
        }, "requires its reservation to go from Active to Consumed");
        // Verbruik compleet, maar de verzendregel wijst (FK) naar een al opgeslagen mutatie in plaats van de nieuwe.
        await AssertViolationAsync(async, async db =>
        {
            var storedMovement = await db.StockMovements.AsNoTracking().SingleAsync(m => m.Type == "Dispatch");
            var dispatch = await SalesTestData.ConsumeInContextAsync(db, other);
            var line = dispatch.Lines.Single();
            line.StockMovement = null;
            line.StockMovementId = storedMovement.Id;
        }, "requires a new dispatch stock movement");
    }

    [Theory]
    [InlineData("quantity", false)]
    [InlineData("quantity", true)]
    [InlineData("unit", false)]
    [InlineData("unit", true)]
    [InlineData("kg", false)]
    [InlineData("kg", true)]
    [InlineData("content", false)]
    [InlineData("content", true)]
    [InlineData("order-line", false)]
    [InlineData("order-line", true)]
    [InlineData("movement", false)]
    [InlineData("movement", true)]
    public async Task Dispatch_Line_That_Differs_From_Its_Reservation_Is_Rejected(string field, bool async)
    {
        await ArrangeAsync();
        var second = await SecondReservationAsync(_seed.SecondPalletApprovedBox);
        var (otherOrder, otherLine, _) = await SalesTestData.SeedOrderAsync(_factory, _seed);
        _ = otherOrder;
        await AssertViolationAsync(async, async db =>
        {
            var first = await SalesTestData.ConsumeInContextAsync(db, _reservationId);
            var line = first.Lines.Single();
            switch (field)
            {
                case "quantity": line.Quantity += 1m; break;
                case "unit": line.Unit = "colli"; break;
                case "kg": line.NetWeightKg += 0.5m; break;
                case "content":
                    line.PalletContentLine = null;
                    line.PalletContentLineId = _seed.SecondPalletApprovedBox;
                    break;
                case "order-line":
                    line.SalesOrderLineId = otherLine;
                    break;
                default:
                {
                    // Twee verbruiken in één save; de verzendregels wijzen naar elkaars mutatie.
                    var secondDispatch = await SalesTestData.ConsumeInContextAsync(db, second);
                    var secondLine = secondDispatch.Lines.Single();
                    (line.StockMovement, secondLine.StockMovement) = (secondLine.StockMovement, line.StockMovement);
                    break;
                }
            }
        }, field == "movement" ? "movement of its own reservation" : "copy quantity, unit, net weight, content line and sales order line");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dispatch_Line_Under_A_Header_Of_Another_Order_Is_Rejected(bool async)
    {
        await ArrangeAsync();
        var (otherOrder, _, _) = await SalesTestData.SeedOrderAsync(_factory, _seed);
        // Navigatie: nieuwe verzending van een andere order.
        await AssertViolationAsync(async, async db =>
        {
            var dispatch = await SalesTestData.ConsumeInContextAsync(db, _reservationId);
            dispatch.SalesOrderId = otherOrder;
        }, "belongs to a dispatch of another sales order");
        // Alleen FK: bestaande verzending van een andere order.
        int foreignDispatch;
        await using (var db = _factory.CreateContext())
        {
            var (_, foreignLine, _) = await SalesTestData.SeedOrderAsync(_factory, _seed);
            var foreign = new StockReservation
            {
                RequestId = Guid.NewGuid(), SalesOrderLineId = foreignLine, PalletContentLineId = _seed.SecondPalletApprovedBox,
                SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.SecondPalletApprovedBox),
                Quantity = 1m, Unit = "box", NetWeightKg = 4m, Source = "Physical", Status = "Active"
            };
            db.StockReservations.Add(foreign);
            await db.SaveChangesAsync();
            foreignDispatch = await SalesTestData.ConsumeAsync(_factory, foreign.Id);
        }
        await AssertViolationAsync(async, async db =>
        {
            await SalesTestData.ConsumeInContextAsync(db, _reservationId, intoDispatchId: foreignDispatch);
        }, "belongs to a dispatch of another sales order");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Valid_Multi_Reservation_Dispatch_With_Navigation_And_FK_References_Succeeds(bool async)
    {
        await ArrangeAsync();
        var second = await SecondReservationAsync(_seed.SecondPalletApprovedBox);
        await using (var db = _factory.CreateContext())
        {
            // Twee reserveringen onder één nieuwe verzending (navigatie).
            var first = await SalesTestData.ConsumeInContextAsync(db, _reservationId, "MULTI");
            await SalesTestData.ConsumeInContextAsync(db, second, "MULTI", into: first);
            await SaveAsync(db, async);
        }
        await using (var verify = _factory.CreateContext())
        {
            var dispatch = await verify.SalesDispatches.Include(d => d.Lines).SingleAsync();
            Assert.Equal(2, dispatch.Lines.Count);
            Assert.Equal(2, await verify.StockReservations.CountAsync(r => r.Status == "Consumed"));
            Assert.Equal(2, await verify.StockMovements.CountAsync(m => m.Type == "Dispatch"));
            Assert.Equal((10m, 40m), ((await verify.PalletContentLines.FindAsync(_seed.ApprovedBox))!.DispatchedQuantity, (await verify.PalletContentLines.FindAsync(_seed.ApprovedBox))!.DispatchedNetWeightKg));
            Assert.Equal((5m, 20m), ((await verify.PalletContentLines.FindAsync(_seed.SecondPalletApprovedBox))!.DispatchedQuantity, (await verify.PalletContentLines.FindAsync(_seed.SecondPalletApprovedBox))!.DispatchedNetWeightKg));
        }

        // Alleen FK voor de verzending: een derde reservering onder de bestaande verzending van dezelfde order; reservering en inhoud ook via FK.
        var third = await SecondReservationAsync(_seed.ApprovedColli, 1m, 5.5m, "colli");
        await using (var db = _factory.CreateContext())
        {
            var existing = await db.SalesDispatches.AsNoTracking().SingleAsync();
            await SalesTestData.ConsumeInContextAsync(db, third, intoDispatchId: existing.Id, fkOnly: true);
            await SaveAsync(db, async);
        }
        await using var check = _factory.CreateContext();
        Assert.Equal(3, await check.SalesDispatchLines.CountAsync(l => l.SalesDispatchId == (check.SalesDispatches.Select(d => d.Id).First())));
        Assert.Equal(3, await check.StockReservations.CountAsync(r => r.Status == "Consumed"));
    }
}
