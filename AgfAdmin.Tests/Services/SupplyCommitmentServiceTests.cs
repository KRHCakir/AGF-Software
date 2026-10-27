using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

public class SupplyCommitmentServiceTests : IDisposable
{
    private static readonly DateTimeOffset ExpectedAt = new(2026, 11, 2, 9, 30, 0, TimeSpan.FromHours(1));

    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record Seed(int VendorId, int OtherVendorId, int CustomerId, int OtherCustomerId, int VariantSize6Id, int VariantSize8Id);

    private async Task<Seed> SeedAsync()
    {
        await using var db = _factory.CreateContext();

        var product = new Product { Name = "Avocado" };
        var size6 = new ProductVariant { Product = product, Name = "Hass 6", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.50m };
        var size8 = new ProductVariant { Product = product, Name = "Hass 8", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 11.50m };
        var vendor = new Vendor { Name = "Supplier B" };
        var otherVendor = new Vendor { Name = "Supplier K" };
        var customer = new Customer { Name = "Customer T" };
        var otherCustomer = new Customer { Name = "Customer U" };

        db.AddRange(product, size6, size8, vendor, otherVendor, customer, otherCustomer);
        await db.SaveChangesAsync();

        return new Seed(vendor.Id, otherVendor.Id, customer.Id, otherCustomer.Id, size6.Id, size8.Id);
    }

    private SupplyCommitmentService CreateService(AgfAdmin.Data.AppDbContext db) => new(db);

    private static CreateSupplyCommitmentDto WarehouseRequest(Seed seed, Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        VendorId = seed.VendorId,
        Route = "Warehouse",
        Origin = "Peru",
        ExpectedAt = ExpectedAt,
        Lines = new List<CreateSupplyCommitmentLineDto>
        {
            new() { ProductVariantId = seed.VariantSize6Id, ExpectedQuantity = 40m, ExpectedUnit = "box", ExpectedNetWeightKg = 160.125m, LotNumber = "LOT-6A" },
            new() { ProductVariantId = seed.VariantSize8Id, ExpectedQuantity = 12.5m, ExpectedUnit = "colli" }
        }
    };

    private static CreateSupplyCommitmentDto DirectTradeRequest(Seed seed, Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        VendorId = seed.VendorId,
        CustomerId = seed.CustomerId,
        Route = "DirectTrade",
        Origin = "Kenya",
        Destination = "Turkey",
        ExpectedAt = ExpectedAt,
        Lines = new List<CreateSupplyCommitmentLineDto>
        {
            new() { ProductVariantId = seed.VariantSize6Id, ExpectedQuantity = 1m, ExpectedUnit = "box", ExpectedNetWeightKg = 4.2m }
        }
    };

    // ---- Palletinvoer: pallets × verpakkingen per pallet; het totaal blijft de bron ----

    private static CreateSupplyCommitmentDto PalletRequest(Seed seed, decimal? quantity = null, decimal? palletCount = 9m, decimal? packagesPerPallet = 120m, Guid? requestId = null, decimal? loosePackages = null)
    {
        var request = WarehouseRequest(seed, requestId);
        request.Lines = new List<CreateSupplyCommitmentLineDto>
        {
            new() { ProductVariantId = seed.VariantSize6Id, ExpectedQuantity = quantity, PalletCount = palletCount, PackagesPerPallet = packagesPerPallet, LoosePackages = loosePackages, ExpectedUnit = "box", LotNumber = "LOT-PAL" }
        };
        return request;
    }

    [Fact]
    public async Task Pallet_Input_With_Loose_Packages_Adds_Them_To_The_Total_And_Shows_Article_Kg_Defaults()
    {
        var seed = await SeedAsync();
        await using (var db = _factory.CreateContext())
        {
            var variant = await db.ProductVariants.SingleAsync(v => v.Id == seed.VariantSize6Id);
            (variant.DefaultNetKgPerPackage, variant.DefaultGrossKgPerPackage) = (10m, 10.3m);
            await db.SaveChangesAsync();
        }
        var result = await CreateAsync(PalletRequest(seed, palletCount: 3m, packagesPerPallet: 120m, loosePackages: 15m));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal((375m, 3m, 120m, 15m), (line.ExpectedQuantity, line.PalletCount, line.PackagesPerPallet, line.LoosePackages));
        // Artikelstandaarden voor de rekenregel bij ontvangst (alleen weergave).
        Assert.Equal((10m, 10.3m), (line.DefaultNetKgPerPackage, line.DefaultGrossKgPerPackage));
        await using var verify = _factory.CreateContext();
        Assert.Equal(15m, (await verify.SupplyCommitmentLines.SingleAsync(l => l.LotNumber == "LOT-PAL")).LoosePackages);
    }

    [Theory]
    [InlineData("fractional-pallets", "lines[0].palletCount")]
    [InlineData("fractional-per-pallet", "lines[0].packagesPerPallet")]
    [InlineData("fractional-loose", "lines[0].loosePackages")]
    [InlineData("negative-loose", "lines[0].loosePackages")]
    [InlineData("loose-without-pallets", "lines[0].loosePackages")]
    [InlineData("total-mismatch-with-loose", "lines[0].expectedQuantity")]
    public async Task New_Pallet_Input_Needs_Whole_Pallets_And_Whole_Loose_Packages(string invalidCase, string expectedErrorKey)
    {
        var seed = await SeedAsync();
        var request = invalidCase switch
        {
            "fractional-pallets" => PalletRequest(seed, palletCount: 3.5m, packagesPerPallet: 120m),
            "fractional-per-pallet" => PalletRequest(seed, palletCount: 3m, packagesPerPallet: 120.5m),
            "fractional-loose" => PalletRequest(seed, palletCount: 3m, packagesPerPallet: 120m, loosePackages: 2.5m),
            "negative-loose" => PalletRequest(seed, palletCount: 3m, packagesPerPallet: 120m, loosePackages: -1m),
            "loose-without-pallets" => PalletRequest(seed, quantity: 15m, palletCount: null, packagesPerPallet: null, loosePackages: 15m),
            _ => PalletRequest(seed, quantity: 360m, palletCount: 3m, packagesPerPallet: 120m, loosePackages: 15m)
        };
        AssertValidation(await CreateAsync(request), expectedErrorKey);
        await AssertNothingStoredAsync();
    }

    // Bestaande aanvoerregels met een gebroken palletaantal blijven leesbaar en een herhaling van die opdracht wordt herkend;
    // aanvoerregels zijn na aanmaken niet wijzigbaar, dus er is geen bewerkpad dat de breuk opnieuw zou moeten valideren.
    [Fact]
    public async Task Existing_Line_With_A_Fractional_Pallet_Count_Stays_Readable_And_Replays()
    {
        var seed = await SeedAsync();
        var requestId = Guid.NewGuid();
        int commitmentId;
        await using (var db = _factory.CreateContext())
        {
            var commitment = new SupplyCommitment
            {
                RequestId = requestId, VendorId = seed.VendorId, Route = "Warehouse", Status = "Expected", Origin = "Peru",
                ExpectedAt = ExpectedAt.UtcDateTime, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                Lines = new List<SupplyCommitmentLine> { new() { ProductVariantId = seed.VariantSize6Id, ExpectedQuantity = 420m, ExpectedUnit = "box", PalletCount = 3.5m, PackagesPerPallet = 120m, LotNumber = "LOT-PAL" } }
            };
            db.SupplyCommitments.Add(commitment);
            await db.SaveChangesAsync();
            commitmentId = commitment.Id;
        }

        await using (var db = _factory.CreateContext())
        {
            var line = Assert.Single((await CreateService(db).GetAsync(commitmentId)).Value.Lines);
            Assert.Equal((420m, 3.5m, 120m, (decimal?)null), (line.ExpectedQuantity, line.PalletCount, line.PackagesPerPallet, line.LoosePackages));
        }
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(PalletRequest(seed, palletCount: 3.5m, packagesPerPallet: 120m, requestId: requestId))).Status);
        AssertValidation(await CreateAsync(PalletRequest(seed, palletCount: 3.5m, packagesPerPallet: 120m)), "lines[0].palletCount");
        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.SupplyCommitments.CountAsync());
    }

    [Fact]
    public async Task Retry_Compares_Loose_Packages()
    {
        var seed = await SeedAsync();
        var requestId = Guid.NewGuid();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PalletRequest(seed, palletCount: 3m, packagesPerPallet: 120m, loosePackages: 15m, requestId: requestId))).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(PalletRequest(seed, palletCount: 3m, packagesPerPallet: 120m, loosePackages: 15m, requestId: requestId))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(PalletRequest(seed, palletCount: 3m, packagesPerPallet: 125m, requestId: requestId))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(PalletRequest(seed, palletCount: 3m, packagesPerPallet: 120m, loosePackages: 14m, quantity: 374m, requestId: requestId))).Status);
    }

    [Theory]
    [InlineData("375", "3", "120", "15", true)]
    [InlineData("420", "3.5", "120", "NULL", true)]
    [InlineData("375", "3", "120", "NULL", false)]
    [InlineData("362.5", "3", "120", "2.5", false)]
    [InlineData("15", "NULL", "NULL", "15", false)]
    public async Task Database_Check_Accepts_Pallets_Times_Per_Pallet_Plus_Loose_Packages(string quantity, string palletCount, string perPallet, string loose, bool accepted)
    {
        var seed = await SeedAsync();
        var commitment = await CreateAsync(WarehouseRequest(seed));
        await using var raw = _factory.CreateContext();
        Task<int> Insert() => raw.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"SupplyCommitmentLines\" (\"SupplyCommitmentId\", \"ProductVariantId\", \"ExpectedQuantity\", \"ExpectedUnit\", \"PalletCount\", \"PackagesPerPallet\", \"LoosePackages\", \"CreatedAt\", \"Version\") " +
            $"VALUES ({commitment.Value.Id}, {seed.VariantSize6Id}, {quantity}, 'box', {palletCount}, {perPallet}, {loose}, '2026-10-20 08:00:00', 0)");
        if (accepted)
        {
            Assert.Equal(1, await Insert());
        }
        else
        {
            Assert.Contains("CK_SupplyCommitmentLines_PalletInput_Consistent", (await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(Insert)).Message);
        }
    }

    [Fact]
    public async Task Pallet_Input_Computes_The_Total_And_Keeps_The_Ratio_On_The_Line()
    {
        var seed = await SeedAsync();
        var result = await CreateAsync(PalletRequest(seed));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(1080m, line.ExpectedQuantity);
        Assert.Equal(9m, line.PalletCount);
        Assert.Equal(120m, line.PackagesPerPallet);

        await using var verify = _factory.CreateContext();
        var stored = await verify.SupplyCommitmentLines.SingleAsync(l => l.LotNumber == "LOT-PAL");
        Assert.Equal(1080m, stored.ExpectedQuantity);
        Assert.Equal((9m, 120m), (stored.PalletCount, stored.PackagesPerPallet));
    }

    [Fact]
    public async Task Pallet_Input_With_An_Explicit_Matching_Total_Succeeds_And_Plain_Lines_Keep_No_Ratio()
    {
        var seed = await SeedAsync();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PalletRequest(seed, quantity: 1080m))).Status);
        var plain = await CreateAsync(WarehouseRequest(seed));
        Assert.All(plain.Value.Lines, l => Assert.Null(l.PalletCount));
    }

    [Theory]
    [InlineData("total-mismatch", "lines[0].expectedQuantity")]
    [InlineData("pallets-only", "lines[0].packagesPerPallet")]
    [InlineData("per-pallet-only", "lines[0].palletCount")]
    [InlineData("pallets-zero", "lines[0].palletCount")]
    [InlineData("per-pallet-four-decimals", "lines[0].packagesPerPallet")]
    public async Task Pallet_Input_Is_Validated_And_Nothing_Is_Stored(string invalidCase, string expectedErrorKey)
    {
        var seed = await SeedAsync();
        var request = invalidCase switch
        {
            "total-mismatch" => PalletRequest(seed, quantity: 1000m),
            "pallets-only" => PalletRequest(seed, packagesPerPallet: null),
            "per-pallet-only" => PalletRequest(seed, palletCount: null),
            "pallets-zero" => PalletRequest(seed, palletCount: 0m),
            _ => PalletRequest(seed, packagesPerPallet: 120.0001m)
        };
        AssertValidation(await CreateAsync(request), expectedErrorKey);
        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.SupplyCommitments.CountAsync());
    }

    // Regressie (review #41): grote maar afzonderlijk geldige waarden geven een validatiefout, geen overflow.
    [Fact]
    public async Task Pallet_Input_Beyond_Decimal_Range_Is_A_Validation_Error_Not_An_Overflow()
    {
        var seed = await SeedAsync();
        AssertValidation(await CreateAsync(PalletRequest(seed, palletCount: 999_999_999_999_999m, packagesPerPallet: 999_999_999_999_999m)), "lines[0].expectedQuantity");
        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.SupplyCommitments.CountAsync());
    }

    [Fact]
    public async Task Retry_With_Same_Total_But_Other_Pallet_Split_Conflicts()
    {
        var seed = await SeedAsync();
        var requestId = Guid.NewGuid();
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(PalletRequest(seed, requestId: requestId))).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await CreateAsync(PalletRequest(seed, requestId: requestId))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(PalletRequest(seed, palletCount: 12m, packagesPerPallet: 90m, requestId: requestId))).Status);
    }

    private async Task<ServiceResult<SupplyCommitmentDto>> CreateAsync(CreateSupplyCommitmentDto request)
    {
        await using var db = _factory.CreateContext();
        return await CreateService(db).CreateAsync(request);
    }

    private async Task AssertNothingStoredAsync()
    {
        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.SupplyCommitments.CountAsync());
        Assert.Equal(0, await verify.SupplyCommitmentLines.CountAsync());
    }

    private static void AssertValidation(ServiceResult<SupplyCommitmentDto> result, string expectedErrorKey)
    {
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
    }

    // ---- Aanmaken ----

    [Fact]
    public async Task Create_Warehouse_Commitment_Stores_Exact_Values_As_Expected()
    {
        var seed = await SeedAsync();
        var request = WarehouseRequest(seed);

        var result = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal("Expected", result.Value.Status);
        Assert.Equal("Warehouse", result.Value.Route);
        Assert.Null(result.Value.CustomerId);

        await using var verify = _factory.CreateContext();
        var stored = await verify.SupplyCommitments.Include(c => c.Lines).SingleAsync();
        Assert.Equal(request.RequestId, stored.RequestId);
        Assert.Equal(seed.VendorId, stored.VendorId);
        Assert.Equal("Expected", stored.Status);
        Assert.Equal("Peru", stored.Origin);
        Assert.Null(stored.Destination);
        Assert.Equal(ExpectedAt.UtcDateTime, stored.ExpectedAt);

        var lines = stored.Lines.OrderBy(l => l.Id).ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal(40m, lines[0].ExpectedQuantity);
        Assert.Equal("box", lines[0].ExpectedUnit);
        Assert.Equal(160.125m, lines[0].ExpectedNetWeightKg);
        Assert.Equal("LOT-6A", lines[0].LotNumber);
        Assert.Equal(12.5m, lines[1].ExpectedQuantity);
        Assert.Equal("colli", lines[1].ExpectedUnit);
    }

    [Fact]
    public async Task Create_Does_Not_Infer_Missing_NetWeight_Or_Lot()
    {
        var seed = await SeedAsync();

        await CreateAsync(WarehouseRequest(seed));

        await using var verify = _factory.CreateContext();
        var line = await verify.SupplyCommitmentLines.SingleAsync(l => l.ProductVariantId == seed.VariantSize8Id);
        Assert.Null(line.ExpectedNetWeightKg);
        Assert.Null(line.LotNumber);
    }

    [Fact]
    public async Task Create_Converts_ExpectedAt_To_Utc()
    {
        var seed = await SeedAsync();

        var result = await CreateAsync(WarehouseRequest(seed));

        // Npgsql weigert een niet-UTC DateTime voor timestamptz; SQLite controleert dat niet, dus expliciet toetsen.
        Assert.Equal(DateTimeKind.Utc, result.Value.ExpectedAt!.Value.Kind);
        Assert.Equal(new DateTime(2026, 11, 2, 8, 30, 0, DateTimeKind.Utc), result.Value.ExpectedAt);
    }

    [Fact]
    public async Task Create_Allows_ExpectedAt_In_The_Past()
    {
        var seed = await SeedAsync();
        var request = WarehouseRequest(seed);
        request.ExpectedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
    }

    [Fact]
    public async Task Create_DirectTrade_Commitment_Keeps_Route_Customer_And_Destination()
    {
        var seed = await SeedAsync();

        var result = await CreateAsync(DirectTradeRequest(seed));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal("DirectTrade", result.Value.Route);
        Assert.Equal(seed.CustomerId, result.Value.CustomerId);
        Assert.Equal("Customer T", result.Value.CustomerName);
        Assert.Equal("Turkey", result.Value.Destination);
    }

    [Fact]
    public async Task Create_Trims_Text_Fields()
    {
        var seed = await SeedAsync();
        var request = DirectTradeRequest(seed);
        request.Origin = "  Kenya ";
        request.Destination = " Turkey  ";
        request.Lines[0].LotNumber = "  LOT-K1 ";

        await CreateAsync(request);

        await using var verify = _factory.CreateContext();
        var stored = await verify.SupplyCommitments.Include(c => c.Lines).SingleAsync();
        Assert.Equal("Kenya", stored.Origin);
        Assert.Equal("Turkey", stored.Destination);
        Assert.Equal("LOT-K1", stored.Lines.Single().LotNumber);
    }

    [Fact]
    public async Task Create_Has_No_Stock_Or_Legacy_Side_Effects()
    {
        var seed = await SeedAsync();

        await CreateAsync(WarehouseRequest(seed));
        await CreateAsync(DirectTradeRequest(seed));

        await using var verify = _factory.CreateContext();
        Assert.Equal(2, await verify.SupplyCommitments.CountAsync());
        Assert.Equal(0, await verify.Inventory.CountAsync());
        Assert.Equal(0, await verify.Shipments.CountAsync());
        Assert.Equal(0, await verify.StockReceipts.CountAsync());
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.StockReservations.CountAsync());
        Assert.Equal(0, await verify.Pallets.CountAsync());
    }

    // ---- Validatie ----

    [Theory]
    [InlineData("missing-request-id", "requestId")]
    [InlineData("empty-request-id", "requestId")]
    [InlineData("route-lowercase", "route")]
    [InlineData("route-unknown", "route")]
    [InlineData("route-empty", "route")]
    [InlineData("route-padded", "route")]
    [InlineData("origin-missing", "origin")]
    [InlineData("origin-blank", "origin")]
    [InlineData("origin-too-long", "origin")]
    [InlineData("destination-blank", "destination")]
    [InlineData("destination-too-long", "destination")]
    [InlineData("expected-at-missing", "expectedAt")]
    [InlineData("warehouse-with-customer", "customerId")]
    [InlineData("lines-missing", "lines")]
    [InlineData("lines-empty", "lines")]
    [InlineData("lines-too-many", "lines")]
    public async Task Create_Rejects_Invalid_Commitment_Fields(string invalidCase, string expectedErrorKey)
    {
        var seed = await SeedAsync();
        var request = WarehouseRequest(seed);

        switch (invalidCase)
        {
            case "missing-request-id": request.RequestId = null; break;
            case "empty-request-id": request.RequestId = Guid.Empty; break;
            case "route-lowercase": request.Route = "warehouse"; break;
            case "route-unknown": request.Route = "Direct"; break;
            case "route-empty": request.Route = ""; break;
            case "route-padded": request.Route = " Warehouse"; break;
            case "origin-missing": request.Origin = null; break;
            case "origin-blank": request.Origin = "   "; break;
            case "origin-too-long": request.Origin = new string('x', 201); break;
            case "destination-blank": request.Destination = "  "; break;
            case "destination-too-long": request.Destination = new string('x', 201); break;
            case "expected-at-missing": request.ExpectedAt = null; break;
            case "warehouse-with-customer": request.CustomerId = seed.CustomerId; break;
            case "lines-missing": request.Lines = null; break;
            case "lines-empty": request.Lines = new List<CreateSupplyCommitmentLineDto>(); break;
            case "lines-too-many":
                request.Lines = Enumerable.Range(0, 201)
                    .Select(i => new CreateSupplyCommitmentLineDto { ProductVariantId = seed.VariantSize6Id, ExpectedQuantity = 1m, ExpectedUnit = "box", LotNumber = $"LOT-{i}" })
                    .ToList();
                break;
        }

        AssertValidation(await CreateAsync(request), expectedErrorKey);
        await AssertNothingStoredAsync();
    }

    [Theory]
    [InlineData("no-customer")]
    [InlineData("no-destination")]
    [InlineData("blank-destination")]
    public async Task Create_DirectTrade_Requires_Customer_And_Destination(string invalidCase)
    {
        var seed = await SeedAsync();
        var request = DirectTradeRequest(seed);
        string expectedErrorKey;

        switch (invalidCase)
        {
            case "no-customer": request.CustomerId = null; expectedErrorKey = "customerId"; break;
            case "no-destination": request.Destination = null; expectedErrorKey = "destination"; break;
            default: request.Destination = "   "; expectedErrorKey = "destination"; break;
        }

        AssertValidation(await CreateAsync(request), expectedErrorKey);
        await AssertNothingStoredAsync();
    }

    [Theory]
    [InlineData("quantity-missing", "lines[1].expectedQuantity")]
    [InlineData("quantity-zero", "lines[1].expectedQuantity")]
    [InlineData("quantity-negative", "lines[1].expectedQuantity")]
    [InlineData("quantity-four-decimals", "lines[1].expectedQuantity")]
    [InlineData("quantity-too-large", "lines[1].expectedQuantity")]
    [InlineData("unit-missing", "lines[1].expectedUnit")]
    [InlineData("unit-empty", "lines[1].expectedUnit")]
    [InlineData("unit-kg", "lines[1].expectedUnit")]
    [InlineData("unit-capitalised", "lines[1].expectedUnit")]
    [InlineData("unit-pallet", "lines[1].expectedUnit")]
    [InlineData("unit-padded", "lines[1].expectedUnit")]
    [InlineData("weight-zero", "lines[1].expectedNetWeightKg")]
    [InlineData("weight-negative", "lines[1].expectedNetWeightKg")]
    [InlineData("weight-four-decimals", "lines[1].expectedNetWeightKg")]
    [InlineData("lot-blank", "lines[1].lotNumber")]
    [InlineData("lot-too-long", "lines[1].lotNumber")]
    [InlineData("line-null", "lines[1]")]
    [InlineData("duplicate-variant-and-lot", "lines[1]")]
    public async Task Create_Rejects_Invalid_Line_Fields(string invalidCase, string expectedErrorKey)
    {
        var seed = await SeedAsync();
        var request = WarehouseRequest(seed);
        var line = request.Lines[1];

        switch (invalidCase)
        {
            case "quantity-missing": line.ExpectedQuantity = null; break;
            case "quantity-zero": line.ExpectedQuantity = 0m; break;
            case "quantity-negative": line.ExpectedQuantity = -1m; break;
            case "quantity-four-decimals": line.ExpectedQuantity = 1.2345m; break;
            case "quantity-too-large": line.ExpectedQuantity = 1_000_000_000_000_000m; break;
            case "unit-missing": line.ExpectedUnit = null; break;
            case "unit-empty": line.ExpectedUnit = ""; break;
            case "unit-kg": line.ExpectedUnit = "kg"; break;
            case "unit-capitalised": line.ExpectedUnit = "Box"; break;
            case "unit-pallet": line.ExpectedUnit = "pallet"; break;
            case "unit-padded": line.ExpectedUnit = "box "; break;
            case "weight-zero": line.ExpectedNetWeightKg = 0m; break;
            case "weight-negative": line.ExpectedNetWeightKg = -1m; break;
            case "weight-four-decimals": line.ExpectedNetWeightKg = 1.2345m; break;
            case "lot-blank": line.LotNumber = "   "; break;
            case "lot-too-long": line.LotNumber = new string('x', 101); break;
            case "line-null": request.Lines[1] = null; break;
            case "duplicate-variant-and-lot":
                line.ProductVariantId = request.Lines[0].ProductVariantId;
                line.LotNumber = " " + request.Lines[0].LotNumber;
                break;
        }

        AssertValidation(await CreateAsync(request), expectedErrorKey);
        await AssertNothingStoredAsync();
    }

    [Fact]
    public async Task Create_Allows_Same_Variant_With_Different_Or_Unknown_Lots()
    {
        var seed = await SeedAsync();
        var request = WarehouseRequest(seed);
        request.Lines = new List<CreateSupplyCommitmentLineDto>
        {
            new() { ProductVariantId = seed.VariantSize6Id, ExpectedQuantity = 1m, ExpectedUnit = "box", LotNumber = "LOT-A" },
            new() { ProductVariantId = seed.VariantSize6Id, ExpectedQuantity = 2m, ExpectedUnit = "box", LotNumber = "LOT-B" },
            new() { ProductVariantId = seed.VariantSize6Id, ExpectedQuantity = 3m, ExpectedUnit = "box" },
            new() { ProductVariantId = seed.VariantSize6Id, ExpectedQuantity = 4m, ExpectedUnit = "tray" }
        };

        var result = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(4, result.Value.Lines.Count);
    }

    [Fact]
    public async Task Create_Reports_All_Validation_Errors_At_Once()
    {
        var seed = await SeedAsync();
        var request = WarehouseRequest(seed);
        request.Origin = " ";
        request.Lines[0].ExpectedUnit = "kg";
        request.Lines[1].ExpectedQuantity = 0m;

        var result = await CreateAsync(request);

        AssertValidation(result, "origin");
        AssertValidation(result, "lines[0].expectedUnit");
        AssertValidation(result, "lines[1].expectedQuantity");
    }

    // ---- Niet gevonden ----

    [Theory]
    [InlineData("vendor")]
    [InlineData("customer")]
    [InlineData("variant")]
    public async Task Create_Returns_NotFound_For_Unknown_References_And_Stores_Nothing(string missing)
    {
        var seed = await SeedAsync();
        var request = DirectTradeRequest(seed);
        request.Lines.Add(new CreateSupplyCommitmentLineDto { ProductVariantId = seed.VariantSize8Id, ExpectedQuantity = 2m, ExpectedUnit = "box" });

        switch (missing)
        {
            case "vendor": request.VendorId = 999_999; break;
            case "customer": request.CustomerId = 999_999; break;
            case "variant": request.Lines[1].ProductVariantId = 999_999; break;
        }

        var result = await CreateAsync(request);

        Assert.Equal(ServiceResultStatus.NotFound, result.Status);
        await AssertNothingStoredAsync();
    }

    // ---- Idempotentie ----

    [Fact]
    public async Task Exact_Retry_Returns_Same_Commitment_Without_New_Rows()
    {
        var seed = await SeedAsync();
        var requestId = Guid.NewGuid();

        var first = await CreateAsync(WarehouseRequest(seed, requestId));
        var retry = await CreateAsync(WarehouseRequest(seed, requestId));

        Assert.Equal(ServiceResultStatus.Created, first.Status);
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal(first.Value.Lines.Select(l => l.Id), retry.Value.Lines.Select(l => l.Id));

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.SupplyCommitments.CountAsync());
        Assert.Equal(2, await verify.SupplyCommitmentLines.CountAsync());
    }

    [Fact]
    public async Task Retry_With_Equivalent_Whitespace_Decimals_And_Offset_Is_Treated_As_Same()
    {
        var seed = await SeedAsync();
        var requestId = Guid.NewGuid();
        await CreateAsync(WarehouseRequest(seed, requestId));

        var retry = WarehouseRequest(seed, requestId);
        retry.Origin = " Peru ";
        retry.ExpectedAt = ExpectedAt.ToOffset(TimeSpan.Zero);
        retry.Lines[0].ExpectedQuantity = 40.000m;
        retry.Lines[0].ExpectedNetWeightKg = 160.1250m;
        retry.Lines[0].LotNumber = "LOT-6A ";

        var result = await CreateAsync(retry);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.SupplyCommitments.CountAsync());
    }

    [Fact]
    public async Task Exact_DirectTrade_Retry_Returns_Same_Commitment()
    {
        var seed = await SeedAsync();
        var requestId = Guid.NewGuid();

        var first = await CreateAsync(DirectTradeRequest(seed, requestId));
        var retry = await CreateAsync(DirectTradeRequest(seed, requestId));

        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);
    }

    [Theory]
    [InlineData("vendor")]
    [InlineData("route-and-customer")]
    [InlineData("origin")]
    [InlineData("destination")]
    [InlineData("expected-at")]
    [InlineData("quantity")]
    [InlineData("unit")]
    [InlineData("weight-added")]
    [InlineData("weight-changed")]
    [InlineData("lot")]
    [InlineData("variant")]
    [InlineData("extra-line")]
    [InlineData("missing-line")]
    [InlineData("line-order")]
    public async Task Reused_RequestId_With_Different_Content_Is_Conflict_And_Changes_Nothing(string change)
    {
        var seed = await SeedAsync();
        var requestId = Guid.NewGuid();
        var first = await CreateAsync(WarehouseRequest(seed, requestId));

        var retry = WarehouseRequest(seed, requestId);
        switch (change)
        {
            case "vendor": retry.VendorId = seed.OtherVendorId; break;
            case "route-and-customer":
                retry.Route = "DirectTrade";
                retry.CustomerId = seed.CustomerId;
                retry.Destination = "Turkey";
                break;
            case "origin": retry.Origin = "Chile"; break;
            case "destination": retry.Destination = "Rotterdam"; break;
            case "expected-at": retry.ExpectedAt = ExpectedAt.AddDays(1); break;
            case "quantity": retry.Lines[0].ExpectedQuantity = 41m; break;
            case "unit": retry.Lines[0].ExpectedUnit = "colli"; break;
            case "weight-added": retry.Lines[1].ExpectedNetWeightKg = 50m; break;
            case "weight-changed": retry.Lines[0].ExpectedNetWeightKg = 160.126m; break;
            case "lot": retry.Lines[0].LotNumber = "LOT-6B"; break;
            case "variant": retry.Lines[1].ProductVariantId = seed.VariantSize6Id; break;
            case "extra-line": retry.Lines.Add(new CreateSupplyCommitmentLineDto { ProductVariantId = seed.VariantSize8Id, ExpectedQuantity = 1m, ExpectedUnit = "tray" }); break;
            case "missing-line": retry.Lines.RemoveAt(1); break;
            case "line-order": retry.Lines.Reverse(); break;
        }

        var result = await CreateAsync(retry);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);

        await using var verify = _factory.CreateContext();
        var stored = await verify.SupplyCommitments.Include(c => c.Lines).SingleAsync();
        Assert.Equal(first.Value.Id, stored.Id);
        Assert.Equal(seed.VendorId, stored.VendorId);
        Assert.Equal("Warehouse", stored.Route);
        Assert.Equal(2, stored.Lines.Count);
        Assert.Equal(40m, stored.Lines.Single(l => l.ProductVariantId == seed.VariantSize6Id).ExpectedQuantity);
    }

    [Fact]
    public async Task Reused_RequestId_With_Different_Customer_Is_Conflict()
    {
        var seed = await SeedAsync();
        var requestId = Guid.NewGuid();
        await CreateAsync(DirectTradeRequest(seed, requestId));

        var retry = DirectTradeRequest(seed, requestId);
        retry.CustomerId = seed.OtherCustomerId;

        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(retry)).Status);
    }

    // ---- Lezen ----

    [Fact]
    public async Task Get_Returns_Commitment_With_Lines_And_Names()
    {
        var seed = await SeedAsync();
        var created = await CreateAsync(DirectTradeRequest(seed));

        await using var db = _factory.CreateContext();
        var result = await CreateService(db).GetAsync(created.Value.Id);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var dto = result.Value;
        Assert.Equal("Supplier B", dto.VendorName);
        Assert.Equal("Customer T", dto.CustomerName);
        Assert.Equal("DirectTrade", dto.Route);
        Assert.Equal(DateTimeKind.Utc, dto.ExpectedAt!.Value.Kind);
        var line = Assert.Single(dto.Lines);
        Assert.Equal(seed.VariantSize6Id, line.ProductVariantId);
        Assert.Equal("Avocado", line.ProductName);
        Assert.Equal("Hass 6", line.ProductVariantName);
        Assert.Equal(1m, line.ExpectedQuantity);
        Assert.Equal("box", line.ExpectedUnit);
        Assert.Equal(4.2m, line.ExpectedNetWeightKg);
    }

    [Fact]
    public async Task Get_Unknown_Id_Is_NotFound()
    {
        await using var db = _factory.CreateContext();
        var result = await CreateService(db).GetAsync(999_999);

        Assert.Equal(ServiceResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task List_Filters_Keep_DirectTrade_And_Warehouse_Separate()
    {
        var seed = await SeedAsync();
        var warehouse = await CreateAsync(WarehouseRequest(seed));
        var directTrade = await CreateAsync(DirectTradeRequest(seed));
        var otherVendorRequest = WarehouseRequest(seed);
        otherVendorRequest.VendorId = seed.OtherVendorId;
        var otherVendor = await CreateAsync(otherVendorRequest);

        await using var db = _factory.CreateContext();
        var service = CreateService(db);

        var all = await service.ListAsync(new SupplyCommitmentFilter());
        Assert.Equal(3, all.Value.Count);

        var direct = await service.ListAsync(new SupplyCommitmentFilter { Route = "DirectTrade" });
        Assert.Equal(new[] { directTrade.Value.Id }, direct.Value.Select(c => c.Id));

        var warehouseOnly = await service.ListAsync(new SupplyCommitmentFilter { Route = "Warehouse" });
        Assert.Equal(new[] { warehouse.Value.Id, otherVendor.Value.Id }.OrderBy(id => id), warehouseOnly.Value.Select(c => c.Id).OrderBy(id => id));

        var byVendor = await service.ListAsync(new SupplyCommitmentFilter { VendorId = seed.OtherVendorId });
        Assert.Equal(new[] { otherVendor.Value.Id }, byVendor.Value.Select(c => c.Id));

        var byCustomer = await service.ListAsync(new SupplyCommitmentFilter { CustomerId = seed.CustomerId });
        Assert.Equal(new[] { directTrade.Value.Id }, byCustomer.Value.Select(c => c.Id));

        var byOtherCustomer = await service.ListAsync(new SupplyCommitmentFilter { CustomerId = seed.OtherCustomerId });
        Assert.Empty(byOtherCustomer.Value);

        var expected = await service.ListAsync(new SupplyCommitmentFilter { Status = "Expected" });
        Assert.Equal(3, expected.Value.Count);

        var received = await service.ListAsync(new SupplyCommitmentFilter { Status = "Received" });
        Assert.Empty(received.Value);
    }

    [Fact]
    public async Task List_Is_Ordered_By_ExpectedAt_Then_Id()
    {
        var seed = await SeedAsync();
        var later = WarehouseRequest(seed);
        later.ExpectedAt = ExpectedAt.AddDays(2);
        var laterId = (await CreateAsync(later)).Value.Id;
        var earlierId = (await CreateAsync(WarehouseRequest(seed))).Value.Id;
        var sameTimeId = (await CreateAsync(WarehouseRequest(seed))).Value.Id;

        await using var db = _factory.CreateContext();
        var result = await CreateService(db).ListAsync(new SupplyCommitmentFilter());

        Assert.Equal(new[] { earlierId, sameTimeId, laterId }, result.Value.Select(c => c.Id));
    }

    [Theory]
    [InlineData("warehouse", null)]
    [InlineData("Direct", null)]
    [InlineData(null, "expected")]
    [InlineData(null, "Open")]
    public async Task List_Rejects_Unknown_Filter_Values(string route, string status)
    {
        await using var db = _factory.CreateContext();
        var result = await CreateService(db).ListAsync(new SupplyCommitmentFilter { Route = route, Status = status });

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
    }
}
