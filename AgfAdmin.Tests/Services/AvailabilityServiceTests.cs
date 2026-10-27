using System.Text.Json;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

public class AvailabilityServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record Seed(
        int VariantA, int VariantWithoutSupply, int CustomerT, int CustomerU,
        int SourceBox, int SourceColli, int DirectT, int DirectU,
        int WarehouseApprovedLine, int DispatchedLine, int CancelledLine);

    // Scenario:
    //   Warehouse-toezegging: SourceBox 100 box (opgegeven 400 kg), SourceColli 50 colli.
    //   DirectTrade: DirectT 30 box voor klant T, DirectU 10 box voor klant U.
    //   P1 Received/Warehouse: 40 box Approved (160.125 kg) + 3 box Quarantine (11.111 kg) uit SourceBox.
    //   P2 InTransit: 20 box Quarantine (80 kg) uit SourceBox.
    //   P3 Received/Crossdock: 10 box Approved (40 kg) uit DirectT.
    //   P4 Dispatched: 5 box Approved (20 kg) uit SourceBox (gesloten, telt wel als geregistreerd).
    //   P5 Cancelled: 10 colli uit SourceColli (gesloten, telt niet als geregistreerd).
    private async Task<Seed> SeedAsync()
    {
        await using var db = _factory.CreateContext();
        var product = new Product { Name = "Avocado" };
        var variantA = new ProductVariant { Product = product, Name = "Hass 6", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.5m };
        var variantB = new ProductVariant { Product = product, Name = "Hass 8", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 11.5m };
        var vendor = new Vendor { Name = "Supplier B" };
        var customerT = new Customer { Name = "Customer T" };
        var customerU = new Customer { Name = "Customer U" };

        SupplyCommitment Commitment(string route, Customer customer) => new()
        {
            RequestId = Guid.NewGuid(),
            Vendor = vendor,
            Customer = customer,
            Route = route,
            Status = "Expected",
            Origin = "Kenya",
            Destination = customer == null ? null : "Turkey",
            ExpectedAt = new DateTime(2026, 11, 2, 8, 0, 0, DateTimeKind.Utc)
        };
        SupplyCommitmentLine Line(SupplyCommitment commitment, decimal quantity, string unit, decimal? declaredKg = null) => new()
        {
            SupplyCommitment = commitment,
            ProductVariant = variantA,
            ExpectedQuantity = quantity,
            ExpectedUnit = unit,
            ExpectedNetWeightKg = declaredKg,
            WeightSource = declaredKg == null ? null : "Entered",
            LotNumber = "PROVISIONAL"
        };

        var warehouse = Commitment("Warehouse", null);
        var sourceBox = Line(warehouse, 100m, "box", 400m);
        var sourceColli = Line(warehouse, 50m, "colli");
        var directT = Line(Commitment("DirectTrade", customerT), 30m, "box");
        var directU = Line(Commitment("DirectTrade", customerU), 10m, "box");

        Pallet NewPallet(string reference, string status, string location, string bin = null) =>
            new() { Reference = reference, LogisticsStatus = status, LocationContext = location, WarehouseLocation = bin };
        PalletContentLine Content(Pallet pallet, SupplyCommitmentLine source, decimal quantity, decimal kg, string quality, string lot = "LOT-1") => new()
        {
            RequestId = Guid.NewGuid(),
            Pallet = pallet,
            SupplyCommitmentLine = source,
            LotNumber = lot,
            Quantity = quantity,
            Unit = source.ExpectedUnit,
            NetWeightKg = kg,
            QualityStatus = quality
        };

        var p1 = NewPallet("P1", "Received", "Warehouse", "COLD-01");
        var p2 = NewPallet("P2", "InTransit", "InTransit");
        var p3 = NewPallet("P3", "Received", "Crossdock");
        var p4 = NewPallet("P4", "Dispatched", "InTransit");
        var p5 = NewPallet("P5", "Cancelled", "Supplier");

        var approved = Content(p1, sourceBox, 40m, 160.125m, "Approved", "LOT-A");
        var quarantine = Content(p1, sourceBox, 3m, 11.111m, "Quarantine", "LOT-B");
        var inTransit = Content(p2, sourceBox, 20m, 80m, "Quarantine");
        var crossdock = Content(p3, directT, 10m, 40m, "Approved");
        var dispatched = Content(p4, sourceBox, 5m, 20m, "Approved");
        var cancelled = Content(p5, sourceColli, 10m, 55m, "Quarantine");

        db.AddRange(product, variantA, variantB, vendor, customerT, customerU, sourceBox, sourceColli, directT, directU,
            p1, p2, p3, p4, p5, approved, quarantine, inTransit, crossdock, dispatched, cancelled);
        await db.SaveChangesAsync();

        return new Seed(variantA.Id, variantB.Id, customerT.Id, customerU.Id, sourceBox.Id, sourceColli.Id, directT.Id, directU.Id,
            approved.Id, dispatched.Id, cancelled.Id);
    }

    private async Task<ServiceResult<AvailabilityOverviewDto>> OverviewAsync(AvailabilityQuery query = null)
    {
        await using var db = _factory.CreateContext();
        return await new AvailabilityService(db).GetOverviewAsync(query ?? new AvailabilityQuery());
    }

    private async Task<ServiceResult<AvailabilityDrillDownDto>> DrillDownAsync(int variantId, AvailabilityQuery query = null)
    {
        await using var db = _factory.CreateContext();
        return await new AvailabilityService(db).GetVariantAsync(variantId, query ?? new AvailabilityQuery());
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public async Task Overview_Computes_Every_Category_Exactly_And_Keeps_DirectTrade_Per_Customer()
    {
        var seed = await SeedAsync();

        var result = await OverviewAsync();

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var variant = Assert.Single(result.Value.Variants);
        Assert.Equal(seed.VariantA, variant.ProductVariantId);
        Assert.Equal("Avocado", variant.ProductName);
        Assert.Equal("Hass 6", variant.VariantName);

        var box = Assert.Single(variant.WarehouseSupply, e => e.Unit == "box");
        Assert.Equal(40m, box.AvailableBeforeReservations.Quantity);
        Assert.Equal(160.125m, box.AvailableBeforeReservations.NetWeightKg);
        Assert.Equal(3m, box.Warehouse.Quarantine.Quantity);
        Assert.Equal(11.111m, box.Warehouse.Quarantine.NetWeightKg);
        Assert.Equal(20m, box.InTransit.Quarantine.Quantity);
        Assert.Equal(80m, box.InTransit.Quarantine.NetWeightKg);
        Assert.Equal(32m, box.ExpectedUnregistered.Quantity); // 100 - (40 + 3 + 20 + 5 verzonden)
        Assert.Null(box.ExpectedUnregistered.NetWeightKg);
        Assert.Equal((0m, 0m, true), (box.Reserved.Quantity, box.Reserved.NetWeightKg, box.Reserved.Applied));
        Assert.Equal((40m, 160.125m), (box.AvailableAfterReservations.Quantity, box.AvailableAfterReservations.NetWeightKg));

        var colli = Assert.Single(variant.WarehouseSupply, e => e.Unit == "colli");
        Assert.Equal(50m, colli.ExpectedUnregistered.Quantity); // geannuleerde pallet trekt niets af
        Assert.Equal(0m, colli.AvailableBeforeReservations.Quantity);
        Assert.Equal(0m, colli.ExpectedOnPallets.Quarantine.Quantity);

        Assert.Equal(new int?[] { seed.CustomerT, seed.CustomerU }, variant.DirectTradeSupply.Select(e => e.CustomerId));
        var forT = variant.DirectTradeSupply[0];
        Assert.Equal("Customer T", forT.CustomerName);
        Assert.Equal(10m, forT.ReceivedOutsideWarehouse.Approved.Quantity);
        Assert.Equal(20m, forT.ExpectedUnregistered.Quantity);
        Assert.Equal(10m, variant.DirectTradeSupply[1].ExpectedUnregistered.Quantity);
    }

    [Fact]
    public async Task Basis_States_That_Reservations_Are_Applied_But_Legacy_Inventory_And_Ledger_Are_Not()
    {
        await SeedAsync();

        var basis = (await OverviewAsync()).Value.Basis;

        Assert.True(basis.ReservationsApplied);
        Assert.False(basis.LegacyInventoryIncluded);
        Assert.False(basis.StockLedgerIncluded);
        Assert.Equal("SupplyCommitmentLines + PalletContentLines", basis.Source);
        Assert.Equal(DateTimeKind.Utc, (await OverviewAsync()).Value.GeneratedAt.Kind);
    }

    [Fact]
    public async Task Legacy_Inventory_And_Shipments_Are_Never_Mixed_In()
    {
        var seed = await SeedAsync();
        var before = Json((await OverviewAsync()).Value.Variants);

        await using (var db = _factory.CreateContext())
        {
            var shipment = new Shipment { ContainerCode = "MSKU123", Origin = "Peru", EstimatedTimeOfArrival = DateTime.UtcNow, Status = "In Transit" };
            db.Shipments.Add(shipment);
            db.Inventory.AddRange(
                new Inventory { ProductVariantId = seed.VariantA, Quantity = 999, TotalWeightKg = 5000m, Location = "A1", Shipment = shipment },
                new Inventory { ProductVariantId = seed.VariantWithoutSupply, Quantity = 50, Location = "B2" });
            await db.SaveChangesAsync();
        }

        var after = (await OverviewAsync()).Value;
        Assert.Equal(before, Json(after.Variants));
        Assert.DoesNotContain(after.Variants, v => v.ProductVariantId == seed.VariantWithoutSupply);
    }

    [Fact]
    public async Task Stock_Movements_And_Legacy_OrderLine_Reservations_Are_Not_Read()
    {
        var seed = await SeedAsync();
        var before = Json((await OverviewAsync()).Value.Variants);

        await using (var db = _factory.CreateContext())
        {
            var order = new Order { CustomCustomerName = "Customer C" };
            var orderLine = new OrderLine { Order = order, ProductVariantId = seed.VariantA, Quantity = 1, UnitPrice = 12.5m };
            db.AddRange(order, orderLine);
            db.StockMovements.Add(new StockMovement { Type = "Receipt", Quantity = 500m, Unit = "box", SupplyCommitmentLineId = seed.SourceBox, PalletContentLineId = seed.WarehouseApprovedLine });
            db.StockReservations.Add(new StockReservation
            {
                OrderLine = orderLine,
                SupplyCommitmentLineId = seed.SourceBox,
                PalletContentLineId = seed.WarehouseApprovedLine,
                Quantity = 40m,
                Unit = "box",
                Source = "Physical",
                Status = "Active"
            });
            await db.SaveChangesAsync();
        }

        var after = (await OverviewAsync()).Value;
        Assert.Equal(before, Json(after.Variants));
        var box = Assert.Single(after.Variants.Single().WarehouseSupply, e => e.Unit == "box");
        Assert.Equal(40m, box.AvailableBeforeReservations.Quantity);
        Assert.Equal(0m, box.Reserved.Quantity);
    }

    [Fact]
    public async Task Overview_Filters_Are_Applied()
    {
        var seed = await SeedAsync();

        var units = (await OverviewAsync(new AvailabilityQuery { Unit = "colli" })).Value.Variants.Single().WarehouseSupply.Select(e => e.Unit);
        Assert.Equal(new[] { "colli" }, units);

        var warehouseOnly = (await OverviewAsync(new AvailabilityQuery { Route = "Warehouse" })).Value.Variants.Single();
        Assert.Empty(warehouseOnly.DirectTradeSupply);

        var directOnly = (await OverviewAsync(new AvailabilityQuery { Route = "DirectTrade" })).Value.Variants.Single();
        Assert.Empty(directOnly.WarehouseSupply);
        Assert.Equal(2, directOnly.DirectTradeSupply.Count);

        var forCustomerU = (await OverviewAsync(new AvailabilityQuery { CustomerId = seed.CustomerU })).Value.Variants.Single();
        Assert.Equal(seed.CustomerU, Assert.Single(forCustomerU.DirectTradeSupply).CustomerId);
        Assert.Equal(2, forCustomerU.WarehouseSupply.Count);

        Assert.Single((await OverviewAsync(new AvailabilityQuery { ProductVariantId = seed.VariantA })).Value.Variants);
        Assert.Empty((await OverviewAsync(new AvailabilityQuery { ProductVariantId = 999_999 })).Value.Variants);
    }

    [Fact]
    public async Task DrillDown_Shows_Source_Lines_And_Active_Content_Lines()
    {
        var seed = await SeedAsync();

        var result = await DrillDownAsync(seed.VariantA);

        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        var drillDown = result.Value;
        Assert.Equal("Hass 6", drillDown.VariantName);
        Assert.True(drillDown.Basis.ReservationsApplied);

        var box = Assert.Single(drillDown.SourceLines, s => s.SupplyCommitmentLineId == seed.SourceBox);
        Assert.Equal(100m, box.ExpectedQuantity);
        Assert.Equal(400m, box.DeclaredExpectedNetWeightKg);
        Assert.Equal(68m, box.RegisteredQuantity);
        Assert.Equal(32m, box.UnregisteredQuantity);
        Assert.Equal(4, drillDown.SourceLines.Count);

        Assert.DoesNotContain(drillDown.ContentLines, c => c.PalletContentLineId == seed.DispatchedLine || c.PalletContentLineId == seed.CancelledLine);
        var approved = Assert.Single(drillDown.ContentLines, c => c.PalletContentLineId == seed.WarehouseApprovedLine);
        Assert.Equal("warehouse", approved.Category);
        Assert.True(approved.SellableNow);
        Assert.Equal("P1", approved.PalletReference);
        Assert.Equal("COLD-01", approved.WarehouseLocation);
        Assert.Equal("LOT-A", approved.LotNumber);
        Assert.Equal(160.125m, approved.NetWeightKg);
        Assert.Equal(4, drillDown.ContentLines.Count);
    }

    [Fact]
    public async Task DrillDown_IncludeClosed_Shows_Closed_Content_With_Real_Status()
    {
        var seed = await SeedAsync();

        var result = await DrillDownAsync(seed.VariantA, new AvailabilityQuery { IncludeClosed = true });

        var dispatched = Assert.Single(result.Value.ContentLines, c => c.PalletContentLineId == seed.DispatchedLine);
        Assert.Equal("closed", dispatched.Category);
        Assert.Equal("Dispatched", dispatched.LogisticsStatus);
        Assert.False(dispatched.SellableNow);
        var cancelled = Assert.Single(result.Value.ContentLines, c => c.PalletContentLineId == seed.CancelledLine);
        Assert.Equal("Cancelled", cancelled.LogisticsStatus);

        var closedOnly = await DrillDownAsync(seed.VariantA, new AvailabilityQuery { IncludeClosed = true, Category = "closed" });
        Assert.Equal(2, closedOnly.Value.ContentLines.Count);
        Assert.All(closedOnly.Value.ContentLines, c => Assert.True(c.Closed));
    }

    [Fact]
    public async Task DrillDown_Filters_Are_Applied()
    {
        var seed = await SeedAsync();

        var byLot = await DrillDownAsync(seed.VariantA, new AvailabilityQuery { Lot = "LOT-B" });
        Assert.Equal(3m, Assert.Single(byLot.Value.ContentLines).Quantity);

        var byQuality = await DrillDownAsync(seed.VariantA, new AvailabilityQuery { QualityStatus = "Quarantine" });
        Assert.Equal(2, byQuality.Value.ContentLines.Count);

        var byCustomer = await DrillDownAsync(seed.VariantA, new AvailabilityQuery { CustomerId = seed.CustomerT });
        Assert.DoesNotContain(byCustomer.Value.SourceLines, s => s.SupplyCommitmentLineId == seed.DirectU);
        Assert.Contains(byCustomer.Value.SourceLines, s => s.SupplyCommitmentLineId == seed.DirectT);
        Assert.Contains(byCustomer.Value.SourceLines, s => s.SupplyCommitmentLineId == seed.SourceBox);

        var byUnit = await DrillDownAsync(seed.VariantA, new AvailabilityQuery { Unit = "colli", IncludeClosed = true });
        Assert.Equal(seed.SourceColli, Assert.Single(byUnit.Value.SourceLines).SupplyCommitmentLineId);
        Assert.Equal(seed.CancelledLine, Assert.Single(byUnit.Value.ContentLines).PalletContentLineId);
    }

    [Fact]
    public async Task DrillDown_Of_Variant_Without_Supply_Is_Empty_And_Unknown_Variant_Is_NotFound()
    {
        var seed = await SeedAsync();

        var empty = await DrillDownAsync(seed.VariantWithoutSupply);
        Assert.Equal(ServiceResultStatus.Ok, empty.Status);
        Assert.Equal("Hass 8", empty.Value.VariantName);
        Assert.Empty(empty.Value.SourceLines);
        Assert.Empty(empty.Value.ContentLines);

        Assert.Equal(ServiceResultStatus.NotFound, (await DrillDownAsync(999_999)).Status);
    }

    [Theory]
    [InlineData("route", "warehouse")]
    [InlineData("route", "Direct")]
    [InlineData("unit", "kg")]
    [InlineData("unit", "Box")]
    public async Task Overview_Rejects_Invalid_Filters(string field, string value)
    {
        var query = field == "route" ? new AvailabilityQuery { Route = value } : new AvailabilityQuery { Unit = value };

        var result = await OverviewAsync(query);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Theory]
    [InlineData("category", "x", false)]
    [InlineData("category", "Warehouse", false)]
    [InlineData("category", "closed", false)]
    [InlineData("qualityStatus", "approved", false)]
    [InlineData("qualityStatus", "PartiallyApproved", false)]
    [InlineData("lot", "   ", false)]
    [InlineData("unit", "pallet", false)]
    public async Task DrillDown_Rejects_Invalid_Filters(string field, string value, bool includeClosed)
    {
        var seed = await SeedAsync();
        var query = new AvailabilityQuery { IncludeClosed = includeClosed };
        switch (field)
        {
            case "category": query.Category = value; break;
            case "qualityStatus": query.QualityStatus = value; break;
            case "lot": query.Lot = value; break;
            case "unit": query.Unit = value; break;
        }

        var result = await DrillDownAsync(seed.VariantA, query);

        Assert.Equal(ServiceResultStatus.Validation, result.Status);
        Assert.Contains(field, result.Errors.Keys);
    }

    [Fact]
    public async Task Reading_Availability_Changes_Nothing()
    {
        var seed = await SeedAsync();
        async Task<string> SnapshotAsync()
        {
            await using var db = _factory.CreateContext();
            return Json(new
            {
                Pallets = await db.Pallets.OrderBy(p => p.Id).Select(p => new { p.Id, p.LogisticsStatus, p.LocationContext, p.Version }).ToListAsync(),
                Lines = await db.PalletContentLines.OrderBy(c => c.Id).Select(c => new { c.Id, c.QualityStatus, c.Quantity, c.Version }).ToListAsync(),
                Sources = await db.SupplyCommitmentLines.CountAsync(),
                Events = await db.PalletAuditEvents.CountAsync(),
                Movements = await db.StockMovements.CountAsync(),
                Reservations = await db.StockReservations.CountAsync()
            });
        }
        var before = await SnapshotAsync();

        await using (var db = _factory.CreateContext())
        {
            var service = new AvailabilityService(db);
            await service.GetOverviewAsync(new AvailabilityQuery());
            await service.GetVariantAsync(seed.VariantA, new AvailabilityQuery { IncludeClosed = true });
            Assert.False(db.ChangeTracker.HasChanges());
            Assert.Empty(db.ChangeTracker.Entries());
        }

        Assert.Equal(before, await SnapshotAsync());
    }
}
