using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

public class TraceabilityGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record Graph(int OrderLineId, int SourceLineA, int SourceLineB, int PalletId, int ContentLineOnA);

    private async Task<Graph> SeedAsync()
    {
        await using var db = _factory.CreateContext();

        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass 6", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.50m };
        var order = new Order { CustomCustomerName = "Customer C" };
        var orderLine = new OrderLine { Order = order, ProductVariant = variant, Quantity = 1, UnitPrice = 12.50m };
        var commitment = new SupplyCommitment { RequestId = Guid.NewGuid(), Vendor = new Vendor { Name = "Supplier B" }, Route = "Warehouse", Status = "Expected", Origin = "Peru" };
        var lineA = new SupplyCommitmentLine { SupplyCommitment = commitment, ProductVariant = variant, ExpectedQuantity = 10m, ExpectedUnit = "box" };
        var lineB = new SupplyCommitmentLine { SupplyCommitment = commitment, ProductVariant = variant, ExpectedQuantity = 10m, ExpectedUnit = "box" };
        var pallet = new Pallet { Reference = "PAL-001", LogisticsStatus = "Received", LocationContext = "Warehouse" };
        var content = NewContentLine(pallet, lineA);

        db.AddRange(variant, orderLine, commitment, lineA, lineB, pallet, content);
        await db.SaveChangesAsync();

        return new Graph(orderLine.Id, lineA.Id, lineB.Id, pallet.Id, content.Id);
    }

    private static PalletContentLine NewContentLine(Pallet pallet, SupplyCommitmentLine source) => new()
    {
        RequestId = Guid.NewGuid(),
        Pallet = pallet,
        SupplyCommitmentLine = source,
        LotNumber = "LOT-1",
        Quantity = 4m,
        Unit = "box",
        NetWeightKg = 16m,
        QualityStatus = "Quarantine"
    };

    private static StockMovement Movement(int sourceLineId, int? contentLineId) => new()
    {
        Type = "Receipt",
        Quantity = 1m,
        SupplyCommitmentLineId = sourceLineId,
        PalletContentLineId = contentLineId
    };

    private static StockReservation Reservation(int orderLineId, int sourceLineId, int? contentLineId) => new()
    {
        OrderLineId = orderLineId,
        SupplyCommitmentLineId = sourceLineId,
        PalletContentLineId = contentLineId,
        Quantity = 1m,
        Source = "Physical",
        Status = "Active"
    };

    private static Task SaveAsync(AppDbContext db, bool async)
    {
        if (async)
        {
            return db.SaveChangesAsync();
        }

        db.SaveChanges();
        return Task.CompletedTask;
    }

    private async Task AssertNoMovementsOrReservationsAsync()
    {
        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.StockReservations.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Movement_And_Reservation_With_Same_Source_As_ContentLine_Are_Saved(bool async)
    {
        var graph = await SeedAsync();

        await using (var db = _factory.CreateContext())
        {
            db.StockMovements.Add(Movement(graph.SourceLineA, graph.ContentLineOnA));
            db.StockReservations.Add(Reservation(graph.OrderLineId, graph.SourceLineA, graph.ContentLineOnA));
            await SaveAsync(db, async);
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.StockMovements.CountAsync());
        Assert.Equal(1, await verify.StockReservations.CountAsync());
    }

    [Theory]
    [InlineData("movement", false)]
    [InlineData("movement", true)]
    [InlineData("reservation", false)]
    [InlineData("reservation", true)]
    public async Task Different_Source_Than_ContentLine_Is_Rejected_And_Nothing_Is_Saved(string kind, bool async)
    {
        var graph = await SeedAsync();

        await using (var db = _factory.CreateContext())
        {
            // Een geldige beweging in dezelfde save mag ook niet worden opgeslagen.
            db.StockMovements.Add(Movement(graph.SourceLineA, null));
            if (kind == "movement")
            {
                db.StockMovements.Add(Movement(graph.SourceLineB, graph.ContentLineOnA));
            }
            else
            {
                db.StockReservations.Add(Reservation(graph.OrderLineId, graph.SourceLineB, graph.ContentLineOnA));
            }

            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }

        await AssertNoMovementsOrReservationsAsync();
    }

    [Theory]
    [InlineData("movement-source")]
    [InlineData("movement-content")]
    [InlineData("reservation-source")]
    [InlineData("reservation-content")]
    public async Task Changing_An_Existing_Record_To_A_Mismatch_Is_Rejected(string change)
    {
        var graph = await SeedAsync();
        int otherContentLineOnB;
        await using (var db = _factory.CreateContext())
        {
            var pallet = await db.Pallets.SingleAsync();
            var sourceB = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == graph.SourceLineB);
            var content = NewContentLine(pallet, sourceB);
            db.PalletContentLines.Add(content);
            db.StockMovements.Add(Movement(graph.SourceLineA, graph.ContentLineOnA));
            db.StockReservations.Add(Reservation(graph.OrderLineId, graph.SourceLineA, graph.ContentLineOnA));
            await db.SaveChangesAsync();
            otherContentLineOnB = content.Id;
        }

        await using (var db = _factory.CreateContext())
        {
            var movement = await db.StockMovements.SingleAsync();
            var reservation = await db.StockReservations.SingleAsync();
            switch (change)
            {
                case "movement-source": movement.SupplyCommitmentLineId = graph.SourceLineB; break;
                case "movement-content": movement.PalletContentLineId = otherContentLineOnB; break;
                case "reservation-source": reservation.SupplyCommitmentLineId = graph.SourceLineB; break;
                case "reservation-content": reservation.PalletContentLineId = otherContentLineOnB; break;
            }

            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(graph.SourceLineA, (await verify.StockMovements.SingleAsync()).SupplyCommitmentLineId);
        Assert.Equal(graph.ContentLineOnA, (await verify.StockMovements.SingleAsync()).PalletContentLineId);
        Assert.Equal(graph.SourceLineA, (await verify.StockReservations.SingleAsync()).SupplyCommitmentLineId);
        Assert.Equal(graph.ContentLineOnA, (await verify.StockReservations.SingleAsync()).PalletContentLineId);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task New_ContentLine_And_Movement_In_Same_Save_Are_Checked_With_Temporary_Keys(bool matching, bool async)
    {
        var graph = await SeedAsync();

        await using (var db = _factory.CreateContext())
        {
            var pallet = await db.Pallets.SingleAsync();
            var sourceA = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == graph.SourceLineA);
            var sourceB = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == graph.SourceLineB);
            var content = NewContentLine(pallet, sourceA);
            db.PalletContentLines.Add(content);
            db.StockMovements.Add(new StockMovement
            {
                Type = "Receipt",
                Quantity = 1m,
                SupplyCommitmentLine = matching ? sourceA : sourceB,
                PalletContentLine = content
            });

            if (matching)
            {
                await SaveAsync(db, async);
            }
            else
            {
                await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            }
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(matching ? 1 : 0, await verify.StockMovements.CountAsync());
        Assert.Equal(matching ? 2 : 1, await verify.PalletContentLines.CountAsync());
    }

    [Fact]
    public async Task No_ContentLine_Link_Is_Not_Checked()
    {
        var graph = await SeedAsync();

        await using (var db = _factory.CreateContext())
        {
            db.StockMovements.Add(Movement(graph.SourceLineB, null));
            db.StockReservations.Add(Reservation(graph.OrderLineId, graph.SourceLineB, null));
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.StockMovements.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_ContentLine_Is_Left_To_The_Foreign_Key(bool async)
    {
        var graph = await SeedAsync();

        await using (var db = _factory.CreateContext())
        {
            db.StockMovements.Add(Movement(graph.SourceLineA, graph.ContentLineOnA + 1000));

            var exception = await Record.ExceptionAsync(() => SaveAsync(db, async));
            Assert.IsType<DbUpdateException>(exception);
        }

        await AssertNoMovementsOrReservationsAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContentLine_Source_Cannot_Be_Changed(bool async)
    {
        var graph = await SeedAsync();

        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync();
            content.SupplyCommitmentLineId = graph.SourceLineB;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }

        await using (var db = _factory.CreateContext())
        {
            // Ook een losgekoppelde Update met een andere bron wordt herkend.
            db.PalletContentLines.Update(new PalletContentLine
            {
                Id = graph.ContentLineOnA,
                RequestId = Guid.NewGuid(),
                PalletId = graph.PalletId,
                SupplyCommitmentLineId = graph.SourceLineB,
                LotNumber = "LOT-1",
                Quantity = 4m,
                Unit = "box",
                NetWeightKg = 16m,
                QualityStatus = "Quarantine"
            });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(graph.SourceLineA, (await verify.PalletContentLines.SingleAsync()).SupplyCommitmentLineId);
    }

    [Fact]
    public async Task ContentLine_Other_Fields_Can_Still_Be_Updated()
    {
        await SeedAsync();

        await using (var db = _factory.CreateContext())
        {
            var content = await db.PalletContentLines.SingleAsync();
            content.QualityReason = "Inspected";
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal("Inspected", (await verify.PalletContentLines.SingleAsync()).QualityReason);
    }

    [Theory]
    [InlineData("modify", false)]
    [InlineData("modify", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    public async Task Audit_Events_Are_Append_Only(string operation, bool async)
    {
        var graph = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            db.PalletAuditEvents.Add(new PalletAuditEvent { PalletId = graph.PalletId, EventType = "PalletCreated", NewValue = "{}", ActorReference = "office-1", OccurredAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateContext())
        {
            var auditEvent = await db.PalletAuditEvents.SingleAsync();
            if (operation == "modify")
            {
                auditEvent.ActorReference = "someone-else";
            }
            else
            {
                db.PalletAuditEvents.Remove(auditEvent);
            }

            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal("office-1", (await verify.PalletAuditEvents.SingleAsync()).ActorReference);
    }

    [Fact]
    public async Task Legacy_Writes_Are_Unaffected()
    {
        await using (var db = _factory.CreateContext())
        {
            db.Customers.Add(new Customer { Name = "Customer T" });
            db.SaveChanges();
            db.Customers.Add(new Customer { Name = "Customer U" });
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(2, await verify.Customers.CountAsync());
    }
}
