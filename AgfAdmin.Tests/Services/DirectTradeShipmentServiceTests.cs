using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Services;

// DirectTrade-verzending: hele Active toewijzingen verbruiken met werkelijke hoeveelheid, eenheid, kg, partij en datum; snapshots van
// klant, leverancier, herkomst en bestemming; capaciteit blijft bezet; magazijn ongewijzigd; idempotentie en races; tijdzone van "vandaag".
public class DirectTradeShipmentServiceTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;
    private int _orderId;
    private int _boxLine;
    private int _colliLine;

    public void Dispose() => _factory.Dispose();

    private async Task ArrangeAsync(decimal boxQuantity = 40m, decimal colliQuantity = 10m)
    {
        _seed = await SalesTestData.SeedAsync(_factory);
        (_orderId, _boxLine, _colliLine) = await SalesTestData.SeedOrderAsync(_factory, _seed, boxQuantity, colliQuantity);
    }

    private async Task<StockReservationDto> AllocatedAsync(decimal quantity, int? lineId = null, int? orderId = null, int? sourceLine = null, string unit = "box")
    {
        await using var db = _factory.CreateContext();
        var result = await new DirectTradeAllocationService(db).AllocateAsync(orderId ?? _orderId, lineId ?? _boxLine, new CreateDirectTradeAllocationDto
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLineId = sourceLine ?? _seed.DirectTradeSourceLine, Quantity = quantity, Unit = unit, AcknowledgeExpectedSupply = true, ActorReference = "office-1"
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value;
    }

    private async Task<ServiceResult<StockReservationDto>> AllocateAsync(int orderId, int lineId, decimal quantity)
    {
        await using var db = _factory.CreateContext();
        return await new DirectTradeAllocationService(db).AllocateAsync(orderId, lineId, new CreateDirectTradeAllocationDto
        {
            RequestId = Guid.NewGuid(), SupplyCommitmentLineId = _seed.DirectTradeSourceLine, Quantity = quantity, Unit = "box", AcknowledgeExpectedSupply = true, ActorReference = "office-1"
        });
    }

    private static CreateDirectTradeShipmentLineDto ShipLine(StockReservationDto allocation, decimal kg = 81.4m, string lot = "LOT-DT-SRC", decimal? quantity = null, string unit = null, string reason = null, int? version = null) => new()
    {
        StockReservationId = allocation.Id, ExpectedReservationVersion = version ?? allocation.Version, Quantity = quantity ?? allocation.Quantity,
        Unit = unit ?? allocation.Unit, NetWeightKg = kg, LotNumber = lot, LotDeviationReason = reason
    };

    private CreateDirectTradeShipmentDto Ship(int orderVersion, params CreateDirectTradeShipmentLineDto[] lines) => new()
    {
        RequestId = Guid.NewGuid(), ExpectedOrderVersion = orderVersion, ActorReference = "office-1", ShippedOn = new DateOnly(2026, 10, 19), Reference = "CMR-DT-1", Lines = lines.ToList()
    };

    private async Task<ServiceResult<DirectTradeShipmentDto>> ShipAsync(CreateDirectTradeShipmentDto request, int? orderId = null, IBusinessCalendar calendar = null, params IInterceptor[] interceptors)
    {
        var effective = calendar ?? new FixedBusinessCalendar(Today);
        // Service en guard delen dezelfde kalender, zoals in productie via DI.
        await using var db = _factory.CreateContext(effective, interceptors);
        return await new DirectTradeShipmentService(db, effective).ShipAsync(orderId ?? _orderId, request);
    }

    private async Task<SalesOrderDto> OrderAsync(int? orderId = null)
    {
        await using var db = _factory.CreateContext();
        return (await new SalesOrderService(db).GetAsync(orderId ?? _orderId)).Value;
    }

    private SalesOrderLineDto BoxLine(SalesOrderDto order) => order.Lines.Single(l => l.Id == _boxLine);

    private async Task<ServiceResult<StockReservationDto>> ReleaseAsync(int id, int version, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new StockReservationService(db).ReleaseAsync(id, new ReleaseStockReservationDto { RequestId = Guid.NewGuid(), ExpectedReservationVersion = version, ActorReference = "office-1", Reason = "Toch niet" });
    }

    private async Task<ServiceResult<SalesOrderDto>> CancelAsync(int version, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new SalesOrderService(db).CancelAsync(_orderId, new CancelSalesOrderDto { RequestId = Guid.NewGuid(), ExpectedOrderVersion = version, ActorReference = "office-1", Reason = "Afgezegd" });
    }

    private async Task<(List<DirectTradeShipment> Shipments, List<DirectTradeShipmentLine> Lines, int Movements, int PalletEvents, List<SalesOrderAuditEvent> Events)> StateAsync()
    {
        await using var db = _factory.CreateContext();
        return (
            await db.DirectTradeShipments.AsNoTracking().ToListAsync(),
            await db.DirectTradeShipmentLines.AsNoTracking().ToListAsync(),
            await db.StockMovements.CountAsync(),
            await db.PalletAuditEvents.CountAsync(),
            await db.SalesOrderAuditEvents.AsNoTracking().Where(e => e.EventType == "DirectTradeShipped").ToListAsync());
    }

    private async Task<decimal> RemainingOnSourceAsync(int orderId, int lineId)
    {
        await using var db = _factory.CreateContext();
        var rows = (await new DirectTradeAllocationService(db).GetAllocatableSupplyAsync(orderId, lineId)).Value;
        return rows.SingleOrDefault(r => r.SupplyCommitmentLineId == _seed.DirectTradeSourceLine)?.RemainingQuantity ?? 0m;
    }

    private async Task<(int OrderId, int LineId)> SecondOrderAsync(decimal quantity)
    {
        await using var db = _factory.CreateContext();
        var order = new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = _seed.CustomerId, Status = "Open", RequestedDeliveryDate = new DateTime(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc), ActorReference = "office-1" };
        var line = new SalesOrderLine { SalesOrder = order, ProductVariantId = _seed.AvocadoId, Quantity = quantity, Unit = "box" };
        db.AddRange(order, line);
        await db.SaveChangesAsync();
        return (order.Id, line.Id);
    }

    // ======================== Gelukte verzending ========================

    [Fact]
    public async Task Shipping_Consumes_The_Whole_Allocation_Records_Actuals_Snapshots_And_Audit_Without_Any_Stock_Effect()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var before = await StateAsync();
        var order = await OrderAsync();

        var result = await ShipAsync(Ship(order.Version, ShipLine(allocation)));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var dto = result.Value;
        Assert.Equal((_orderId, _seed.CustomerId, "Customer T", new DateOnly(2026, 10, 19), "CMR-DT-1", false), (dto.SalesOrderId, dto.CustomerId, dto.CustomerName, dto.ShippedOn, dto.Reference, dto.DeliveryRecorded));
        var line = Assert.Single(dto.Lines);
        Assert.Equal((allocation.Id, _seed.DirectTradeSourceLine, _seed.DirectTradeCommitmentId, "Supplier B", "Peru", "Turkije"), (line.StockReservationId, line.SupplyCommitmentLineId, line.SupplyCommitmentId, line.VendorName, line.Origin, line.Destination));
        Assert.Equal((20m, "box", 81.4m, "LOT-DT-SRC", "LOT-DT-SRC", (string)null), (line.Quantity, line.Unit, line.NetWeightKg, line.LotNumber, line.SourceLotNumber, line.LotDeviationReason));

        var after = await StateAsync();
        Assert.Equal(before.Movements, after.Movements);
        Assert.Equal(before.PalletEvents, after.PalletEvents);
        var audit = Assert.Single(after.Events);
        var snapshot = JsonDocument.Parse(audit.NewValue).RootElement;
        Assert.Equal((dto.RequestId, "office-1"), (audit.RequestId!.Value, audit.ActorReference));
        Assert.Equal(("2026-10-19", 1, 81.4m, false, 0, "Customer T"), (snapshot.GetProperty("shippedOn").GetString(), snapshot.GetProperty("consumedAllocationCount").GetInt32(),
            snapshot.GetProperty("shippedNetWeightKg").GetDecimal(), snapshot.GetProperty("deliveryRecorded").GetBoolean(), snapshot.GetProperty("lotDeviationCount").GetInt32(), snapshot.GetProperty("customerName").GetString()));

        var updated = await OrderAsync();
        var boxLine = BoxLine(updated);
        Assert.Equal((0m, 0m, 20m, 20m, 81.4m, 20m, false), (boxLine.AllocatedQuantity, boxLine.ReservedQuantity, boxLine.DispatchedQuantity, boxLine.DirectTradeShippedQuantity, boxLine.DispatchedNetWeightKg, boxLine.OpenQuantity, boxLine.Fulfilled));
        Assert.Equal(order.Version + 1, updated.Version);
        var reservation = boxLine.Reservations.Single(r => r.Id == allocation.Id);
        Assert.Equal(("Consumed", dto.Id, new DateOnly(2026, 10, 19), 81.4m, "LOT-DT-SRC"), (reservation.Status, reservation.DirectTradeShipmentId!.Value, reservation.ShippedOn!.Value, reservation.ShippedNetWeightKg!.Value, reservation.ShippedLotNumber));
        Assert.NotNull(reservation.ConsumedAt);
        Assert.Null(reservation.NetWeightKg);
        Assert.Null(reservation.PalletContentLineId);

        await using var db = _factory.CreateContext();
        var stored = await db.StockReservations.AsNoTracking().SingleAsync(r => r.Id == allocation.Id);
        Assert.Equal(DateTimeKind.Utc, DateTime.SpecifyKind(stored.ConsumedAt!.Value, DateTimeKind.Utc).Kind);
        Assert.Empty(await db.SalesDispatches.ToListAsync());
    }

    [Fact]
    public async Task Historical_Shipment_Information_Does_Not_Change_When_Master_Data_Changes_Later()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var shipped = await ShipAsync(Ship((await OrderAsync()).Version, ShipLine(allocation)));
        Assert.Equal(ServiceResultStatus.Created, shipped.Status);

        await using (var db = _factory.CreateContext())
        {
            await SalesTestData.RenameRelationAsync(db, (await db.Customers.FindAsync(_seed.CustomerId))!.RelationId, "Customer T (hernoemd)");
            var commitment = await db.SupplyCommitments.Include(c => c.Vendor).SingleAsync(c => c.Id == _seed.DirectTradeCommitmentId);
            await SalesTestData.RenameRelationAsync(db, commitment.Vendor.RelationId, "Supplier B (hernoemd)");
            commitment.Origin = "Chili";
            commitment.Destination = "Duitsland";
            (await db.SupplyCommitmentLines.FindAsync(_seed.DirectTradeSourceLine))!.LotNumber = "LOT-GEWIJZIGD";
            await db.SaveChangesAsync();
        }

        await using var read = _factory.CreateContext();
        var list = (await new DirectTradeShipmentService(read, new FixedBusinessCalendar(Today)).ListAsync(_orderId)).Value;
        var shipment = Assert.Single(list);
        var line = Assert.Single(shipment.Lines);
        Assert.Equal(("Customer T", "Supplier B", "Peru", "Turkije", "LOT-DT-SRC", "LOT-DT-SRC"), (shipment.CustomerName, line.VendorName, line.Origin, line.Destination, line.LotNumber, line.SourceLotNumber));
        // De verwijzingen blijven naar dezelfde stamgegevens wijzen.
        Assert.Equal((_seed.CustomerId, _seed.DirectTradeCommitmentId, _seed.DirectTradeSourceLine), (shipment.CustomerId, line.SupplyCommitmentId, line.SupplyCommitmentLineId));
    }

    // ======================== Capaciteit (onafhankelijk van ordervraag) ========================

    [Fact]
    public async Task A_Shipped_Allocation_Keeps_Occupying_Source_Capacity_Checked_On_A_Second_Order_With_Ample_Demand()
    {
        await ArrangeAsync(boxQuantity: 100m);
        var allocation = await AllocatedAsync(90m);
        Assert.Equal(ServiceResultStatus.Created, (await ShipAsync(Ship((await OrderAsync()).Version, ShipLine(allocation, kg: 365m)))).Status);
        var (secondOrder, secondLine) = await SecondOrderAsync(50m);
        Assert.Equal(10m, await RemainingOnSourceAsync(secondOrder, secondLine));

        // De tweede order heeft 50 open vraag: 15 faalt dus alleen op de broncapaciteit (verwacht 100, verzonden 90).
        var tooMuch = await AllocateAsync(secondOrder, secondLine, 15m);
        Assert.Equal(ServiceResultStatus.Conflict, tooMuch.Status);
        Assert.Contains("still unallocated", tooMuch.Message);
        Assert.DoesNotContain("still needs only", tooMuch.Message);

        Assert.Equal(ServiceResultStatus.Created, (await AllocateAsync(secondOrder, secondLine, 10m)).Status);
        Assert.Equal(0m, await RemainingOnSourceAsync(secondOrder, secondLine));
        await using var db = _factory.CreateContext();
        var commitment = (await new SupplyCommitmentService(db).GetAsync(_seed.DirectTradeCommitmentId)).Value;
        Assert.Equal((100m, 0m), (commitment.Lines.Single(l => l.Id == _seed.DirectTradeSourceLine).DirectTradeAllocatedQuantity, commitment.Lines.Single(l => l.Id == _seed.DirectTradeSourceLine).DirectTradeRemainingQuantity));
    }

    [Fact]
    public async Task Mixed_Order_Is_Only_Fulfilled_When_Physical_And_DirectTrade_Are_Both_Shipped()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Created, (await new StockReservationService(db).ReserveAsync(_orderId, _boxLine, new CreateStockReservationDto
            {
                RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 20m, Unit = "box", NetWeightKg = 80m, ActorReference = "office-1"
            })).Status);
        }
        var line = BoxLine(await OrderAsync());
        Assert.Equal((20m, 20m, 0m, false), (line.AllocatedQuantity, line.ReservedQuantity, line.OpenQuantity, line.Fulfilled));

        Assert.Equal(ServiceResultStatus.Created, (await ShipAsync(Ship((await OrderAsync()).Version, ShipLine(allocation)))).Status);
        var order = await OrderAsync();
        line = BoxLine(order);
        Assert.Equal((0m, 20m, 20m, 0m, false, false), (line.AllocatedQuantity, line.ReservedQuantity, line.DispatchedQuantity, line.OpenQuantity, line.Fulfilled, order.Fulfilled));

        var physical = line.Reservations.Single(r => r.Source == "Physical");
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Created, (await new SalesDispatchService(db).DispatchAsync(_orderId, new CreateSalesDispatchDto
            {
                RequestId = Guid.NewGuid(), ExpectedOrderVersion = order.Version, ActorReference = "office-1",
                Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = physical.Id, ExpectedReservationVersion = physical.Version } }
            })).Status);
        }
        order = await OrderAsync();
        line = BoxLine(order);
        Assert.Equal((40m, 20m, 161.4m, true), (line.DispatchedQuantity, line.DirectTradeShippedQuantity, line.DispatchedNetWeightKg, line.Fulfilled));
    }

    [Fact]
    public async Task Warehouse_Availability_And_Reservable_Stock_Are_Unchanged_By_A_DirectTrade_Shipment()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        string overview, reservable;
        await using (var db = _factory.CreateContext())
        {
            overview = JsonSerializer.Serialize((await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = _seed.AvocadoId })).Value.Variants);
            reservable = JsonSerializer.Serialize((await new SalesOrderService(db).GetReservableStockAsync(_orderId, _boxLine)).Value);
        }
        Assert.Equal(ServiceResultStatus.Created, (await ShipAsync(Ship((await OrderAsync()).Version, ShipLine(allocation)))).Status);
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(overview, JsonSerializer.Serialize((await new AvailabilityService(db).GetOverviewAsync(new AvailabilityQuery { ProductVariantId = _seed.AvocadoId })).Value.Variants));
            Assert.Equal(reservable, JsonSerializer.Serialize((await new SalesOrderService(db).GetReservableStockAsync(_orderId, _boxLine)).Value));
            Assert.All(await db.PalletContentLines.AsNoTracking().ToListAsync(), c => Assert.Equal(0m, c.DispatchedQuantity));
            Assert.Empty(await db.StockReceipts.ToListAsync());
        }
    }

    // ======================== Partij ========================

    [Fact]
    public async Task A_Deviating_Lot_Needs_A_Reason_And_Stores_Both_Values_While_A_Source_Without_Lot_Needs_No_Reason()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var colli = await AllocatedAsync(5m, _colliLine, sourceLine: _seed.DirectTradeColliSourceLine, unit: "colli");
        var version = (await OrderAsync()).Version;

        var missing = await ShipAsync(Ship(version, ShipLine(allocation, lot: "LOT-ANDERS")));
        Assert.Equal(ServiceResultStatus.Validation, missing.Status);
        Assert.Contains("lines[0].lotDeviationReason", missing.Errors.Keys);
        Assert.Empty((await StateAsync()).Shipments);

        var blank = await ShipAsync(Ship(version, ShipLine(allocation, lot: "LOT-ANDERS", reason: "   ")));
        Assert.Equal(ServiceResultStatus.Validation, blank.Status);

        var withReason = await ShipAsync(Ship(version, ShipLine(allocation, lot: "LOT-ANDERS", reason: "Leverancier herpakt onder nieuwe partij"), ShipLine(colli, kg: 27.5m, lot: "LOT-KOLLI-1", reason: "geen afwijking")));
        Assert.Equal(ServiceResultStatus.Created, withReason.Status);
        var box = withReason.Value.Lines.Single(l => l.StockReservationId == allocation.Id);
        Assert.Equal(("LOT-ANDERS", "LOT-DT-SRC", "Leverancier herpakt onder nieuwe partij"), (box.LotNumber, box.SourceLotNumber, box.LotDeviationReason));
        var colliLine = withReason.Value.Lines.Single(l => l.StockReservationId == colli.Id);
        Assert.Equal(("LOT-KOLLI-1", (string)null, (string)null), (colliLine.LotNumber, colliLine.SourceLotNumber, colliLine.LotDeviationReason));
        Assert.Equal(1, JsonDocument.Parse((await StateAsync()).Events.Single().NewValue).RootElement.GetProperty("lotDeviationCount").GetInt32());
    }

    // ======================== Regels (409) ========================

    [Theory]
    [InlineData("less", "release this allocation, allocate the shipped quantity")]
    [InlineData("more", "first allocate the extra quantity")]
    [InlineData("unit", "must exactly match the allocation unit")]
    [InlineData("physical", "warehouse reservations are dispatched with the warehouse dispatch")]
    [InlineData("released", "is Released and cannot be shipped")]
    [InlineData("shipped", "has already been shipped")]
    [InlineData("stale-reservation", "is at version")]
    [InlineData("stale-order", "is at version")]
    [InlineData("other-order", "does not belong to sales order")]
    [InlineData("cancelled", "requires an open order")]
    public async Task Business_Rule_Violations_Are_Conflict_Without_Change(string scenario, string message)
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var version = (await OrderAsync()).Version;
        var request = Ship(version, ShipLine(allocation));
        switch (scenario)
        {
            case "less": request = Ship(version, ShipLine(allocation, quantity: 19m)); break;
            case "more": request = Ship(version, ShipLine(allocation, quantity: 21m)); break;
            case "unit": request = Ship(version, ShipLine(allocation, unit: "colli")); break;
            case "physical":
                await using (var db = _factory.CreateContext())
                {
                    var physical = (await new StockReservationService(db).ReserveAsync(_orderId, _boxLine, new CreateStockReservationDto
                    {
                        RequestId = Guid.NewGuid(), PalletContentLineId = _seed.ApprovedBox, Quantity = 5m, Unit = "box", NetWeightKg = 20m, ActorReference = "office-1"
                    })).Value;
                    request = Ship((await OrderAsync()).Version, ShipLine(physical, kg: 20m, lot: "LOT-A"));
                }
                break;
            case "released":
                Assert.Equal(ServiceResultStatus.Ok, (await ReleaseAsync(allocation.Id, allocation.Version)).Status);
                request = Ship(version, ShipLine(allocation, version: allocation.Version + 1));
                break;
            case "shipped":
                Assert.Equal(ServiceResultStatus.Created, (await ShipAsync(Ship(version, ShipLine(allocation)))).Status);
                request = Ship((await OrderAsync()).Version, ShipLine(allocation, version: allocation.Version + 1));
                break;
            case "stale-reservation": request = Ship(version, ShipLine(allocation, version: allocation.Version + 1)); break;
            case "stale-order": request = Ship(version + 1, ShipLine(allocation)); break;
            case "other-order":
                var (otherOrder, otherLine) = await SecondOrderAsync(10m);
                var foreign = await AllocatedAsync(5m, otherLine, otherOrder);
                request = Ship(version, ShipLine(foreign));
                break;
            case "cancelled":
                Assert.Equal(ServiceResultStatus.Ok, (await CancelAsync(version)).Status);
                request = Ship(version + 1, ShipLine(allocation, version: allocation.Version + 1));
                break;
        }
        var before = await StateAsync();

        var result = await ShipAsync(request);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Contains(message, result.Message);
        Assert.Equal(before.Shipments.Count, (await StateAsync()).Shipments.Count);
    }

    // ======================== Invoer (400), datum en 404 ========================

    [Theory]
    [InlineData("no-request-id", "requestId")]
    [InlineData("no-order-version", "expectedOrderVersion")]
    [InlineData("no-actor", "actorReference")]
    [InlineData("no-date", "shippedOn")]
    [InlineData("future-date", "shippedOn")]
    [InlineData("reference-too-long", "reference")]
    [InlineData("no-lines", "lines")]
    [InlineData("kg-missing", "lines[0].netWeightKg")]
    [InlineData("kg-zero", "lines[0].netWeightKg")]
    [InlineData("kg-four-decimals", "lines[0].netWeightKg")]
    [InlineData("quantity-zero", "lines[0].quantity")]
    [InlineData("bad-unit", "lines[0].unit")]
    [InlineData("lot-blank", "lines[0].lotNumber")]
    [InlineData("lot-too-long", "lines[0].lotNumber")]
    [InlineData("reason-too-long", "lines[0].lotDeviationReason")]
    [InlineData("duplicate", "lines[1]")]
    public async Task Invalid_Input_Is_Rejected_With_The_Field_Key_And_Changes_Nothing(string invalidCase, string key)
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var request = Ship((await OrderAsync()).Version, ShipLine(allocation));
        var line = request.Lines[0];
        switch (invalidCase)
        {
            case "no-request-id": request.RequestId = Guid.Empty; break;
            case "no-order-version": request.ExpectedOrderVersion = null; break;
            case "no-actor": request.ActorReference = " "; break;
            case "no-date": request.ShippedOn = null; break;
            case "future-date": request.ShippedOn = Today.AddDays(1); break;
            case "reference-too-long": request.Reference = new string('x', 101); break;
            case "no-lines": request.Lines = new List<CreateDirectTradeShipmentLineDto>(); break;
            case "kg-missing": line.NetWeightKg = null; break;
            case "kg-zero": line.NetWeightKg = 0m; break;
            case "kg-four-decimals": line.NetWeightKg = 1.0001m; break;
            case "quantity-zero": line.Quantity = 0m; break;
            case "bad-unit": line.Unit = "kg"; break;
            case "lot-blank": line.LotNumber = "  "; break;
            case "lot-too-long": line.LotNumber = new string('L', 101); break;
            case "reason-too-long": line.LotDeviationReason = new string('r', 501); break;
            case "duplicate": request.Lines.Add(ShipLine(allocation)); break;
        }

        var result = await ShipAsync(request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        Assert.Empty((await StateAsync()).Shipments);
    }

    [Fact]
    public async Task Null_Body_And_Unknown_Order_Are_Handled()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(5m);
        Assert.Equal(ServiceResultStatus.Validation, (await ShipAsync(null)).Status);
        Assert.Equal(ServiceResultStatus.NotFound, (await ShipAsync(Ship(0, ShipLine(allocation)), orderId: 999_999)).Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.NotFound, (await new DirectTradeShipmentService(db, new FixedBusinessCalendar(Today)).ListAsync(999_999)).Status);
    }

    // "Vandaag" is Europe/Amsterdam, niet UTC of de servertijd: rond middernacht verschilt de kalenderdatum.
    [Theory]
    [InlineData("2026-10-20T21:30:00Z", "2026-10-21", false)] // 23:30 in Amsterdam (CEST): 21 oktober is nog morgen
    [InlineData("2026-10-20T22:30:00Z", "2026-10-21", true)]  // 00:30 in Amsterdam: in UTC nog 20 oktober, maar 21 oktober is vandaag
    [InlineData("2026-10-20T22:30:00Z", "2026-10-22", false)]
    [InlineData("2026-12-31T22:59:59Z", "2027-01-01", false)] // 23:59:59 in Amsterdam (CET)
    [InlineData("2026-12-31T23:00:00Z", "2027-01-01", true)]  // 00:00 in Amsterdam
    public async Task The_Shipping_Date_Limit_Uses_Today_In_Europe_Amsterdam_Around_Midnight(string utcNow, string shippedOn, bool accepted)
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var request = Ship((await OrderAsync()).Version, ShipLine(allocation));
        request.ShippedOn = DateOnly.Parse(shippedOn);
        var calendar = new AmsterdamBusinessCalendar(new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));

        var result = await ShipAsync(request, calendar: calendar);

        if (accepted)
        {
            Assert.Equal(ServiceResultStatus.Created, result.Status);
            Assert.Equal(DateOnly.Parse(shippedOn), result.Value.ShippedOn);
        }
        else
        {
            Assert.Equal(ServiceResultStatus.Validation, result.Status);
            Assert.Contains("Europe/Amsterdam", result.Errors["shippedOn"].Single());
        }
    }

    [Theory]
    [InlineData("2026-03-28T22:59:59Z", "2026-03-28")] // nacht voor de zomertijd: 23:59:59 CET
    [InlineData("2026-03-28T23:00:00Z", "2026-03-29")]
    [InlineData("2026-10-24T21:59:59Z", "2026-10-24")] // nacht voor de wintertijd: 23:59:59 CEST
    [InlineData("2026-10-24T22:00:00Z", "2026-10-25")]
    [InlineData("2026-10-25T22:59:59Z", "2026-10-25")] // na de overgang: 23:59:59 CET
    [InlineData("2026-10-25T23:00:00Z", "2026-10-26")]
    public void Amsterdam_Calendar_Today_Follows_Local_Midnight_Including_Dst_Changes(string utcNow, string expected)
    {
        var calendar = new AmsterdamBusinessCalendar(new FixedTimeProvider(DateTimeOffset.Parse(utcNow)));
        Assert.Equal(DateOnly.Parse(expected), calendar.Today());
    }

    // ======================== Idempotentie ========================

    [Fact]
    public async Task Exact_Retry_Returns_The_Same_Shipment_And_Any_Changed_Field_Is_Conflict()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var version = (await OrderAsync()).Version;
        var request = Ship(version, ShipLine(allocation, lot: "LOT-ANDERS", reason: "herpakt"));
        var created = await ShipAsync(request);
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        var before = await StateAsync();

        var retry = await ShipAsync(request);
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(created.Value.Id, retry.Value.Id);
        var after = await StateAsync();
        Assert.Equal((before.Shipments.Count, before.Lines.Count, before.Events.Count), (after.Shipments.Count, after.Lines.Count, after.Events.Count));

        CreateDirectTradeShipmentDto Changed(Action<CreateDirectTradeShipmentDto> change)
        {
            var copy = Ship(version, ShipLine(allocation, lot: "LOT-ANDERS", reason: "herpakt"));
            copy.RequestId = request.RequestId;
            change(copy);
            return copy;
        }
        foreach (var changed in new[]
                 {
                     Changed(r => r.Lines[0].NetWeightKg = 81.5m),
                     Changed(r => r.Lines[0].LotNumber = "LOT-DT-SRC"),
                     Changed(r => r.Lines[0].LotDeviationReason = "andere reden"),
                     Changed(r => r.ShippedOn = new DateOnly(2026, 10, 18)),
                     Changed(r => r.Reference = "CMR-ANDERS"),
                     Changed(r => r.ActorReference = "office-2"),
                     Changed(r => r.ExpectedOrderVersion = version + 5),
                 })
        {
            Assert.Equal(ServiceResultStatus.Conflict, (await ShipAsync(changed)).Status);
        }
        Assert.Single((await StateAsync()).Shipments);
    }

    // ======================== Races ========================

    [Theory]
    [InlineData("release")]
    [InlineData("cancel")]
    [InlineData("second-shipment")]
    public async Task Concurrent_Writers_Have_Exactly_One_Winner_And_Never_Ship_Twice(string concurrent)
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var version = (await OrderAsync()).Version;
        ServiceResultStatus concurrentStatus = default;
        var interceptor = new ConcurrentSaveInterceptor(async () =>
        {
            concurrentStatus = concurrent switch
            {
                "release" => (await ReleaseAsync(allocation.Id, allocation.Version)).Status,
                "cancel" => (await CancelAsync(version)).Status,
                _ => (await ShipAsync(Ship(version, ShipLine(allocation, kg: 80m)))).Status
            };
        });

        var result = await ShipAsync(Ship(version, ShipLine(allocation)), interceptors: interceptor);

        Assert.Equal(concurrent == "second-shipment" ? ServiceResultStatus.Created : ServiceResultStatus.Ok, concurrentStatus);
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        var state = await StateAsync();
        Assert.Equal(concurrent == "second-shipment" ? 1 : 0, state.Shipments.Count);
        await using var db = _factory.CreateContext();
        var reservation = await db.StockReservations.AsNoTracking().SingleAsync(r => r.Id == allocation.Id);
        Assert.Equal(concurrent == "second-shipment" ? "Consumed" : "Released", reservation.Status);
    }

    [Fact]
    public async Task A_Release_That_Loses_To_A_Shipment_Is_Conflict()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var version = (await OrderAsync()).Version;
        ServiceResultStatus shipStatus = default;
        var interceptor = new ConcurrentSaveInterceptor(async () => shipStatus = (await ShipAsync(Ship(version, ShipLine(allocation)))).Status);

        var released = await ReleaseAsync(allocation.Id, allocation.Version, interceptor);

        Assert.Equal(ServiceResultStatus.Created, shipStatus);
        Assert.Equal(ServiceResultStatus.Conflict, released.Status);
        Assert.Equal("Consumed", BoxLine(await OrderAsync()).Reservations.Single(r => r.Id == allocation.Id).Status);
    }

    [Fact]
    public async Task Two_Concurrent_Identical_Retries_Create_One_Shipment()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var request = Ship((await OrderAsync()).Version, ShipLine(allocation));
        ServiceResultStatus concurrentStatus = default;
        var interceptor = new ConcurrentSaveInterceptor(async () => concurrentStatus = (await ShipAsync(request)).Status);

        var result = await ShipAsync(request, interceptors: interceptor);

        Assert.Equal(ServiceResultStatus.Created, concurrentStatus);
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var state = await StateAsync();
        Assert.Single(state.Shipments);
        Assert.Single(state.Lines);
        Assert.Single(state.Events);
    }

    // ======================== Scheiding van de magazijnflow ========================

    [Fact]
    public async Task After_Shipping_Release_Cancel_Warehouse_Dispatch_And_Warehouse_Correction_All_Refuse()
    {
        await ArrangeAsync();
        var allocation = await AllocatedAsync(20m);
        var shipped = await ShipAsync(Ship((await OrderAsync()).Version, ShipLine(allocation)));
        Assert.Equal(ServiceResultStatus.Created, shipped.Status);
        var order = await OrderAsync();
        var reservation = BoxLine(order).Reservations.Single(r => r.Id == allocation.Id);

        Assert.Equal(ServiceResultStatus.Conflict, (await ReleaseAsync(reservation.Id, reservation.Version)).Status);
        var cancel = await CancelAsync(order.Version);
        Assert.Equal(ServiceResultStatus.Conflict, cancel.Status);
        Assert.Contains("consumed", cancel.Message);
        await using var db = _factory.CreateContext();
        var dispatch = await new SalesDispatchService(db).DispatchAsync(_orderId, new CreateSalesDispatchDto
        {
            RequestId = Guid.NewGuid(), ExpectedOrderVersion = order.Version, ActorReference = "office-1",
            Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = reservation.Id, ExpectedReservationVersion = reservation.Version } }
        });
        Assert.Equal(ServiceResultStatus.Conflict, dispatch.Status);
        // De magazijncorrectie kent alleen magazijnverzendingen: een DirectTrade-verzending is daar niet te vinden.
        Assert.Empty((await new SalesDispatchService(db).ListAsync(_orderId)).Value);
        var correction = await new SalesDispatchCorrectionService(db).CorrectAsync(_orderId, shipped.Value.Id, new CorrectSalesDispatchDto
        {
            RequestId = Guid.NewGuid(), ExpectedOrderVersion = order.Version, ActorReference = "office-1", Reason = "poging", GoodsNeverLeftWarehouse = true,
            Lines = new List<CreateSalesDispatchLineDto> { new() { StockReservationId = reservation.Id, ExpectedReservationVersion = reservation.Version } }
        });
        Assert.Equal(ServiceResultStatus.NotFound, correction.Status);
        Assert.Equal("Consumed", (await db.StockReservations.AsNoTracking().SingleAsync(r => r.Id == allocation.Id)).Status);
    }
}
