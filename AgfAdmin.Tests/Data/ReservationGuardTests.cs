using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Guard-regels 5 en 6: vangnet voor elke writer. Alleen voor nieuwe Active reserveringen van een SalesOrderLine.
public class ReservationGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private SalesSeed _seed;
    private int _boxLine;
    private int _colliLine;

    private async Task ArrangeAsync()
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity: 500m, colliQuantity: 500m);
    }

    private static async Task<StockReservation> ReservationAsync(AppDbContext db, int salesLine, int contentLine, decimal quantity, decimal kg, string unit = null)
    {
        var content = await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == contentLine);
        return new StockReservation
        {
            RequestId = Guid.NewGuid(),
            SalesOrderLineId = salesLine,
            PalletContentLineId = contentLine,
            SupplyCommitmentLineId = content.SupplyCommitmentLineId,
            Quantity = quantity,
            Unit = unit ?? content.Unit,
            NetWeightKg = kg,
            Source = "Physical",
            Status = "Active"
        };
    }

    private static Task SaveAsync(AppDbContext db, bool async)
    {
        if (async) return db.SaveChangesAsync();
        db.SaveChanges();
        return Task.CompletedTask;
    }

    private async Task<int> ActiveCountAsync()
    {
        await using var verify = _factory.CreateContext();
        return await verify.StockReservations.CountAsync(r => r.Status == "Active" && r.SalesOrderLineId != null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approved_Received_Warehouse_Content_Can_Be_Reserved(bool async)
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 10m, 40m));
            await SaveAsync(db, async);
        }
        Assert.Equal(1, await ActiveCountAsync());
    }

    [Theory]
    [InlineData("quarantine", false)]
    [InlineData("quarantine", true)]
    [InlineData("blocked", false)]
    [InlineData("blocked", true)]
    [InlineData("rejected", false)]
    [InlineData("rejected", true)]
    [InlineData("in-transit", false)]
    [InlineData("in-transit", true)]
    [InlineData("crossdock", false)]
    [InlineData("crossdock", true)]
    [InlineData("direct-trade", false)]
    [InlineData("direct-trade", true)]
    [InlineData("unit-mismatch", false)]
    [InlineData("unit-mismatch", true)]
    public async Task Non_Reservable_Content_Is_Rejected_By_The_Guard(string scenario, bool async)
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            var reservation = scenario switch
            {
                "quarantine" => await ReservationAsync(db, _boxLine, _seed.QuarantineBox, 1m, 4m),
                "blocked" => await ReservationAsync(db, _boxLine, _seed.BlockedBox, 1m, 4m),
                "rejected" => await ReservationAsync(db, _boxLine, _seed.RejectedBox, 1m, 4m),
                "in-transit" => await ReservationAsync(db, _boxLine, _seed.InTransitBox, 1m, 4m),
                "crossdock" => await ReservationAsync(db, _boxLine, _seed.CrossdockBox, 1m, 4m),
                "direct-trade" => await ReservationAsync(db, _boxLine, _seed.DirectTradeBox, 1m, 4m),
                _ => await ReservationAsync(db, _colliLine, _seed.ApprovedBox, 1m, 4m, unit: "colli"),
            };
            db.StockReservations.Add(reservation);
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        Assert.Equal(0, await ActiveCountAsync());
    }

    [Theory]
    [InlineData("quantity-in-one-save", false)]
    [InlineData("quantity-in-one-save", true)]
    [InlineData("quantity-on-top-of-stored", false)]
    [InlineData("quantity-on-top-of-stored", true)]
    [InlineData("weight-on-top-of-stored", false)]
    [InlineData("weight-on-top-of-stored", true)]
    public async Task Sum_Of_Active_Reservations_May_Not_Exceed_Content_Quantity_Or_Weight(string scenario, bool async)
    {
        await ArrangeAsync();
        if (scenario != "quantity-in-one-save")
        {
            await using var db = _factory.CreateContext();
            db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 30m, 100m));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateContext())
        {
            switch (scenario)
            {
                case "quantity-in-one-save":
                    db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 25m, 80m));
                    db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 16m, 70m));
                    break;
                case "quantity-on-top-of-stored":
                    db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 11m, 40m));
                    break;
                default:
                    db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 5m, 60.001m));
                    break;
            }
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        Assert.Equal(scenario == "quantity-in-one-save" ? 0 : 1, await ActiveCountAsync());
    }

    [Fact]
    public async Task Reserving_Exactly_The_Content_Limit_Is_Allowed_And_Released_Reservations_Do_Not_Count()
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            var released = await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 40m, 160m);
            released.Status = "Released";
            released.ReleasedAt = DateTime.UtcNow;
            released.ReleaseReason = "Fout";
            db.StockReservations.Add(released);
            db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 30m, 120m));
            db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 10m, 40m));
            await db.SaveChangesAsync();
        }
        Assert.Equal(2, await ActiveCountAsync());
    }

    [Fact]
    public async Task Legacy_OrderLine_Reservations_Are_Outside_Rules_5_And_6()
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            // Legacy-eigenaar op geblokkeerde inhoud en boven de limiet: regel 5/6 gelden hier niet (regel 1 wel).
            var blocked = await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == _seed.BlockedBox);
            db.StockReservations.Add(new StockReservation
            {
                OrderLineId = _seed.LegacyOrderLineId,
                SupplyCommitmentLineId = blocked.SupplyCommitmentLineId,
                PalletContentLineId = blocked.Id,
                Quantity = 99m,
                Source = "Physical",
                Status = "Active"
            });
            var approved = await db.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == _seed.ApprovedBox);
            db.StockReservations.Add(new StockReservation
            {
                OrderLineId = _seed.LegacyOrderLineId,
                SupplyCommitmentLineId = approved.SupplyCommitmentLineId,
                PalletContentLineId = approved.Id,
                Quantity = 40m,
                Source = "Physical",
                Status = "Active"
            });
            await db.SaveChangesAsync();
        }

        // Legacy-reserveringen tellen niet mee in de som van de nieuwe flow.
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 40m, 160m));
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await ActiveCountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Releasing_A_Reservation_On_Content_That_Became_Blocked_Is_Always_Allowed(bool async)
    {
        await ArrangeAsync();
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(await ReservationAsync(db, _boxLine, _seed.ApprovedBox, 10m, 40m));
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.PalletContentLines.SingleAsync(c => c.Id == _seed.ApprovedBox)).QualityStatus = "Blocked";
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateContext())
        {
            var reservation = await db.StockReservations.SingleAsync();
            reservation.Status = "Released";
            reservation.ReleasedAt = DateTime.UtcNow;
            reservation.ReleaseReason = "Partij geblokkeerd";
            await SaveAsync(db, async);
        }
        Assert.Equal(0, await ActiveCountAsync());
    }
}
