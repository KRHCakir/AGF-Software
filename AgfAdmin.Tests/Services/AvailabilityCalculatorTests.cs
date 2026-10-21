using AgfAdmin.DTOs;
using AgfAdmin.Services;

namespace AgfAdmin.Tests.Services;

// Pure berekening, zonder database: invoerfeiten -> overzicht/drill-down.
public class AvailabilityCalculatorTests
{
    private const int Variant = 1;
    private int _nextContentId = 100;

    private static SourceLineFact Source(
        int id,
        decimal quantity,
        string unit = "box",
        string route = "Warehouse",
        int? customerId = null,
        string status = "Expected",
        int variantId = Variant,
        decimal? declaredKg = null,
        string lot = null) => new(
            SupplyCommitmentLineId: id,
            SupplyCommitmentId: id * 10,
            Route: route,
            CustomerId: customerId,
            CustomerName: customerId == null ? null : $"Customer {customerId}",
            CommitmentStatus: status,
            ExpectedAt: new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc),
            ProductVariantId: variantId,
            ProductName: variantId == Variant ? "Avocado" : "Banana",
            VariantName: variantId == Variant ? "Hass 6" : "Cavendish",
            LotNumber: lot,
            Unit: unit,
            ExpectedQuantity: quantity,
            DeclaredExpectedNetWeightKg: declaredKg);

    private ContentLineFact Content(
        SourceLineFact source,
        decimal quantity,
        decimal kg,
        string logistics = "Received",
        string location = "Warehouse",
        string quality = "Approved",
        string unit = null,
        string lot = "LOT-1") => new(
            PalletContentLineId: _nextContentId++,
            PalletId: 7,
            PalletReference: "PAL-7",
            LogisticsStatus: logistics,
            LocationContext: location,
            WarehouseLocation: location == "Warehouse" && logistics == "Received" ? "COLD-01" : null,
            SupplyCommitmentLineId: source.SupplyCommitmentLineId,
            Route: source.Route,
            CustomerId: source.CustomerId,
            CustomerName: source.CustomerName,
            ProductVariantId: source.ProductVariantId,
            ProductName: source.ProductName,
            VariantName: source.VariantName,
            LotNumber: lot,
            Unit: unit ?? source.Unit,
            Quantity: quantity,
            NetWeightKg: kg,
            QualityStatus: quality);

    private static List<VariantAvailabilityDto> Overview(IEnumerable<SourceLineFact> sources, IEnumerable<ContentLineFact> contents, AvailabilityQuery query = null) =>
        AvailabilityCalculator.BuildOverview(sources.ToList(), contents.ToList(), query ?? new AvailabilityQuery());

    private static WarehouseSupplyDto WarehouseEntry(List<VariantAvailabilityDto> overview, string unit = "box") =>
        Assert.Single(Assert.Single(overview).WarehouseSupply, e => e.Unit == unit);

    private static AvailabilityQuantityDto Bucket(QualityBreakdownDto breakdown, string quality) => quality switch
    {
        "Approved" => breakdown.Approved,
        "Quarantine" => breakdown.Quarantine,
        "Blocked" => breakdown.Blocked,
        _ => breakdown.Rejected
    };

    // ---- Gewichtsbasis: gewogen, berekend of gemengd ----

    [Fact]
    public void WeightBasis_Reflects_Weighed_Calculated_Or_Mixed_Content_With_Stock()
    {
        var source = Source(1, 100m);
        var weighed = Content(source, 10m, 40m);
        var calculated = Content(source, 20m, 80m) with { WeightMethod = WeightRules.Calculated, NetKgPerPackage = 4m };
        var depletedCalculated = Content(source, 5m, 20m) with { WeightMethod = WeightRules.Calculated, NetKgPerPackage = 4m, DispatchedQuantity = 5m, DispatchedNetWeightKg = 20m };

        Assert.Equal("weighed", WarehouseEntry(Overview(new[] { source }, new[] { weighed })).WeightBasis);
        Assert.Equal("calculated", WarehouseEntry(Overview(new[] { source }, new[] { calculated })).WeightBasis);
        Assert.Equal("mixed", WarehouseEntry(Overview(new[] { source }, new[] { weighed, calculated })).WeightBasis);
        // Uitgeputte inhoud bepaalt de basis niet meer; zonder voorraad op pallets is de basis onbekend (null).
        Assert.Equal("weighed", WarehouseEntry(Overview(new[] { source }, new[] { weighed, depletedCalculated })).WeightBasis);
        Assert.Null(WarehouseEntry(Overview(new[] { source }, new[] { depletedCalculated })).WeightBasis);

        var drillDown = AvailabilityCalculator.BuildDrillDown(new[] { source }, new[] { calculated }, new AvailabilityQuery { ProductVariantId = Variant });
        var line = Assert.Single(drillDown.ContentLines);
        Assert.Equal(WeightRules.Calculated, line.WeightMethod);
        Assert.Equal(4m, line.NetKgPerPackage);
    }

    // ---- Beschikbaar en kwaliteit ----

    [Fact]
    public void Approved_Received_Warehouse_Stock_Is_Available()
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160.125m) }));

        Assert.Equal(40m, entry.AvailableBeforeReservations.Quantity);
        Assert.Equal(160.125m, entry.AvailableBeforeReservations.NetWeightKg);
        Assert.Equal(40m, entry.Warehouse.Approved.Quantity);
        Assert.Equal(160.125m, entry.Warehouse.Approved.NetWeightKg);
    }

    [Fact]
    public void Reservations_Are_Applied_And_Reserved_Is_Zero_Without_Sales_Reservations()
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160m) }));

        Assert.Equal((0m, 0m, true), (entry.Reserved.Quantity, entry.Reserved.NetWeightKg, entry.Reserved.Applied));
        Assert.Equal((40m, 160m), (entry.AvailableAfterReservations.Quantity, entry.AvailableAfterReservations.NetWeightKg));
    }

    [Theory]
    [InlineData("Quarantine")]
    [InlineData("Blocked")]
    [InlineData("Rejected")]
    public void Non_Approved_Quality_Is_Shown_Separately_And_Never_Available(string quality)
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160m, quality: quality) }));

        Assert.Equal(0m, entry.AvailableBeforeReservations.Quantity);
        Assert.Equal(0m, entry.AvailableBeforeReservations.NetWeightKg);
        Assert.Equal(0m, entry.Warehouse.Approved.Quantity);
        Assert.Equal(40m, Bucket(entry.Warehouse, quality).Quantity);
        Assert.Equal(160m, Bucket(entry.Warehouse, quality).NetWeightKg);
    }

    [Theory]
    [InlineData("Crossdock")]
    [InlineData("Customer")]
    [InlineData("External")]
    [InlineData("Supplier")]
    [InlineData("InTransit")]
    public void Received_Content_Outside_The_Warehouse_Is_Physical_But_Not_Available(string location)
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160m, location: location) }));

        Assert.Equal(40m, entry.ReceivedOutsideWarehouse.Approved.Quantity);
        Assert.Equal(0m, entry.Warehouse.Approved.Quantity);
        Assert.Equal(0m, entry.AvailableBeforeReservations.Quantity);
    }

    [Theory]
    [InlineData("Expected", "Supplier")]
    [InlineData("InTransit", "InTransit")]
    public void Approved_Content_On_Expected_Or_InTransit_Pallet_Is_Not_Available(string logistics, string location)
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160m, logistics, location) }));

        var category = logistics == "Expected" ? entry.ExpectedOnPallets : entry.InTransit;
        Assert.Equal(40m, category.Approved.Quantity);
        Assert.Equal(0m, entry.AvailableBeforeReservations.Quantity);
    }

    // ---- Verwacht zonder dubbeltelling ----

    [Fact]
    public void Registered_Content_Is_Subtracted_From_Expected_Supply()
    {
        var source = Source(1, 100m, declaredKg: 400m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160.125m, "InTransit", "InTransit", "Quarantine") }));

        Assert.Equal(40m, entry.InTransit.Quarantine.Quantity);
        Assert.Equal(60m, entry.ExpectedUnregistered.Quantity);
        // Nooit een deel van het opgegeven verwachte gewicht toerekenen: dat zou een schatting zijn.
        Assert.Null(entry.ExpectedUnregistered.NetWeightKg);
    }

    [Fact]
    public void Over_Registration_Leaves_No_Unregistered_Expected_And_Warns()
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 120m, 480m) }));

        Assert.Equal(0m, entry.ExpectedUnregistered.Quantity);
        var warning = Assert.Single(entry.Warnings);
        Assert.Equal("OVER_REGISTERED", warning.Code);
        Assert.Equal(1, warning.SupplyCommitmentLineId);
        Assert.Equal(20m, warning.Quantity);
    }

    [Fact]
    public void Cancelled_Pallet_Content_Is_Not_Shown_And_Does_Not_Reduce_Expected()
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160m, "Cancelled", "Supplier", "Quarantine") }));

        Assert.Equal(100m, entry.ExpectedUnregistered.Quantity);
        Assert.Equal(0m, entry.ExpectedOnPallets.Quarantine.Quantity);
        Assert.Equal(0m, entry.Warehouse.Quarantine.Quantity);
    }

    [Theory]
    [InlineData("Dispatched", "InTransit")]
    [InlineData("Delivered", "Customer")]
    public void Dispatched_Or_Delivered_Content_Is_Not_Shown_But_Counts_As_Registered(string logistics, string location)
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160m, logistics, location) }));

        Assert.Equal(60m, entry.ExpectedUnregistered.Quantity);
        Assert.Equal(0m, entry.AvailableBeforeReservations.Quantity);
        Assert.Equal(0m, entry.ReceivedOutsideWarehouse.Approved.Quantity);
        Assert.Equal(0m, entry.InTransit.Approved.Quantity);
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Received")]
    [InlineData("Delivered")]
    public void Closed_Commitment_Has_No_Unregistered_Expected_But_Its_Content_Stays_Visible(string status)
    {
        var source = Source(1, 100m, status: status);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160m) }));

        Assert.Equal(0m, entry.ExpectedUnregistered.Quantity);
        Assert.Equal(40m, entry.AvailableBeforeReservations.Quantity);
    }

    [Fact]
    public void Closed_Commitment_Without_Content_Produces_No_Entry()
    {
        Assert.Empty(Overview(new[] { Source(1, 100m, status: "Cancelled") }, Array.Empty<ContentLineFact>()));
    }

    [Fact]
    public void PartiallyReceived_Commitment_Is_Still_Open()
    {
        var source = Source(1, 100m, status: "PartiallyReceived");

        Assert.Equal(100m, WarehouseEntry(Overview(new[] { source }, Array.Empty<ContentLineFact>())).ExpectedUnregistered.Quantity);
    }

    // ---- DirectTrade ----

    [Fact]
    public void DirectTrade_Is_Only_Shown_For_Its_Customer_And_Never_As_Warehouse_Supply()
    {
        var source = Source(1, 100m, route: "DirectTrade", customerId: 7);
        var contents = new[]
        {
            Content(source, 30m, 120m, "InTransit", "InTransit"),
            Content(source, 20m, 80m, "Received", "Crossdock")
        };

        var variant = Assert.Single(Overview(new[] { source }, contents));

        Assert.Empty(variant.WarehouseSupply);
        var entry = Assert.Single(variant.DirectTradeSupply);
        Assert.Equal(7, entry.CustomerId);
        Assert.Equal("Customer 7", entry.CustomerName);
        Assert.True(entry.CustomerBound);
        Assert.Equal(30m, entry.InTransit.Approved.Quantity);
        Assert.Equal(20m, entry.ReceivedOutsideWarehouse.Approved.Quantity);
        Assert.Equal(50m, entry.ExpectedUnregistered.Quantity);
    }

    [Fact]
    public void DirectTrade_Response_Shape_Has_No_Available_Or_Warehouse_Field()
    {
        // De vorm zelf maakt het onmogelijk DirectTrade op te tellen bij magazijnbeschikbaarheid.
        Assert.Null(typeof(DirectTradeSupplyDto).GetProperty("AvailableBeforeReservations"));
        Assert.Null(typeof(DirectTradeSupplyDto).GetProperty("Warehouse"));
        Assert.Null(typeof(DirectTradeSupplyDto).GetProperty("Reserved"));
        Assert.NotNull(typeof(WarehouseSupplyDto).GetProperty("AvailableBeforeReservations"));
    }

    [Fact]
    public void Json_Shape_Shows_Unknown_Reservations_And_No_Available_Field_For_DirectTrade()
    {
        var warehouse = Source(1, 10m);
        var direct = Source(2, 5m, route: "DirectTrade", customerId: 7);
        var variant = Assert.Single(Overview(new[] { warehouse, direct }, Array.Empty<ContentLineFact>()));
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);

        var warehouseJson = System.Text.Json.JsonSerializer.Serialize(variant.WarehouseSupply.Single(), web);
        Assert.Contains("\"reserved\":{\"quantity\":0,\"netWeightKg\":0,\"applied\":true}", warehouseJson);
        Assert.Contains("\"reservedOnHold\":{\"quantity\":0,\"netWeightKg\":0}", warehouseJson);
        Assert.Contains("\"availableAfterReservations\":{\"quantity\":0,\"netWeightKg\":0}", warehouseJson);
        Assert.Contains("\"availableBeforeReservations\":{\"quantity\":0,\"netWeightKg\":0}", warehouseJson);
        Assert.Contains("\"expectedUnregistered\":{\"quantity\":10,\"netWeightKg\":null}", warehouseJson);

        var directJson = System.Text.Json.JsonSerializer.Serialize(variant.DirectTradeSupply.Single(), web);
        Assert.Contains("\"customerBound\":true", directJson);
        Assert.DoesNotContain("available", directJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"warehouse\"", directJson);
        Assert.DoesNotContain("reserved", directJson);
    }

    [Fact]
    public void DirectTrade_For_Different_Customers_Is_Never_Combined()
    {
        var forSeven = Source(1, 10m, route: "DirectTrade", customerId: 7);
        var forEight = Source(2, 20m, route: "DirectTrade", customerId: 8);

        var entries = Assert.Single(Overview(new[] { forSeven, forEight }, Array.Empty<ContentLineFact>())).DirectTradeSupply;

        Assert.Equal(new int?[] { 7, 8 }, entries.Select(e => e.CustomerId));
        Assert.Equal(new[] { 10m, 20m }, entries.Select(e => e.ExpectedUnregistered.Quantity));
    }

    [Fact]
    public void DirectTrade_Content_Classified_In_Warehouse_Is_Never_Counted_But_Flagged()
    {
        // Kan via de API niet ontstaan (guard-regel 4); legacy/SQL-data mag de magazijnvoorraad nooit ophogen.
        var source = Source(1, 10m, route: "DirectTrade", customerId: 7);
        var content = Content(source, 5m, 20m);

        var variant = Assert.Single(Overview(new[] { source }, new[] { content }));

        Assert.Empty(variant.WarehouseSupply);
        var warning = Assert.Single(Assert.Single(variant.DirectTradeSupply).Warnings);
        Assert.Equal("DIRECT_TRADE_IN_WAREHOUSE", warning.Code);
        Assert.Equal(content.PalletContentLineId, warning.PalletContentLineId);
    }

    // ---- Eenheden, partijen en gewichten ----

    [Fact]
    public void Different_Units_Are_Never_Merged_And_Have_No_Variant_Total()
    {
        var boxes = Source(1, 100m, unit: "box");
        var colli = Source(2, 50m, unit: "colli");

        var overview = Overview(new[] { boxes, colli }, new[] { Content(boxes, 40m, 160m), Content(colli, 10m, 55m) });

        Assert.Equal(new[] { "box", "colli" }, Assert.Single(overview).WarehouseSupply.Select(e => e.Unit));
        Assert.Equal(40m, WarehouseEntry(overview, "box").AvailableBeforeReservations.Quantity);
        Assert.Equal(10m, WarehouseEntry(overview, "colli").AvailableBeforeReservations.Quantity);
        Assert.Equal(60m, WarehouseEntry(overview, "box").ExpectedUnregistered.Quantity);
        Assert.Equal(40m, WarehouseEntry(overview, "colli").ExpectedUnregistered.Quantity);
        Assert.Null(typeof(VariantAvailabilityDto).GetProperty("TotalQuantity"));
    }

    [Fact]
    public void Content_In_A_Different_Unit_Than_Its_Source_Is_Not_Subtracted()
    {
        var source = Source(1, 100m, unit: "box");

        var overview = Overview(new[] { source }, new[] { Content(source, 30m, 90m, unit: "colli") });

        Assert.Equal(100m, WarehouseEntry(overview, "box").ExpectedUnregistered.Quantity);
        Assert.Equal(30m, WarehouseEntry(overview, "colli").AvailableBeforeReservations.Quantity);
    }

    [Fact]
    public void Lots_Of_The_Same_Variant_And_Unit_Are_Summed_In_The_Overview()
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160.125m, lot: "LOT-A"), Content(source, 3m, 11.111m, lot: "LOT-B") }));

        Assert.Equal(43m, entry.AvailableBeforeReservations.Quantity);
        Assert.Equal(171.236m, entry.AvailableBeforeReservations.NetWeightKg);
    }

    [Fact]
    public void Weights_Are_Summed_Only_From_Actual_Content_Lines()
    {
        var withContent = Source(1, 100m, declaredKg: 400m);
        var withoutContent = Source(2, 50m, unit: "tray", declaredKg: 200m);

        var overview = Overview(new[] { withContent, withoutContent }, new[] { Content(withContent, 40m, 160.125m), Content(withContent, 3m, 11.111m, quality: "Quarantine") });

        var box = WarehouseEntry(overview, "box");
        Assert.Equal(160.125m, box.Warehouse.Approved.NetWeightKg);
        Assert.Equal(11.111m, box.Warehouse.Quarantine.NetWeightKg);
        Assert.Null(box.ExpectedUnregistered.NetWeightKg);

        var tray = WarehouseEntry(overview, "tray");
        Assert.Equal(0m, tray.Warehouse.Approved.NetWeightKg);
        Assert.Equal(50m, tray.ExpectedUnregistered.Quantity);
        Assert.Null(tray.ExpectedUnregistered.NetWeightKg);
    }

    [Fact]
    public void Legacy_Source_Without_Unit_Is_Shown_Separately_With_Warning_And_Nothing_Subtracted()
    {
        var legacy = Source(1, 100m, unit: null);

        var overview = Overview(new[] { legacy }, new[] { Content(legacy, 30m, 90m, unit: "tray") });

        var unknown = WarehouseEntry(overview, null);
        Assert.Equal(100m, unknown.ExpectedUnregistered.Quantity);
        var warning = Assert.Single(unknown.Warnings);
        Assert.Equal("SOURCE_UNIT_UNKNOWN", warning.Code);
        Assert.Equal(1, warning.SupplyCommitmentLineId);
        Assert.Equal(30m, WarehouseEntry(overview, "tray").AvailableBeforeReservations.Quantity);
        Assert.Equal(new[] { "tray", null }, Assert.Single(overview).WarehouseSupply.Select(e => e.Unit));
    }

    // ---- Volgorde, leegte en filters ----

    [Fact]
    public void Empty_Input_Gives_Empty_Overview()
    {
        Assert.Empty(Overview(Array.Empty<SourceLineFact>(), Array.Empty<ContentLineFact>()));
    }

    [Fact]
    public void Ordering_Is_Deterministic()
    {
        var sources = new[]
        {
            Source(1, 1m, unit: "dozen"),
            Source(2, 1m, unit: "box"),
            Source(3, 1m, unit: "tray"),
            Source(4, 1m, route: "DirectTrade", customerId: 9),
            Source(5, 1m, route: "DirectTrade", customerId: 3),
            Source(6, 1m, variantId: 2)
        };

        var overview = Overview(sources, Array.Empty<ContentLineFact>());

        Assert.Equal(new[] { "Avocado", "Banana" }, overview.Select(v => v.ProductName));
        Assert.Equal(new[] { "box", "tray", "dozen" }, overview[0].WarehouseSupply.Select(e => e.Unit));
        Assert.Equal(new int?[] { 3, 9 }, overview[0].DirectTradeSupply.Select(e => e.CustomerId));
    }

    [Fact]
    public void Filters_On_Unit_Route_And_Variant_Apply_To_The_Overview()
    {
        var box = Source(1, 10m, unit: "box");
        var colli = Source(2, 20m, unit: "colli");
        var direct = Source(3, 30m, route: "DirectTrade", customerId: 7);
        var otherVariant = Source(4, 40m, variantId: 2);
        var sources = new[] { box, colli, direct, otherVariant };

        Assert.Equal(new[] { "box" }, Overview(sources, Array.Empty<ContentLineFact>(), new AvailabilityQuery { Unit = "box" })[0].WarehouseSupply.Select(e => e.Unit));

        var warehouseOnly = Overview(sources, Array.Empty<ContentLineFact>(), new AvailabilityQuery { Route = "Warehouse" });
        Assert.All(warehouseOnly, v => Assert.Empty(v.DirectTradeSupply));

        var directOnly = Overview(sources, Array.Empty<ContentLineFact>(), new AvailabilityQuery { Route = "DirectTrade" });
        var directVariant = Assert.Single(directOnly);
        Assert.Empty(directVariant.WarehouseSupply);
        Assert.Single(directVariant.DirectTradeSupply);

        Assert.Equal(new[] { 2 }, Overview(sources, Array.Empty<ContentLineFact>(), new AvailabilityQuery { ProductVariantId = 2 }).Select(v => v.ProductVariantId));
    }

    [Fact]
    public void Customer_Filter_Restricts_Only_DirectTrade_And_Keeps_Warehouse_Supply()
    {
        var warehouse = Source(1, 10m);
        var forSeven = Source(2, 20m, route: "DirectTrade", customerId: 7);
        var forEight = Source(3, 30m, route: "DirectTrade", customerId: 8);

        var variant = Assert.Single(Overview(new[] { warehouse, forSeven, forEight }, Array.Empty<ContentLineFact>(), new AvailabilityQuery { CustomerId = 7 }));

        Assert.Equal(10m, Assert.Single(variant.WarehouseSupply).ExpectedUnregistered.Quantity);
        Assert.Equal(7, Assert.Single(variant.DirectTradeSupply).CustomerId);
    }

    // ---- Drill-down ----

    private (List<SourceLineAvailabilityDto> SourceLines, List<ContentLineAvailabilityDto> ContentLines) DrillDown(
        IEnumerable<SourceLineFact> sources, IEnumerable<ContentLineFact> contents, AvailabilityQuery query = null) =>
        AvailabilityCalculator.BuildDrillDown(sources.ToList(), contents.ToList(), query ?? new AvailabilityQuery { ProductVariantId = Variant });

    [Theory]
    [InlineData("Dispatched", "InTransit")]
    [InlineData("Delivered", "Customer")]
    [InlineData("Cancelled", "Supplier")]
    public void Closed_Content_Is_Hidden_By_Default_And_Shown_As_Closed_With_Its_Real_Status(string logistics, string location)
    {
        var source = Source(1, 100m);
        var closed = Content(source, 40m, 160m, logistics, location);
        var active = Content(source, 5m, 20m);

        var hidden = DrillDown(new[] { source }, new[] { closed, active });
        Assert.Equal(new[] { active.PalletContentLineId }, hidden.ContentLines.Select(c => c.PalletContentLineId));

        var shown = DrillDown(new[] { source }, new[] { closed, active }, new AvailabilityQuery { ProductVariantId = Variant, IncludeClosed = true });
        var closedLine = Assert.Single(shown.ContentLines, c => c.PalletContentLineId == closed.PalletContentLineId);
        Assert.Equal("closed", closedLine.Category);
        Assert.Equal(logistics, closedLine.LogisticsStatus);
        Assert.True(closedLine.Closed);
        Assert.False(closedLine.SellableNow);
    }

    [Fact]
    public void Closed_Content_Never_Counts_In_The_Overview_Even_When_Approved()
    {
        var source = Source(1, 100m);

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { Content(source, 40m, 160m, "Dispatched", "InTransit"), Content(source, 5m, 20m) }));

        Assert.Equal(5m, entry.AvailableBeforeReservations.Quantity);
        Assert.Equal(5m, entry.Warehouse.Approved.Quantity + entry.ReceivedOutsideWarehouse.Approved.Quantity + entry.InTransit.Approved.Quantity + entry.ExpectedOnPallets.Approved.Quantity);
    }

    [Fact]
    public void DrillDown_Source_Lines_Show_Expected_Registered_And_Unregistered()
    {
        var source = Source(1, 100m, declaredKg: 400m, lot: "PROVISIONAL");
        var legacy = Source(2, 50m, unit: null);
        var contents = new[]
        {
            Content(source, 40m, 160m, "InTransit", "InTransit", "Quarantine"),
            Content(source, 10m, 40m, "Cancelled", "Supplier")
        };

        var result = DrillDown(new[] { source, legacy }, contents);

        var line = Assert.Single(result.SourceLines, s => s.SupplyCommitmentLineId == 1);
        Assert.Equal(100m, line.ExpectedQuantity);
        Assert.Equal(400m, line.DeclaredExpectedNetWeightKg);
        Assert.Equal(40m, line.RegisteredQuantity);
        Assert.Equal(60m, line.UnregisteredQuantity);
        Assert.Equal("PROVISIONAL", line.LotNumber);
        Assert.Equal(DateTimeKind.Utc, line.ExpectedAt!.Value.Kind);

        var unknown = Assert.Single(result.SourceLines, s => s.SupplyCommitmentLineId == 2);
        Assert.Null(unknown.RegisteredQuantity);
        Assert.Equal(50m, unknown.UnregisteredQuantity);
        Assert.Equal("SOURCE_UNIT_UNKNOWN", Assert.Single(unknown.Warnings).Code);
    }

    [Fact]
    public void DrillDown_Keeps_Lots_Separate_And_Marks_Only_Approved_Warehouse_Content_Sellable()
    {
        var source = Source(1, 100m);
        var contents = new[]
        {
            Content(source, 40m, 160m, lot: "LOT-A"),
            Content(source, 3m, 11m, lot: "LOT-B", quality: "Blocked"),
            Content(source, 7m, 28m, "Received", "Crossdock", lot: "LOT-C")
        };

        var result = DrillDown(new[] { source }, contents);

        Assert.Equal(new[] { "LOT-A", "LOT-B", "LOT-C" }, result.ContentLines.Select(c => c.LotNumber));
        Assert.Equal(new[] { true, false, false }, result.ContentLines.Select(c => c.SellableNow));
        Assert.Equal(new[] { "warehouse", "warehouse", "receivedOutsideWarehouse" }, result.ContentLines.Select(c => c.Category));
        Assert.Equal("COLD-01", result.ContentLines[0].WarehouseLocation);
        Assert.Equal("PAL-7", result.ContentLines[0].PalletReference);
    }

    [Fact]
    public void DrillDown_Lot_Category_And_Quality_Filters_Apply_To_Content_Lines_Only()
    {
        var source = Source(1, 100m, lot: "PROVISIONAL");
        var contents = new[]
        {
            Content(source, 40m, 160m, lot: "LOT-A"),
            Content(source, 3m, 11m, lot: "LOT-B", quality: "Blocked"),
            Content(source, 7m, 28m, "InTransit", "InTransit", lot: "LOT-A")
        };

        var byLot = DrillDown(new[] { source }, contents, new AvailabilityQuery { ProductVariantId = Variant, Lot = "LOT-A" });
        Assert.Equal(2, byLot.ContentLines.Count);
        Assert.Single(byLot.SourceLines);
        Assert.Equal(50m, byLot.SourceLines[0].RegisteredQuantity); // registratie telt altijd alle inhoud

        var byCategory = DrillDown(new[] { source }, contents, new AvailabilityQuery { ProductVariantId = Variant, Category = "inTransit" });
        Assert.Equal(7m, Assert.Single(byCategory.ContentLines).Quantity);

        var byQuality = DrillDown(new[] { source }, contents, new AvailabilityQuery { ProductVariantId = Variant, QualityStatus = "Blocked" });
        Assert.Equal("LOT-B", Assert.Single(byQuality.ContentLines).LotNumber);
    }

    [Fact]
    public void DrillDown_Customer_Filter_Restricts_Only_DirectTrade_Lines()
    {
        var warehouse = Source(1, 10m);
        var forSeven = Source(2, 20m, route: "DirectTrade", customerId: 7);
        var forEight = Source(3, 30m, route: "DirectTrade", customerId: 8);
        var contents = new[] { Content(warehouse, 1m, 4m), Content(forSeven, 2m, 8m, "InTransit", "InTransit"), Content(forEight, 3m, 12m, "InTransit", "InTransit") };

        var result = DrillDown(new[] { warehouse, forSeven, forEight }, contents, new AvailabilityQuery { ProductVariantId = Variant, CustomerId = 7 });

        Assert.Equal(new[] { 1, 2 }, result.SourceLines.Select(s => s.SupplyCommitmentLineId));
        Assert.Equal(new int?[] { null, 7 }, result.ContentLines.Select(c => c.CustomerId));
    }

    // ---- Afboeking: kwaliteitsemmers en beschikbaarheid op voorraad, registratie op ontvangst ----

    [Fact]
    public void Dispatched_Content_Counts_On_Hand_In_Quality_Buckets_But_Receipt_For_Registration()
    {
        var source = Source(1, 100m);
        var content = Content(source, 40m, 160m, "Received", "Warehouse", "Approved") with { DispatchedQuantity = 25m, DispatchedNetWeightKg = 100m, ReservedQuantity = 5m, ReservedNetWeightKg = 20m };

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { content }));

        Assert.Equal((15m, 60m), (entry.Warehouse.Approved.Quantity, entry.Warehouse.Approved.NetWeightKg));
        Assert.Equal((15m, 60m), (entry.AvailableBeforeReservations.Quantity, entry.AvailableBeforeReservations.NetWeightKg));
        Assert.Equal((5m, 20m), (entry.Reserved.Quantity, entry.Reserved.NetWeightKg));
        Assert.Equal((10m, 40m), (entry.AvailableAfterReservations.Quantity, entry.AvailableAfterReservations.NetWeightKg));
        // Verwacht-minus-geregistreerd gebruikt de ontvangst (40), niet de voorraad (15).
        Assert.Equal(60m, entry.ExpectedUnregistered.Quantity);
        Assert.Empty(entry.Warnings);
    }

    [Fact]
    public void Depleted_Content_Shows_Zero_On_Hand_And_Depleted_Flag_In_Drill_Down_But_Keeps_Receipt()
    {
        var source = Source(1, 100m);
        var content = Content(source, 40m, 160m, "Received", "Warehouse", "Approved") with { DispatchedQuantity = 40m, DispatchedNetWeightKg = 160m };

        var (sourceLines, contentLines) = AvailabilityCalculator.BuildDrillDown(new[] { source }, new[] { content }, new AvailabilityQuery { Route = "Warehouse" });

        var line = Assert.Single(contentLines);
        Assert.Equal((40m, 160m, 40m, 0m, 0m, true, 0m, false), (line.Quantity, line.NetWeightKg, line.DispatchedQuantity, line.OnHandQuantity, line.OnHandNetWeightKg, line.Depleted, line.AvailableQuantity, line.SellableNow));
        Assert.Equal(40m, Assert.Single(sourceLines).RegisteredQuantity);
        var entry = WarehouseEntry(Overview(new[] { source }, new[] { content }));
        Assert.Equal(0m, entry.AvailableBeforeReservations.Quantity);
        Assert.Equal(60m, entry.ExpectedUnregistered.Quantity);
    }

    [Fact]
    public void Reservation_Above_On_Hand_Is_Flagged_Even_When_Below_Receipt()
    {
        var source = Source(1, 100m);
        var content = Content(source, 40m, 160m, "Received", "Warehouse", "Approved") with { DispatchedQuantity = 35m, DispatchedNetWeightKg = 140m, ReservedQuantity = 10m, ReservedNetWeightKg = 40m };

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { content }));

        Assert.Contains(entry.Warnings, w => w.Code == "RESERVATION_EXCEEDS_CONTENT");
        Assert.Equal(0m, entry.AvailableAfterReservations.Quantity);
    }

    [Fact]
    public void Correction_Content_Counts_As_Quarantine_Stock_But_Never_As_Registration_Against_The_Source()
    {
        var source = Source(1, 100m);
        var original = Content(source, 40m, 160m, "Received", "Warehouse", "Approved") with { DispatchedQuantity = 15m, DispatchedNetWeightKg = 60m };
        var restored = Content(source, 15m, 60m, "Received", "Warehouse", "Quarantine") with { PalletContentLineId = 99, FromCorrection = true };

        var entry = WarehouseEntry(Overview(new[] { source }, new[] { original, restored }));

        Assert.Equal((25m, 100m), (entry.Warehouse.Approved.Quantity, entry.Warehouse.Approved.NetWeightKg));
        Assert.Equal((15m, 60m), (entry.Warehouse.Quarantine.Quantity, entry.Warehouse.Quarantine.NetWeightKg));
        Assert.Equal(25m, entry.AvailableBeforeReservations.Quantity);
        // Geregistreerd blijft 40 (alleen de fysieke ontvangst): 100 - 40 = 60 nog verwacht, geen OVER_REGISTERED.
        Assert.Equal(60m, entry.ExpectedUnregistered.Quantity);
        Assert.DoesNotContain(entry.Warnings, w => w.Code == "OVER_REGISTERED");
        var (sourceLines, contentLines) = AvailabilityCalculator.BuildDrillDown(new[] { source }, new[] { original, restored }, new AvailabilityQuery { Route = "Warehouse" });
        Assert.Equal(40m, Assert.Single(sourceLines).RegisteredQuantity);
        Assert.Equal(new[] { "DispatchCorrection", "Receipt" }, contentLines.Select(c => c.Origin).OrderBy(o => o, StringComparer.Ordinal));
        Assert.Equal("DispatchCorrection", contentLines.Single(c => c.PalletContentLineId == 99).Origin);
    }
}
