using System.Text.Json;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AgfAdmin.Tests.Services;

public class PalletStateChangeServiceTests : IDisposable
{
    private const string Actor = "warehouse-1";

    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record Seed(int WarehouseLine, int DirectLine1, int DirectLine2);

    private Seed _seed;

    private async Task<Seed> SeedAsync()
    {
        if (_seed != null)
        {
            return _seed;
        }

        await using var db = _factory.CreateContext();
        var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass 6", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.50m };
        var vendor = new Vendor { Name = "Supplier B" };

        SupplyCommitment Commitment(string route, Customer customer) => new()
        {
            RequestId = Guid.NewGuid(),
            Vendor = vendor,
            Customer = customer,
            Route = route,
            Status = "Expected",
            Origin = "Kenya",
            Destination = customer == null ? null : "Turkey"
        };

        SupplyCommitmentLine Line(SupplyCommitment commitment) => new()
        {
            SupplyCommitment = commitment,
            ProductVariant = variant,
            ExpectedQuantity = 100m,
            ExpectedUnit = "box"
        };

        var warehouse = Line(Commitment("Warehouse", null));
        var direct1 = Line(Commitment("DirectTrade", new Customer { Name = "Customer T" }));
        var direct2 = Line(Commitment("DirectTrade", new Customer { Name = "Customer U" }));
        db.AddRange(variant, warehouse, direct1, direct2);
        await db.SaveChangesAsync();

        _seed = new Seed(warehouse.Id, direct1.Id, direct2.Id);
        return _seed;
    }

    private int SourceFor(string route) => route == "DirectTrade" ? _seed.DirectLine1 : _seed.WarehouseLine;

    // ---- Opzet via de echte service ----

    private async Task<int> CreatePalletAsync(string status, string location, string bin = null)
    {
        await using var db = _factory.CreateContext();
        var result = await new PalletService(db).CreateAsync(new CreatePalletDto
        {
            Reference = $"PAL-{Guid.NewGuid():N}"[..24],
            LogisticsStatus = status,
            LocationContext = location,
            WarehouseLocation = bin,
            ActorReference = "office-1"
        });
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value.Id;
    }

    private async Task<ServiceResult<PalletContentLineDto>> AddLineResultAsync(int palletId, int sourceLineId, string lot = "LOT-1")
    {
        await using var db = _factory.CreateContext();
        return await new PalletService(db).AddContentLineAsync(palletId, new AddPalletContentLineDto
        {
            RequestId = Guid.NewGuid(),
            SupplyCommitmentLineId = sourceLineId,
            LotNumber = lot,
            Quantity = 4m,
            Unit = "box",
            NetWeightKg = 16m,
            ActorReference = "office-1"
        });
    }

    private async Task<int> AddLineAsync(int palletId, int sourceLineId, string lot = "LOT-1")
    {
        var result = await AddLineResultAsync(palletId, sourceLineId, lot);
        Assert.Equal(ServiceResultStatus.Created, result.Status);
        return result.Value.Id;
    }

    private async Task<int> PalletVersionAsync(int palletId)
    {
        await using var db = _factory.CreateContext();
        return (await db.Pallets.SingleAsync(p => p.Id == palletId)).Version;
    }

    private async Task<int> LineVersionAsync(int lineId)
    {
        await using var db = _factory.CreateContext();
        return (await db.PalletContentLines.SingleAsync(c => c.Id == lineId)).Version;
    }

    private async Task<Pallet> LoadPalletAsync(int palletId)
    {
        await using var db = _factory.CreateContext();
        return await db.Pallets.SingleAsync(p => p.Id == palletId);
    }

    private async Task<PalletContentLine> LoadLineAsync(int lineId)
    {
        await using var db = _factory.CreateContext();
        return await db.PalletContentLines.SingleAsync(c => c.Id == lineId);
    }

    private async Task<List<PalletAuditEvent>> EventsAsync()
    {
        await using var db = _factory.CreateContext();
        return await db.PalletAuditEvents.OrderBy(e => e.Id).ToListAsync();
    }

    private async Task<ChangeLogisticsStatusDto> StatusCommandAsync(int palletId, string to, string location = null, string bin = null, string reason = null) => new()
    {
        RequestId = Guid.NewGuid(),
        ExpectedPalletVersion = await PalletVersionAsync(palletId),
        ToStatus = to,
        LocationContext = location,
        WarehouseLocation = bin,
        ActorReference = Actor,
        Reason = reason
    };

    private async Task<ChangePalletLocationDto> LocationCommandAsync(int palletId, string location, string bin = null, string reason = null) => new()
    {
        RequestId = Guid.NewGuid(),
        ExpectedPalletVersion = await PalletVersionAsync(palletId),
        LocationContext = location,
        WarehouseLocation = bin,
        ActorReference = Actor,
        Reason = reason
    };

    private async Task<ChangeQualityStatusDto> QualityCommandAsync(int lineId, string to, string reason = null) => new()
    {
        RequestId = Guid.NewGuid(),
        ExpectedContentLineVersion = await LineVersionAsync(lineId),
        ToStatus = to,
        ActorReference = Actor,
        Reason = reason
    };

    private async Task<ServiceResult<PalletDto>> ChangeStatusAsync(int palletId, ChangeLogisticsStatusDto command, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new PalletService(db).ChangeLogisticsStatusAsync(palletId, command);
    }

    private async Task<ServiceResult<PalletDto>> ChangeLocationAsync(int palletId, ChangePalletLocationDto command, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new PalletService(db).ChangeLocationAsync(palletId, command);
    }

    private async Task<ServiceResult<PalletContentLineDto>> ChangeQualityAsync(int palletId, int lineId, ChangeQualityStatusDto command, params IInterceptor[] interceptors)
    {
        await using var db = _factory.CreateContext(interceptors);
        return await new PalletService(db).ChangeQualityStatusAsync(palletId, lineId, command);
    }

    private async Task SetQualityAsync(int palletId, int lineId, string quality)
    {
        if (quality == "Quarantine")
        {
            return;
        }

        if (quality == "Rejected")
        {
            await SetQualityAsync(palletId, lineId, "Blocked");
        }

        var result = await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, quality, quality == "Approved" ? null : $"{quality} by inspection"));
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
    }

    private async Task MoveAsync(int palletId, string to, string location = null, string reason = null)
    {
        var result = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, to, location, reason: reason));
        Assert.True(result.Status == ServiceResultStatus.Ok, result.Message);
    }

    // Brengt een pallet met één inhoudsregel (in de gevraagde kwaliteit) via de echte commando's in de gevraagde status.
    private async Task<(int PalletId, int LineId)> PalletInStateAsync(string status, string route = "Warehouse", string quality = "Approved")
    {
        await SeedAsync();
        var source = SourceFor(route);
        int palletId;
        switch (status)
        {
            case "Expected":
            case "Cancelled":
                palletId = await CreatePalletAsync("Expected", "Supplier");
                break;
            case "InTransit":
                palletId = await CreatePalletAsync("InTransit", "InTransit");
                break;
            default:
                palletId = route == "DirectTrade"
                    ? await CreatePalletAsync("Received", "Crossdock")
                    : await CreatePalletAsync("Received", "Warehouse", "COLD-01");
                break;
        }

        var lineId = await AddLineAsync(palletId, source);
        await SetQualityAsync(palletId, lineId, quality);

        if (status is "Dispatched" or "Delivered")
        {
            await MoveAsync(palletId, "Dispatched", "InTransit");
        }
        if (status == "Delivered")
        {
            await MoveAsync(palletId, "Delivered", "Customer");
        }
        if (status == "Cancelled")
        {
            await MoveAsync(palletId, "Cancelled", reason: "Order cancelled by supplier");
        }

        return (palletId, lineId);
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement;

    private static string Prop(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? (value.ValueKind == JsonValueKind.Null ? null : value.ToString()) : "<absent>";

    // ======================== Logistieke status ========================

    [Theory]
    [InlineData("Expected", "InTransit", "Warehouse", null)]
    [InlineData("Expected", "Received", "Warehouse", null)]
    [InlineData("Expected", "Cancelled", "Warehouse", null)]
    [InlineData("InTransit", "Received", "Warehouse", "Warehouse")]
    [InlineData("InTransit", "Delivered", "DirectTrade", "Customer")]
    [InlineData("Received", "Dispatched", "Warehouse", "InTransit")]
    [InlineData("Dispatched", "Delivered", "Warehouse", "Customer")]
    public async Task Allowed_Logistics_Transition_Changes_Status_Version_And_Writes_One_Event(string from, string to, string route, string location)
    {
        var (palletId, _) = await PalletInStateAsync(from, route);
        var versionBefore = await PalletVersionAsync(palletId);
        var palletBefore = await LoadPalletAsync(palletId);
        var eventsBefore = (await EventsAsync()).Count;
        var command = await StatusCommandAsync(palletId, to, location, reason: "Planned move");

        var result = await ChangeStatusAsync(palletId, command);

        Assert.True(result.Status == ServiceResultStatus.Ok, result.Message);
        Assert.Equal(to, result.Value.LogisticsStatus);
        Assert.Equal(versionBefore + 1, result.Value.Version);

        var pallet = await LoadPalletAsync(palletId);
        Assert.Equal(to, pallet.LogisticsStatus);
        Assert.Equal(location ?? palletBefore.LocationContext, pallet.LocationContext);
        Assert.Equal(versionBefore + 1, pallet.Version);

        var events = await EventsAsync();
        Assert.Equal(eventsBefore + 1, events.Count);
        var auditEvent = events.Last();
        Assert.Equal("LogisticsStatusChanged", auditEvent.EventType);
        Assert.Equal(palletId, auditEvent.PalletId);
        Assert.Null(auditEvent.PalletContentLineId);
        Assert.Equal(command.RequestId, auditEvent.RequestId);
        Assert.Equal(Actor, auditEvent.ActorReference);
        Assert.Equal("Planned move", auditEvent.Reason);
        Assert.True((DateTime.UtcNow - auditEvent.OccurredAt).Duration() < TimeSpan.FromMinutes(1));

        var oldValue = Json(auditEvent.OldValue);
        Assert.Equal(from, Prop(oldValue, "logisticsStatus"));
        Assert.Equal(palletBefore.LocationContext, Prop(oldValue, "locationContext"));
        Assert.Equal(versionBefore.ToString(), Prop(oldValue, "version"));
        var newValue = Json(auditEvent.NewValue);
        Assert.Equal(to, Prop(newValue, "logisticsStatus"));
        Assert.Equal(location ?? "<absent>", Prop(newValue, "locationContext"));
    }

    [Theory]
    [InlineData("Expected", "Dispatched")]
    [InlineData("Expected", "Delivered")]
    [InlineData("InTransit", "Expected")]
    [InlineData("InTransit", "Dispatched")]
    [InlineData("InTransit", "Cancelled")]
    [InlineData("Received", "Expected")]
    [InlineData("Received", "InTransit")]
    [InlineData("Received", "Delivered")]
    [InlineData("Received", "Cancelled")]
    [InlineData("Dispatched", "Received")]
    [InlineData("Dispatched", "InTransit")]
    [InlineData("Dispatched", "Cancelled")]
    public async Task Forbidden_Logistics_Transition_Is_Conflict_Without_Change_Or_Event(string from, string to)
    {
        var (palletId, _) = await PalletInStateAsync(from);
        var versionBefore = await PalletVersionAsync(palletId);
        var eventsBefore = (await EventsAsync()).Count;

        var result = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, to, location: "Customer", reason: "Try"));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal(from, (await LoadPalletAsync(palletId)).LogisticsStatus);
        Assert.Equal(versionBefore, await PalletVersionAsync(palletId));
        Assert.Equal(eventsBefore, (await EventsAsync()).Count);
    }

    [Theory]
    [InlineData("Cancelled", "Expected")]
    [InlineData("Cancelled", "InTransit")]
    [InlineData("Cancelled", "Received")]
    [InlineData("Cancelled", "Dispatched")]
    [InlineData("Cancelled", "Delivered")]
    [InlineData("Delivered", "Expected")]
    [InlineData("Delivered", "InTransit")]
    [InlineData("Delivered", "Received")]
    [InlineData("Delivered", "Dispatched")]
    [InlineData("Delivered", "Cancelled")]
    public async Task Cancelled_And_Delivered_Pallets_Never_Become_Operational_Again(string terminal, string to)
    {
        var (palletId, _) = await PalletInStateAsync(terminal);
        var eventsBefore = (await EventsAsync()).Count;

        var result = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, to, location: "Customer", reason: "Reopen"));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal(terminal, (await LoadPalletAsync(palletId)).LogisticsStatus);
        Assert.Equal(eventsBefore, (await EventsAsync()).Count);
    }

    [Fact]
    public async Task Same_Logistics_Status_Is_Conflict()
    {
        var (palletId, _) = await PalletInStateAsync("Received");

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Received"))).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Cancellation_Requires_A_Reason(string reason)
    {
        var (palletId, _) = await PalletInStateAsync("Expected");

        var result = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Cancelled", reason: reason));

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains("reason", result.Errors.Keys);
        Assert.Equal("Expected", (await LoadPalletAsync(palletId)).LogisticsStatus);
    }

    [Fact]
    public async Task Dispatch_From_Warehouse_Requires_A_New_Location()
    {
        var (palletId, _) = await PalletInStateAsync("Received");

        var withoutLocation = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Dispatched"));
        Assert.Equal(ServiceResultStatus.Conflict, withoutLocation.Status);

        var withLocation = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Dispatched", "InTransit"));
        Assert.Equal(ServiceResultStatus.Ok, withLocation.Status);
        var pallet = await LoadPalletAsync(palletId);
        Assert.Equal("InTransit", pallet.LocationContext);
        Assert.Null(pallet.WarehouseLocation);

        var auditEvent = (await EventsAsync()).Last();
        Assert.Equal("Warehouse", Prop(Json(auditEvent.OldValue), "locationContext"));
        Assert.Equal("COLD-01", Prop(Json(auditEvent.OldValue), "warehouseLocation"));
        Assert.Equal("InTransit", Prop(Json(auditEvent.NewValue), "locationContext"));
        Assert.Null(Prop(Json(auditEvent.NewValue), "warehouseLocation"));
    }

    [Theory]
    [InlineData("Crossdock", false)]
    [InlineData("InTransit", false)]
    [InlineData("Supplier", false)]
    [InlineData("Customer", true)]
    [InlineData("External", true)]
    public async Task Delivered_Is_Only_Allowed_At_Customer_Or_External(string location, bool allowed)
    {
        var (palletId, _) = await PalletInStateAsync("Dispatched");

        var result = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Delivered", location));

        Assert.Equal(allowed ? ServiceResultStatus.Ok : ServiceResultStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Delivered_Without_New_Location_Keeps_Location_And_Needs_It_To_Be_Valid()
    {
        var (palletId, _) = await PalletInStateAsync("Dispatched");

        // De pallet staat na dispatch op InTransit: Delivered zonder nieuwe locatie is ongeldig.
        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Delivered"))).Status);
    }

    [Fact]
    public async Task Empty_Pallet_Cannot_Be_Dispatched_Or_Delivered()
    {
        await SeedAsync();
        var receivedId = await CreatePalletAsync("Received", "Warehouse");
        var inTransitId = await CreatePalletAsync("InTransit", "InTransit");

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeStatusAsync(receivedId, await StatusCommandAsync(receivedId, "Dispatched", "InTransit"))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeStatusAsync(inTransitId, await StatusCommandAsync(inTransitId, "Delivered", "Customer"))).Status);
    }

    [Theory]
    [InlineData("dispatch", "Quarantine")]
    [InlineData("dispatch", "Blocked")]
    [InlineData("dispatch", "Rejected")]
    [InlineData("direct-deliver", "Quarantine")]
    [InlineData("direct-deliver", "Blocked")]
    [InlineData("direct-deliver", "Rejected")]
    public async Task Dispatched_And_Delivered_Require_All_Content_Approved(string transition, string quality)
    {
        var (palletId, _) = transition == "dispatch"
            ? await PalletInStateAsync("Received", "Warehouse", quality)
            : await PalletInStateAsync("InTransit", "DirectTrade", quality);
        var command = transition == "dispatch"
            ? await StatusCommandAsync(palletId, "Dispatched", "InTransit")
            : await StatusCommandAsync(palletId, "Delivered", "Customer");

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeStatusAsync(palletId, command)).Status);
    }

    [Fact]
    public async Task One_Unapproved_Line_Blocks_Dispatch_Of_The_Whole_Pallet()
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        await AddLineAsync(palletId, _seed.WarehouseLine, "LOT-2");

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Dispatched", "InTransit"))).Status);
    }

    [Fact]
    public async Task InTransit_To_Delivered_Is_Only_For_DirectTrade_Content()
    {
        var (palletId, _) = await PalletInStateAsync("InTransit", "Warehouse");

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Delivered", "Customer"))).Status);
    }

    [Fact]
    public async Task Receiving_DirectTrade_Content_Into_The_Warehouse_Is_Conflict()
    {
        var (palletId, _) = await PalletInStateAsync("InTransit", "DirectTrade");

        var result = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Received", "Warehouse"));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal("InTransit", (await LoadPalletAsync(palletId)).LocationContext);
    }

    [Fact]
    public async Task Receiving_Warehouse_Content_Into_A_Bin_Stores_The_Trimmed_Bin()
    {
        var (palletId, _) = await PalletInStateAsync("InTransit");

        var result = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Received", "Warehouse", "  COLD-02 "));

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal("COLD-02", (await LoadPalletAsync(palletId)).WarehouseLocation);
    }

    [Theory]
    [InlineData("Dispatched")]
    [InlineData("Delivered")]
    [InlineData("Cancelled")]
    public async Task Adding_Content_To_A_Dispatched_Delivered_Or_Cancelled_Pallet_Stays_Blocked(string status)
    {
        var (palletId, _) = await PalletInStateAsync(status);

        Assert.Equal(ServiceResultStatus.Conflict, (await AddLineResultAsync(palletId, _seed.WarehouseLine, "LOT-LATE")).Status);
    }

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("request-id-empty", "requestId")]
    [InlineData("version-missing", "expectedPalletVersion")]
    [InlineData("to-missing", "toStatus")]
    [InlineData("to-unknown", "toStatus")]
    [InlineData("to-lowercase", "toStatus")]
    [InlineData("actor-missing", "actorReference")]
    [InlineData("actor-blank", "actorReference")]
    [InlineData("actor-too-long", "actorReference")]
    [InlineData("reason-too-long", "reason")]
    [InlineData("location-unknown", "locationContext")]
    [InlineData("bin-without-location", "warehouseLocation")]
    [InlineData("bin-outside-warehouse", "warehouseLocation")]
    [InlineData("bin-too-long", "warehouseLocation")]
    public async Task Logistics_Command_Rejects_Invalid_Input(string invalidCase, string expectedErrorKey)
    {
        var (palletId, _) = await PalletInStateAsync("InTransit");
        var command = await StatusCommandAsync(palletId, "Received");
        switch (invalidCase)
        {
            case "request-id-missing": command.RequestId = null; break;
            case "request-id-empty": command.RequestId = Guid.Empty; break;
            case "version-missing": command.ExpectedPalletVersion = null; break;
            case "to-missing": command.ToStatus = null; break;
            case "to-unknown": command.ToStatus = "Shipped"; break;
            case "to-lowercase": command.ToStatus = "received"; break;
            case "actor-missing": command.ActorReference = null; break;
            case "actor-blank": command.ActorReference = "  "; break;
            case "actor-too-long": command.ActorReference = new string('A', 101); break;
            case "reason-too-long": command.Reason = new string('x', 501); break;
            case "location-unknown": command.LocationContext = "Truck"; break;
            case "bin-without-location": command.WarehouseLocation = "COLD-01"; break;
            case "bin-outside-warehouse": command.LocationContext = "Crossdock"; command.WarehouseLocation = "COLD-01"; break;
            case "bin-too-long": command.LocationContext = "Warehouse"; command.WarehouseLocation = new string('B', 65); break;
        }

        var result = await ChangeStatusAsync(palletId, command);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
        Assert.Equal("InTransit", (await LoadPalletAsync(palletId)).LogisticsStatus);
    }

    // ======================== Locatie ========================

    [Fact]
    public async Task DirectTrade_Pallet_Can_Never_Move_To_Warehouse()
    {
        var (palletId, _) = await PalletInStateAsync("Received", "DirectTrade");
        var eventsBefore = (await EventsAsync()).Count;

        var result = await ChangeLocationAsync(palletId, await LocationCommandAsync(palletId, "Warehouse"));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal("Crossdock", (await LoadPalletAsync(palletId)).LocationContext);
        Assert.Equal(eventsBefore, (await EventsAsync()).Count);
    }

    [Theory]
    [InlineData("Expected", "Supplier", "Warehouse", null, false)]
    [InlineData("InTransit", "InTransit", "Warehouse", null, false)]
    [InlineData("Received", "Crossdock", "Warehouse", null, true)]
    [InlineData("Received", "Crossdock", "Warehouse", "COLD-03", true)]
    [InlineData("Expected", "Supplier", "Crossdock", null, true)]
    [InlineData("Received", "Warehouse", "Crossdock", null, true)]
    public async Task Location_Change_Respects_Warehouse_Status_Rules(string status, string fromLocation, string toLocation, string bin, bool allowed)
    {
        await SeedAsync();
        var palletId = await CreatePalletAsync(status, fromLocation);
        await AddLineAsync(palletId, _seed.WarehouseLine);

        var result = await ChangeLocationAsync(palletId, await LocationCommandAsync(palletId, toLocation, bin));

        Assert.Equal(allowed ? ServiceResultStatus.Ok : ServiceResultStatus.Conflict, result.Status);
        var pallet = await LoadPalletAsync(palletId);
        Assert.Equal(allowed ? toLocation : fromLocation, pallet.LocationContext);
        Assert.Equal(allowed ? bin : null, pallet.WarehouseLocation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_Bin_Is_Normalised_To_Null(string bin)
    {
        var (palletId, _) = await PalletInStateAsync("Received");

        var result = await ChangeLocationAsync(palletId, await LocationCommandAsync(palletId, "Warehouse", bin));

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Null((await LoadPalletAsync(palletId)).WarehouseLocation);
        var auditEvent = (await EventsAsync()).Last();
        Assert.Equal("COLD-01", Prop(Json(auditEvent.OldValue), "warehouseLocation"));
        Assert.Null(Prop(Json(auditEvent.NewValue), "warehouseLocation"));
    }

    [Fact]
    public async Task Bin_Change_Within_Warehouse_Writes_One_LocationChanged_Event()
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        var versionBefore = await PalletVersionAsync(palletId);
        var command = await LocationCommandAsync(palletId, "Warehouse", "COLD-02", "Rearranged");

        var result = await ChangeLocationAsync(palletId, command);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(versionBefore + 1, result.Value.Version);
        var auditEvent = (await EventsAsync()).Last();
        Assert.Equal("LocationChanged", auditEvent.EventType);
        Assert.Equal(palletId, auditEvent.PalletId);
        Assert.Equal(command.RequestId, auditEvent.RequestId);
        Assert.Equal("Rearranged", auditEvent.Reason);
        Assert.Equal(Actor, auditEvent.ActorReference);
        var oldValue = Json(auditEvent.OldValue);
        Assert.Equal("Warehouse", Prop(oldValue, "locationContext"));
        Assert.Equal("COLD-01", Prop(oldValue, "warehouseLocation"));
        Assert.Equal(versionBefore.ToString(), Prop(oldValue, "version"));
        var newValue = Json(auditEvent.NewValue);
        Assert.Equal("Warehouse", Prop(newValue, "locationContext"));
        Assert.Equal("COLD-02", Prop(newValue, "warehouseLocation"));
    }

    [Theory]
    [InlineData("COLD-01")]
    [InlineData(" COLD-01 ")]
    public async Task Same_Location_And_Bin_Is_Conflict(string bin)
    {
        var (palletId, _) = await PalletInStateAsync("Received");

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeLocationAsync(palletId, await LocationCommandAsync(palletId, "Warehouse", bin))).Status);
    }

    [Theory]
    [InlineData("Delivered", "External")]
    [InlineData("Cancelled", "Crossdock")]
    public async Task Location_Of_Delivered_Or_Cancelled_Pallet_Cannot_Change(string status, string location)
    {
        var (palletId, _) = await PalletInStateAsync(status);

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeLocationAsync(palletId, await LocationCommandAsync(palletId, location))).Status);
    }

    [Theory]
    [InlineData("Crossdock", true)]
    [InlineData("Warehouse", false)]
    public async Task Dispatched_Pallet_Can_Move_But_Not_Into_The_Warehouse(string location, bool allowed)
    {
        var (palletId, _) = await PalletInStateAsync("Dispatched");

        var result = await ChangeLocationAsync(palletId, await LocationCommandAsync(palletId, location));

        Assert.Equal(allowed ? ServiceResultStatus.Ok : ServiceResultStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task Existing_Invalid_Mix_In_Database_Blocks_Every_Change()
    {
        await SeedAsync();
        var palletId = await CreatePalletAsync("Expected", "Supplier");
        await using (var db = _factory.CreateContext())
        {
            // Buiten de API om (bijvoorbeeld legacy/SQL): Warehouse- en DirectTrade-inhoud op één pallet.
            foreach (var (source, lot) in new[] { (_seed.WarehouseLine, "LOT-W"), (_seed.DirectLine1, "LOT-D") })
            {
                db.PalletContentLines.Add(new PalletContentLine
                {
                    RequestId = Guid.NewGuid(),
                    PalletId = palletId,
                    SupplyCommitmentLineId = source,
                    LotNumber = lot,
                    Quantity = 1m,
                    Unit = "box",
                    NetWeightKg = 4m,
                    QualityStatus = "Quarantine"
                });
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeLocationAsync(palletId, await LocationCommandAsync(palletId, "Crossdock"))).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "InTransit"))).Status);
    }

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("version-missing", "expectedPalletVersion")]
    [InlineData("location-missing", "locationContext")]
    [InlineData("location-unknown", "locationContext")]
    [InlineData("bin-outside-warehouse", "warehouseLocation")]
    [InlineData("actor-missing", "actorReference")]
    [InlineData("reason-too-long", "reason")]
    public async Task Location_Command_Rejects_Invalid_Input(string invalidCase, string expectedErrorKey)
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        var command = await LocationCommandAsync(palletId, "Warehouse", "COLD-09");
        switch (invalidCase)
        {
            case "request-id-missing": command.RequestId = null; break;
            case "version-missing": command.ExpectedPalletVersion = null; break;
            case "location-missing": command.LocationContext = null; command.WarehouseLocation = null; break;
            case "location-unknown": command.LocationContext = "Truck"; command.WarehouseLocation = null; break;
            case "bin-outside-warehouse": command.LocationContext = "Supplier"; break;
            case "actor-missing": command.ActorReference = null; break;
            case "reason-too-long": command.Reason = new string('x', 501); break;
        }

        var result = await ChangeLocationAsync(palletId, command);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
    }

    // ======================== Kwaliteit per inhoudsregel ========================

    [Theory]
    [InlineData("Quarantine", "Approved", null)]
    [InlineData("Quarantine", "Approved", "Looks fine")]
    [InlineData("Quarantine", "Blocked", "Temperature log missing")]
    [InlineData("Quarantine", "Rejected", "Mould")]
    [InlineData("Approved", "Blocked", "Customer claim")]
    [InlineData("Blocked", "Approved", "Claim withdrawn")]
    [InlineData("Blocked", "Rejected", "Claim confirmed")]
    public async Task Allowed_Quality_Transition_Changes_Only_That_Line_And_Writes_One_Event(string from, string to, string reason)
    {
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: from);
        var otherLineId = await AddLineAsync(palletId, _seed.WarehouseLine, "LOT-OTHER");
        var lineBefore = await LoadLineAsync(lineId);
        var palletVersionBefore = await PalletVersionAsync(palletId);
        var eventsBefore = (await EventsAsync()).Count;
        var command = await QualityCommandAsync(lineId, to, reason);

        var result = await ChangeQualityAsync(palletId, lineId, command);

        Assert.True(result.Status == ServiceResultStatus.Ok, result.Message);
        Assert.Equal(to, result.Value.QualityStatus);
        Assert.Equal(lineBefore.Version + 1, result.Value.Version);

        var line = await LoadLineAsync(lineId);
        Assert.Equal(to, line.QualityStatus);
        Assert.Equal(reason, line.QualityReason);
        Assert.Equal(lineBefore.Version + 1, line.Version);
        // Een kwaliteitswijziging raakt ook de pallet, zodat een gelijktijdige dispatch niet op verouderde kwaliteit beslist.
        Assert.Equal(palletVersionBefore + 1, await PalletVersionAsync(palletId));
        Assert.Equal(palletVersionBefore + 1, result.Value.PalletVersion);
        Assert.Equal("Quarantine", (await LoadLineAsync(otherLineId)).QualityStatus);

        var events = await EventsAsync();
        Assert.Equal(eventsBefore + 1, events.Count);
        var auditEvent = events.Last();
        Assert.Equal("QualityStatusChanged", auditEvent.EventType);
        Assert.Equal(palletId, auditEvent.PalletId);
        Assert.Equal(lineId, auditEvent.PalletContentLineId);
        Assert.Equal(command.RequestId, auditEvent.RequestId);
        Assert.Equal(Actor, auditEvent.ActorReference);
        Assert.Equal(reason, auditEvent.Reason);
        var oldValue = Json(auditEvent.OldValue);
        Assert.Equal(from, Prop(oldValue, "qualityStatus"));
        Assert.Equal(lineBefore.QualityReason, Prop(oldValue, "qualityReason"));
        Assert.Equal(lineBefore.Version.ToString(), Prop(oldValue, "version"));
        var newValue = Json(auditEvent.NewValue);
        Assert.Equal(to, Prop(newValue, "qualityStatus"));
        Assert.Equal(reason, Prop(newValue, "qualityReason"));
    }

    [Theory]
    [InlineData("Approved", "Quarantine")]
    [InlineData("Approved", "Rejected")]
    [InlineData("Approved", "Approved")]
    [InlineData("Blocked", "Quarantine")]
    [InlineData("Blocked", "Blocked")]
    [InlineData("Rejected", "Approved")]
    [InlineData("Rejected", "Blocked")]
    [InlineData("Rejected", "Quarantine")]
    [InlineData("Quarantine", "Quarantine")]
    public async Task Forbidden_Quality_Transition_Is_Conflict_Without_Change_Or_Event(string from, string to)
    {
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: from);
        var versionBefore = await LineVersionAsync(lineId);
        var eventsBefore = (await EventsAsync()).Count;

        var result = await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, to, "Some reason"));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal(from, (await LoadLineAsync(lineId)).QualityStatus);
        Assert.Equal(versionBefore, await LineVersionAsync(lineId));
        Assert.Equal(eventsBefore, (await EventsAsync()).Count);
    }

    [Theory]
    [InlineData("Quarantine", "Blocked")]
    [InlineData("Quarantine", "Rejected")]
    [InlineData("Approved", "Blocked")]
    [InlineData("Blocked", "Approved")]
    [InlineData("Blocked", "Rejected")]
    public async Task Blocking_Rejecting_And_Lifting_Require_A_Reason(string from, string to)
    {
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: from);

        var result = await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, to, "   "));

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains("reason", result.Errors.Keys);
        Assert.Equal(from, (await LoadLineAsync(lineId)).QualityStatus);
    }

    [Theory]
    [InlineData("Delivered")]
    [InlineData("Cancelled")]
    public async Task Quality_Of_Delivered_Or_Cancelled_Pallet_Cannot_Change(string status)
    {
        var (palletId, lineId) = await PalletInStateAsync(status, quality: status == "Cancelled" ? "Quarantine" : "Approved");
        var to = status == "Cancelled" ? "Approved" : "Blocked";

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, to, "Late claim"))).Status);
    }

    [Fact]
    public async Task Quality_Of_Dispatched_Pallet_Can_Be_Blocked_For_A_Recall()
    {
        var (palletId, lineId) = await PalletInStateAsync("Dispatched");

        Assert.Equal(ServiceResultStatus.Ok, (await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, "Blocked", "Recall"))).Status);
    }

    [Theory]
    [InlineData("other-pallet")]
    [InlineData("unknown-line")]
    [InlineData("unknown-pallet")]
    public async Task Quality_Change_For_Line_Not_On_Pallet_Is_NotFound(string scenario)
    {
        var (palletId, lineId) = await PalletInStateAsync("Received");
        var (otherPalletId, otherLineId) = await PalletInStateAsync("Received");
        var command = await QualityCommandAsync(otherLineId, "Blocked", "Claim");

        var result = scenario switch
        {
            "other-pallet" => await ChangeQualityAsync(palletId, otherLineId, command),
            "unknown-line" => await ChangeQualityAsync(palletId, 999_999, command),
            _ => await ChangeQualityAsync(999_999, lineId, command)
        };

        Assert.Equal(ServiceResultStatus.NotFound, result.Status);
        Assert.Equal("Approved", (await LoadLineAsync(otherLineId)).QualityStatus);
        Assert.NotEqual(otherPalletId, palletId);
    }

    [Theory]
    [InlineData("request-id-missing", "requestId")]
    [InlineData("version-missing", "expectedContentLineVersion")]
    [InlineData("to-missing", "toStatus")]
    [InlineData("to-partial", "toStatus")]
    [InlineData("to-lowercase", "toStatus")]
    [InlineData("actor-missing", "actorReference")]
    [InlineData("reason-too-long", "reason")]
    public async Task Quality_Command_Rejects_Invalid_Input(string invalidCase, string expectedErrorKey)
    {
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: "Quarantine");
        var command = await QualityCommandAsync(lineId, "Approved");
        switch (invalidCase)
        {
            case "request-id-missing": command.RequestId = null; break;
            case "version-missing": command.ExpectedContentLineVersion = null; break;
            case "to-missing": command.ToStatus = null; break;
            case "to-partial": command.ToStatus = "PartiallyApproved"; break;
            case "to-lowercase": command.ToStatus = "approved"; break;
            case "actor-missing": command.ActorReference = null; break;
            case "reason-too-long": command.Reason = new string('x', 501); break;
        }

        var result = await ChangeQualityAsync(palletId, lineId, command);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(expectedErrorKey, result.Errors.Keys);
    }

    // ======================== Idempotentie ========================

    [Fact]
    public async Task Exact_Logistics_Retry_Returns_Current_State_Without_New_Event()
    {
        var (palletId, _) = await PalletInStateAsync("Expected");
        var command = await StatusCommandAsync(palletId, "InTransit", "InTransit", reason: "Truck left");
        await ChangeStatusAsync(palletId, command);
        var versionAfter = await PalletVersionAsync(palletId);
        var eventsAfter = (await EventsAsync()).Count;

        var retry = new ChangeLogisticsStatusDto
        {
            RequestId = command.RequestId,
            ExpectedPalletVersion = command.ExpectedPalletVersion,
            ToStatus = "InTransit",
            LocationContext = "InTransit",
            ActorReference = " warehouse-1 ",
            Reason = " Truck left "
        };
        var result = await ChangeStatusAsync(palletId, retry);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal("InTransit", result.Value.LogisticsStatus);
        Assert.Equal(versionAfter, await PalletVersionAsync(palletId));
        Assert.Equal(eventsAfter, (await EventsAsync()).Count);
    }

    [Fact]
    public async Task Exact_Location_Retry_Returns_Current_State_Without_New_Event()
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        var command = await LocationCommandAsync(palletId, "Warehouse", "COLD-02");
        await ChangeLocationAsync(palletId, command);
        var eventsAfter = (await EventsAsync()).Count;

        var result = await ChangeLocationAsync(palletId, command);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal("COLD-02", result.Value.WarehouseLocation);
        Assert.Equal(eventsAfter, (await EventsAsync()).Count);
    }

    [Fact]
    public async Task Exact_Quality_Retry_Returns_Current_State_Without_New_Event()
    {
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: "Quarantine");
        var command = await QualityCommandAsync(lineId, "Blocked", "Claim");
        await ChangeQualityAsync(palletId, lineId, command);
        var versionAfter = await LineVersionAsync(lineId);
        var eventsAfter = (await EventsAsync()).Count;

        var result = await ChangeQualityAsync(palletId, lineId, command);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal("Blocked", result.Value.QualityStatus);
        Assert.Equal(versionAfter, await LineVersionAsync(lineId));
        Assert.Equal(eventsAfter, (await EventsAsync()).Count);
    }

    [Theory]
    [InlineData("to")]
    [InlineData("actor")]
    [InlineData("reason")]
    [InlineData("reason-removed")]
    [InlineData("location-changed")]
    [InlineData("location-removed")]
    [InlineData("bin-added")]
    [InlineData("version")]
    [InlineData("other-pallet")]
    public async Task Reused_Logistics_RequestId_With_Different_Command_Is_Conflict(string change)
    {
        await SeedAsync();
        var palletId = await CreatePalletAsync("InTransit", "InTransit");
        var otherPalletId = await CreatePalletAsync("InTransit", "InTransit");
        var command = await StatusCommandAsync(palletId, "Received", "Crossdock", reason: "Arrived");
        Assert.Equal(ServiceResultStatus.Ok, (await ChangeStatusAsync(palletId, command)).Status);
        var eventsAfter = (await EventsAsync()).Count;

        var retry = new ChangeLogisticsStatusDto
        {
            RequestId = command.RequestId,
            ExpectedPalletVersion = command.ExpectedPalletVersion,
            ToStatus = command.ToStatus,
            LocationContext = command.LocationContext,
            WarehouseLocation = command.WarehouseLocation,
            ActorReference = command.ActorReference,
            Reason = command.Reason
        };
        var target = palletId;
        switch (change)
        {
            case "to": retry.ToStatus = "Delivered"; break;
            case "actor": retry.ActorReference = "someone-else"; break;
            case "reason": retry.Reason = "Different reason"; break;
            case "reason-removed": retry.Reason = null; break;
            case "location-changed": retry.LocationContext = "Warehouse"; break;
            case "location-removed": retry.LocationContext = null; break;
            case "bin-added": retry.LocationContext = "Warehouse"; retry.WarehouseLocation = "COLD-01"; break;
            case "version": retry.ExpectedPalletVersion = command.ExpectedPalletVersion + 1; break;
            case "other-pallet": target = otherPalletId; break;
        }

        var result = await ChangeStatusAsync(target, retry);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal(eventsAfter, (await EventsAsync()).Count);
        Assert.Equal("InTransit", (await LoadPalletAsync(otherPalletId)).LogisticsStatus);
    }

    [Theory]
    [InlineData("location")]
    [InlineData("bin")]
    [InlineData("actor")]
    [InlineData("reason")]
    [InlineData("version")]
    public async Task Reused_Location_RequestId_With_Different_Command_Is_Conflict(string change)
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        var command = await LocationCommandAsync(palletId, "Warehouse", "COLD-02", "Rearranged");
        await ChangeLocationAsync(palletId, command);

        var retry = new ChangePalletLocationDto
        {
            RequestId = command.RequestId,
            ExpectedPalletVersion = command.ExpectedPalletVersion,
            LocationContext = command.LocationContext,
            WarehouseLocation = command.WarehouseLocation,
            ActorReference = command.ActorReference,
            Reason = command.Reason
        };
        switch (change)
        {
            case "location": retry.LocationContext = "Crossdock"; retry.WarehouseLocation = null; break;
            case "bin": retry.WarehouseLocation = "COLD-03"; break;
            case "actor": retry.ActorReference = "someone-else"; break;
            case "reason": retry.Reason = null; break;
            case "version": retry.ExpectedPalletVersion = command.ExpectedPalletVersion - 1; break;
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeLocationAsync(palletId, retry)).Status);
        Assert.Equal("COLD-02", (await LoadPalletAsync(palletId)).WarehouseLocation);
    }

    [Theory]
    [InlineData("to")]
    [InlineData("actor")]
    [InlineData("reason")]
    [InlineData("other-line")]
    [InlineData("version")]
    public async Task Reused_Quality_RequestId_With_Different_Command_Is_Conflict(string change)
    {
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: "Quarantine");
        var otherLineId = await AddLineAsync(palletId, _seed.WarehouseLine, "LOT-OTHER");
        var command = await QualityCommandAsync(lineId, "Blocked", "Claim");
        await ChangeQualityAsync(palletId, lineId, command);

        var retry = new ChangeQualityStatusDto
        {
            RequestId = command.RequestId,
            ExpectedContentLineVersion = command.ExpectedContentLineVersion,
            ToStatus = command.ToStatus,
            ActorReference = command.ActorReference,
            Reason = command.Reason
        };
        var targetLine = lineId;
        switch (change)
        {
            case "to": retry.ToStatus = "Rejected"; break;
            case "actor": retry.ActorReference = "someone-else"; break;
            case "reason": retry.Reason = "Other claim"; break;
            case "other-line": targetLine = otherLineId; break;
            case "version": retry.ExpectedContentLineVersion = command.ExpectedContentLineVersion + 1; break;
        }

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeQualityAsync(palletId, targetLine, retry)).Status);
        Assert.Equal("Quarantine", (await LoadLineAsync(otherLineId)).QualityStatus);
        Assert.Equal("Blocked", (await LoadLineAsync(lineId)).QualityStatus);
    }

    [Fact]
    public async Task RequestId_Of_One_Command_Type_Cannot_Be_Reused_For_Another()
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        var location = await LocationCommandAsync(palletId, "Warehouse", "COLD-02");
        await ChangeLocationAsync(palletId, location);

        var status = await StatusCommandAsync(palletId, "Dispatched", "InTransit");
        status.RequestId = location.RequestId;

        Assert.Equal(ServiceResultStatus.Conflict, (await ChangeStatusAsync(palletId, status)).Status);
        Assert.Equal("Received", (await LoadPalletAsync(palletId)).LogisticsStatus);
    }

    [Fact]
    public async Task Late_Retry_Of_A_Block_After_Unblock_Does_Not_Block_Again()
    {
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: "Approved");
        var block = await QualityCommandAsync(lineId, "Blocked", "Claim");
        Assert.Equal(ServiceResultStatus.Ok, (await ChangeQualityAsync(palletId, lineId, block)).Status);
        Assert.Equal(ServiceResultStatus.Ok, (await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, "Approved", "Claim withdrawn"))).Status);
        var eventsAfter = (await EventsAsync()).Count;

        var lateRetry = await ChangeQualityAsync(palletId, lineId, block);

        Assert.Equal(ServiceResultStatus.Ok, lateRetry.Status);
        Assert.Equal("Approved", lateRetry.Value.QualityStatus);
        Assert.Equal("Approved", (await LoadLineAsync(lineId)).QualityStatus);
        Assert.Equal(eventsAfter, (await EventsAsync()).Count);
    }

    // ======================== Versies en concurrency ========================

    [Theory]
    [InlineData("status", -1)]
    [InlineData("status", 1)]
    [InlineData("location", -1)]
    [InlineData("location", 1)]
    [InlineData("quality", -1)]
    [InlineData("quality", 1)]
    public async Task Unexpected_Version_Is_Conflict_Without_Change_Or_Event(string command, int offset)
    {
        // Alleen de versie wijkt af: met de juiste versie zou elk van deze commando's slagen.
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: "Approved");
        var palletVersion = await PalletVersionAsync(palletId);
        var lineVersion = await LineVersionAsync(lineId);
        var eventsBefore = (await EventsAsync()).Count;

        ServiceResultStatus status;
        switch (command)
        {
            case "status":
                var statusCommand = await StatusCommandAsync(palletId, "Dispatched", "InTransit");
                statusCommand.ExpectedPalletVersion += offset;
                status = (await ChangeStatusAsync(palletId, statusCommand)).Status;
                break;
            case "location":
                var locationCommand = await LocationCommandAsync(palletId, "Warehouse", "COLD-02");
                locationCommand.ExpectedPalletVersion += offset;
                status = (await ChangeLocationAsync(palletId, locationCommand)).Status;
                break;
            default:
                var qualityCommand = await QualityCommandAsync(lineId, "Blocked", "Claim");
                qualityCommand.ExpectedContentLineVersion += offset;
                status = (await ChangeQualityAsync(palletId, lineId, qualityCommand)).Status;
                break;
        }

        Assert.Equal(ServiceResultStatus.Conflict, status);
        Assert.Equal(palletVersion, await PalletVersionAsync(palletId));
        Assert.Equal(lineVersion, await LineVersionAsync(lineId));
        Assert.Equal(eventsBefore, (await EventsAsync()).Count);
    }

    [Fact]
    public async Task Versions_Start_At_Zero_And_Increase_After_Every_Successful_Change()
    {
        await SeedAsync();
        var palletId = await CreatePalletAsync("InTransit", "InTransit");
        Assert.Equal(0, await PalletVersionAsync(palletId));

        var lineId = await AddLineAsync(palletId, _seed.WarehouseLine);
        Assert.Equal(0, await LineVersionAsync(lineId));
        Assert.Equal(1, await PalletVersionAsync(palletId)); // inhoud toevoegen raakt de pallet

        await MoveAsync(palletId, "Received", "Warehouse");
        Assert.Equal(2, await PalletVersionAsync(palletId));

        Assert.Equal(ServiceResultStatus.Ok, (await ChangeLocationAsync(palletId, await LocationCommandAsync(palletId, "Warehouse", "COLD-04"))).Status);
        Assert.Equal(3, await PalletVersionAsync(palletId));

        await SetQualityAsync(palletId, lineId, "Approved");
        Assert.Equal(1, await LineVersionAsync(lineId));
        Assert.Equal(4, await PalletVersionAsync(palletId)); // kwaliteit wijzigen raakt de pallet

        await using var db = _factory.CreateContext();
        var dto = (await new PalletService(db).GetAsync(palletId)).Value;
        Assert.Equal(4, dto.Version);
        Assert.Equal(1, dto.ContentLines.Single().Version);
        Assert.Equal(4, dto.ContentLines.Single().PalletVersion);
    }

    [Fact]
    public async Task Successful_Quality_Change_Increments_Line_And_Pallet_Version_And_Returns_Both()
    {
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: "Approved");
        var palletVersionBefore = await PalletVersionAsync(palletId);
        var lineVersionBefore = await LineVersionAsync(lineId);

        var result = await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, "Blocked", "Customer claim"));

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(lineVersionBefore + 1, await LineVersionAsync(lineId));
        Assert.Equal(palletVersionBefore + 1, await PalletVersionAsync(palletId));
        Assert.Equal(lineVersionBefore + 1, result.Value.Version);
        Assert.Equal(palletVersionBefore + 1, result.Value.PalletVersion);

        // Met de teruggegeven palletversie kan de client direct verder, zonder eerst te herladen.
        var block = await ChangeStatusAsync(palletId, new ChangeLogisticsStatusDto
        {
            RequestId = Guid.NewGuid(),
            ExpectedPalletVersion = result.Value.PalletVersion,
            ToStatus = "Dispatched",
            LocationContext = "InTransit",
            ActorReference = Actor
        });
        Assert.Equal(ServiceResultStatus.Conflict, block.Status); // regel is Blocked: geen dispatch
        Assert.DoesNotContain("version", block.Message, StringComparison.OrdinalIgnoreCase);
    }

    // Pallet Received in het magazijn met één Approved regel, beide nog op versie 0 (rechtstreeks opgeslagen).
    private async Task<(int PalletId, int LineId)> ReceivedPalletWithApprovedLineAtVersionZeroAsync()
    {
        await SeedAsync();
        await using var db = _factory.CreateContext();
        var pallet = new Pallet { Reference = "PAL-RACE", LogisticsStatus = "Received", LocationContext = "Warehouse", WarehouseLocation = "COLD-01" };
        var line = new PalletContentLine
        {
            RequestId = Guid.NewGuid(),
            Pallet = pallet,
            SupplyCommitmentLineId = _seed.WarehouseLine,
            LotNumber = "LOT-RACE",
            Quantity = 4m,
            Unit = "box",
            NetWeightKg = 16m,
            QualityStatus = "Approved"
        };
        db.AddRange(pallet, line);
        await db.SaveChangesAsync();
        Assert.Equal(0, pallet.Version);
        Assert.Equal(0, line.Version);
        return (pallet.Id, line.Id);
    }

    // Deterministische race: het eerste commando heeft alles gevalideerd en staat op het punt op te slaan; precies
    // dan voert een tweede context het andere commando volledig uit. Beide zijn gebaseerd op dezelfde versies (0).
    [Theory]
    [InlineData("dispatch-saves-last")]
    [InlineData("quality-saves-last")]
    public async Task Concurrent_Dispatch_And_Block_Never_End_As_Dispatched_With_Blocked_Content(string order)
    {
        var (palletId, lineId) = await ReceivedPalletWithApprovedLineAtVersionZeroAsync();
        var dispatch = new ChangeLogisticsStatusDto
        {
            RequestId = Guid.NewGuid(),
            ExpectedPalletVersion = 0,
            ToStatus = "Dispatched",
            LocationContext = "InTransit",
            ActorReference = "warehouse-1"
        };
        var block = new ChangeQualityStatusDto
        {
            RequestId = Guid.NewGuid(),
            ExpectedContentLineVersion = 0,
            ToStatus = "Blocked",
            ActorReference = "qc-1",
            Reason = "Recall"
        };

        ServiceResultStatus dispatchStatus = default, blockStatus = default;
        if (order == "dispatch-saves-last")
        {
            var concurrentBlock = new ConcurrentChangeInterceptor(async () =>
                blockStatus = (await ChangeQualityAsync(palletId, lineId, block)).Status);
            dispatchStatus = (await ChangeStatusAsync(palletId, dispatch, concurrentBlock)).Status;
        }
        else
        {
            var concurrentDispatch = new ConcurrentChangeInterceptor(async () =>
                dispatchStatus = (await ChangeStatusAsync(palletId, dispatch)).Status);
            blockStatus = (await ChangeQualityAsync(palletId, lineId, block, concurrentDispatch)).Status;
        }

        // Precies één wint; de verliezer krijgt 409.
        var statuses = new[] { dispatchStatus, blockStatus };
        Assert.Equal(1, statuses.Count(s => s == ServiceResultStatus.Ok));
        Assert.Equal(1, statuses.Count(s => s == ServiceResultStatus.Conflict));
        Assert.Equal(order == "dispatch-saves-last" ? ServiceResultStatus.Conflict : ServiceResultStatus.Ok, dispatchStatus);

        // Nooit Dispatched met Blocked inhoud.
        var pallet = await LoadPalletAsync(palletId);
        var line = await LoadLineAsync(lineId);
        Assert.False(pallet.LogisticsStatus == "Dispatched" && line.QualityStatus == "Blocked");
        Assert.Equal(order == "dispatch-saves-last" ? ("Received", "Blocked") : ("Dispatched", "Approved"), (pallet.LogisticsStatus, line.QualityStatus));

        // Alleen de winnaar heeft een auditrecord; de verliezer schreef niets.
        var events = await EventsAsync();
        var winner = Assert.Single(events);
        Assert.Equal(order == "dispatch-saves-last" ? "QualityStatusChanged" : "LogisticsStatusChanged", winner.EventType);
        Assert.Equal(order == "dispatch-saves-last" ? block.RequestId : dispatch.RequestId, winner.RequestId);
        Assert.Equal(1, pallet.Version);
    }

    private sealed class ConcurrentChangeInterceptor : SaveChangesInterceptor
    {
        private readonly Func<Task> _concurrentChange;
        private bool _done;

        public ConcurrentChangeInterceptor(Func<Task> concurrentChange) => _concurrentChange = concurrentChange;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await _concurrentChange();
            }
            return result;
        }
    }

    private ConcurrentChangeInterceptor TouchPalletConcurrently(int palletId) => new(async () =>
    {
        await using var other = _factory.CreateContext();
        (await other.Pallets.SingleAsync(p => p.Id == palletId)).PalletType = "Changed by someone else";
        await other.SaveChangesAsync();
    });

    [Fact]
    public async Task Concurrent_Pallet_Change_During_Logistics_Command_Is_Conflict_Without_Event()
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        var eventsBefore = (await EventsAsync()).Count;

        var result = await ChangeStatusAsync(palletId, await StatusCommandAsync(palletId, "Dispatched", "InTransit"), TouchPalletConcurrently(palletId));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal("Received", (await LoadPalletAsync(palletId)).LogisticsStatus);
        Assert.Equal(eventsBefore, (await EventsAsync()).Count);
    }

    [Fact]
    public async Task Concurrent_Pallet_Change_During_Location_Command_Is_Conflict_Without_Event()
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        var eventsBefore = (await EventsAsync()).Count;

        var result = await ChangeLocationAsync(palletId, await LocationCommandAsync(palletId, "Warehouse", "COLD-02"), TouchPalletConcurrently(palletId));

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal("COLD-01", (await LoadPalletAsync(palletId)).WarehouseLocation);
        Assert.Equal(eventsBefore, (await EventsAsync()).Count);
    }

    [Fact]
    public async Task Concurrent_Quality_Change_Is_Conflict_Without_Event()
    {
        var (palletId, lineId) = await PalletInStateAsync("Received", quality: "Quarantine");
        var eventsBefore = (await EventsAsync()).Count;
        var concurrent = new ConcurrentChangeInterceptor(async () =>
        {
            await using var other = _factory.CreateContext();
            (await other.PalletContentLines.SingleAsync(c => c.Id == lineId)).QualityReason = "Someone else";
            await other.SaveChangesAsync();
        });

        var result = await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, "Approved"), concurrent);

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal("Quarantine", (await LoadLineAsync(lineId)).QualityStatus);
        Assert.Equal(eventsBefore, (await EventsAsync()).Count);
    }

    [Fact]
    public async Task Concurrent_Pallet_Change_While_Adding_Content_Is_Conflict()
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        var linesBefore = (await LoadPalletLinesAsync(palletId)).Count;

        await using var db = _factory.CreateContext(TouchPalletConcurrently(palletId));
        var result = await new PalletService(db).AddContentLineAsync(palletId, new AddPalletContentLineDto
        {
            RequestId = Guid.NewGuid(),
            SupplyCommitmentLineId = _seed.WarehouseLine,
            LotNumber = "LOT-RACE",
            Quantity = 1m,
            Unit = "box",
            NetWeightKg = 4m,
            ActorReference = Actor
        });

        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        Assert.Equal(linesBefore, (await LoadPalletLinesAsync(palletId)).Count);
    }

    private async Task<List<PalletContentLine>> LoadPalletLinesAsync(int palletId)
    {
        await using var db = _factory.CreateContext();
        return await db.PalletContentLines.Where(c => c.PalletId == palletId).ToListAsync();
    }

    // ======================== Audit lezen en neveneffecten ========================

    [Fact]
    public async Task Audit_Events_Are_Returned_In_Order_And_Never_Rewritten()
    {
        var (palletId, _) = await PalletInStateAsync("Received");
        var initial = await EventsAsync();
        await MoveAsync(palletId, "Dispatched", "InTransit");
        await MoveAsync(palletId, "Delivered", "Customer");

        await using var db = _factory.CreateContext();
        var result = await new PalletService(db).GetAuditEventsAsync(palletId);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(
            new[] { "PalletCreated", "ContentLineAdded", "QualityStatusChanged", "LogisticsStatusChanged", "LogisticsStatusChanged" },
            result.Value.Select(e => e.EventType));
        Assert.All(result.Value, e => Assert.Equal(DateTimeKind.Utc, e.OccurredAt.Kind));

        var after = await EventsAsync();
        foreach (var original in initial)
        {
            var current = after.Single(e => e.Id == original.Id);
            Assert.Equal(original.OldValue, current.OldValue);
            Assert.Equal(original.NewValue, current.NewValue);
            Assert.Equal(original.ActorReference, current.ActorReference);
            Assert.Equal(original.Reason, current.Reason);
        }
    }

    [Fact]
    public async Task Audit_Events_Of_Unknown_Pallet_Is_NotFound()
    {
        await using var db = _factory.CreateContext();
        Assert.Equal(ServiceResultStatus.NotFound, (await new PalletService(db).GetAuditEventsAsync(999_999)).Status);
    }

    [Fact]
    public async Task State_Changes_Have_No_Stock_Reservation_Or_Commitment_Side_Effects()
    {
        var (palletId, lineId) = await PalletInStateAsync("Received");
        await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, "Blocked", "Claim"));
        await ChangeQualityAsync(palletId, lineId, await QualityCommandAsync(lineId, "Approved", "Claim withdrawn"));
        await MoveAsync(palletId, "Dispatched", "InTransit");

        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.StockMovements.CountAsync());
        Assert.Equal(0, await verify.StockReservations.CountAsync());
        Assert.Equal(0, await verify.StockReceipts.CountAsync());
        Assert.Equal(0, await verify.Inventory.CountAsync());
        Assert.All(await verify.SupplyCommitments.ToListAsync(), c => Assert.Equal("Expected", c.Status));
    }
}
