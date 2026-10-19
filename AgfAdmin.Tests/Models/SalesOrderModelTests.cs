using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Models;

public class SalesOrderModelTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task AssertRejectedAsync(Action<AgfAdmin.Data.AppDbContext> arrange)
    {
        await using var db = _factory.CreateContext();
        arrange(db);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    private static SalesOrder NewOrder(int customerId, Guid? requestId = null, string status = "Open") => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        CustomerId = customerId,
        Status = status,
        RequestedDeliveryDate = new DateTime(2026, 10, 23, 6, 0, 0, DateTimeKind.Utc),
        ActorReference = "office-1"
    };

    private static StockReservation NewSalesReservation(int salesOrderLineId, int contentLineId, int supplyLineId) => new()
    {
        RequestId = Guid.NewGuid(),
        SalesOrderLineId = salesOrderLineId,
        PalletContentLineId = contentLineId,
        SupplyCommitmentLineId = supplyLineId,
        Quantity = 4m,
        Unit = "box",
        NetWeightKg = 16m,
        Source = "Physical",
        Status = "Active"
    };

    // ---- SalesOrder en SalesOrderLine ----

    [Fact]
    public async Task SalesOrder_With_Lines_Is_Saved_With_Versions_At_Zero()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, boxLineId, colliLineId) = await SalesTestData.SeedOrderAsync(_factory, seed);

        await using var verify = _factory.CreateContext();
        var order = await verify.SalesOrders.Include(o => o.Lines).SingleAsync(o => o.Id == orderId);
        Assert.Equal(0, order.Version);
        Assert.Equal(new[] { boxLineId, colliLineId }, order.Lines.OrderBy(l => l.Id).Select(l => l.Id));
        Assert.All(order.Lines, line => Assert.Equal(0, line.Version));
    }

    [Theory]
    [InlineData("customer-missing")]
    [InlineData("status-unknown")]
    [InlineData("status-lowercase")]
    public async Task SalesOrder_Requires_Existing_Customer_And_Allowed_Status(string invalidCase)
    {
        var seed = await SalesTestData.SeedAsync(_factory);

        await AssertRejectedAsync(db => db.SalesOrders.Add(invalidCase switch
        {
            "customer-missing" => NewOrder(999_999),
            "status-unknown" => NewOrder(seed.CustomerId, status: "Confirmed"),
            _ => NewOrder(seed.CustomerId, status: "open")
        }));
    }

    [Fact]
    public async Task SalesOrder_RequestId_Is_Unique()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var requestId = Guid.NewGuid();
        await using (var db = _factory.CreateContext())
        {
            db.SalesOrders.Add(NewOrder(seed.CustomerId, requestId));
            await db.SaveChangesAsync();
        }

        await AssertRejectedAsync(db => db.SalesOrders.Add(NewOrder(seed.CustomerId, requestId)));
    }

    [Theory]
    [InlineData("quantity-zero")]
    [InlineData("quantity-negative")]
    [InlineData("unit-kg")]
    [InlineData("unit-capitalised")]
    [InlineData("variant-missing")]
    public async Task SalesOrderLine_Requires_Positive_Quantity_Known_Unit_And_Existing_Variant(string invalidCase)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, seed);

        await AssertRejectedAsync(db => db.SalesOrderLines.Add(new SalesOrderLine
        {
            SalesOrderId = orderId,
            ProductVariantId = invalidCase == "variant-missing" ? 999_999 : seed.MangoId,
            Quantity = invalidCase switch { "quantity-zero" => 0m, "quantity-negative" => -1m, _ => 1m },
            Unit = invalidCase switch { "unit-kg" => "kg", "unit-capitalised" => "Box", _ => "box" }
        }));
    }

    [Fact]
    public async Task SalesOrderLine_Same_Variant_And_Unit_Twice_Is_Rejected_But_Other_Unit_Is_Allowed()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, seed);

        await AssertRejectedAsync(db => db.SalesOrderLines.Add(new SalesOrderLine { SalesOrderId = orderId, ProductVariantId = seed.AvocadoId, Quantity = 1m, Unit = "box" }));

        await using (var db = _factory.CreateContext())
        {
            db.SalesOrderLines.Add(new SalesOrderLine { SalesOrderId = orderId, ProductVariantId = seed.AvocadoId, Quantity = 1m, Unit = "tray" });
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(3, await verify.SalesOrderLines.CountAsync(l => l.SalesOrderId == orderId));
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("order")]
    [InlineData("variant")]
    public async Task Deleting_Customer_Order_Or_Variant_Referenced_By_Sales_Data_Is_Restricted(string principal)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, seed);

        await AssertRejectedAsync(db =>
        {
            switch (principal)
            {
                case "customer": db.Customers.Remove(new Customer { Id = seed.CustomerId }); break;
                case "order": db.SalesOrders.Remove(new SalesOrder { Id = orderId }); break;
                default: db.ProductVariants.Remove(new ProductVariant { Id = seed.AvocadoId }); break;
            }
        });
    }

    [Fact]
    public void Sales_Metadata_Has_Tokens_Unique_Indexes_And_Restrict()
    {
        using var db = _factory.CreateContext();
        var order = db.Model.FindEntityType(typeof(SalesOrder))!;
        var line = db.Model.FindEntityType(typeof(SalesOrderLine))!;
        var reservation = db.Model.FindEntityType(typeof(StockReservation))!;

        Assert.True(order.FindProperty(nameof(SalesOrder.Version))!.IsConcurrencyToken);
        Assert.True(line.FindProperty(nameof(SalesOrderLine.Version))!.IsConcurrencyToken);
        Assert.True(reservation.FindProperty(nameof(StockReservation.Version))!.IsConcurrencyToken);
        Assert.Equal(18, line.FindProperty(nameof(SalesOrderLine.Quantity))!.GetPrecision());
        Assert.Equal(3, line.FindProperty(nameof(SalesOrderLine.Quantity))!.GetScale());

        Assert.True(Assert.Single(order.GetIndexes(), i => i.Properties.Single().Name == nameof(SalesOrder.RequestId)).IsUnique);
        var lineIndex = Assert.Single(line.GetIndexes(), i => i.Properties.Count == 3);
        Assert.True(lineIndex.IsUnique);
        Assert.Equal(new[] { nameof(SalesOrderLine.SalesOrderId), nameof(SalesOrderLine.ProductVariantId), nameof(SalesOrderLine.Unit) }, lineIndex.Properties.Select(p => p.Name));
        Assert.True(Assert.Single(reservation.GetIndexes(), i => i.Properties.Single().Name == nameof(StockReservation.RequestId)).IsUnique);
        Assert.True(Assert.Single(reservation.GetIndexes(), i => i.Properties.Single().Name == nameof(StockReservation.ReleaseRequestId)).IsUnique);

        Assert.All(order.GetForeignKeys().Concat(line.GetForeignKeys()), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        var salesLineFk = Assert.Single(reservation.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(SalesOrderLine));
        Assert.Equal(DeleteBehavior.Restrict, salesLineFk.DeleteBehavior);
        Assert.True(reservation.FindProperty(nameof(StockReservation.OrderLineId))!.IsNullable);
        Assert.True(reservation.FindProperty(nameof(StockReservation.SalesOrderLineId))!.IsNullable);
    }

    // ---- Annulering: database-checks, unieke sleutel en append-only orderhistorie ----

    [Theory]
    [InlineData("reason-null")]
    [InlineData("reason-empty")]
    [InlineData("reason-whitespace")]
    [InlineData("cancelled-at-null")]
    public async Task Cancelled_Order_Requires_CancelledAt_And_A_Non_Blank_Reason(string invalidCase)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        await AssertRejectedAsync(db =>
        {
            var order = NewOrder(seed.CustomerId, status: "Cancelled");
            order.CancelledAt = invalidCase == "cancelled-at-null" ? null : DateTime.UtcNow;
            order.CancelReason = invalidCase switch
            {
                "reason-null" => null,
                "reason-empty" => "",
                "reason-whitespace" => "   ",
                _ => "Klant heeft afgezegd"
            };
            db.SalesOrders.Add(order);
        });

        await using var ok = _factory.CreateContext();
        var valid = NewOrder(seed.CustomerId, status: "Cancelled");
        valid.CancelledAt = DateTime.UtcNow;
        valid.CancelReason = "Klant heeft afgezegd";
        ok.SalesOrders.Add(valid);
        await ok.SaveChangesAsync();
        // Een Open order mag de annuleringsvelden leeg laten.
        ok.SalesOrders.Add(NewOrder(seed.CustomerId));
        await ok.SaveChangesAsync();
    }

    [Fact]
    public async Task CancelRequestId_Is_Unique_But_Null_Is_Allowed_Many_Times()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var cancelId = Guid.NewGuid();
        await using (var db = _factory.CreateContext())
        {
            var first = NewOrder(seed.CustomerId, status: "Cancelled");
            first.CancelledAt = DateTime.UtcNow;
            first.CancelReason = "Eerste";
            first.CancelRequestId = cancelId;
            db.SalesOrders.AddRange(first, NewOrder(seed.CustomerId), NewOrder(seed.CustomerId));
            await db.SaveChangesAsync();
        }
        await AssertRejectedAsync(db =>
        {
            var duplicate = NewOrder(seed.CustomerId, status: "Cancelled");
            duplicate.CancelledAt = DateTime.UtcNow;
            duplicate.CancelReason = "Tweede";
            duplicate.CancelRequestId = cancelId;
            db.SalesOrders.Add(duplicate);
        });
    }

    [Fact]
    public async Task SalesOrder_Audit_Events_Are_Append_Only_With_Unique_RequestId_And_Restrict()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, _, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        var requestId = Guid.NewGuid();
        await using (var db = _factory.CreateContext())
        {
            db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent { SalesOrderId = orderId, EventType = "SalesOrderCancelled", Reason = "Test", ActorReference = "office-1", OccurredAt = DateTime.UtcNow, RequestId = requestId });
            await db.SaveChangesAsync();
        }
        await AssertRejectedAsync(db => db.SalesOrderAuditEvents.Add(new SalesOrderAuditEvent { SalesOrderId = orderId, EventType = "SalesOrderCancelled", OccurredAt = DateTime.UtcNow, RequestId = requestId }));

        await using (var db = _factory.CreateContext())
        {
            (await db.SalesOrderAuditEvents.SingleAsync()).Reason = "Gewijzigd";
            await Assert.ThrowsAsync<AgfAdmin.Data.TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            db.SalesOrderAuditEvents.Remove(await db.SalesOrderAuditEvents.SingleAsync());
            Assert.Throws<AgfAdmin.Data.TraceabilityViolationException>(() => db.SaveChanges());
        }
        await using (var db = _factory.CreateContext())
        {
            db.SalesOrders.Remove(await db.SalesOrders.SingleAsync(o => o.Id == orderId));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var meta = _factory.CreateContext();
        var entity = meta.Model.FindEntityType(typeof(SalesOrderAuditEvent))!;
        Assert.True(Assert.Single(entity.GetIndexes(), i => i.Properties.Single().Name == nameof(SalesOrderAuditEvent.RequestId)).IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, Assert.Single(entity.GetForeignKeys()).DeleteBehavior);
        Assert.Equal(2000, entity.FindProperty(nameof(SalesOrderAuditEvent.NewValue))!.GetMaxLength());
        var order = meta.Model.FindEntityType(typeof(SalesOrder))!;
        Assert.True(Assert.Single(order.GetIndexes(), i => i.Properties.Single().Name == nameof(SalesOrder.CancelRequestId)).IsUnique);
        Assert.Equal(500, order.FindProperty(nameof(SalesOrder.CancelReason))!.GetMaxLength());
    }

    // ---- StockReservation: eigenaar, status en compleetheid van de verkoopflow ----

    [Fact]
    public async Task Sales_Reservation_With_Complete_Fields_Is_Saved()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (_, boxLineId, _) = await SalesTestData.SeedOrderAsync(_factory, seed);

        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(NewSalesReservation(boxLineId, seed.ApprovedBox, await SalesTestData.SupplyLineOfAsync(db, seed.ApprovedBox)));
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        var saved = await verify.StockReservations.SingleAsync();
        Assert.Equal(boxLineId, saved.SalesOrderLineId);
        Assert.Null(saved.OrderLineId);
        Assert.Equal(0, saved.Version);
    }

    [Theory]
    [InlineData("no-owner")]
    [InlineData("two-owners")]
    [InlineData("status-unknown")]
    [InlineData("quantity-zero")]
    [InlineData("sales-no-content-line")]
    [InlineData("sales-no-request-id")]
    [InlineData("sales-no-unit")]
    [InlineData("sales-no-weight")]
    [InlineData("sales-zero-weight")]
    [InlineData("sales-source-expected")]
    [InlineData("released-without-reason")]
    [InlineData("released-blank-reason")]
    [InlineData("released-without-time")]
    public async Task Reservation_Checks_Are_Enforced_By_Database(string invalidCase)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (_, boxLineId, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        int supplyLine;
        await using (var db = _factory.CreateContext())
        {
            supplyLine = await SalesTestData.SupplyLineOfAsync(db, seed.ApprovedBox);
        }

        await AssertRejectedAsync(db =>
        {
            var reservation = NewSalesReservation(boxLineId, seed.ApprovedBox, supplyLine);
            switch (invalidCase)
            {
                case "no-owner": reservation.SalesOrderLineId = null; break;
                case "two-owners": reservation.OrderLineId = seed.LegacyOrderLineId; break;
                case "status-unknown": reservation.Status = "Pending"; break;
                case "quantity-zero": reservation.Quantity = 0m; break;
                case "sales-no-content-line": reservation.PalletContentLineId = null; break;
                case "sales-no-request-id": reservation.RequestId = null; break;
                case "sales-no-unit": reservation.Unit = null; break;
                case "sales-no-weight": reservation.NetWeightKg = null; break;
                case "sales-zero-weight": reservation.NetWeightKg = 0m; break;
                case "sales-source-expected": reservation.Source = "Expected"; break;
                case "released-without-reason": reservation.Status = "Released"; reservation.ReleasedAt = DateTime.UtcNow; break;
                case "released-blank-reason": reservation.Status = "Released"; reservation.ReleasedAt = DateTime.UtcNow; reservation.ReleaseReason = "  "; break;
                case "released-without-time": reservation.Status = "Released"; reservation.ReleaseReason = "Klant annuleert"; break;
            }
            db.StockReservations.Add(reservation);
        });
    }

    [Fact]
    public async Task Legacy_OrderLine_Reservation_Remains_Valid_Without_Sales_Fields()
    {
        var seed = await SalesTestData.SeedAsync(_factory);

        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(new StockReservation
            {
                OrderLineId = seed.LegacyOrderLineId,
                SupplyCommitmentLineId = await SalesTestData.SupplyLineOfAsync(db, seed.ApprovedBox),
                Quantity = 1m,
                Source = "Expected",
                Status = "Active"
            });
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.StockReservations.CountAsync(r => r.OrderLineId == seed.LegacyOrderLineId));
    }

    [Fact]
    public async Task Reservation_RequestId_And_ReleaseRequestId_Are_Unique()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (_, boxLineId, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        var requestId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        int supplyLine;
        await using (var db = _factory.CreateContext())
        {
            supplyLine = await SalesTestData.SupplyLineOfAsync(db, seed.ApprovedBox);
            var first = NewSalesReservation(boxLineId, seed.ApprovedBox, supplyLine);
            first.RequestId = requestId;
            first.Status = "Released";
            first.ReleasedAt = DateTime.UtcNow;
            first.ReleaseReason = "Fout";
            first.ReleaseRequestId = releaseId;
            db.StockReservations.Add(first);
            await db.SaveChangesAsync();
        }

        await AssertRejectedAsync(db =>
        {
            var duplicate = NewSalesReservation(boxLineId, seed.ApprovedBox, supplyLine);
            duplicate.RequestId = requestId;
            db.StockReservations.Add(duplicate);
        });
        await AssertRejectedAsync(db =>
        {
            var duplicate = NewSalesReservation(boxLineId, seed.ApprovedBox, supplyLine);
            duplicate.Status = "Released";
            duplicate.ReleasedAt = DateTime.UtcNow;
            duplicate.ReleaseReason = "Fout";
            duplicate.ReleaseRequestId = releaseId;
            db.StockReservations.Add(duplicate);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reservation_SalesOrder_And_Line_Versions_Increment_Centrally(bool async)
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (orderId, boxLineId, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(NewSalesReservation(boxLineId, seed.ApprovedBox, await SalesTestData.SupplyLineOfAsync(db, seed.ApprovedBox)));
            await db.SaveChangesAsync();
        }

        await using (var db = _factory.CreateContext())
        {
            (await db.StockReservations.SingleAsync()).Status = "Consumed";
            (await db.SalesOrders.SingleAsync(o => o.Id == orderId)).CustomerReference = "PO-1";
            (await db.SalesOrderLines.SingleAsync(l => l.Id == boxLineId)).Quantity = 51m;
            if (async) await db.SaveChangesAsync(); else db.SaveChanges();
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, (await verify.StockReservations.SingleAsync()).Version);
        Assert.Equal(1, (await verify.SalesOrders.SingleAsync(o => o.Id == orderId)).Version);
        Assert.Equal(1, (await verify.SalesOrderLines.SingleAsync(l => l.Id == boxLineId)).Version);
    }

    [Fact]
    public async Task Stale_Reservation_Version_Is_A_Concurrency_Conflict()
    {
        var seed = await SalesTestData.SeedAsync(_factory);
        var (_, boxLineId, _) = await SalesTestData.SeedOrderAsync(_factory, seed);
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(NewSalesReservation(boxLineId, seed.ApprovedBox, await SalesTestData.SupplyLineOfAsync(db, seed.ApprovedBox)));
            await db.SaveChangesAsync();
        }

        await using var first = _factory.CreateContext();
        await using var second = _factory.CreateContext();
        (await first.StockReservations.SingleAsync()).Status = "Consumed";
        var stale = await second.StockReservations.SingleAsync();
        stale.Status = "Released";
        stale.ReleasedAt = DateTime.UtcNow;
        stale.ReleaseReason = "Te laat";
        await first.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verify = _factory.CreateContext();
        Assert.Equal("Consumed", (await verify.StockReservations.SingleAsync()).Status);
    }
}
