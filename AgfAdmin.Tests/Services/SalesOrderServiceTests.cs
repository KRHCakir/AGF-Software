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

    private CreateSalesOrderDto PalletOrder(decimal? quantity = null, decimal? palletCount = 9m, decimal? packagesPerPallet = 120m, Guid? requestId = null, decimal? loosePackages = null)
    {
        var request = Request(requestId);
        request.Lines = new List<CreateSalesOrderLineDto> { new() { ProductVariantId = _seed.AvocadoId, Quantity = quantity, PalletCount = palletCount, PackagesPerPallet = packagesPerPallet, LoosePackages = loosePackages, Unit = "box" } };
        return request;
    }

    [Fact]
    public async Task Pallet_Input_With_Loose_Packages_Adds_Them_To_The_Total_And_Keeps_Them_On_The_Line()
    {
        await SeedAsync();
        var result = await CreateAsync(PalletOrder(palletCount: 3m, packagesPerPallet: 120m, loosePackages: 15m));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal((375m, 3m, 120m, 15m), (line.Quantity, line.PalletCount, line.PackagesPerPallet, line.LoosePackages));
        Assert.Equal(375m, line.OpenQuantity);
        // Met een expliciet totaal moet dat exact pallets × per pallet + los zijn.
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PalletOrder(quantity: 375m, palletCount: 3m, packagesPerPallet: 120m, loosePackages: 15m))).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await CreateAsync(PalletOrder(quantity: 360m, palletCount: 3m, packagesPerPallet: 120m, loosePackages: 15m))).Status);
        // Nul losse verpakkingen is toegestaan.
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PalletOrder(palletCount: 3m, packagesPerPallet: 120m, loosePackages: 0m))).Status);

        await using var verify = _factory.CreateContext();
        var stored = await verify.SalesOrderLines.SingleAsync(l => l.Id == line.Id);
        Assert.Equal((375m, 15m), (stored.Quantity, stored.LoosePackages));
    }

    [Theory]
    [InlineData("fractional-pallets", "lines[0].palletCount")]
    [InlineData("fractional-per-pallet", "lines[0].packagesPerPallet")]
    [InlineData("fractional-loose", "lines[0].loosePackages")]
    [InlineData("negative-loose", "lines[0].loosePackages")]
    [InlineData("loose-without-pallets", "lines[0].loosePackages")]
    public async Task New_Pallet_Input_Needs_Whole_Pallets_And_Whole_Loose_Packages(string invalidCase, string expectedErrorKey)
    {
        await SeedAsync();
        var request = invalidCase switch
        {
            "fractional-pallets" => PalletOrder(palletCount: 3.5m, packagesPerPallet: 120m),
            "fractional-per-pallet" => PalletOrder(palletCount: 3m, packagesPerPallet: 120.5m),
            "fractional-loose" => PalletOrder(palletCount: 3m, packagesPerPallet: 120m, loosePackages: 2.5m),
            "negative-loose" => PalletOrder(palletCount: 3m, packagesPerPallet: 120m, loosePackages: -1m),
            _ => PalletOrder(quantity: 15m, palletCount: null, packagesPerPallet: null, loosePackages: 15m)
        };
        var result = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
        Assert.Equal(0, await OrderCountAsync());
    }

    // Bestaande regels van vóór de hele-pallet-regel ("3,5 pallets × 120 = 420") blijven leesbaar, en een herhaling van precies
    // die opgeslagen opdracht wordt nog steeds herkend. Orderregels zijn na aanmaken niet wijzigbaar, dus er is geen bewerkpad.
    [Fact]
    public async Task Existing_Line_With_A_Fractional_Pallet_Count_Stays_Readable_And_Replays()
    {
        await SeedAsync();
        var requestId = Guid.NewGuid();
        int orderId;
        await using (var db = _factory.CreateContext())
        {
            var order = new AgfAdmin.Models.SalesOrder
            {
                RequestId = requestId, CustomerId = _seed.CustomerId, Status = "Open", CustomerReference = "PO-1", ActorReference = "office-1",
                RequestedDeliveryDate = UtcPrecision.ToStoredUtc(Delivery), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                Lines = new List<AgfAdmin.Models.SalesOrderLine> { new() { ProductVariantId = _seed.AvocadoId, Quantity = 420m, Unit = "box", PalletCount = 3.5m, PackagesPerPallet = 120m } }
            };
            db.SalesOrders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }

        await using (var db = _factory.CreateContext())
        {
            var read = await new SalesOrderService(db).GetAsync(orderId);
            var line = Assert.Single(read.Value.Lines);
            Assert.Equal((420m, 3.5m, 120m, (decimal?)null), (line.Quantity, line.PalletCount, line.PackagesPerPallet, line.LoosePackages));
        }

        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(PalletOrder(palletCount: 3.5m, packagesPerPallet: 120m, requestId: requestId))).Status);
        // Dezelfde breuk als nieuwe opdracht wordt geweigerd.
        var fresh = await CreateAsync(PalletOrder(palletCount: 3.5m, packagesPerPallet: 120m));
        Assert.Equal(ServiceResultStatus.Validation, fresh.Status);
        Assert.Contains("lines[0].palletCount", fresh.Errors.Keys);
        Assert.Equal(1, await OrderCountAsync());
    }

    [Fact]
    public async Task Order_Retry_Compares_Loose_Packages()
    {
        await SeedAsync();
        var requestId = Guid.NewGuid();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PalletOrder(palletCount: 3m, packagesPerPallet: 120m, loosePackages: 15m, requestId: requestId))).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(PalletOrder(palletCount: 3m, packagesPerPallet: 120m, loosePackages: 15m, requestId: requestId))).Status);
        // Zelfde totaal (375), andere verdeling of gewone invoer: een andere opdracht.
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(PalletOrder(palletCount: 3m, packagesPerPallet: 125m, requestId: requestId))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(PalletOrder(quantity: 375m, palletCount: null, packagesPerPallet: null, requestId: requestId))).Status);
        // Een opdracht zonder losse verpakkingen (zoals vóór dit veld) blijft gelijk aan een opgeslagen regel zonder losse verpakkingen.
        var oldStyle = Guid.NewGuid();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PalletOrder(requestId: oldStyle))).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(PalletOrder(requestId: oldStyle))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(PalletOrder(loosePackages: 0m, requestId: oldStyle))).Status);
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

    // Databasecheck buiten de service om: totaal = pallets × per pallet + los; los alleen bij palletinvoer, heel en >= 0.
    [Theory]
    [InlineData("375", "3", "120", "15", true)]
    [InlineData("360", "3", "120", "NULL", true)]
    [InlineData("420", "3.5", "120", "NULL", true)]   // bestaande regel met een gebroken palletaantal blijft toegestaan
    [InlineData("360", "3", "120", "0", true)]
    [InlineData("375", "3", "120", "NULL", false)]
    [InlineData("362.5", "3", "120", "2.5", false)]
    [InlineData("355", "3", "120", "-5", false)]
    [InlineData("15", "NULL", "NULL", "15", false)]
    public async Task Database_Check_Accepts_Pallets_Times_Per_Pallet_Plus_Loose_Packages(string quantity, string palletCount, string perPallet, string loose, bool accepted)
    {
        await SeedAsync();
        var order = await CreateAsync(Request());
        await using var raw = _factory.CreateContext();
        Task<int> Insert() => raw.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"SalesOrderLines\" (\"SalesOrderId\", \"ProductVariantId\", \"Quantity\", \"Unit\", \"PalletCount\", \"PackagesPerPallet\", \"LoosePackages\", \"CreatedAt\", \"Version\") " +
            $"VALUES ({order.Value.Id}, {_seed.AvocadoId}, {quantity}, 'tray', {palletCount}, {perPallet}, {loose}, '2026-10-20 08:00:00', 0)");
        if (accepted)
        {
            Assert.Equal(1, await Insert());
        }
        else
        {
            Assert.Contains("CK_SalesOrderLines_PalletInput_Consistent", (await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(Insert)).Message);
        }
    }

    // ---- Kg op de orderregel: netto en bruto optioneel, berekend (aantal × artikelstandaard) of ingevuld ----

    private CreateSalesOrderDto KgOrder(decimal quantity, decimal? netKg, decimal? grossKg, string source, Guid? requestId = null)
    {
        var request = Request(requestId);
        request.Lines = new List<CreateSalesOrderLineDto> { new() { ProductVariantId = _seed.AvocadoId, Quantity = quantity, Unit = "colli", NetWeightKg = netKg, GrossWeightKg = grossKg, WeightSource = source } };
        return request;
    }

    // Artikelstandaard 10 kg netto / 10,3 kg bruto per colli (zoals in de praktijk: 1.560 colli = 15.600 kg netto, 16.068 kg bruto).
    private async Task SetKgDefaultsAsync(decimal? net, decimal? gross)
    {
        await using var db = _factory.CreateContext();
        var variant = await db.ProductVariants.SingleAsync(v => v.Id == _seed.AvocadoId);
        variant.DefaultNetKgPerPackage = net;
        variant.DefaultGrossKgPerPackage = gross;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Calculated_Kg_Is_Stored_On_The_Line_And_Shown_In_Detail_And_List()
    {
        await SeedAsync();
        await SetKgDefaultsAsync(10m, 10.3m);
        var result = await CreateAsync(KgOrder(1560m, 15600m, 16068m, "Calculated"));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal((15600m, 16068m, "Calculated"), (line.NetWeightKg, line.GrossWeightKg, line.WeightSource));

        await using var db = _factory.CreateContext();
        var service = new SalesOrderService(db);
        var detail = Assert.Single((await service.GetAsync(result.Value.Id)).Value.Lines);
        Assert.Equal((15600m, 16068m, "Calculated"), (detail.NetWeightKg, detail.GrossWeightKg, detail.WeightSource));
        var listed = Assert.Single((await service.ListAsync(new SalesOrderFilter())).Value, o => o.Id == result.Value.Id);
        Assert.Equal((15600m, 16068m), (listed.Lines[0].NetWeightKg, listed.Lines[0].GrossWeightKg));
    }

    [Fact]
    public async Task Entered_Kg_Is_Stored_As_Given_Without_Article_Default_And_Never_Changes_The_Article()
    {
        await SeedAsync();
        // Gewogen artikel zonder kg per colli (zoals Avocado · Green): de gebruiker vult zelf kg in.
        var result = await CreateAsync(KgOrder(221m, 2210m, 2290.5m, "Entered"));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal((2210m, 2290.5m, "Entered"), (result.Value.Lines[0].NetWeightKg, result.Value.Lines[0].GrossWeightKg, result.Value.Lines[0].WeightSource));
        // Overschreven bij een artikel mét standaard: ook Entered, en de waarde wordt niet teruggerekend.
        await SetKgDefaultsAsync(10m, 10.3m);
        var overridden = await CreateAsync(KgOrder(1560m, 15450.5m, 16068m, "Entered"));
        Assert.Equal(ServiceResultStatus.Created, overridden.Status);
        Assert.Equal((15450.5m, 16068m, "Entered"), (overridden.Value.Lines[0].NetWeightKg, overridden.Value.Lines[0].GrossWeightKg, overridden.Value.Lines[0].WeightSource));
        // Kg zonder bron is Entered.
        var withoutSource = await CreateAsync(KgOrder(5m, 51m, null, null));
        Assert.Equal("Entered", withoutSource.Value.Lines[0].WeightSource);

        await using var db = _factory.CreateContext();
        var variant = await db.ProductVariants.SingleAsync(v => v.Id == _seed.AvocadoId);
        Assert.Equal((10m, 10.3m), (variant.DefaultNetKgPerPackage, variant.DefaultGrossKgPerPackage));
    }

    [Fact]
    public async Task A_Line_Without_Kg_Is_Allowed_And_Has_No_Weight_Source()
    {
        await SeedAsync();
        await SetKgDefaultsAsync(10m, 10.3m);
        var result = await CreateAsync(KgOrder(30m, null, null, null));
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(((decimal?)null, (decimal?)null, (string)null), (result.Value.Lines[0].NetWeightKg, result.Value.Lines[0].GrossWeightKg, result.Value.Lines[0].WeightSource));
    }

    [Theory]
    [InlineData("gross-below-net", "lines[0].grossWeightKg")]
    [InlineData("net-zero", "lines[0].netWeightKg")]
    [InlineData("gross-negative", "lines[0].grossWeightKg")]
    [InlineData("net-four-decimals", "lines[0].netWeightKg")]
    [InlineData("unknown-source", "lines[0].weightSource")]
    [InlineData("source-without-kg", "lines[0].weightSource")]
    [InlineData("calculated-wrong-net", "lines[0].netWeightKg")]
    [InlineData("calculated-wrong-gross", "lines[0].grossWeightKg")]
    [InlineData("calculated-without-default", "lines[0].grossWeightKg")]
    public async Task Invalid_Kg_Is_Refused_And_Nothing_Is_Stored(string invalidCase, string expectedErrorKey)
    {
        await SeedAsync();
        await SetKgDefaultsAsync(10m, invalidCase == "calculated-without-default" ? null : 10.3m);
        var request = invalidCase switch
        {
            "gross-below-net" => KgOrder(10m, 100m, 99.999m, "Entered"),
            "net-zero" => KgOrder(10m, 0m, null, "Entered"),
            "gross-negative" => KgOrder(10m, null, -1m, "Entered"),
            "net-four-decimals" => KgOrder(10m, 100.0001m, null, "Entered"),
            "unknown-source" => KgOrder(10m, 100m, null, "Weighed"),
            "source-without-kg" => KgOrder(10m, null, null, "Calculated"),
            "calculated-wrong-net" => KgOrder(10m, 101m, 103m, "Calculated"),
            "calculated-wrong-gross" => KgOrder(10m, 100m, 104m, "Calculated"),
            _ => KgOrder(10m, 100m, 103m, "Calculated")
        };
        var result = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
        Assert.Equal(0, await OrderCountAsync());
    }

    [Fact]
    public async Task Order_Retry_Compares_Kg_And_Source_And_Old_Payloads_Without_Kg_Still_Replay()
    {
        await SeedAsync();
        await SetKgDefaultsAsync(10m, 10.3m);
        var requestId = Guid.NewGuid();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(KgOrder(1560m, 15600m, 16068m, "Calculated", requestId))).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(KgOrder(1560m, 15600m, 16068m, "Calculated", requestId))).Status);
        // Herhaling na een wijziging van de artikelstandaard: dezelfde opdracht, dus herkend (de controle hoort bij nieuwe invoer).
        await SetKgDefaultsAsync(11m, 11.3m);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(KgOrder(1560m, 15600m, 16068m, "Calculated", requestId))).Status);
        // Andere kg, andere bron of geen kg: een andere opdracht.
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(KgOrder(1560m, 15600m, 16000m, "Calculated", requestId))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(KgOrder(1560m, 15600m, 16068m, "Entered", requestId))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(KgOrder(1560m, null, null, null, requestId))).Status);

        // Een opdracht van vóór dit veld (geen kg in de payload) blijft gelijk aan een opgeslagen regel zonder kg.
        var oldStyle = Guid.NewGuid();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(Request(oldStyle))).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(Request(oldStyle))).Status);
        var withKg = Request(oldStyle);
        withKg.Lines[0].NetWeightKg = 300m;
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(withKg)).Status);
    }

    // Databasecheck buiten de service om: kg leeg of positief, bruto >= netto, en een bron precies als er kg is.
    [Theory]
    [InlineData("100", "103", "'Calculated'", true)]
    [InlineData("100", "NULL", "'Entered'", true)]
    [InlineData("NULL", "103", "'Entered'", true)]
    [InlineData("NULL", "NULL", "NULL", true)]
    [InlineData("100", "100", "'Entered'", true)]
    [InlineData("100", "99.5", "'Entered'", false)]
    [InlineData("0", "NULL", "'Entered'", false)]
    [InlineData("NULL", "-1", "'Entered'", false)]
    [InlineData("100", "NULL", "NULL", false)]
    [InlineData("NULL", "NULL", "'Entered'", false)]
    [InlineData("100", "NULL", "'Weighed'", false)]
    public async Task Database_Check_Accepts_Positive_Kg_Gross_Not_Below_Net_And_A_Source_Only_With_Kg(string net, string gross, string source, bool accepted)
    {
        await SeedAsync();
        var order = await CreateAsync(Request());
        await using var raw = _factory.CreateContext();
        Task<int> Insert() => raw.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"SalesOrderLines\" (\"SalesOrderId\", \"ProductVariantId\", \"Quantity\", \"Unit\", \"NetWeightKg\", \"GrossWeightKg\", \"WeightSource\", \"CreatedAt\", \"Version\") " +
            $"VALUES ({order.Value.Id}, {_seed.AvocadoId}, 10, 'tray', {net}, {gross}, {source}, '2026-10-20 08:00:00', 0)");
        if (accepted)
        {
            Assert.Equal(1, await Insert());
        }
        else
        {
            Assert.Contains("CK_SalesOrderLines_", (await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(Insert)).Message);
        }
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
