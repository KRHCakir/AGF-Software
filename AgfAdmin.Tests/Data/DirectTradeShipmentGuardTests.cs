using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Guard-regel 11 (DirectTrade-verzending in beide richtingen), de Physical-afbakening van regel 8, Consumed als eindtoestand,
// append-only verzendingen, capaciteit inclusief verzonden toewijzingen en de databasechecks op verzendregels.
public class DirectTradeShipmentGuardTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;
    private int _colliLine;
    private int _vendorId;

    public void Dispose() => _factory.Dispose();

    private async Task<int> ArrangeAllocationAsync(decimal quantity = 20m)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity: 500m, colliQuantity: 500m);
        await using var db = _factory.CreateContext();
        _vendorId = (await db.SupplyCommitments.AsNoTracking().SingleAsync(c => c.Id == _seed.DirectTradeCommitmentId)).VendorId;
        var allocation = new StockReservation
        {
            RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, SupplyCommitmentLineId = _seed.DirectTradeSourceLine,
            Quantity = quantity, Unit = "box", Source = "DirectTrade", Status = "Active"
        };
        db.StockReservations.Add(allocation);
        await db.SaveChangesAsync();
        return allocation.Id;
    }

    private static Task SaveAsync(AppDbContext db, bool async)
    {
        if (async) return db.SaveChangesAsync();
        db.SaveChanges();
        return Task.CompletedTask;
    }

    // De volledige graaf zoals de service die schrijft; `mutate` past één onderdeel aan. Slaat niet zelf op.
    private async Task<(StockReservation Reservation, DirectTradeShipment Shipment, DirectTradeShipmentLine Line)> ShipInContextAsync(AppDbContext db, int reservationId, int? orderId = null, bool consume = true, bool addLine = true)
    {
        var reservation = await db.StockReservations.SingleAsync(r => r.Id == reservationId);
        var order = await db.SalesOrders.SingleAsync(o => o.Id == (orderId ?? _orderId));
        var now = DateTime.UtcNow;
        if (consume)
        {
            reservation.Status = "Consumed";
            reservation.ConsumedAt = now;
        }
        var shipment = new DirectTradeShipment
        {
            RequestId = Guid.NewGuid(), SalesOrderId = order.Id, CustomerId = order.CustomerId, CustomerNameSnapshot = "Customer T",
            ShippedOn = new DateOnly(2026, 10, 19), ActorReference = "test", RecordedAt = now
        };
        var line = new DirectTradeShipmentLine
        {
            StockReservation = reservation, SalesOrderLineId = reservation.SalesOrderLineId!.Value, SupplyCommitmentLineId = reservation.SupplyCommitmentLineId,
            SupplyCommitmentId = _seed.DirectTradeCommitmentId, VendorId = _vendorId, VendorNameSnapshot = "Supplier B", OriginSnapshot = "Peru", DestinationSnapshot = "Turkije",
            AllocatedQuantity = reservation.Quantity, Quantity = reservation.Quantity, Unit = reservation.Unit, NetWeightKg = 81.4m, LotNumber = "LOT-DT-SRC",
            SourceLotNumber = "LOT-DT-SRC", ExpectedReservationVersion = reservation.Version
        };
        if (addLine)
        {
            shipment.Lines.Add(line);
        }
        db.DirectTradeShipments.Add(shipment);
        return (reservation, shipment, line);
    }

    private async Task<string> StatusAsync(int id)
    {
        await using var db = _factory.CreateContext();
        return (await db.StockReservations.AsNoTracking().SingleAsync(r => r.Id == id)).Status;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Complete_Graph_Is_Accepted(bool async)
    {
        var id = await ArrangeAllocationAsync();
        await using (var db = _factory.CreateContext())
        {
            await ShipInContextAsync(db, id);
            await SaveAsync(db, async);
        }
        Assert.Equal("Consumed", await StatusAsync(id));
    }

    [Theory]
    [InlineData("line-without-consumption", false)]
    [InlineData("line-without-consumption", true)]
    [InlineData("quantity", false)]
    [InlineData("quantity", true)]
    [InlineData("allocated-copy", false)]
    [InlineData("allocated-copy", true)]
    [InlineData("unit", false)]
    [InlineData("unit", true)]
    [InlineData("source-line", false)]
    [InlineData("source-line", true)]
    [InlineData("no-consumed-at", false)]
    [InlineData("no-consumed-at", true)]
    [InlineData("other-order", false)]
    [InlineData("other-order", true)]
    [InlineData("other-customer", false)]
    [InlineData("other-customer", true)]
    [InlineData("lot-deviation-without-reason", false)]
    [InlineData("lot-deviation-without-reason", true)]
    [InlineData("dispatch-movement", false)]
    [InlineData("dispatch-movement", true)]
    public async Task Every_Missing_Or_Deviating_Component_Is_Rejected(string scenario, bool async)
    {
        var id = await ArrangeAllocationAsync();
        await using (var db = _factory.CreateContext())
        {
            int? otherOrderId = null;
            if (scenario == "other-order")
            {
                var other = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = _seed.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "t" };
                db.SalesOrders.Add(other);
                await db.SaveChangesAsync();
                otherOrderId = other.Id;
            }
            var (reservation, shipment, line) = await ShipInContextAsync(db, id, otherOrderId, consume: scenario != "line-without-consumption");
            switch (scenario)
            {
                case "quantity": line.Quantity = 19m; line.AllocatedQuantity = 19m; break;
                case "allocated-copy": line.AllocatedQuantity = 21m; line.Quantity = 21m; break;
                case "unit": line.Unit = "colli"; break;
                case "source-line": line.SupplyCommitmentLineId = _seed.DirectTradeColliSourceLine; break;
                case "no-consumed-at": reservation.ConsumedAt = null; break;
                case "other-customer": shipment.CustomerId = _seed.OtherCustomerId; break;
                case "lot-deviation-without-reason": line.LotNumber = "LOT-ANDERS"; break;
                case "dispatch-movement":
                    db.StockMovements.Add(new StockMovement { Type = "Dispatch", Quantity = 20m, Unit = "box", SupplyCommitmentLineId = reservation.SupplyCommitmentLineId, StockReservation = reservation, CreatedAt = DateTime.UtcNow });
                    break;
            }
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        Assert.Equal("Active", await StatusAsync(id));
    }

    [Theory]
    [InlineData("Released", false)]
    [InlineData("Released", true)]
    [InlineData("Active", false)]
    [InlineData("Active", true)]
    public async Task A_Shipped_Allocation_Is_Terminal(string target, bool async)
    {
        var id = await ArrangeAllocationAsync();
        await using (var db = _factory.CreateContext())
        {
            await ShipInContextAsync(db, id);
            await SaveAsync(db, async);
        }
        await using (var db = _factory.CreateContext())
        {
            var reservation = await db.StockReservations.SingleAsync(r => r.Id == id);
            reservation.Status = target;
            reservation.ReleasedAt = target == "Released" ? DateTime.UtcNow : null;
            reservation.ReleaseReason = target == "Released" ? "poging" : null;
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            Assert.Contains("shipped (Consumed)", exception.Message);
        }
        Assert.Equal("Consumed", await StatusAsync(id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shipments_And_Lines_Are_Append_Only(bool async)
    {
        var id = await ArrangeAllocationAsync();
        await using (var db = _factory.CreateContext())
        {
            await ShipInContextAsync(db, id);
            await SaveAsync(db, async);
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.DirectTradeShipmentLines.SingleAsync()).NetWeightKg = 99m;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.DirectTradeShipments.SingleAsync()).CustomerNameSnapshot = "Hernoemd";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        await using (var db = _factory.CreateContext())
        {
            db.DirectTradeShipments.Remove(await db.DirectTradeShipments.SingleAsync());
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shipped_Allocations_Keep_Counting_As_Source_Capacity(bool async)
    {
        var id = await ArrangeAllocationAsync(90m);
        await using (var db = _factory.CreateContext())
        {
            await ShipInContextAsync(db, id);
            await SaveAsync(db, async);
        }
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(new StockReservation
            {
                RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, SupplyCommitmentLineId = _seed.DirectTradeSourceLine, Quantity = 11m, Unit = "box", Source = "DirectTrade", Status = "Active"
            });
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            Assert.Contains("exceed its expected quantity", exception.Message);
        }
        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(new StockReservation
            {
                RequestId = Guid.NewGuid(), SalesOrderLineId = _boxLine, SupplyCommitmentLineId = _seed.DirectTradeSourceLine, Quantity = 10m, Unit = "box", Source = "DirectTrade", Status = "Active"
            });
            await SaveAsync(db, async);
        }
    }

    // ---- Databasechecks buiten de guard om ----

    private async Task<int> RawLineAsync(decimal allocated, decimal quantity, string lot, string sourceLot, string reason)
    {
        var id = await ArrangeAllocationAsync();
        int shipmentId;
        await using (var db = _factory.CreateContext())
        {
            var (_, shipment, _) = await ShipInContextAsync(db, id, consume: false, addLine: false);
            await db.SaveChangesAsync();
            shipmentId = shipment.Id;
        }
        await using var raw = _factory.CreateContext();
        string Q(string v) => v == null ? "NULL" : $"'{v}'";
        return await raw.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"DirectTradeShipmentLines\" (\"DirectTradeShipmentId\", \"StockReservationId\", \"SalesOrderLineId\", \"SupplyCommitmentLineId\", \"SupplyCommitmentId\", \"VendorId\", \"VendorNameSnapshot\", \"OriginSnapshot\", \"DestinationSnapshot\", \"AllocatedQuantity\", \"Quantity\", \"Unit\", \"NetWeightKg\", \"LotNumber\", \"SourceLotNumber\", \"LotDeviationReason\", \"ExpectedReservationVersion\") " +
            $"VALUES ({shipmentId}, {id}, {_boxLine}, {_seed.DirectTradeSourceLine}, {_seed.DirectTradeCommitmentId}, 1, 'Supplier B', 'Peru', 'Turkije', '{allocated}', '{quantity}', 'box', '81.4', {Q(lot)}, {Q(sourceLot)}, {Q(reason)}, 0)");
    }

    [Theory]
    [InlineData(20, 19, "LOT-DT-SRC", "LOT-DT-SRC", null, "CK_DirectTradeShipmentLines_WholeAllocation")]
    [InlineData(20, 20, "LOT-ANDERS", "LOT-DT-SRC", null, "CK_DirectTradeShipmentLines_LotDeviation_Reason")]
    [InlineData(20, 20, "LOT-ANDERS", "LOT-DT-SRC", "  ", "CK_DirectTradeShipmentLines_LotDeviation_Reason")]
    [InlineData(20, 20, " ", null, null, "CK_DirectTradeShipmentLines_Lot_NotBlank")]
    public async Task Database_Checks_Reject_Partial_Quantities_And_Unexplained_Lot_Deviations(int allocated, int quantity, string lot, string sourceLot, string reason, string check)
    {
        var exception = await Assert.ThrowsAsync<SqliteException>(() => RawLineAsync(allocated, quantity, lot, sourceLot, reason));
        Assert.Contains(check, exception.Message);
    }

    // Een tweede verzendregel voor dezelfde toewijzing (ook buiten de guard om) weigert de unieke index.
    [Fact]
    public async Task A_Second_Shipment_Line_For_The_Same_Allocation_Is_Rejected_By_The_Unique_Index()
    {
        Assert.Equal(1, await RawLineAsync(20, 20, "LOT-DT-SRC", "LOT-DT-SRC", null));
        await using var raw = _factory.CreateContext();
        var line = await raw.DirectTradeShipmentLines.AsNoTracking().SingleAsync();
        var exception = await Assert.ThrowsAsync<SqliteException>(() => raw.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"DirectTradeShipmentLines\" (\"DirectTradeShipmentId\", \"StockReservationId\", \"SalesOrderLineId\", \"SupplyCommitmentLineId\", \"SupplyCommitmentId\", \"VendorId\", \"VendorNameSnapshot\", \"OriginSnapshot\", \"AllocatedQuantity\", \"Quantity\", \"Unit\", \"NetWeightKg\", \"LotNumber\", \"ExpectedReservationVersion\") " +
            $"VALUES ({line.DirectTradeShipmentId}, {line.StockReservationId}, {line.SalesOrderLineId}, {line.SupplyCommitmentLineId}, {line.SupplyCommitmentId}, 1, 'x', 'x', '20', '20', 'box', '1', 'LOT-2', 0)"));
        Assert.Contains("UNIQUE constraint failed", exception.Message);
    }

    [Theory]
    [InlineData("LOT-DT-SRC", "LOT-DT-SRC", null)]
    [InlineData("LOT-ANDERS", "LOT-DT-SRC", "herpakt")]
    [InlineData("LOT-ZONDER-BRON", null, null)]
    public async Task Database_Checks_Accept_Valid_Lot_Combinations(string lot, string sourceLot, string reason)
    {
        Assert.Equal(1, await RawLineAsync(20, 20, lot, sourceLot, reason));
    }

    // ---- Review PR #31: de oorspronkelijke toewijzing ligt vast bij en na verbruik ----

    private async Task<(string Status, decimal Quantity, string Unit, int SourceLine, int? OrderLine, int Shipments, decimal Remaining, decimal LineAllocated, decimal LineDispatched, decimal LineOpen)> SnapshotAsync(int id)
    {
        await using var db = _factory.CreateContext();
        var r = await db.StockReservations.AsNoTracking().SingleAsync(x => x.Id == id);
        var remaining = (await new AgfAdmin.Services.DirectTradeAllocationService(db).GetAllocatableSupplyAsync(_orderId, _boxLine)).Value
            .SingleOrDefault(x => x.SupplyCommitmentLineId == _seed.DirectTradeSourceLine)?.RemainingQuantity ?? 0m;
        var line = (await new AgfAdmin.Services.SalesOrderService(db).GetAsync(_orderId)).Value.Lines.Single(l => l.Id == _boxLine);
        return (r.Status, r.Quantity, r.Unit, r.SupplyCommitmentLineId, r.SalesOrderLineId, await db.DirectTradeShipmentLines.CountAsync(), remaining, line.AllocatedQuantity, line.DispatchedQuantity, line.OpenQuantity);
    }

    private void Tamper(StockReservation reservation, DirectTradeShipmentLine line, string field)
    {
        switch (field)
        {
            case "quantity":
                reservation.Quantity = 25m;
                if (line != null) { line.Quantity = 25m; line.AllocatedQuantity = 25m; }
                break;
            case "unit":
                reservation.Unit = "colli";
                if (line != null) line.Unit = "colli";
                break;
            case "source-line":
                reservation.SupplyCommitmentLineId = _seed.DirectTradeColliSourceLine;
                if (line != null) line.SupplyCommitmentLineId = _seed.DirectTradeColliSourceLine;
                break;
            case "order-line":
                reservation.SalesOrderLineId = _colliLine;
                if (line != null) line.SalesOrderLineId = _colliLine;
                break;
        }
    }

    // De verzendregel kopieert de gewijzigde waarden exact, dus een vergelijking met de huidige reservering zou slagen.
    [Theory]
    [InlineData("quantity", false)]
    [InlineData("quantity", true)]
    [InlineData("unit", false)]
    [InlineData("unit", true)]
    [InlineData("source-line", false)]
    [InlineData("source-line", true)]
    [InlineData("order-line", false)]
    [InlineData("order-line", true)]
    public async Task Changing_The_Allocation_In_The_Same_Save_As_Shipping_It_Is_Rejected(string field, bool async)
    {
        var id = await ArrangeAllocationAsync();
        var before = await SnapshotAsync(id);
        await using (var db = _factory.CreateContext())
        {
            var (reservation, _, line) = await ShipInContextAsync(db, id);
            Tamper(reservation, line, field);
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            Assert.Contains("cannot change", exception.Message);
        }
        var after = await SnapshotAsync(id);
        Assert.Equal(("Active", 20m, "box", _seed.DirectTradeSourceLine, (int?)_boxLine, 0), (after.Status, after.Quantity, after.Unit, after.SourceLine, after.OrderLine, after.Shipments));
        Assert.Equal((before.Remaining, before.LineAllocated, before.LineDispatched, before.LineOpen), (after.Remaining, after.LineAllocated, after.LineDispatched, after.LineOpen));
    }

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
    public async Task A_Shipped_Allocation_Cannot_Change_While_Staying_Consumed(string field, bool async)
    {
        var id = await ArrangeAllocationAsync();
        await using (var db = _factory.CreateContext())
        {
            await ShipInContextAsync(db, id);
            await SaveAsync(db, async);
        }
        var shipped = await SnapshotAsync(id);
        Assert.Equal(("Consumed", 80m, 0m, 20m, 480m), (shipped.Status, shipped.Remaining, shipped.LineAllocated, shipped.LineDispatched, shipped.LineOpen));
        DirectTradeShipmentLine lineBefore;
        await using (var db = _factory.CreateContext())
        {
            lineBefore = await db.DirectTradeShipmentLines.AsNoTracking().SingleAsync();
        }

        await using (var db = _factory.CreateContext())
        {
            var reservation = await db.StockReservations.SingleAsync(r => r.Id == id);
            if (field == "consumed-at") reservation.ConsumedAt = reservation.ConsumedAt!.Value.AddDays(-1);
            else Tamper(reservation, null, field);
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
            Assert.Contains("cannot change", exception.Message);
        }

        var after = await SnapshotAsync(id);
        Assert.Equal(shipped, after);
        await using var verify = _factory.CreateContext();
        var lineAfter = await verify.DirectTradeShipmentLines.AsNoTracking().SingleAsync();
        Assert.Equal((lineBefore.Quantity, lineBefore.Unit, lineBefore.NetWeightKg, lineBefore.SupplyCommitmentLineId, lineBefore.SalesOrderLineId),
            (lineAfter.Quantity, lineAfter.Unit, lineAfter.NetWeightKg, lineAfter.SupplyCommitmentLineId, lineAfter.SalesOrderLineId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_Active_Allocation_Cannot_Grow_In_Place_Past_The_Capacity_Check(bool async)
    {
        var id = await ArrangeAllocationAsync();
        await using (var db = _factory.CreateContext())
        {
            (await db.StockReservations.SingleAsync(r => r.Id == id)).Quantity = 150m;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        Assert.Equal(20m, (await SnapshotAsync(id)).Quantity);
    }

    // ---- Review PR #31: bronpartij, toezegging en leverancier tegen de echte bron ----

    [Theory]
    [InlineData("snapshot-null-deviating-no-reason", false)]
    [InlineData("snapshot-null-deviating-no-reason", true)]
    [InlineData("snapshot-forged-to-actual-lot", false)]
    [InlineData("snapshot-forged-to-actual-lot", true)]
    [InlineData("snapshot-null-same-lot", false)]
    [InlineData("snapshot-null-same-lot", true)]
    [InlineData("wrong-commitment", false)]
    [InlineData("wrong-commitment", true)]
    [InlineData("wrong-vendor", false)]
    [InlineData("wrong-vendor", true)]
    public async Task Source_Lot_Commitment_And_Vendor_Are_Checked_Against_The_Real_Source(string scenario, bool async)
    {
        var id = await ArrangeAllocationAsync();
        await using (var db = _factory.CreateContext())
        {
            var (_, _, line) = await ShipInContextAsync(db, id);
            switch (scenario)
            {
                case "snapshot-null-deviating-no-reason": line.SourceLotNumber = null; line.LotNumber = "LOT-ANDERS"; line.LotDeviationReason = null; break;
                case "snapshot-forged-to-actual-lot": line.SourceLotNumber = "LOT-ANDERS"; line.LotNumber = "LOT-ANDERS"; line.LotDeviationReason = null; break;
                case "snapshot-null-same-lot": line.SourceLotNumber = null; break;
                case "wrong-commitment": line.SupplyCommitmentId = _seed.DirectTradeCommitmentId + 1; break;
                case "wrong-vendor": line.VendorId = _vendorId + 1000; break;
            }
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        Assert.Equal(("Active", 0), ((await SnapshotAsync(id)).Status, (await SnapshotAsync(id)).Shipments));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Without_A_Real_Source_Lot_The_Actual_Lot_Suffices_But_A_Forged_Source_Lot_Is_Rejected(bool async)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity: 500m, colliQuantity: 500m);
        int id;
        await using (var db = _factory.CreateContext())
        {
            _vendorId = (await db.SupplyCommitments.AsNoTracking().SingleAsync(c => c.Id == _seed.DirectTradeCommitmentId)).VendorId;
            var allocation = new StockReservation { RequestId = Guid.NewGuid(), SalesOrderLineId = _colliLine, SupplyCommitmentLineId = _seed.DirectTradeColliSourceLine, Quantity = 5m, Unit = "colli", Source = "DirectTrade", Status = "Active" };
            db.StockReservations.Add(allocation);
            await db.SaveChangesAsync();
            id = allocation.Id;
        }
        await using (var db = _factory.CreateContext())
        {
            var (_, _, line) = await ShipInContextAsync(db, id);
            line.SourceLotNumber = "LOT-VERZONNEN";
            line.LotNumber = "LOT-VERZONNEN";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => SaveAsync(db, async));
        }
        await using (var db = _factory.CreateContext())
        {
            var (_, _, line) = await ShipInContextAsync(db, id);
            line.SourceLotNumber = null;
            line.LotNumber = "LOT-KOLLI-1";
            line.LotDeviationReason = null;
            await SaveAsync(db, async);
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal("Consumed", (await verify.StockReservations.AsNoTracking().SingleAsync(r => r.Id == id)).Status);
    }

    // Stamgegevens mogen na verzending wijzigen; de historische verzendregel blijft gelijk en de guard controleert haar niet opnieuw.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Master_Data_Changes_After_Shipping_Are_Allowed_And_Leave_The_Historical_Line_Intact(bool async)
    {
        var id = await ArrangeAllocationAsync();
        await using (var db = _factory.CreateContext())
        {
            await ShipInContextAsync(db, id);
            await SaveAsync(db, async);
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.SupplyCommitmentLines.FindAsync(_seed.DirectTradeSourceLine))!.LotNumber = "LOT-GEWIJZIGD";
            var commitment = await db.SupplyCommitments.Include(c => c.Vendor).SingleAsync(c => c.Id == _seed.DirectTradeCommitmentId);
            commitment.Vendor.Name = "Supplier B (hernoemd)";
            commitment.Origin = "Chili";
            await SaveAsync(db, async);
        }
        await using var verify = _factory.CreateContext();
        var line = await verify.DirectTradeShipmentLines.AsNoTracking().SingleAsync();
        Assert.Equal(("LOT-DT-SRC", "LOT-DT-SRC", "Supplier B", "Peru"), (line.LotNumber, line.SourceLotNumber, line.VendorNameSnapshot, line.OriginSnapshot));
    }
}

