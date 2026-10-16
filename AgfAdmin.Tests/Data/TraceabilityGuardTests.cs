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

    // ---- Versies: start op 0, centrale verhoging bij elke succesvolle wijziging, concurrency-token ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_Pallet_And_ContentLine_Start_At_Version_Zero(bool async)
    {
        var graph = await SeedAsync();

        await using var verify = _factory.CreateContext();
        Assert.Equal(0, (await verify.Pallets.SingleAsync(p => p.Id == graph.PalletId)).Version);
        Assert.Equal(0, (await verify.PalletContentLines.SingleAsync(c => c.Id == graph.ContentLineOnA)).Version);

        await using (var db = _factory.CreateContext())
        {
            var pallet = new Pallet { Reference = "PAL-002", LogisticsStatus = "Expected", LocationContext = "Supplier", Version = 0 };
            db.Pallets.Add(pallet);
            await SaveAsync(db, async);
            Assert.Equal(0, pallet.Version);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Version_Increments_Centrally_On_Every_Successful_Modification(bool async)
    {
        var graph = await SeedAsync();

        for (var expected = 1; expected <= 3; expected++)
        {
            await using var db = _factory.CreateContext();
            var pallet = await db.Pallets.SingleAsync(p => p.Id == graph.PalletId);
            var content = await db.PalletContentLines.SingleAsync(c => c.Id == graph.ContentLineOnA);
            pallet.PalletType = $"EUR-{expected}";
            content.QualityReason = $"Check {expected}";
            await SaveAsync(db, async);
            Assert.Equal(expected, pallet.Version);
            Assert.Equal(expected, content.Version);
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(3, (await verify.Pallets.SingleAsync(p => p.Id == graph.PalletId)).Version);
        Assert.Equal(3, (await verify.PalletContentLines.SingleAsync(c => c.Id == graph.ContentLineOnA)).Version);
    }

    [Fact]
    public async Task Version_Does_Not_Change_Without_Modification()
    {
        var graph = await SeedAsync();

        await using (var db = _factory.CreateContext())
        {
            await db.Pallets.SingleAsync(p => p.Id == graph.PalletId);
            db.Customers.Add(new Customer { Name = "Customer T" });
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(0, (await verify.Pallets.SingleAsync(p => p.Id == graph.PalletId)).Version);
    }

    [Theory]
    [InlineData("pallet", false)]
    [InlineData("pallet", true)]
    [InlineData("content", false)]
    [InlineData("content", true)]
    public async Task Stale_Version_Is_Rejected_As_Concurrency_Conflict(string entity, bool async)
    {
        var graph = await SeedAsync();

        await using var first = _factory.CreateContext();
        await using var second = _factory.CreateContext();
        var firstPallet = await first.Pallets.SingleAsync(p => p.Id == graph.PalletId);
        var secondPallet = await second.Pallets.SingleAsync(p => p.Id == graph.PalletId);
        var firstContent = await first.PalletContentLines.SingleAsync(c => c.Id == graph.ContentLineOnA);
        var secondContent = await second.PalletContentLines.SingleAsync(c => c.Id == graph.ContentLineOnA);

        if (entity == "pallet")
        {
            firstPallet.PalletType = "EUR";
            secondPallet.PalletType = "BLOK";
        }
        else
        {
            firstContent.QualityReason = "first";
            secondContent.QualityReason = "second";
        }

        await SaveAsync(first, async);
        var exception = await Record.ExceptionAsync(() => SaveAsync(second, async));
        Assert.IsType<DbUpdateConcurrencyException>(exception);

        await using var verify = _factory.CreateContext();
        if (entity == "pallet")
        {
            var pallet = await verify.Pallets.SingleAsync(p => p.Id == graph.PalletId);
            Assert.Equal("EUR", pallet.PalletType);
            Assert.Equal(1, pallet.Version);
        }
        else
        {
            var content = await verify.PalletContentLines.SingleAsync(c => c.Id == graph.ContentLineOnA);
            Assert.Equal("first", content.QualityReason);
            Assert.Equal(1, content.Version);
        }
    }

    // ---- Regel 4: DirectTrade-inhoud komt nooit op een Warehouse-pallet ----

    private async Task<int> AddDirectTradeSourceLineAsync()
    {
        await using var db = _factory.CreateContext();
        var variant = await db.ProductVariants.FirstAsync();
        var commitment = new SupplyCommitment
        {
            RequestId = Guid.NewGuid(),
            VendorId = (await db.Vendors.FirstAsync()).Id,
            Customer = new Customer { Name = "Customer T" },
            Route = "DirectTrade",
            Status = "Expected",
            Origin = "Kenya",
            Destination = "Turkey"
        };
        var line = new SupplyCommitmentLine { SupplyCommitment = commitment, ProductVariantId = variant.Id, ExpectedQuantity = 5m, ExpectedUnit = "box" };
        db.AddRange(commitment, line);
        await db.SaveChangesAsync();
        return line.Id;
    }

    private async Task<int> AddCrossdockPalletAsync(int? directTradeSourceLineId)
    {
        await using var db = _factory.CreateContext();
        var pallet = new Pallet { Reference = $"PAL-X-{Guid.NewGuid():N}"[..20], LogisticsStatus = "Received", LocationContext = "Crossdock" };
        db.Pallets.Add(pallet);
        if (directTradeSourceLineId != null)
        {
            var source = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == directTradeSourceLineId);
            db.PalletContentLines.Add(NewContentLine(pallet, source));
        }
        await db.SaveChangesAsync();
        return pallet.Id;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectTrade_ContentLine_On_Existing_Warehouse_Pallet_Is_Rejected(bool async)
    {
        await SeedAsync();
        var directLineId = await AddDirectTradeSourceLineAsync();

        await using (var db = _factory.CreateContext())
        {
            db.PalletContentLines.Add(new PalletContentLine
            {
                RequestId = Guid.NewGuid(),
                PalletId = (await db.Pallets.SingleAsync()).Id,
                SupplyCommitmentLineId = directLineId,
                LotNumber = "LOT-D",
                Quantity = 1m,
                Unit = "box",
                NetWeightKg = 4m,
                QualityStatus = "Quarantine"
            });
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.PalletContentLines.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_Warehouse_Pallet_With_New_DirectTrade_Content_In_Same_Save_Is_Rejected(bool async)
    {
        await SeedAsync();
        var directLineId = await AddDirectTradeSourceLineAsync();

        await using (var db = _factory.CreateContext())
        {
            var pallet = new Pallet { Reference = "PAL-NEW", LogisticsStatus = "Received", LocationContext = "Warehouse" };
            var source = await db.SupplyCommitmentLines.SingleAsync(l => l.Id == directLineId);
            db.Pallets.Add(pallet);
            db.PalletContentLines.Add(NewContentLine(pallet, source));
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }

        await using var verify = _factory.CreateContext();
        Assert.False(await verify.Pallets.AnyAsync(p => p.Reference == "PAL-NEW"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moving_A_Pallet_With_DirectTrade_Content_To_Warehouse_Is_Rejected(bool async)
    {
        await SeedAsync();
        var directLineId = await AddDirectTradeSourceLineAsync();
        var palletId = await AddCrossdockPalletAsync(directLineId);

        await using (var db = _factory.CreateContext())
        {
            (await db.Pallets.SingleAsync(p => p.Id == palletId)).LocationContext = "Warehouse";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal("Crossdock", (await verify.Pallets.SingleAsync(p => p.Id == palletId)).LocationContext);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectTrade_Content_Outside_Warehouse_And_Warehouse_Moves_Without_DirectTrade_Are_Allowed(bool async)
    {
        await SeedAsync();
        var directLineId = await AddDirectTradeSourceLineAsync();
        var directPalletId = await AddCrossdockPalletAsync(directLineId);
        var emptyPalletId = await AddCrossdockPalletAsync(null);

        await using (var db = _factory.CreateContext())
        {
            (await db.Pallets.SingleAsync(p => p.Id == emptyPalletId)).LocationContext = "Warehouse";
            (await db.Pallets.SingleAsync(p => p.Id == directPalletId)).LocationContext = "Customer";
            await SaveAsync(db, async);
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal("Warehouse", (await verify.Pallets.SingleAsync(p => p.Id == emptyPalletId)).LocationContext);
        Assert.Equal("Customer", (await verify.Pallets.SingleAsync(p => p.Id == directPalletId)).LocationContext);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audit_Event_With_RequestId_Is_Also_Append_Only(bool async)
    {
        var graph = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            db.PalletAuditEvents.Add(new PalletAuditEvent { PalletId = graph.PalletId, EventType = "LogisticsStatusChanged", RequestId = Guid.NewGuid(), ActorReference = "office-1", OccurredAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateContext())
        {
            (await db.PalletAuditEvents.SingleAsync()).NewValue = "{\"logisticsStatus\":\"Delivered\"}";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }

        await using var verify = _factory.CreateContext();
        Assert.Null((await verify.PalletAuditEvents.SingleAsync()).NewValue);
    }
}
