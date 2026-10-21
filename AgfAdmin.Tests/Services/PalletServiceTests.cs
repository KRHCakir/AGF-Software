using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

public class PalletServiceTests : IDisposable
{
    private const string ValidSscc = "006141411234567890";
    private const string OtherValidSscc = "106141411234567897";

    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    // Bronregels: W = Warehouse (eenheid box), L = legacy Warehouse zonder eenheid,
    // D1/D2 = DirectTrade voor klant 1/2, X = geannuleerde Warehouse-toezegging.
    private sealed record Seed(int WarehouseLine, int LegacyLine, int DirectLineCustomer1, int DirectLineCustomer2, int CancelledLine, int CustomerId1);

    private async Task<Seed> SeedAsync()
    {
        await using var db = _factory.CreateContext();

        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass 6", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.50m };
        var vendor = new Vendor { Name = "Supplier B" };
        var customer1 = new Customer { Name = "Customer T" };
        var customer2 = new Customer { Name = "Customer U" };

        SupplyCommitment Commitment(string route, Customer customer, string status = "Expected") => new()
        {
            RequestId = Guid.NewGuid(),
            Vendor = vendor,
            Customer = customer,
            Route = route,
            Status = status,
            Origin = "Kenya",
            Destination = customer == null ? null : "Turkey",
            ExpectedAt = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc)
        };

        SupplyCommitmentLine Line(SupplyCommitment commitment, string unit) => new()
        {
            SupplyCommitment = commitment,
            ProductVariant = variant,
            ExpectedQuantity = 100m,
            ExpectedUnit = unit,
            ExpectedNetWeightKg = 400m,
            LotNumber = "PROVISIONAL"
        };

        var warehouse = Commitment("Warehouse", null);
        var direct1 = Commitment("DirectTrade", customer1);
        var direct2 = Commitment("DirectTrade", customer2);
        var cancelled = Commitment("Warehouse", null, "Cancelled");

        var w = Line(warehouse, "box");
        var legacy = Line(warehouse, null);
        var d1 = Line(direct1, "box");
        var d2 = Line(direct2, "box");
        var x = Line(cancelled, "box");

        db.AddRange(variant, vendor, customer1, customer2, warehouse, direct1, direct2, cancelled, w, legacy, d1, d2, x);
        await db.SaveChangesAsync();

        return new Seed(w.Id, legacy.Id, d1.Id, d2.Id, x.Id, customer1.Id);
    }

    private static PalletService CreateService(AppDbContext db) => new(db);

    private static CreatePalletDto PalletRequest(string reference = "PAL-001") => new()
    {
        Reference = reference,
        Sscc = ValidSscc,
        PalletType = "EUR",
        TareKg = 25m,
        GrossKg = 425.5m,
        DeclaredNetKg = 400.25m,
        LogisticsStatus = "Received",
        LocationContext = "Warehouse",
        WarehouseLocation = "COLD-01",
        ActorReference = "office-1",
        Reason = "Arrival scan"
    };

    private static CreatePalletDto SupplierPalletRequest(string reference = "PAL-SUP") => new()
    {
        Reference = reference,
        LogisticsStatus = "Expected",
        LocationContext = "Supplier",
        ActorReference = "office-1"
    };

    private static AddPalletContentLineDto ContentRequest(int sourceLineId, Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        SupplyCommitmentLineId = sourceLineId,
        LotNumber = "LOT-6A",
        Quantity = 40m,
        Unit = "box",
        NetWeightKg = 160.125m,
        ActorReference = "warehouse-1",
        Reason = "Counted on arrival"
    };

    private async Task<ServiceResult<PalletDto>> CreatePalletAsync(CreatePalletDto request)
    {
        await using var db = _factory.CreateContext();
        return await CreateService(db).CreateAsync(request);
    }

    private async Task<ServiceResult<PalletContentLineDto>> AddContentAsync(int palletId, AddPalletContentLineDto request)
    {
        await using var db = _factory.CreateContext();
        return await CreateService(db).AddContentLineAsync(palletId, request);
    }

    private async Task<int> CreatedPalletIdAsync(CreatePalletDto request)
    {
        var result = await CreatePalletAsync(request);
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value.Id;
    }

    private static void AssertValidation<T>(ServiceResult<T> result, string expectedErrorKey)
    {
        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
    }

    private async Task<(int Pallets, int ContentLines, int AuditEvents)> CountsAsync()
    {
        await using var verify = _factory.CreateContext();
        return (await verify.Pallets.CountAsync(), await verify.PalletContentLines.CountAsync(), await verify.PalletAuditEvents.CountAsync());
    }

    // ---- Pallet aanmaken ----

    [Fact]
    public async Task Create_Pallet_Stores_Exact_Values_And_PalletCreated_Event()
    {
        var result = await CreatePalletAsync(PalletRequest());

        Assert.Equal(ServiceResultStatus.Created, result.Status);

        await using var verify = _factory.CreateContext();
        var pallet = await verify.Pallets.SingleAsync();
        Assert.Equal("PAL-001", pallet.Reference);
        Assert.Equal(ValidSscc, pallet.Sscc);
        Assert.Equal("EUR", pallet.PalletType);
        Assert.Equal(25m, pallet.TareKg);
        Assert.Equal(425.5m, pallet.GrossKg);
        Assert.Equal(400.25m, pallet.DeclaredNetKg);
        Assert.Equal("Received", pallet.LogisticsStatus);
        Assert.Equal("Warehouse", pallet.LocationContext);
        Assert.Equal("COLD-01", pallet.WarehouseLocation);
        Assert.Null(pallet.StockReceiptId);

        var auditEvent = await verify.PalletAuditEvents.SingleAsync();
        Assert.Equal(pallet.Id, auditEvent.PalletId);
        Assert.Null(auditEvent.PalletContentLineId);
        Assert.Equal("PalletCreated", auditEvent.EventType);
        Assert.Null(auditEvent.OldValue);
        Assert.Contains("PAL-001", auditEvent.NewValue);
        Assert.Contains("Received", auditEvent.NewValue);
        Assert.Equal("office-1", auditEvent.ActorReference);
        Assert.Equal("Arrival scan", auditEvent.Reason);
        Assert.True((DateTime.UtcNow - auditEvent.OccurredAt).Duration() < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Create_Pallet_Without_Sscc_Never_Generates_One()
    {
        var result = await CreatePalletAsync(SupplierPalletRequest());

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Null(result.Value.Sscc);
        await using var verify = _factory.CreateContext();
        Assert.Null((await verify.Pallets.SingleAsync()).Sscc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_Pallet_Normalises_Blank_WarehouseLocation_To_Null(string warehouseLocation)
    {
        var request = PalletRequest();
        request.WarehouseLocation = warehouseLocation;

        var result = await CreatePalletAsync(request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        await using var verify = _factory.CreateContext();
        Assert.Null((await verify.Pallets.SingleAsync()).WarehouseLocation);
    }

    [Theory]
    [InlineData("Expected", "Supplier")]
    [InlineData("InTransit", "InTransit")]
    [InlineData("InTransit", "Crossdock")]
    [InlineData("Expected", "External")]
    [InlineData("Received", "Customer")]
    [InlineData("Received", "Warehouse")]
    public async Task Create_Pallet_Accepts_Initial_Status_And_Location(string status, string location)
    {
        var request = SupplierPalletRequest();
        request.LogisticsStatus = status;
        request.LocationContext = location;

        Assert.Equal(ServiceResultStatus.Created, (await CreatePalletAsync(request)).Status);
    }

    // ---- Eigen palletnummer optioneel: servergegenereerd nummer, idempotent via RequestId ----

    private static CreatePalletDto PalletRequestWithoutOwnNumber(Guid requestId) => new()
    {
        RequestId = requestId,
        Reference = "   ",
        LogisticsStatus = "Received",
        LocationContext = "Warehouse",
        WarehouseLocation = "DOCK-1",
        ActorReference = null
    };

    [Fact]
    public async Task Create_Pallet_Without_Own_Number_Generates_Recognisable_Unique_Reference()
    {
        var result = await CreatePalletAsync(PalletRequestWithoutOwnNumber(Guid.NewGuid()));

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal(PalletService.GeneratedReference(result.Value.Id), result.Value.Reference);
        Assert.Matches("^P-[0-9]{6}$", result.Value.Reference);

        await using var verify = _factory.CreateContext();
        var pallet = await verify.Pallets.SingleAsync();
        Assert.Equal(result.Value.Reference, pallet.Reference);
        Assert.NotNull(pallet.RequestId);
        var auditEvent = await verify.PalletAuditEvents.SingleAsync();
        Assert.Contains(result.Value.Reference, auditEvent.NewValue);
        Assert.Contains("ReferenceGenerated", auditEvent.NewValue, StringComparison.OrdinalIgnoreCase);
        Assert.Null(auditEvent.ActorReference);
        Assert.Equal(pallet.RequestId, auditEvent.RequestId);
    }

    [Fact]
    public async Task Two_Pallets_Without_Own_Number_Get_Different_Generated_References()
    {
        var first = await CreatePalletAsync(PalletRequestWithoutOwnNumber(Guid.NewGuid()));
        var second = await CreatePalletAsync(PalletRequestWithoutOwnNumber(Guid.NewGuid()));

        Assert.Equal(ServiceResultStatus.Created, second.Status);
        Assert.NotEqual(first.Value.Reference, second.Value.Reference);
        Assert.Equal((2, 0, 2), await CountsAsync());
    }

    [Fact]
    public async Task Identical_Retry_Without_Own_Number_Returns_Same_Pallet_And_Creates_Nothing()
    {
        var requestId = Guid.NewGuid();
        var first = await CreatePalletAsync(PalletRequestWithoutOwnNumber(requestId));
        var retry = await CreatePalletAsync(PalletRequestWithoutOwnNumber(requestId));

        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal(first.Value.Reference, retry.Value.Reference);
        Assert.Equal((1, 0, 1), await CountsAsync());
    }

    [Fact]
    public async Task Retry_With_Same_RequestId_But_Different_Content_Conflicts()
    {
        var requestId = Guid.NewGuid();
        await CreatePalletAsync(PalletRequestWithoutOwnNumber(requestId));
        var changed = PalletRequestWithoutOwnNumber(requestId);
        changed.WarehouseLocation = "DOCK-2";

        Assert.Equal(ServiceResultStatus.Conflict, (await CreatePalletAsync(changed)).Status);
        Assert.Equal((1, 0, 1), await CountsAsync());
    }

    [Fact]
    public async Task Own_Number_With_RequestId_Is_Kept_And_Replayed_On_RequestId()
    {
        var requestId = Guid.NewGuid();
        var request = PalletRequest("EIGEN-7");
        request.RequestId = requestId;
        var first = await CreatePalletAsync(request);
        Assert.Equal("EIGEN-7", first.Value.Reference);

        var retry = PalletRequest("EIGEN-7");
        retry.RequestId = requestId;
        Assert.Equal(ServiceResultStatus.Ok, (await CreatePalletAsync(retry)).Status);

        // Zelfde RequestId met een ander eigen nummer is een ander verzoek: conflict, geen tweede pallet.
        var other = PalletRequest("EIGEN-8");
        other.RequestId = requestId;
        Assert.Equal(ServiceResultStatus.Conflict, (await CreatePalletAsync(other)).Status);
        Assert.Equal((1, 0, 1), await CountsAsync());
    }

    [Fact]
    public async Task Receipt_Without_Actor_Stores_Null_Actor_And_Never_Invents_One()
    {
        var seed = await SeedAsync();
        var palletRequest = PalletRequest();
        palletRequest.ActorReference = " ";
        var palletId = await CreatedPalletIdAsync(palletRequest);
        var content = ContentRequest(seed.WarehouseLine);
        content.ActorReference = null;

        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(palletId, content)).Status);

        await using var verify = _factory.CreateContext();
        var events = await verify.PalletAuditEvents.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Null(e.ActorReference));
    }

    [Fact]
    public async Task Quality_Decision_Still_Requires_Actor()
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        var line = (await AddContentAsync(palletId, ContentRequest(seed.WarehouseLine))).Value;

        await using var db = _factory.CreateContext();
        var result = await CreateService(db).ChangeQualityStatusAsync(palletId, line.Id, new ChangeQualityStatusDto
        {
            RequestId = Guid.NewGuid(),
            ExpectedContentLineVersion = line.Version,
            ToStatus = "Approved",
            ActorReference = "  "
        });

        AssertValidation(result, "actorReference");
    }

    [Fact]
    public async Task Exact_Pallet_Retry_Returns_Same_Pallet_Without_New_Event()
    {
        var first = await CreatePalletAsync(PalletRequest());

        var retryRequest = PalletRequest();
        retryRequest.Reference = " PAL-001 ";
        retryRequest.GrossKg = 425.50m;
        retryRequest.ActorReference = "office-2";
        var retry = await CreatePalletAsync(retryRequest);

        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal((1, 0, 1), await CountsAsync());
    }

    [Theory]
    [InlineData("sscc")]
    [InlineData("no-sscc")]
    [InlineData("type")]
    [InlineData("tare")]
    [InlineData("gross")]
    [InlineData("declared")]
    [InlineData("status")]
    [InlineData("location")]
    [InlineData("bin")]
    public async Task Reused_Reference_With_Different_Content_Is_Conflict(string change)
    {
        await CreatePalletAsync(PalletRequest());

        var request = PalletRequest();
        switch (change)
        {
            case "sscc": request.Sscc = OtherValidSscc; break;
            case "no-sscc": request.Sscc = null; break;
            case "type": request.PalletType = "BLOK"; break;
            case "tare": request.TareKg = 24m; break;
            case "gross": request.GrossKg = 430m; break;
            case "declared": request.DeclaredNetKg = null; break;
            case "status": request.LocationContext = "Supplier"; request.WarehouseLocation = null; request.LogisticsStatus = "Expected"; break;
            case "location": request.LocationContext = "Customer"; request.WarehouseLocation = null; break;
            case "bin": request.WarehouseLocation = "COLD-02"; break;
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await CreatePalletAsync(request)).Status);
        Assert.Equal((1, 0, 1), await CountsAsync());
    }

    [Fact]
    public async Task Sscc_Of_Another_Pallet_Is_Conflict()
    {
        await CreatePalletAsync(PalletRequest("PAL-001"));

        var result = await CreatePalletAsync(PalletRequest("PAL-002"));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal((1, 0, 1), await CountsAsync());
    }

    [Theory]
    [InlineData("reference-too-long", "reference")]
    [InlineData("reference-missing-without-request-id", "requestId")]
    [InlineData("request-id-empty", "requestId")]
    [InlineData("sscc-check-digit", "sscc")]
    [InlineData("sscc-17-digits", "sscc")]
    [InlineData("sscc-letters", "sscc")]
    [InlineData("sscc-empty", "sscc")]
    [InlineData("sscc-spaces", "sscc")]
    [InlineData("type-blank", "palletType")]
    [InlineData("type-too-long", "palletType")]
    [InlineData("status-missing", "logisticsStatus")]
    [InlineData("status-dispatched", "logisticsStatus")]
    [InlineData("status-delivered", "logisticsStatus")]
    [InlineData("status-cancelled", "logisticsStatus")]
    [InlineData("status-lowercase", "logisticsStatus")]
    [InlineData("location-missing", "locationContext")]
    [InlineData("location-unknown", "locationContext")]
    [InlineData("warehouse-expected", "locationContext")]
    [InlineData("warehouse-in-transit", "locationContext")]
    [InlineData("bin-at-supplier", "warehouseLocation")]
    [InlineData("bin-too-long", "warehouseLocation")]
    [InlineData("tare-zero", "tareKg")]
    [InlineData("tare-four-decimals", "tareKg")]
    [InlineData("gross-negative", "grossKg")]
    [InlineData("gross-too-large", "grossKg")]
    [InlineData("declared-zero", "declaredNetKg")]
    [InlineData("gross-below-tare", "grossKg")]
    [InlineData("actor-too-long", "actorReference")]
    [InlineData("reason-too-long", "reason")]
    public async Task Create_Pallet_Rejects_Invalid_Input_And_Stores_Nothing(string invalidCase, string expectedErrorKey)
    {
        var request = PalletRequest();
        switch (invalidCase)
        {
            case "reference-missing-without-request-id": request.Reference = null; request.RequestId = null; break;
            case "request-id-empty": request.RequestId = Guid.Empty; break;
            case "reference-too-long": request.Reference = new string('R', 65); break;
            case "sscc-check-digit": request.Sscc = "006141411234567891"; break;
            case "sscc-17-digits": request.Sscc = "00614141123456789"; break;
            case "sscc-letters": request.Sscc = "00614141123456789A"; break;
            case "sscc-empty": request.Sscc = ""; break;
            case "sscc-spaces": request.Sscc = " 006141411234567890"; break;
            case "type-blank": request.PalletType = "  "; break;
            case "type-too-long": request.PalletType = new string('T', 33); break;
            case "status-missing": request.LogisticsStatus = null; break;
            case "status-dispatched": request.LogisticsStatus = "Dispatched"; break;
            case "status-delivered": request.LogisticsStatus = "Delivered"; break;
            case "status-cancelled": request.LogisticsStatus = "Cancelled"; break;
            case "status-lowercase": request.LogisticsStatus = "received"; break;
            case "location-missing": request.LocationContext = null; break;
            case "location-unknown": request.LocationContext = "Truck"; break;
            case "warehouse-expected": request.LogisticsStatus = "Expected"; request.WarehouseLocation = null; break;
            case "warehouse-in-transit": request.LogisticsStatus = "InTransit"; request.WarehouseLocation = null; break;
            case "bin-at-supplier": request.LocationContext = "Supplier"; break;
            case "bin-too-long": request.WarehouseLocation = new string('B', 65); break;
            case "tare-zero": request.TareKg = 0m; break;
            case "tare-four-decimals": request.TareKg = 25.0001m; break;
            case "gross-negative": request.GrossKg = -1m; break;
            case "gross-too-large": request.GrossKg = 1_000_000_000_000_000m; break;
            case "declared-zero": request.DeclaredNetKg = 0m; break;
            case "gross-below-tare": request.GrossKg = 24.999m; break;
            case "actor-too-long": request.ActorReference = new string('A', 101); break;
            case "reason-too-long": request.Reason = new string('x', 501); break;
        }

        AssertValidation(await CreatePalletAsync(request), expectedErrorKey);
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task WarehouseLocation_Requires_Warehouse_And_Received()
    {
        var request = SupplierPalletRequest();
        request.WarehouseLocation = "COLD-01";
        request.LogisticsStatus = "Received";

        AssertValidation(await CreatePalletAsync(request), "warehouseLocation");
    }

    // ---- Inhoudsregel toevoegen ----

    [Fact]
    public async Task Add_ContentLine_Stores_Exact_Values_In_Quarantine_With_Event()
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        var request = ContentRequest(seed.WarehouseLine);

        var result = await AddContentAsync(palletId, request);

        Assert.Equal(ServiceResultStatus.Created, result.Status);
        Assert.Equal("Quarantine", result.Value.QualityStatus);

        await using var verify = _factory.CreateContext();
        var line = await verify.PalletContentLines.SingleAsync();
        Assert.Equal(request.RequestId, line.RequestId);
        Assert.Equal(palletId, line.PalletId);
        Assert.Equal(seed.WarehouseLine, line.SupplyCommitmentLineId);
        Assert.Equal("LOT-6A", line.LotNumber);
        Assert.Equal(40m, line.Quantity);
        Assert.Equal("box", line.Unit);
        Assert.Equal(160.125m, line.NetWeightKg);
        Assert.Equal("Quarantine", line.QualityStatus);
        Assert.Null(line.QualityReason);

        var auditEvent = await verify.PalletAuditEvents.SingleAsync(e => e.EventType == "ContentLineAdded");
        Assert.Equal(palletId, auditEvent.PalletId);
        Assert.Equal(line.Id, auditEvent.PalletContentLineId);
        Assert.Null(auditEvent.OldValue);
        Assert.Contains("LOT-6A", auditEvent.NewValue);
        Assert.Contains("160.125", auditEvent.NewValue);
        Assert.Contains("Quarantine", auditEvent.NewValue);
        Assert.Equal("warehouse-1", auditEvent.ActorReference);
        Assert.Equal("Counted on arrival", auditEvent.Reason);
    }

    [Fact]
    public async Task Add_ContentLine_Never_Derives_Or_Changes_Weights()
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());

        var first = ContentRequest(seed.WarehouseLine);
        var second = ContentRequest(seed.WarehouseLine);
        second.LotNumber = "LOT-6B";
        second.Quantity = 3m;
        second.NetWeightKg = 11.111m;
        await AddContentAsync(palletId, first);
        await AddContentAsync(palletId, second);

        await using var verify = _factory.CreateContext();
        var pallet = await verify.Pallets.SingleAsync();
        Assert.Equal(25m, pallet.TareKg);
        Assert.Equal(425.5m, pallet.GrossKg);
        Assert.Equal(400.25m, pallet.DeclaredNetKg);

        var lines = await verify.PalletContentLines.OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(new[] { 160.125m, 11.111m }, lines.Select(l => l.NetWeightKg));
        Assert.Equal(new[] { 40m, 3m }, lines.Select(l => l.Quantity));

        // De verwachte waarden van de bronregel worden niet overgenomen of aangepast.
        var source = await verify.SupplyCommitmentLines.SingleAsync(l => l.Id == seed.WarehouseLine);
        Assert.Equal(100m, source.ExpectedQuantity);
        Assert.Equal(400m, source.ExpectedNetWeightKg);
        Assert.Equal("PROVISIONAL", source.LotNumber);
    }

    [Fact]
    public async Task Add_ContentLine_Accepts_Any_Allowed_Unit_For_Legacy_Source_Without_Unit()
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        var request = ContentRequest(seed.LegacyLine);
        request.Unit = "dozen";

        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(palletId, request)).Status);
    }

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("request-id-empty", "requestId")]
    [InlineData("source-missing", "supplyCommitmentLineId")]
    [InlineData("lot-missing", "lotNumber")]
    [InlineData("lot-blank", "lotNumber")]
    [InlineData("lot-too-long", "lotNumber")]
    [InlineData("quantity-missing", "quantity")]
    [InlineData("quantity-zero", "quantity")]
    [InlineData("quantity-negative", "quantity")]
    [InlineData("quantity-four-decimals", "quantity")]
    [InlineData("weight-missing", "netWeightKg")]
    [InlineData("weight-zero", "netWeightKg")]
    [InlineData("weight-four-decimals", "netWeightKg")]
    [InlineData("unit-missing", "unit")]
    [InlineData("unit-kg", "unit")]
    [InlineData("unit-capitalised", "unit")]
    [InlineData("unit-pallet", "unit")]
    [InlineData("actor-too-long", "actorReference")]
    [InlineData("reason-too-long", "reason")]
    public async Task Add_ContentLine_Rejects_Invalid_Input_And_Stores_Nothing(string invalidCase, string expectedErrorKey)
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        var request = ContentRequest(seed.WarehouseLine);

        switch (invalidCase)
        {
            case "request-id-missing": request.RequestId = null; break;
            case "request-id-empty": request.RequestId = Guid.Empty; break;
            case "source-missing": request.SupplyCommitmentLineId = null; break;
            case "lot-missing": request.LotNumber = null; break;
            case "lot-blank": request.LotNumber = "  "; break;
            case "lot-too-long": request.LotNumber = new string('L', 101); break;
            case "quantity-missing": request.Quantity = null; break;
            case "quantity-zero": request.Quantity = 0m; break;
            case "quantity-negative": request.Quantity = -1m; break;
            case "quantity-four-decimals": request.Quantity = 1.0001m; break;
            case "weight-missing": request.NetWeightKg = null; break;
            case "weight-zero": request.NetWeightKg = 0m; break;
            case "weight-four-decimals": request.NetWeightKg = 1.0001m; break;
            case "unit-missing": request.Unit = null; break;
            case "unit-kg": request.Unit = "kg"; break;
            case "unit-capitalised": request.Unit = "Box"; break;
            case "unit-pallet": request.Unit = "pallet"; break;
            case "actor-too-long": request.ActorReference = new string('A', 101); break;
            case "reason-too-long": request.Reason = new string('x', 501); break;
        }

        AssertValidation(await AddContentAsync(palletId, request), expectedErrorKey);
        Assert.Equal((1, 0, 1), await CountsAsync());
    }

    [Theory]
    [InlineData("unit-differs-from-source")]
    [InlineData("direct-trade-on-warehouse-pallet")]
    [InlineData("cancelled-commitment")]
    [InlineData("gross-exceeded-with-tare")]
    public async Task Add_ContentLine_Business_Rule_Violations_Are_Conflict(string violation)
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        var request = ContentRequest(seed.WarehouseLine);

        switch (violation)
        {
            case "unit-differs-from-source": request.Unit = "colli"; break;
            case "direct-trade-on-warehouse-pallet": request.SupplyCommitmentLineId = seed.DirectLineCustomer1; break;
            case "cancelled-commitment": request.SupplyCommitmentLineId = seed.CancelledLine; break;
            // 25 tara + 400.501 netto > 425.5 bruto
            case "gross-exceeded-with-tare": request.NetWeightKg = 400.501m; break;
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await AddContentAsync(palletId, request)).Status);
        Assert.Equal((1, 0, 1), await CountsAsync());
    }

    [Fact]
    public async Task Add_ContentLine_Accepts_Weight_Exactly_At_Gross_Limit_And_Rejects_Sum_Above_It()
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());

        var first = ContentRequest(seed.WarehouseLine);
        first.NetWeightKg = 300m;
        var second = ContentRequest(seed.WarehouseLine);
        second.LotNumber = "LOT-6B";
        second.NetWeightKg = 100.5m; // 25 + 300 + 100.5 = 425.5: precies gelijk aan bruto
        var third = ContentRequest(seed.WarehouseLine);
        third.LotNumber = "LOT-6C";
        third.NetWeightKg = 0.001m;

        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(palletId, first)).Status);
        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(palletId, second)).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await AddContentAsync(palletId, third)).Status);
    }

    [Fact]
    public async Task Gross_Check_Without_Tare_Uses_Net_Only_And_Is_Skipped_Without_Gross()
    {
        var seed = await SeedAsync();
        var grossOnly = PalletRequest("PAL-GROSS");
        grossOnly.Sscc = null;
        grossOnly.TareKg = null;
        grossOnly.GrossKg = 100m;
        var grossOnlyId = await CreatedPalletIdAsync(grossOnly);

        var nothingMeasured = PalletRequest("PAL-NONE");
        nothingMeasured.Sscc = null;
        nothingMeasured.TareKg = null;
        nothingMeasured.GrossKg = null;
        var nothingMeasuredId = await CreatedPalletIdAsync(nothingMeasured);

        var tooHeavy = ContentRequest(seed.WarehouseLine);
        tooHeavy.NetWeightKg = 100.001m;
        Assert.Equal(ServiceResultStatus.Conflict, (await AddContentAsync(grossOnlyId, tooHeavy)).Status);

        var fits = ContentRequest(seed.WarehouseLine);
        fits.NetWeightKg = 100m;
        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(grossOnlyId, fits)).Status);

        var unbounded = ContentRequest(seed.WarehouseLine);
        unbounded.NetWeightKg = 99_999m;
        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(nothingMeasuredId, unbounded)).Status);
    }

    [Theory]
    [InlineData("Dispatched")]
    [InlineData("Delivered")]
    [InlineData("Cancelled")]
    public async Task Add_ContentLine_To_Terminal_Pallet_Is_Conflict(string terminalStatus)
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        await using (var db = _factory.CreateContext())
        {
            // Een eindstatus is nooit Warehouse (database-check); de pallet staat dan bij de klant.
            var pallet = await db.Pallets.SingleAsync();
            pallet.LogisticsStatus = terminalStatus;
            pallet.LocationContext = "Customer";
            pallet.WarehouseLocation = null;
            await db.SaveChangesAsync();
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await AddContentAsync(palletId, ContentRequest(seed.WarehouseLine))).Status);
    }

    [Theory]
    [InlineData("warehouse-after-direct")]
    [InlineData("direct-after-warehouse")]
    [InlineData("other-customer")]
    public async Task Pallet_Cannot_Mix_Routes_Or_DirectTrade_Customers(string scenario)
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(SupplierPalletRequest());

        var (firstSource, secondSource) = scenario switch
        {
            "warehouse-after-direct" => (seed.DirectLineCustomer1, seed.WarehouseLine),
            "direct-after-warehouse" => (seed.WarehouseLine, seed.DirectLineCustomer1),
            _ => (seed.DirectLineCustomer1, seed.DirectLineCustomer2)
        };

        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(palletId, ContentRequest(firstSource))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await AddContentAsync(palletId, ContentRequest(secondSource))).Status);
        Assert.Equal((1, 1, 2), await CountsAsync());
    }

    [Theory]
    [InlineData("Supplier")]
    [InlineData("InTransit")]
    [InlineData("Crossdock")]
    [InlineData("Customer")]
    [InlineData("External")]
    public async Task DirectTrade_Content_Is_Allowed_Outside_The_Warehouse(string location)
    {
        var seed = await SeedAsync();
        var request = SupplierPalletRequest();
        request.LocationContext = location;
        var palletId = await CreatedPalletIdAsync(request);

        var first = await AddContentAsync(palletId, ContentRequest(seed.DirectLineCustomer1));
        var sameCustomer = ContentRequest(seed.DirectLineCustomer1);
        sameCustomer.LotNumber = "LOT-6B";
        var second = await AddContentAsync(palletId, sameCustomer);

        Assert.Equal(ServiceResultStatus.Created, first.Status);
        Assert.Equal(ServiceResultStatus.Created, second.Status);
        Assert.Equal("DirectTrade", first.Value.Route);
        Assert.Equal(seed.CustomerId1, first.Value.CustomerId);
    }

    [Fact]
    public async Task Multiple_Lots_On_One_Pallet_And_One_Source_Over_Multiple_Pallets_Are_Allowed()
    {
        var seed = await SeedAsync();
        var pallet1 = await CreatedPalletIdAsync(SupplierPalletRequest("PAL-A"));
        var pallet2 = await CreatedPalletIdAsync(SupplierPalletRequest("PAL-B"));

        var lotA = ContentRequest(seed.WarehouseLine);
        var lotB = ContentRequest(seed.WarehouseLine);
        lotB.LotNumber = "LOT-6B";
        var otherPallet = ContentRequest(seed.WarehouseLine);

        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(pallet1, lotA)).Status);
        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(pallet1, lotB)).Status);
        Assert.Equal(ServiceResultStatus.Created, (await AddContentAsync(pallet2, otherPallet)).Status);
        Assert.Equal((2, 3, 5), await CountsAsync());
    }

    [Theory]
    [InlineData("pallet")]
    [InlineData("source")]
    public async Task Add_ContentLine_Unknown_Pallet_Or_Source_Is_NotFound(string missing)
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        var request = ContentRequest(seed.WarehouseLine);
        if (missing == "source")
        {
            request.SupplyCommitmentLineId = 999_999;
        }

        var result = await AddContentAsync(missing == "pallet" ? 999_999 : palletId, request);

        Assert.Equal(ServiceResultStatus.NotFound, result.Status);
        Assert.Equal((1, 0, 1), await CountsAsync());
    }

    // ---- Idempotentie van inhoudsregels ----

    [Fact]
    public async Task Exact_ContentLine_Retry_Returns_Same_Line_Without_New_Event()
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        var requestId = Guid.NewGuid();

        var first = await AddContentAsync(palletId, ContentRequest(seed.WarehouseLine, requestId));
        var retryRequest = ContentRequest(seed.WarehouseLine, requestId);
        retryRequest.LotNumber = " LOT-6A ";
        retryRequest.NetWeightKg = 160.1250m;
        var retry = await AddContentAsync(palletId, retryRequest);

        Assert.Equal(ServiceResultStatus.Created, first.Status);
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal((1, 1, 2), await CountsAsync());
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("weight")]
    [InlineData("lot")]
    [InlineData("unit")]
    [InlineData("source")]
    [InlineData("other-pallet")]
    public async Task Reused_ContentLine_RequestId_With_Different_Content_Is_Conflict(string change)
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        var otherPalletId = await CreatedPalletIdAsync(SupplierPalletRequest());
        var requestId = Guid.NewGuid();
        await AddContentAsync(palletId, ContentRequest(seed.WarehouseLine, requestId));

        var retry = ContentRequest(seed.WarehouseLine, requestId);
        var targetPallet = palletId;
        switch (change)
        {
            case "quantity": retry.Quantity = 41m; break;
            case "weight": retry.NetWeightKg = 160.126m; break;
            case "lot": retry.LotNumber = "LOT-6B"; break;
            case "unit": retry.SupplyCommitmentLineId = seed.LegacyLine; retry.Unit = "tray"; break;
            case "source": retry.SupplyCommitmentLineId = seed.LegacyLine; break;
            case "other-pallet": targetPallet = otherPalletId; break;
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await AddContentAsync(targetPallet, retry)).Status);
        Assert.Equal((2, 1, 3), await CountsAsync());
    }

    // ---- Lezen ----

    [Fact]
    public async Task Get_Returns_Pallet_With_Lines_Source_Info_And_Summed_Net()
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        var first = ContentRequest(seed.WarehouseLine);
        var second = ContentRequest(seed.WarehouseLine);
        second.LotNumber = "LOT-6B";
        second.NetWeightKg = 11.111m;
        await AddContentAsync(palletId, first);
        await AddContentAsync(palletId, second);

        await using var db = _factory.CreateContext();
        var result = await CreateService(db).GetAsync(palletId);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var pallet = result.Value;
        Assert.Equal("PAL-001", pallet.Reference);
        Assert.Equal(171.236m, pallet.ContentNetKg);
        Assert.Equal(400.25m, pallet.DeclaredNetKg);
        Assert.Equal(2, pallet.ContentLines.Count);
        var line = pallet.ContentLines[0];
        Assert.Equal(seed.WarehouseLine, line.SupplyCommitmentLineId);
        Assert.Equal("Warehouse", line.Route);
        Assert.Null(line.CustomerId);
        Assert.Equal("Avocado", line.ProductName);
        Assert.Equal("Hass 6", line.ProductVariantName);
        Assert.Equal("Quarantine", line.QualityStatus);
    }

    [Fact]
    public async Task Get_Empty_Pallet_Has_Zero_Content_Net()
    {
        var palletId = await CreatedPalletIdAsync(SupplierPalletRequest());

        await using var db = _factory.CreateContext();
        var result = await CreateService(db).GetAsync(palletId);

        Assert.Equal(0m, result.Value.ContentNetKg);
        Assert.Empty(result.Value.ContentLines);
    }

    [Fact]
    public async Task Get_Unknown_Pallet_Is_NotFound()
    {
        await using var db = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.NotFound, (await CreateService(db).GetAsync(999_999)).Status);
    }

    [Fact]
    public async Task Pallet_Api_Has_No_Stock_Or_Commitment_Side_Effects()
    {
        var seed = await SeedAsync();
        var palletId = await CreatedPalletIdAsync(PalletRequest());
        await AddContentAsync(palletId, ContentRequest(seed.WarehouseLine));

        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.StockReservations.CountAsync());
        Assert.Equal(0, await verify.StockReceipts.CountAsync());
        Assert.Equal(0, await verify.Inventory.CountAsync());
        Assert.All(await verify.SupplyCommitments.ToListAsync(), c => Assert.Contains(c.Status, new[] { "Expected", "Cancelled" }));
        Assert.Equal("Expected", (await verify.SupplyCommitmentLines.Include(l => l.SupplyCommitment).SingleAsync(l => l.Id == seed.WarehouseLine)).SupplyCommitment.Status);
    }
}
