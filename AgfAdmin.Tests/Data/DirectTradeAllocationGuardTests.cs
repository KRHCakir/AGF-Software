using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Guard-regel 10 (vangnet voor elke writer) en de NULL-veilige database-check op de bron van een verkoopreservering:
// een DirectTrade-toewijzing staat nooit op inhoud, heeft nooit kg, hoort bij dezelfde klant, variant en eenheid, blijft binnen
// de verwachte hoeveelheid en kent alleen Active en Released. Een NULL of onbekende bron passeert nooit (ook niet via SQL UNKNOWN).
public class DirectTradeAllocationGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;
    private int _colliLine;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync()
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity: 500m, colliQuantity: 500m);
    }

    private StockReservation Allocation(decimal quantity, int? sourceLineId = null, int? salesLineId = null, string unit = "box", string source = "DirectTrade", string status = "Active") => new()
    {
        RequestId = Guid.NewGuid(),
        SalesOrderLineId = salesLineId ?? _boxLine,
        SupplyCommitmentLineId = sourceLineId ?? _seed.DirectTradeSourceLine,
        Quantity = quantity,
        Unit = unit,
        Source = source,
        Status = status
    };

    private static Task SaveAsync(AppDbContext db, bool async)
    {
        if (async) return db.SaveChangesAsync();
        db.SaveChanges();
        return Task.CompletedTask;
    }

    private async Task<int> ActiveAllocationCountAsync()
    {
        await using var verify = _factory.CreateContext();
        return await verify.StockReservations.CountAsync(r => r.Status == "Active" && r.Source == "DirectTrade" && r.SalesOrderLineId != null);
    }

    private async Task AssertGuardRejectsAsync(Func<AppDbContext, Task<StockReservation>> build, bool async, string message)
    {
        await using var db = _factory.CreateContext();
        db.StockReservations.Add(await build(db));
        var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        Assert.Contains(message, exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Valid_Allocation_Is_Accepted_And_Can_Be_Released(bool async)
    {
        await ArrangeAsync();
        int id;
        await using (var db = _factory.CreateContext())
        {
            var allocation = Allocation(10m);
            db.StockReservations.Add(allocation);
            await SaveAsync(db, async);
            id = allocation.Id;
        }
        Assert.Equal(1, await ActiveAllocationCountAsync());

        await using (var db = _factory.CreateContext())
        {
            var allocation = await db.StockReservations.SingleAsync(r => r.Id == id);
            allocation.Status = "Released";
            allocation.ReleasedAt = DateTime.UtcNow;
            allocation.ReleaseReason = "test";
            await SaveAsync(db, async);
        }
        Assert.Equal(0, await ActiveAllocationCountAsync());
    }

    [Theory]
    [InlineData("on-content", false)]
    [InlineData("on-content", true)]
    [InlineData("with-kg", false)]
    [InlineData("with-kg", true)]
    [InlineData("other-customer", false)]
    [InlineData("other-customer", true)]
    [InlineData("wrong-unit", false)]
    [InlineData("wrong-unit", true)]
    [InlineData("colli-source-on-box-line", false)]
    [InlineData("colli-source-on-box-line", true)]
    [InlineData("received-status", false)]
    [InlineData("received-status", true)]
    [InlineData("warehouse-source", false)]
    [InlineData("warehouse-source", true)]
    [InlineData("over-expected", false)]
    [InlineData("over-expected", true)]
    [InlineData("null-source", false)]
    [InlineData("null-source", true)]
    [InlineData("unknown-source", false)]
    [InlineData("unknown-source", true)]
    [InlineData("physical-without-content", false)]
    [InlineData("physical-without-content", true)]
    [InlineData("created-consumed", false)]
    [InlineData("created-consumed", true)]
    public async Task Invalid_Allocations_Are_Rejected_By_The_Guard(string scenario, bool async)
    {
        await ArrangeAsync();
        var (build, message) = scenario switch
        {
            "on-content" => ((Func<AppDbContext, Task<StockReservation>>)(_ =>
            {
                var r = Allocation(1m);
                r.PalletContentLineId = _seed.DirectTradeBox;
                return Task.FromResult(r);
            }), "never be placed on a pallet content line"),
            "with-kg" => (_ => { var r = Allocation(1m); r.NetWeightKg = 4m; return Task.FromResult(r); }, "no measured net weight"),
            "other-customer" => (_ => Task.FromResult(Allocation(1m, _seed.OtherCustomerDirectTradeSourceLine)), "bound to customer"),
            "wrong-unit" => (_ => Task.FromResult(Allocation(1m, unit: "colli")), "exactly the unit"),
            "colli-source-on-box-line" => (_ => Task.FromResult(Allocation(1m, _seed.DirectTradeColliSourceLine, unit: "colli")), "exactly the unit"),
            "received-status" => (_ => Task.FromResult(Allocation(1m, _seed.ReceivedDirectTradeSourceLine)), "only Expected or PartiallyReceived"),
            "warehouse-source" => (async db => Allocation(1m, await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox)), "not DirectTrade supply"),
            "over-expected" => (_ => Task.FromResult(Allocation(100.001m)), "exceed its expected quantity"),
            "null-source" => (_ => Task.FromResult(Allocation(1m, source: null)), "known source"),
            "unknown-source" => (_ => Task.FromResult(Allocation(1m, source: "Expected")), "known source"),
            "physical-without-content" => (_ => { var r = Allocation(1m, source: "Physical"); r.NetWeightKg = 4m; return Task.FromResult(r); }, "exactly one pallet content line"),
            "created-consumed" => (_ => { var r = Allocation(1m, status: "Consumed"); r.ConsumedAt = DateTime.UtcNow; return Task.FromResult(r); }, "never be created as Consumed"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        await AssertGuardRejectsAsync(build, async, message);
        Assert.Equal(0, await ActiveAllocationCountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Sum_Of_Allocations_Per_Source_Line_Counts_Stored_And_Tracked_Rows(bool async)
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(Allocation(90m));
            await SaveAsync(db, async);
        }
        // 90 opgeslagen + 20 nieuw > 100.
        await AssertGuardRejectsAsync(_ => Task.FromResult(Allocation(20m)), async, "exceed its expected quantity");
        // Twee nieuwe in één save: 90 + 6 + 5 > 100.
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.AddRange(Allocation(6m), Allocation(5m));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        // Precies vol mag; een Released toewijzing telt niet mee.
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(Allocation(10m));
            await SaveAsync(db, async);
        }
        Assert.Equal(2, await ActiveAllocationCountAsync());
    }

    // Sinds de DirectTrade-verzending: Consumed alleen samen met een verzendregel (regel 11); Corrected bestaat nog niet voor DirectTrade.
    [Theory]
    [InlineData("Consumed", false, "must be shipped by exactly one new DirectTrade shipment line")]
    [InlineData("Consumed", true, "must be shipped by exactly one new DirectTrade shipment line")]
    [InlineData("Corrected", false, "Only a shipped (Consumed) DirectTrade allocation can become Corrected")]
    [InlineData("Corrected", true, "Only a shipped (Consumed) DirectTrade allocation can become Corrected")]
    public async Task An_Allocation_Can_Never_Become_Consumed_Without_A_Shipment_Or_Corrected(string status, bool async, string message)
    {
        await ArrangeAsync();
        int id;
        await using (var db = _factory.CreateContext())
        {
            var allocation = Allocation(10m);
            db.StockReservations.Add(allocation);
            await SaveAsync(db, async);
            id = allocation.Id;
        }
        await using (var db = _factory.CreateContext())
        {
            var allocation = await db.StockReservations.SingleAsync(r => r.Id == id);
            allocation.Status = status;
            allocation.ConsumedAt = DateTime.UtcNow;
            allocation.CorrectedAt = status == "Corrected" ? DateTime.UtcNow : null;
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            Assert.Contains(message, exception.Message);
        }
        Assert.Equal(1, await ActiveAllocationCountAsync());
    }

    // ---- Database-check, buiten de guard om (ruwe SQL): NULL-veilig en per bron ----

    private async Task<int> RawInsertAsync(string source, string status, string contentLine, string kg, int? salesLine = null, int? sourceLine = null)
    {
        await using var db = _factory.CreateContext();
        var sourceSql = source == null ? "NULL" : $"'{source}'";
        var released = status == "Released";
        var sql = "INSERT INTO \"StockReservations\" (\"RequestId\", \"SalesOrderLineId\", \"SupplyCommitmentLineId\", \"PalletContentLineId\", \"Quantity\", \"Unit\", \"NetWeightKg\", \"Source\", \"Status\", \"CreatedAt\", \"UpdatedAt\", \"Version\", \"ConsumedAt\", \"CorrectedAt\", \"ReleasedAt\", \"ReleaseReason\") " +
                  $"VALUES ('{Guid.NewGuid()}', {salesLine ?? _boxLine}, {sourceLine ?? _seed.DirectTradeSourceLine}, {contentLine}, '1.0', 'box', {kg}, {sourceSql}, '{status}', '2026-10-20 08:00:00', '2026-10-20 08:00:00', 0, " +
                  $"{(status is "Consumed" or "Corrected" ? "'2026-10-20 08:00:00'" : "NULL")}, {(status == "Corrected" ? "'2026-10-20 08:00:00'" : "NULL")}, {(released ? "'2026-10-20 08:00:00'" : "NULL")}, {(released ? "'test'" : "NULL")})";
        return await db.Database.ExecuteSqlRawAsync(sql);
    }

    [Theory]
    [InlineData(null, "Active", "NULL", "NULL")]            // NULL bron: nooit via UNKNOWN
    [InlineData("Expected", "Active", "NULL", "NULL")]      // onbekende bron
    [InlineData("physical", "Active", "content", "'4.0'")]   // hoofdlettergevoelig
    [InlineData("Physical", "Active", "NULL", "'4.0'")]      // Physical zonder inhoud
    [InlineData("Physical", "Active", "content", "NULL")]    // Physical zonder kg
    [InlineData("Physical", "Active", "content", "'0'")]     // Physical met kg 0
    [InlineData("DirectTrade", "Active", "content", "NULL")] // DirectTrade op inhoud
    [InlineData("DirectTrade", "Active", "NULL", "'4.0'")]   // DirectTrade met kg
    public async Task Database_Check_Rejects_Null_Unknown_And_Mismatched_Sources(string source, string status, string contentLine, string kg)
    {
        await ArrangeAsync();
        var content = contentLine == "content" ? _seed.ApprovedBox.ToString() : contentLine;
        await using var db = _factory.CreateContext();
        var sourceLine = contentLine == "content" ? await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox) : _seed.DirectTradeSourceLine;

        var exception = await Assert.ThrowsAsync<SqliteException>(() => RawInsertAsync(source, status, content, kg, sourceLine: sourceLine));

        // Een NULL bron strandt al op NOT NULL; elke andere combinatie op de check zelf.
        Assert.Contains("constraint failed", exception.Message);

        Assert.Equal(0, await db.StockReservations.CountAsync(r => r.SalesOrderLineId != null));
    }

    [Fact]
    public async Task Database_Check_Accepts_A_Complete_Physical_Reservation_And_A_Complete_Allocation()
    {
        await ArrangeAsync();
        await using var db = _factory.CreateContext();
        var warehouseSource = await SalesTestData.SupplyLineOfAsync(db, _seed.ApprovedBox);
        Assert.Equal(1, await RawInsertAsync("Physical", "Active", _seed.ApprovedBox.ToString(), "'4.0'", sourceLine: warehouseSource));
        Assert.Equal(1, await RawInsertAsync("DirectTrade", "Active", "NULL", "NULL"));
        Assert.Equal(1, await RawInsertAsync("DirectTrade", "Released", "NULL", "NULL"));
        // Sinds de DirectTrade-verzendcorrectie: Corrected is een toegestane (terminale) DirectTrade-status in de database.
        Assert.Equal(1, await RawInsertAsync("DirectTrade", "Corrected", "NULL", "NULL"));
        Assert.Equal(4, await db.StockReservations.CountAsync(r => r.SalesOrderLineId != null));
    }

    // Regressie (review PR #30): vrijgeven, de vrijgekomen capaciteit elders toewijzen en daarna de oude toewijzing reactiveren
    // zou anders boven de verwachte hoeveelheid uitkomen, omdat de capaciteitscontrole alleen nieuwe rijen telt.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Released_Allocation_Can_Never_Be_Reactivated_After_Its_Capacity_Was_Allocated_Elsewhere(bool async)
    {
        await ArrangeAsync();
        int oldId;
        await using (var db = _factory.CreateContext())
        {
            var full = Allocation(100m);
            db.StockReservations.Add(full);
            await SaveAsync(db, async);
            oldId = full.Id;
        }
        await using (var db = _factory.CreateContext())
        {
            var old = await db.StockReservations.SingleAsync(r => r.Id == oldId);
            old.Status = "Released";
            old.ReleasedAt = DateTime.UtcNow;
            old.ReleaseReason = "vrijgegeven";
            await SaveAsync(db, async);
        }
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(Allocation(100m, salesLineId: _boxLine));
            await SaveAsync(db, async);
        }

        await using (var db = _factory.CreateContext())
        {
            var old = await db.StockReservations.SingleAsync(r => r.Id == oldId);
            old.Status = "Active";
            old.ReleasedAt = null;
            old.ReleaseReason = null;
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            Assert.Contains("can never become Active again", exception.Message);
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal("Released", (await verify.StockReservations.AsNoTracking().SingleAsync(r => r.Id == oldId)).Status);
        Assert.Equal(100m, (await verify.StockReservations.AsNoTracking()
            .Where(r => r.Source == "DirectTrade" && r.Status == "Active" && r.SupplyCommitmentLineId == _seed.DirectTradeSourceLine).ToListAsync()).Sum(r => r.Quantity));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Source_Of_An_Existing_Sales_Reservation_Cannot_Switch_To_Or_From_DirectTrade(bool async)
    {
        await ArrangeAsync();
        int id;
        await using (var db = _factory.CreateContext())
        {
            var allocation = Allocation(10m);
            db.StockReservations.Add(allocation);
            await SaveAsync(db, async);
            id = allocation.Id;
        }
        await using (var db = _factory.CreateContext())
        {
            var allocation = await db.StockReservations.SingleAsync(r => r.Id == id);
            allocation.Source = "Physical";
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            Assert.Contains("cannot change", exception.Message);
        }
    }
}
