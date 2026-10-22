using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Guard-regel 9 (verzendcorrecties in beide richtingen), onveranderlijke herkomst en Corrected als eindtoestand, sync en async.
public class CorrectionLedgerGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _boxLine;
    private int _reservationId;
    private int _dispatchId;

    public void Dispose() => _factory.Dispose();

    private const string Ggn = "0049929123456";
    private const string OtherGgn = "0049929000002";

    private async Task ArrangeAsync(string ggn = null)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_, _boxLine, _) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity: 100m);
        await using (var db = _factory.CreateContext())
        {
            if (ggn != null)
            {
                // Partij-GGN zoals bij ontvangst vastgelegd (vóór reserveren en verzenden). Rechtstreeks in de database: de guard weigert
                // juist het achteraf zetten op een bestaande regel, en het seed-schema kent de kolom niet als parameter.
                await db.Database.ExecuteSqlRawAsync("UPDATE \"PalletContentLines\" SET \"Ggn\" = {0} WHERE \"Id\" = {1}", ggn, _seed.ApprovedBox);
            }
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
        _dispatchId = await SalesTestData.ConsumeAsync(_factory, _reservationId);
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
    public async Task A_Complete_Correction_Is_Accepted_And_Inserted_Atomically(bool async)
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            await SalesTestData.CorrectInContextAsync(db, _dispatchId);
            await SaveAsync(db, async);
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal("Corrected", (await verify.StockReservations.FindAsync(_reservationId))!.Status);
        var restored = await verify.PalletContentLines.SingleAsync(c => c.OriginCorrectionLineId != null);
        Assert.Equal(("Quarantine", 10m, 40m, 0m), (restored.QualityStatus, restored.Quantity, restored.NetWeightKg, restored.DispatchedQuantity));
        Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.Type == "Correction" && m.SalesDispatchCorrectionLineId != null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrected_Without_The_Complete_Correction_Is_Rejected(bool async)
    {
        await ArrangeAsync();
        await AssertViolationAsync(async, async db =>
        {
            var r = await db.StockReservations.FindAsync(_reservationId);
            r!.Status = "Corrected";
            r.CorrectedAt = DateTime.UtcNow;
        }, "exactly one corrected reservation, one correction line, one restored content line and one compensating correction movement");
        await AssertViolationAsync(async, async db =>
        {
            var (correction, restored, movement) = await SalesTestData.CorrectInContextAsync(db, _dispatchId);
            db.Entry(restored).State = EntityState.Detached;
            db.Entry(movement).State = EntityState.Detached;
        }, "exactly one corrected reservation");
        await AssertViolationAsync(async, async db =>
        {
            await SalesTestData.CorrectInContextAsync(db, _dispatchId);
            (await db.StockReservations.FindAsync(_reservationId))!.CorrectedAt = null;
        }, "without a correction time");
        // Een Active reservering mag nooit rechtstreeks Corrected worden.
        await AssertViolationAsync(async, async db =>
        {
            var supply = await SalesTestData.SupplyLineOfAsync(db, _seed.SecondPalletApprovedBox);
            var active = new StockReservation { RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, PalletContentLineId = _seed.SecondPalletApprovedBox, SupplyCommitmentLineId = supply, Quantity = 1m, Unit = "box", NetWeightKg = 4m, Source = "Physical", Status = "Active" };
            db.StockReservations.Add(active);
            await db.SaveChangesAsync();
            active.Status = "Corrected";
            active.CorrectedAt = DateTime.UtcNow;
        }, "Only a Consumed reservation can become Corrected");
    }

    [Theory]
    [InlineData("quantity", false)]
    [InlineData("quantity", true)]
    [InlineData("kg", false)]
    [InlineData("kg", true)]
    [InlineData("quality", false)]
    [InlineData("quality", true)]
    [InlineData("lot", false)]
    [InlineData("lot", true)]
    [InlineData("pallet", false)]
    [InlineData("pallet", true)]
    [InlineData("movement-kg", false)]
    [InlineData("movement-kg", true)]
    [InlineData("dispatched", false)]
    [InlineData("dispatched", true)]
    public async Task Restored_Content_Or_Compensation_That_Differs_From_The_Dispatch_Line_Is_Rejected(string field, bool async)
    {
        await ArrangeAsync();
        await AssertViolationAsync(async, async db =>
        {
            var (correction, restored, movement) = await SalesTestData.CorrectInContextAsync(db, _dispatchId);
            switch (field)
            {
                case "quantity": restored.Quantity = 9m; break;
                case "kg": restored.NetWeightKg = 39m; break;
                case "quality": restored.QualityStatus = "Approved"; break;
                case "lot": restored.LotNumber = "LOT-OTHER"; break;
                case "pallet": restored.PalletId = _seed.SecondWarehousePalletId; break;
                case "movement-kg": movement.NetWeightKg = 41m; break;
                default: restored.DispatchedQuantity = 1m; break;
            }
        }, field == "movement-kg" ? "compensating movement" : field == "dispatched" ? "starts with nothing dispatched" : "must be Quarantine on the original pallet");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Correction_Content_And_Compensations_Never_Exist_Outside_A_Correction(bool async)
    {
        await ArrangeAsync();
        var correctionLineId = (await SalesTestData.CorrectAsync(_factory, _dispatchId)).CorrectionLineId;
        // Een losse inhoudsregel met herkomst correctie.
        await AssertViolationAsync(async, async db =>
        {
            var original = await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == _seed.ApprovedBox);
            db.PalletContentLines.Add(new PalletContentLine
            {
                RequestId = Guid.NewGuid(), PalletId = original.PalletId, SupplyCommitmentLineId = original.SupplyCommitmentLineId, LotNumber = original.LotNumber,
                Quantity = 1m, Unit = "box", NetWeightKg = 4m, QualityStatus = "Quarantine", OriginCorrectionLineId = correctionLineId
            });
        }, "exactly one corrected reservation");
        // Een losse compenserende mutatie.
        await AssertViolationAsync(async, async db =>
        {
            db.StockMovements.Add(new StockMovement { Type = "Correction", Quantity = 1m, Unit = "box", NetWeightKg = 4m, SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox), PalletContentLineId = _seed.ApprovedBox, StockReservationId = _reservationId });
        }, "exactly one corrected reservation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Origin_Source_And_Lot_Of_Correction_Content_Are_Immutable_And_Corrected_Is_Terminal(bool async)
    {
        await ArrangeAsync();
        var (_, restoredId, _) = await SalesTestData.CorrectAsync(_factory, _dispatchId);
        await AssertViolationAsync(async, async db => { (await db.PalletContentLines.FindAsync(restoredId))!.OriginCorrectionLineId = null; }, "correction origin");
        await AssertViolationAsync(async, async db => { (await db.PalletContentLines.FindAsync(_seed.ApprovedBox))!.OriginCorrectionLineId = 1; }, "correction origin");
        await AssertViolationAsync(async, async db => { (await db.PalletContentLines.FindAsync(restoredId))!.LotNumber = "LOT-X"; }, "lot of correction content");
        await AssertViolationAsync(async, async db =>
        {
            var line = await db.PalletContentLines.FindAsync(restoredId);
            line!.SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedColli);
        }, "cannot be changed");
        foreach (var next in new[] { "Active", "Consumed", "Released" })
        {
            await AssertViolationAsync(async, async db =>
            {
                var r = await db.StockReservations.FindAsync(_reservationId);
                r!.Status = next;
                if (next == "Released") { r.ReleasedAt = DateTime.UtcNow; r.ReleaseReason = "x"; }
            }, "terminal");
        }
        // Correcties en compensaties zijn append-only; een reguliere Correction-mutatie zonder koppeling blijft vrij.
        await AssertViolationAsync(async, async db => { (await db.SalesDispatchCorrections.SingleAsync()).Reason = "gewijzigd"; }, "append-only");
        await AssertViolationAsync(async, async db => { db.SalesDispatchCorrectionLines.Remove(await db.SalesDispatchCorrectionLines.SingleAsync()); }, "append-only");
        await AssertViolationAsync(async, async db => { (await db.StockMovements.SingleAsync(m => m.SalesDispatchCorrectionLineId != null)).Reason = "gewijzigd"; }, "append-only");
        await using (var db = _factory.CreateContext())
        {
            db.StockMovements.Add(new StockMovement { Type = "Correction", Quantity = 1m, Unit = "box", SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox), PalletContentLineId = _seed.ApprovedBox, Reason = "telling" });
            await SaveAsync(db, async);
        }
    }

    // ---- Partij-GGN: historisch op de inhoudsregel, exact gekopieerd door correctie-inhoud, artikelstandaard blijft vrij ----

    private async Task<(string Status, decimal DispatchedQuantity, string OriginalGgn, int Restored, int Movements, decimal LineQuantity, decimal LineKg)> HistoryAsync()
    {
        await using var db = _factory.CreateContext();
        var line = await db.SalesDispatchLines.SingleAsync(l => l.StockReservationId == _reservationId);
        var original = await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == _seed.ApprovedBox);
        return ((await db.StockReservations.FindAsync(_reservationId))!.Status, original.DispatchedQuantity, original.Ggn,
            await db.PalletContentLines.CountAsync(c => c.OriginCorrectionLineId != null), await db.StockMovements.CountAsync(), line.Quantity, line.NetWeightKg);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task A_Complete_Correction_Copies_The_Stored_Ggn_Of_The_Original_Including_Null_And_Keeps_It_Fixed(bool withGgn, bool async)
    {
        await ArrangeAsync(withGgn ? Ggn : null);
        await using (var db = _factory.CreateContext())
        {
            await SalesTestData.CorrectInContextAsync(db, _dispatchId);
            await SaveAsync(db, async);
        }
        int restoredId;
        await using (var verify = _factory.CreateContext())
        {
            var restored = await verify.PalletContentLines.SingleAsync(c => c.OriginCorrectionLineId != null);
            Assert.Equal(withGgn ? Ggn : null, restored.Ggn);
            restoredId = restored.Id;
        }
        // Ook de teruggeboekte regel houdt haar GGN daarna vast (wijzigen, wissen of alsnog zetten).
        await AssertViolationAsync(async, async db => { (await db.PalletContentLines.FindAsync(restoredId))!.Ggn = withGgn ? null : Ggn; }, "GGN");
        await AssertViolationAsync(async, async db => { (await db.PalletContentLines.FindAsync(restoredId))!.Ggn = OtherGgn; }, "GGN");
        await using var check = _factory.CreateContext();
        Assert.Equal(withGgn ? Ggn : null, (await check.PalletContentLines.FindAsync(restoredId))!.Ggn);
    }

    [Theory]
    [InlineData("other", false)]
    [InlineData("other", true)]
    [InlineData("cleared", false)]
    [InlineData("cleared", true)]
    [InlineData("from-null", false)]
    [InlineData("from-null", true)]
    public async Task Restored_Content_With_A_Different_Ggn_Than_The_Stored_Original_Is_Rejected_And_History_Stays(string scenario, bool async)
    {
        await ArrangeAsync(scenario == "from-null" ? null : Ggn);
        var before = await HistoryAsync();
        await AssertViolationAsync(async, async db =>
        {
            var (_, restored, _) = await SalesTestData.CorrectInContextAsync(db, _dispatchId);
            // "Bij vergissing" uit het artikel afgeleid, gewist of gezet: nooit de opgeslagen partij-GGN.
            restored.Ggn = scenario == "cleared" ? null : OtherGgn;
        }, "GGN");
        Assert.Equal(before, await HistoryAsync());
        Assert.Equal(("Consumed", 10m, 0, 10m, 40m), (before.Status, before.DispatchedQuantity, before.Restored, before.LineQuantity, before.LineKg));
    }

    [Theory]
    [InlineData("dispatch", false)]
    [InlineData("dispatch", true)]
    [InlineData("correction", false)]
    [InlineData("correction", true)]
    public async Task Ggn_Of_Existing_Content_Cannot_Change_Together_With_A_Dispatch_Or_Correction(string when, bool async)
    {
        await ArrangeAsync(Ggn);
        var before = await HistoryAsync();
        if (when == "dispatch")
        {
            int secondReservation;
            await using (var db = _factory.CreateContext())
            {
                var reservation = new StockReservation
                {
                    RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, PalletContentLineId = _seed.ApprovedBox,
                    SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox),
                    Quantity = 5m, Unit = "box", NetWeightKg = 20m, Source = "Physical", Status = "Active"
                };
                db.StockReservations.Add(reservation);
                await db.SaveChangesAsync();
                secondReservation = reservation.Id;
            }
            await AssertViolationAsync(async, async db =>
            {
                var dispatch = await SalesTestData.ConsumeInContextAsync(db, secondReservation);
                dispatch.Lines.Single().PalletContentLine.Ggn = OtherGgn;
            }, "GGN");
            await using var verify = _factory.CreateContext();
            Assert.Equal("Active", (await verify.StockReservations.FindAsync(secondReservation))!.Status);
            Assert.Equal(1, await verify.SalesDispatches.CountAsync());
        }
        else
        {
            await AssertViolationAsync(async, async db =>
            {
                await SalesTestData.CorrectInContextAsync(db, _dispatchId);
                (await db.PalletContentLines.FindAsync(_seed.ApprovedBox))!.Ggn = null;
            }, "GGN");
        }
        // Historische verzendgegevens en de opgeslagen GGN blijven gelijk.
        Assert.Equal(before, await HistoryAsync());
        Assert.Equal((Ggn, 10m, "Consumed"), (before.OriginalGgn, before.DispatchedQuantity, before.Status));
    }

    [Fact]
    public async Task Database_Enforces_Corrected_Time_Confirmation_And_One_Compensation_Per_Reservation()
    {
        await ArrangeAsync();
        await using var db = _factory.CreateContext();
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"StockReservations\" SET \"Status\" = 'Corrected' WHERE \"Id\" = {0}", _reservationId));
        var supply = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox);
        await db.Database.ExecuteSqlRawAsync("INSERT INTO \"StockMovements\" (\"Type\", \"Quantity\", \"Unit\", \"NetWeightKg\", \"SupplyCommitmentLineId\", \"PalletContentLineId\", \"StockReservationId\", \"CreatedAt\") VALUES ('Correction', 10, 'box', 40, {0}, {1}, {2}, {3})", supply, _seed.ApprovedBox, _reservationId, DateTime.UtcNow);
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("INSERT INTO \"StockMovements\" (\"Type\", \"Quantity\", \"Unit\", \"NetWeightKg\", \"SupplyCommitmentLineId\", \"PalletContentLineId\", \"StockReservationId\", \"CreatedAt\") VALUES ('Correction', 10, 'box', 40, {0}, {1}, {2}, {3})", supply, _seed.ApprovedBox, _reservationId, DateTime.UtcNow));
        var orderId = (await db.SalesOrderLines.FindAsync(_boxLine))!.SalesOrderId;
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("INSERT INTO \"SalesDispatchCorrections\" (\"RequestId\", \"SalesDispatchId\", \"SalesOrderId\", \"Reason\", \"ActorReference\", \"GoodsNeverLeftWarehouseConfirmed\", \"CorrectedAt\") VALUES ({0}, {1}, {2}, 'x', 'a', 0, {3})", Guid.NewGuid(), _dispatchId, orderId, DateTime.UtcNow));
    }
}
