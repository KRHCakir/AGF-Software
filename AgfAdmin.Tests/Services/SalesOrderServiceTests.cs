using AgfAdmin.DTOs;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

public class SalesOrderServiceTests : IDisposable
{
    private static readonly DateTimeOffset Delivery = new(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(2));

    private readonly SqliteAppDbContextFactory _factory = new();
    private SalesSeed _seed;

    public void Dispose() => _factory.Dispose();

    private async Task<SalesSeed> SeedAsync() => _seed ??= await SalesTestData.SeedAsync(_factory);

    private CreateSalesOrderDto Request(Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        CustomerId = _seed.CustomerId,
        CustomerReference = "PO-1",
        RequestedDeliveryDate = Delivery,
        ActorReference = "office-1",
        Lines = new List<CreateSalesOrderLineDto>
        {
            new() { ProductVariantId = _seed.AvocadoId, Quantity = 30m, Unit = "box" },
            new() { ProductVariantId = _seed.AvocadoId, Quantity = 5m, Unit = "colli" },
        }
    };

    // ---- Palletinvoer op orderregels: Quantity blijft de bron voor vraag, reservering en verzending ----

    private CreateSalesOrderDto PalletOrder(decimal? quantity = null, decimal? palletCount = 9m, decimal? packagesPerPallet = 120m, Guid? requestId = null)
    {
        var request = Request(requestId);
        request.Lines = new List<CreateSalesOrderLineDto> { new() { ProductVariantId = _seed.AvocadoId, Quantity = quantity, PalletCount = palletCount, PackagesPerPallet = packagesPerPallet, Unit = "box" } };
        return request;
    }

    [Fact]
    public async Task Pallet_Input_On_An_Order_Line_Computes_The_Total_And_Is_Shown_On_The_Line()
    {
        await SeedAsync();
        var result = await CreateAsync(PalletOrder());
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(1080m, line.Quantity);
        Assert.Equal(1080m, line.OpenQuantity);
        Assert.Equal(9m, line.PalletCount);
        Assert.Equal(120m, line.PackagesPerPallet);
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PalletOrder(quantity: 1080m))).Status);
    }

    [Theory]
    [InlineData("total-mismatch", "lines[0].quantity")]
    [InlineData("pallets-only", "lines[0].packagesPerPallet")]
    [InlineData("per-pallet-only", "lines[0].palletCount")]
    public async Task Pallet_Input_On_An_Order_Is_Validated(string invalidCase, string expectedErrorKey)
    {
        await SeedAsync();
        var request = invalidCase switch
        {
            "total-mismatch" => PalletOrder(quantity: 1000m),
            "pallets-only" => PalletOrder(packagesPerPallet: null),
            _ => PalletOrder(palletCount: null)
        };
        var result = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Pallet_Input_On_An_Order_Beyond_Decimal_Range_Is_A_Validation_Error()
    {
        await SeedAsync();
        var result = await CreateAsync(PalletOrder(palletCount: 999_999_999_999_999m, packagesPerPallet: 999_999_999_999_999m));
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains("lines[0].quantity", result.Errors.Keys);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Order_Retry_With_Other_Pallet_Split_Conflicts()
    {
        await SeedAsync();
        var requestId = Guid.NewGuid();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PalletOrder(requestId: requestId))).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(PalletOrder(requestId: requestId))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(PalletOrder(palletCount: 12m, packagesPerPallet: 90m, requestId: requestId))).Status);
    }

    private async Task<ServiceResult<SalesOrderDto>> CreateAsync(CreateSalesOrderDto request)
    {
        await using var db = _factory.CreateContext();
        return await new SalesOrderService(db).CreateAsync(request);
    }

    private async Task<int> OrderCountAsync()
    {
        await using var db = _factory.CreateContext();
        return await db.SalesOrders.CountAsync();
    }

    private async Task<ServiceResult<StockReservationDto>> ReserveAsync(int orderId, int lineId, int contentLineId, decimal quantity, decimal kg, string unit = "box")
    {
        await using var db = _factory.CreateContext();
        return await new StockReservationService(db).ReserveAsync(orderId, lineId, new CreateStockReservationDto
        {
            RequestId = Guid.NewGuid(),
            PalletContentLineId = contentLineId,
            Quantity = quantity,
            Unit = unit,
            NetWeightKg = kg,
            ActorReference = "office-1"
        });
    }

    // ---- Aanmaken ----

    [Fact]
    public async Task Create_Stores_Open_Order_With_Exact_Normalised_Values()
    {
        await SeedAsync();
        var request = Request();
        request.CustomerReference = "  PO-1 ";
        request.ActorReference = " office-1 ";

        var result = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var order = result.Value;
        Assert.Equal("Open", order.Status);
        Assert.Equal(_seed.CustomerId, order.CustomerId);
        Assert.Equal("Customer T", order.CustomerName);
        Assert.Equal("PO-1", order.CustomerReference);
        Assert.Equal("office-1", order.ActorReference);
        Assert.Equal(new DateTime(2026, 10, 23, 6, 0, 0, DateTimeKind.Utc), order.RequestedDeliveryDate);
        Assert.Equal(DateTimeKind.Utc, order.RequestedDeliveryDate.Kind);
        Assert.Equal(0, order.Version);
        Assert.Equal(new[] { ("box", 30m), ("colli", 5m) }, order.Lines.Select(l => (l.Unit, l.Quantity)));
        Assert.All(order.Lines, line => Assert.Equal(0m, line.ReservedQuantity));
        Assert.All(order.Lines, line => Assert.Equal(line.Quantity, line.OpenQuantity));

        await using var verify = _factory.CreateContext();
        var stored = await verify.SalesOrders.SingleAsync();
        Assert.Equal("PO-1", stored.CustomerReference);
        Assert.Equal(request.RequestId, stored.RequestId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_Customer_Reference_Becomes_Null(string reference)
    {
        await SeedAsync();
        var request = Request();
        request.CustomerReference = reference;

        Assert.Null((await CreateAsync(request)).Value.CustomerReference);
    }

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("request-id-empty", "requestId")]
    [InlineData("customer-missing", "customerId")]
    [InlineData("reference-too-long", "customerReference")]
    [InlineData("delivery-missing", "requestedDeliveryDate")]
    [InlineData("actor-missing", "actorReference")]
    [InlineData("actor-blank", "actorReference")]
    [InlineData("actor-too-long", "actorReference")]
    [InlineData("lines-missing", "lines")]
    [InlineData("lines-empty", "lines")]
    [InlineData("lines-too-many", "lines")]
    [InlineData("line-null", "lines[1]")]
    [InlineData("variant-missing", "lines[1].productVariantId")]
    [InlineData("quantity-missing", "lines[1].quantity")]
    [InlineData("quantity-zero", "lines[1].quantity")]
    [InlineData("quantity-four-decimals", "lines[1].quantity")]
    [InlineData("quantity-too-large", "lines[1].quantity")]
    [InlineData("unit-missing", "lines[1].unit")]
    [InlineData("unit-kg", "lines[1].unit")]
    [InlineData("unit-capitalised", "lines[1].unit")]
    [InlineData("duplicate-variant-and-unit", "lines[1]")]
    public async Task Create_Rejects_Invalid_Input_And_Stores_Nothing(string invalidCase, string key)
    {
        await SeedAsync();
        var request = Request();
        var line = request.Lines[1];
        switch (invalidCase)
        {
            case "request-id-missing": request.RequestId = null; break;
            case "request-id-empty": request.RequestId = Guid.Empty; break;
            case "customer-missing": request.CustomerId = null; break;
            case "reference-too-long": request.CustomerReference = new string('R', 101); break;
            case "delivery-missing": request.RequestedDeliveryDate = null; break;
            case "actor-missing": request.ActorReference = null; break;
            case "actor-blank": request.ActorReference = "  "; break;
            case "actor-too-long": request.ActorReference = new string('A', 101); break;
            case "lines-missing": request.Lines = null; break;
            case "lines-empty": request.Lines = new List<CreateSalesOrderLineDto>(); break;
            case "lines-too-many":
                request.Lines = Enumerable.Range(0, 201).Select(_ => new CreateSalesOrderLineDto { ProductVariantId = _seed.AvocadoId, Quantity = 1m, Unit = "box" }).ToList();
                break;
            case "line-null": request.Lines[1] = null; break;
            case "variant-missing": line.ProductVariantId = null; break;
            case "quantity-missing": line.Quantity = null; break;
            case "quantity-zero": line.Quantity = 0m; break;
            case "quantity-four-decimals": line.Quantity = 1.0001m; break;
            case "quantity-too-large": line.Quantity = 1_000_000_000_000_000m; break;
            case "unit-missing": line.Unit = null; break;
            case "unit-kg": line.Unit = "kg"; break;
            case "unit-capitalised": line.Unit = "Box"; break;
            case "duplicate-variant-and-unit": line.Unit = "box"; break;
        }

        var result = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(key, result.Errors.Keys);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Same_Variant_In_Different_Units_Is_Allowed_As_Separate_Lines()
    {
        await SeedAsync();
        var request = Request();
        request.Lines.Add(new CreateSalesOrderLineDto { ProductVariantId = _seed.AvocadoId, Quantity = 2m, Unit = "tray" });

        var result = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(3, result.Value.Lines.Count);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("variant")]
    public async Task Unknown_Customer_Or_Variant_Is_NotFound_And_Stores_Nothing(string missing)
    {
        await SeedAsync();
        var request = Request();
        if (missing == "customer") request.CustomerId = 999_999; else request.Lines[1].ProductVariantId = 999_999;

        Assert.Equal(ServiceResultStatus.NotFound, (await CreateAsync(request)).Status);
        Assert.Equal(0, await OrderCountAsync());
    }

    // ---- Idempotentie met genormaliseerde payload ----

    [Theory]
    [InlineData("identical")]
    [InlineData("reference-padded")]
    [InlineData("actor-padded")]
    [InlineData("same-instant-other-offset")]
    [InlineData("sub-microsecond-ticks")]
    [InlineData("quantity-trailing-zero")]
    [InlineData("note-padded")]
    public async Task Equivalent_Retry_Returns_Same_Order_Without_New_Rows(string variant)
    {
        await SeedAsync();
        var requestId = Guid.NewGuid();
        var first = Request(requestId);
        first.RequestedDeliveryDate = variant == "sub-microsecond-ticks" ? Delivery.AddTicks(5) : Delivery;
        if (variant == "note-padded") first.InternalNote = "Klant komt morgen langs";
        var created = await CreateAsync(first);

        var retry = Request(requestId);
        switch (variant)
        {
            case "reference-padded": retry.CustomerReference = "  PO-1  "; break;
            case "note-padded": retry.InternalNote = "  Klant komt morgen langs "; break;
            case "actor-padded": retry.ActorReference = " office-1"; break;
            case "same-instant-other-offset": retry.RequestedDeliveryDate = Delivery.ToOffset(TimeSpan.Zero); break;
            case "sub-microsecond-ticks": retry.RequestedDeliveryDate = Delivery.AddTicks(7); break;
            case "quantity-trailing-zero": retry.Lines[0].Quantity = 30.000m; break;
        }

        var result = await CreateAsync(retry);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(created.Value.Id, result.Value.Id);
        Assert.Equal(1, await OrderCountAsync());
    }

    [Fact]
    public async Task Blank_And_Null_Reference_Are_The_Same_On_Retry()
    {
        await SeedAsync();
        var requestId = Guid.NewGuid();
        var first = Request(requestId);
        first.CustomerReference = "   ";
        await CreateAsync(first);
        var retry = Request(requestId);
        retry.CustomerReference = null;

        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(retry)).Status);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("reference")]
    [InlineData("reference-removed")]
    [InlineData("delivery-day")]
    [InlineData("delivery-microsecond")]
    [InlineData("actor")]
    [InlineData("quantity")]
    [InlineData("unit")]
    [InlineData("variant")]
    [InlineData("extra-line")]
    [InlineData("line-order")]
    [InlineData("internal-note")]
    public async Task Reused_RequestId_With_Different_Payload_Is_Conflict(string change)
    {
        await SeedAsync();
        var requestId = Guid.NewGuid();
        await CreateAsync(Request(requestId));
        var retry = Request(requestId);
        switch (change)
        {
            case "customer": retry.CustomerId = _seed.OtherCustomerId; break;
            case "internal-note": retry.InternalNote = "Klant komt morgen langs"; break;
            case "reference": retry.CustomerReference = "PO-2"; break;
            case "reference-removed": retry.CustomerReference = null; break;
            case "delivery-day": retry.RequestedDeliveryDate = Delivery.AddDays(1); break;
            case "delivery-microsecond": retry.RequestedDeliveryDate = Delivery.AddTicks(10); break;
            case "actor": retry.ActorReference = "office-2"; break;
            case "quantity": retry.Lines[0].Quantity = 31m; break;
            case "unit": retry.Lines[1].Unit = "tray"; break;
            case "variant": retry.Lines[1].ProductVariantId = _seed.MangoId; break;
            case "extra-line": retry.Lines.Add(new CreateSalesOrderLineDto { ProductVariantId = _seed.MangoId, Quantity = 1m, Unit = "box" }); break;
            case "line-order": retry.Lines.Reverse(); break;
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(retry)).Status);
        Assert.Equal(1, await OrderCountAsync());
    }

    // ---- Lezen ----

    [Fact]
    public async Task Get_Shows_Reserved_And_Open_Quantities_From_Active_Reservations_Only()
    {
        await SeedAsync();
        var order = (await CreateAsync(Request())).Value;
        var box = order.Lines.Single(l => l.Unit == "box");
        var first = await ReserveAsync(order.Id, box.Id, _seed.ApprovedBox, 10m, 40m);
        await ReserveAsync(order.Id, box.Id, _seed.SecondPalletApprovedBox, 5m, 20m);
        await using (var db = _factory.CreateContext())
        {
            await new StockReservationService(db).ReleaseAsync(first.Value.Id, new ReleaseStockReservationDto
            {
                RequestId = Guid.NewGuid(),
                ExpectedReservationVersion = first.Value.Version,
                ActorReference = "office-1",
                Reason = "Klant wil andere partij"
            });
        }

        await using var read = _factory.CreateContext();
        var result = await new SalesOrderService(read).GetAsync(order.Id);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var line = result.Value.Lines.Single(l => l.Unit == "box");
        Assert.Equal(5m, line.ReservedQuantity);
        Assert.Equal(20m, line.ReservedNetWeightKg);
        Assert.Equal(25m, line.OpenQuantity);
        Assert.Equal(2, line.Reservations.Count);
        var active = Assert.Single(line.Reservations, r => r.Status == "Active");
        Assert.Equal("P-WH2", active.PalletReference);
        Assert.Equal("LOT-C", active.LotNumber);
        var released = Assert.Single(line.Reservations, r => r.Status == "Released");
        Assert.Equal("Klant wil andere partij", released.ReleaseReason);
    }

    [Fact]
    public async Task Get_Flags_Active_Reservations_On_Non_Approved_Content_As_On_Hold_From_Current_Quality()
    {
        await SeedAsync();
        var order = (await CreateAsync(Request())).Value;
        var box = order.Lines.Single(l => l.Unit == "box");
        var onFirstPallet = await ReserveAsync(order.Id, box.Id, _seed.ApprovedBox, 10m, 40m);
        var released = await ReserveAsync(order.Id, box.Id, _seed.ApprovedBox, 2m, 8m);
        var onSecondPallet = await ReserveAsync(order.Id, box.Id, _seed.SecondPalletApprovedBox, 5m, 20m);
        await using (var db = _factory.CreateContext())
        {
            Assert.Equal(ServiceResultStatus.Ok, (await new StockReservationService(db).ReleaseAsync(released.Value.Id, new ReleaseStockReservationDto
            {
                RequestId = Guid.NewGuid(),
                ExpectedReservationVersion = released.Value.Version,
                ActorReference = "office-1",
                Reason = "Verkeerde partij"
            })).Status);
        }

        // Vóór de blokkade: alles Approved en niets on hold.
        await using (var before = _factory.CreateContext())
        {
            var line = (await new SalesOrderService(before).GetAsync(order.Id)).Value.Lines.Single(l => l.Unit == "box");
            Assert.All(line.Reservations, r => Assert.Equal(("Approved", false), (r.ContentQualityStatus, r.OnHold)));
        }

        Assert.Equal(ServiceResultStatus.Ok, await BlockAsync(_seed.ApprovedBox));

        await using var read = _factory.CreateContext();
        var after = (await new SalesOrderService(read).GetAsync(order.Id)).Value.Lines.Single(l => l.Unit == "box");
        var held = after.Reservations.Single(r => r.Id == onFirstPallet.Value.Id);
        Assert.Equal(("Active", "Blocked", true), (held.Status, held.ContentQualityStatus, held.OnHold));
        // Een vrijgegeven reservering op geblokkeerde inhoud is niet on hold: er staat niets meer vast.
        var done = after.Reservations.Single(r => r.Id == released.Value.Id);
        Assert.Equal(("Released", "Blocked", false), (done.Status, done.ContentQualityStatus, done.OnHold));
        var fine = after.Reservations.Single(r => r.Id == onSecondPallet.Value.Id);
        Assert.Equal(("Active", "Approved", false), (fine.Status, fine.ContentQualityStatus, fine.OnHold));
        // De totalen van de orderregel tellen de reservering on hold gewoon mee: de klantvraag is nog steeds vastgelegd.
        Assert.Equal(15m, after.ReservedQuantity);
        Assert.Equal(15m, after.OpenQuantity);
    }

    private async Task<ServiceResultStatus> BlockAsync(int contentLineId)
    {
        await using var read = _factory.CreateContext();
        var line = await read.PalletContentLines.AsNoTracking().SingleAsync(c => c.Id == contentLineId);
        await using var service = _factory.CreateContext();
        return (await new PalletService(service).ChangeQualityStatusAsync(line.PalletId, line.Id, new ChangeQualityStatusDto
        {
            RequestId = Guid.NewGuid(),
            ExpectedContentLineVersion = line.Version,
            ToStatus = "Blocked",
            ActorReference = "qc-1",
            Reason = "Claim"
        })).Status;
    }

    [Fact]
    public async Task Get_Unknown_Order_Is_NotFound()
    {
        await using var db = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.NotFound, (await new SalesOrderService(db).GetAsync(999_999)).Status);
    }

    [Fact]
    public async Task List_Filters_By_Customer_And_Status()
    {
        await SeedAsync();
        await CreateAsync(Request());
        var other = Request();
        other.CustomerId = _seed.OtherCustomerId;
        await CreateAsync(other);

        await using var db = _factory.CreateContext();
        var service = new SalesOrderService(db);
        Assert.Equal(2, (await service.ListAsync(new SalesOrderFilter())).Value.Count);
        Assert.Equal(_seed.OtherCustomerId, Assert.Single((await service.ListAsync(new SalesOrderFilter { CustomerId = _seed.OtherCustomerId })).Value).CustomerId);
        Assert.Equal(2, (await service.ListAsync(new SalesOrderFilter { Status = "Open" })).Value.Count);
        Assert.Empty((await service.ListAsync(new SalesOrderFilter { Status = "Cancelled" })).Value);
        Assert.Equal(ServiceResultStatus.Validation, (await service.ListAsync(new SalesOrderFilter { Status = "open" })).Status);
    }

    // ---- Reserveerbare voorraad ----

    [Fact]
    public async Task Reservable_Stock_Lists_Only_Approved_Received_Warehouse_Content_Of_Same_Variant_And_Unit()
    {
        await SeedAsync();
        var order = (await CreateAsync(Request())).Value;
        var box = order.Lines.Single(l => l.Unit == "box");
        await ReserveAsync(order.Id, box.Id, _seed.ApprovedBox, 10m, 40m);

        await using var db = _factory.CreateContext();
        var result = await new SalesOrderService(db).GetReservableStockAsync(order.Id, box.Id);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(new[] { _seed.ApprovedBox, _seed.SecondPalletApprovedBox }, result.Value.Select(r => r.PalletContentLineId).OrderBy(id => id));
        var partial = result.Value.Single(r => r.PalletContentLineId == _seed.ApprovedBox);
        Assert.Equal((40m, 160m, 10m, 40m, 30m, 120m), (partial.Quantity, partial.NetWeightKg, partial.ReservedQuantity, partial.ReservedNetWeightKg, partial.AvailableQuantity, partial.AvailableNetWeightKg));
        Assert.Equal("P-WH", partial.PalletReference);
        Assert.Equal("COLD-01", partial.WarehouseLocation);
        Assert.Equal("LOT-A", partial.LotNumber);
        Assert.Equal(1, partial.ContentLineVersion);
        Assert.All(result.Value, r => Assert.Equal("box", r.Unit));
    }

    [Fact]
    public async Task Fully_Reserved_Content_Is_Not_Listed_As_Reservable()
    {
        await SeedAsync();
        var request = Request();
        request.Lines[0].Quantity = 100m;
        var order = (await CreateAsync(request)).Value;
        var box = order.Lines.Single(l => l.Unit == "box");
        await ReserveAsync(order.Id, box.Id, _seed.ApprovedBox, 40m, 160m);

        await using var db = _factory.CreateContext();
        var result = await new SalesOrderService(db).GetReservableStockAsync(order.Id, box.Id);

        Assert.Equal(new[] { _seed.SecondPalletApprovedBox }, result.Value.Select(r => r.PalletContentLineId));
    }

    [Theory]
    [InlineData("order")]
    [InlineData("line-of-other-order")]
    public async Task Reservable_Stock_For_Unknown_Order_Or_Foreign_Line_Is_NotFound(string scenario)
    {
        await SeedAsync();
        var order = (await CreateAsync(Request())).Value;
        var other = (await CreateAsync(Request())).Value;

        await using var db = _factory.CreateContext();
        var result = scenario == "order"
            ? await new SalesOrderService(db).GetReservableStockAsync(999_999, order.Lines[0].Id)
            : await new SalesOrderService(db).GetReservableStockAsync(order.Id, other.Lines[0].Id);

        Assert.Equal(ServiceResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Creating_Orders_Touches_No_Legacy_Or_Stock_Tables()
    {
        await SeedAsync();
        await using (var before = _factory.CreateContext())
        {
            Assert.Equal(1, await before.Orders.CountAsync());
        }

        await CreateAsync(Request());

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.Orders.CountAsync());
        Assert.Equal(1, await verify.OrderLines.CountAsync());
        Assert.Equal(0, await verify.OrderLineAllocations.CountAsync());
        Assert.Equal(0, await verify.Inventory.CountAsync());
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.StockReservations.CountAsync());
    }
}
