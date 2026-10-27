using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Models;

public class InventoryAvailabilityModelTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed record SeededGraph(int ProductVariantId, int VendorId, int OrderLineId, int CommitmentId, int CommitmentLineId);

    private async Task<SeededGraph> SeedGraphAsync()
    {
        await using var db = _factory.CreateContext();

        var product = new Product { Name = "Avocado" };
        var variant = new ProductVariant { Product = product, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.50m };
        var vendor = new Vendor { Name = "Supplier B" };
        var order = new Order { CustomCustomerName = "Customer C" };
        var orderLine = new OrderLine { Order = order, ProductVariant = variant, Quantity = 1, UnitPrice = 12.50m };
        var commitment = new SupplyCommitment { RequestId = Guid.NewGuid(), Vendor = vendor, Route = "Warehouse", Status = "Expected", Origin = "Peru" };
        var commitmentLine = new SupplyCommitmentLine { SupplyCommitment = commitment, ProductVariant = variant, ExpectedQuantity = 3m };

        db.AddRange(product, variant, vendor, order, orderLine, commitment, commitmentLine);
        await db.SaveChangesAsync();

        return new SeededGraph(variant.Id, vendor.Id, orderLine.Id, commitment.Id, commitmentLine.Id);
    }

    private async Task AssertSaveIsRejectedAsync(Action<AgfAdmin.Data.AppDbContext> arrange)
    {
        await using var db = _factory.CreateContext();
        arrange(db);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task StockReceipt_Duplicate_RequestId_Is_Rejected()
    {
        var graph = await SeedGraphAsync();
        var requestId = Guid.NewGuid();

        await using (var db = _factory.CreateContext())
        {
            db.StockReceipts.Add(new StockReceipt { RequestId = requestId, SupplyCommitmentId = graph.CommitmentId });
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db =>
            db.StockReceipts.Add(new StockReceipt { RequestId = requestId, SupplyCommitmentId = graph.CommitmentId }));

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.StockReceipts.CountAsync(r => r.RequestId == requestId));
    }

    [Fact]
    public async Task SupplyCommitmentLine_Requires_Existing_Commitment()
    {
        var graph = await SeedGraphAsync();

        await AssertSaveIsRejectedAsync(db => db.SupplyCommitmentLines.Add(new SupplyCommitmentLine
        {
            SupplyCommitmentId = graph.CommitmentId + 1000,
            ProductVariantId = graph.ProductVariantId,
            ExpectedQuantity = 1m
        }));
    }

    [Fact]
    public async Task SupplyCommitmentLine_Requires_Existing_ProductVariant()
    {
        var graph = await SeedGraphAsync();

        await AssertSaveIsRejectedAsync(db => db.SupplyCommitmentLines.Add(new SupplyCommitmentLine
        {
            SupplyCommitmentId = graph.CommitmentId,
            ProductVariantId = graph.ProductVariantId + 1000,
            ExpectedQuantity = 1m
        }));
    }

    [Fact]
    public async Task SupplyCommitment_Requires_Existing_Vendor()
    {
        var graph = await SeedGraphAsync();

        await AssertSaveIsRejectedAsync(db => db.SupplyCommitments.Add(new SupplyCommitment
        {
            RequestId = Guid.NewGuid(),
            VendorId = graph.VendorId + 1000,
            Route = "Warehouse",
            Status = "Expected",
            Origin = "Peru"
        }));
    }

    [Fact]
    public async Task StockMovement_Requires_Existing_SupplyCommitmentLine()
    {
        await SeedGraphAsync();

        await AssertSaveIsRejectedAsync(db => db.StockMovements.Add(new StockMovement
        {
            Type = "Correction",
            Quantity = 1m,
            SupplyCommitmentLineId = 999_999,
            Reason = "Test"
        }));
    }

    [Fact]
    public async Task StockReservation_Requires_Existing_OrderLine()
    {
        var graph = await SeedGraphAsync();

        await AssertSaveIsRejectedAsync(db => db.StockReservations.Add(new StockReservation
        {
            OrderLineId = graph.OrderLineId + 1000,
            SupplyCommitmentLineId = graph.CommitmentLineId,
            Quantity = 1m,
            Source = "Expected",
            Status = "Active"
        }));
    }

    [Fact]
    public async Task StockReservation_Requires_Existing_SupplyCommitmentLine()
    {
        var graph = await SeedGraphAsync();

        await AssertSaveIsRejectedAsync(db => db.StockReservations.Add(new StockReservation
        {
            OrderLineId = graph.OrderLineId,
            SupplyCommitmentLineId = graph.CommitmentLineId + 1000,
            Quantity = 1m,
            Source = "Expected",
            Status = "Active"
        }));
    }

    [Fact]
    public async Task StockReservation_Links_One_OrderLine_To_One_SupplyLine()
    {
        var graph = await SeedGraphAsync();
        int reservationId;

        await using (var db = _factory.CreateContext())
        {
            var reservation = new StockReservation
            {
                OrderLineId = graph.OrderLineId,
                SupplyCommitmentLineId = graph.CommitmentLineId,
                Quantity = 1m,
                Source = "Expected",
                Status = "Active"
            };
            db.StockReservations.Add(reservation);
            await db.SaveChangesAsync();
            reservationId = reservation.Id;
        }

        await using var verify = _factory.CreateContext();
        var saved = await verify.StockReservations.SingleAsync(r => r.Id == reservationId);
        Assert.Equal(graph.OrderLineId, saved.OrderLineId);
        Assert.Equal(graph.CommitmentLineId, saved.SupplyCommitmentLineId);
        Assert.Equal(1m, saved.Quantity);
    }

    [Fact]
    public async Task Deleting_SupplyCommitment_With_Lines_Is_Restricted()
    {
        var graph = await SeedGraphAsync();

        await AssertSaveIsRejectedAsync(db =>
            db.SupplyCommitments.Remove(new SupplyCommitment { Id = graph.CommitmentId }));

        await using var verify = _factory.CreateContext();
        Assert.True(await verify.SupplyCommitmentLines.AnyAsync(l => l.Id == graph.CommitmentLineId));
    }

    [Theory]
    [InlineData("movement")]
    [InlineData("reservation")]
    public async Task Deleting_SupplyCommitmentLine_With_Movement_Or_Reservation_Is_Restricted(string dependent)
    {
        var graph = await SeedGraphAsync();

        await using (var db = _factory.CreateContext())
        {
            if (dependent == "movement")
            {
                db.StockMovements.Add(new StockMovement { Type = "Correction", Quantity = 1m, SupplyCommitmentLineId = graph.CommitmentLineId, Reason = "Test" });
            }
            else
            {
                db.StockReservations.Add(new StockReservation
                {
                    OrderLineId = graph.OrderLineId,
                    SupplyCommitmentLineId = graph.CommitmentLineId,
                    Quantity = 1m,
                    Source = "Expected",
                    Status = "Active"
                });
            }
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db =>
            db.SupplyCommitmentLines.Remove(new SupplyCommitmentLine { Id = graph.CommitmentLineId }));

        await using var verify = _factory.CreateContext();
        Assert.True(await verify.SupplyCommitmentLines.AnyAsync(l => l.Id == graph.CommitmentLineId));
        if (dependent == "movement")
        {
            Assert.Equal(1, await verify.StockMovements.CountAsync());
        }
        else
        {
            Assert.Equal(1, await verify.StockReservations.CountAsync());
        }
    }

    [Fact]
    public async Task Deleting_StockReceipt_With_Movements_Is_Restricted()
    {
        var graph = await SeedGraphAsync();
        int receiptId;

        await using (var db = _factory.CreateContext())
        {
            var receipt = new StockReceipt { RequestId = Guid.NewGuid(), SupplyCommitmentId = graph.CommitmentId };
            db.StockReceipts.Add(receipt);
            db.StockMovements.Add(new StockMovement { Type = "Receipt", Quantity = 3m, SupplyCommitmentLineId = graph.CommitmentLineId, StockReceipt = receipt });
            await db.SaveChangesAsync();
            receiptId = receipt.Id;
        }

        await AssertSaveIsRejectedAsync(db =>
            db.StockReceipts.Remove(new StockReceipt { Id = receiptId }));

        await using var verify = _factory.CreateContext();
        Assert.True(await verify.StockReceipts.AnyAsync(r => r.Id == receiptId));
        Assert.Equal(1, await verify.StockMovements.CountAsync(m => m.StockReceiptId == receiptId));
    }

    [Theory]
    [InlineData("orderLine")]
    [InlineData("productVariant")]
    public async Task Deleting_OrderLine_Or_ProductVariant_Referenced_By_Availability_Records_Is_Restricted(string principal)
    {
        var graph = await SeedGraphAsync();

        await using (var db = _factory.CreateContext())
        {
            db.StockReservations.Add(new StockReservation
            {
                OrderLineId = graph.OrderLineId,
                SupplyCommitmentLineId = graph.CommitmentLineId,
                Quantity = 1m,
                Source = "Expected",
                Status = "Active"
            });
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db =>
        {
            if (principal == "orderLine")
            {
                db.OrderLines.Remove(new OrderLine { Id = graph.OrderLineId });
            }
            else
            {
                db.ProductVariants.Remove(new ProductVariant { Id = graph.ProductVariantId });
            }
        });

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.StockReservations.CountAsync());
        Assert.True(await verify.SupplyCommitmentLines.AnyAsync(l => l.Id == graph.CommitmentLineId));
    }

    [Fact]
    public async Task Availability_Quantity_And_NetWeight_RoundTrip_With_Three_Decimals()
    {
        var graph = await SeedGraphAsync();
        int lineId, movementId, reservationId;

        await using (var db = _factory.CreateContext())
        {
            var line = new SupplyCommitmentLine
            {
                SupplyCommitmentId = graph.CommitmentId,
                ProductVariantId = graph.ProductVariantId,
                ExpectedQuantity = 12.345m,
                ExpectedUnit = "box",
                ExpectedNetWeightKg = 67.890m,
                WeightSource = "Entered"
            };
            db.SupplyCommitmentLines.Add(line);
            await db.SaveChangesAsync();

            var movement = new StockMovement { Type = "Receipt", Quantity = 12.345m, Unit = "box", NetWeightKg = 67.890m, SupplyCommitmentLineId = line.Id };
            var reservation = new StockReservation
            {
                OrderLineId = graph.OrderLineId,
                SupplyCommitmentLineId = line.Id,
                Quantity = 12.345m,
                Unit = "box",
                NetWeightKg = 67.890m,
                Source = "Expected",
                Status = "Active"
            };
            db.AddRange(movement, reservation);
            await db.SaveChangesAsync();

            (lineId, movementId, reservationId) = (line.Id, movement.Id, reservation.Id);
        }

        await using var verify = _factory.CreateContext();
        var savedLine = await verify.SupplyCommitmentLines.SingleAsync(l => l.Id == lineId);
        Assert.Equal(12.345m, savedLine.ExpectedQuantity);
        Assert.Equal("box", savedLine.ExpectedUnit);
        Assert.Equal(67.890m, savedLine.ExpectedNetWeightKg);

        var savedMovement = await verify.StockMovements.SingleAsync(m => m.Id == movementId);
        Assert.Equal(12.345m, savedMovement.Quantity);
        Assert.Equal("box", savedMovement.Unit);
        Assert.Equal(67.890m, savedMovement.NetWeightKg);

        var savedReservation = await verify.StockReservations.SingleAsync(r => r.Id == reservationId);
        Assert.Equal(12.345m, savedReservation.Quantity);
        Assert.Equal("box", savedReservation.Unit);
        Assert.Equal(67.890m, savedReservation.NetWeightKg);
    }

    [Fact]
    public async Task Legacy_Availability_Metadata_Can_Remain_Unknown_Until_A_New_Write()
    {
        var graph = await SeedGraphAsync();
        int movementId, reservationId;

        await using (var db = _factory.CreateContext())
        {
            var movement = new StockMovement { Type = "Correction", Quantity = 1m, SupplyCommitmentLineId = graph.CommitmentLineId, Reason = "Test" };
            var reservation = new StockReservation
            {
                OrderLineId = graph.OrderLineId,
                SupplyCommitmentLineId = graph.CommitmentLineId,
                Quantity = 1m,
                Source = "Expected",
                Status = "Active"
            };
            db.AddRange(movement, reservation);
            await db.SaveChangesAsync();
            (movementId, reservationId) = (movement.Id, reservation.Id);
        }

        await using var verify = _factory.CreateContext();
        var variant = await verify.ProductVariants.SingleAsync(v => v.Id == graph.ProductVariantId);
        Assert.Null(variant.AvailabilityUnit);

        var line = await verify.SupplyCommitmentLines.SingleAsync(l => l.Id == graph.CommitmentLineId);
        Assert.Null(line.ExpectedUnit);
        Assert.Null(line.ExpectedNetWeightKg);

        var movementRow = await verify.StockMovements.SingleAsync(m => m.Id == movementId);
        Assert.Null(movementRow.Unit);
        Assert.Null(movementRow.NetWeightKg);

        var reservationRow = await verify.StockReservations.SingleAsync(r => r.Id == reservationId);
        Assert.Null(reservationRow.Unit);
        Assert.Null(reservationRow.NetWeightKg);
    }

    [Theory]
    [InlineData(typeof(SupplyCommitmentLine), nameof(SupplyCommitmentLine.ExpectedQuantity))]
    [InlineData(typeof(SupplyCommitmentLine), nameof(SupplyCommitmentLine.ExpectedNetWeightKg))]
    [InlineData(typeof(SupplyCommitmentLine), nameof(SupplyCommitmentLine.ExpectedGrossWeightKg))]
    [InlineData(typeof(SalesOrderLine), nameof(SalesOrderLine.NetWeightKg))]
    [InlineData(typeof(SalesOrderLine), nameof(SalesOrderLine.GrossWeightKg))]
    [InlineData(typeof(StockMovement), nameof(StockMovement.Quantity))]
    [InlineData(typeof(StockMovement), nameof(StockMovement.NetWeightKg))]
    [InlineData(typeof(StockReservation), nameof(StockReservation.Quantity))]
    [InlineData(typeof(StockReservation), nameof(StockReservation.NetWeightKg))]
    [InlineData(typeof(Pallet), nameof(Pallet.TareKg))]
    [InlineData(typeof(Pallet), nameof(Pallet.GrossKg))]
    [InlineData(typeof(Pallet), nameof(Pallet.DeclaredNetKg))]
    [InlineData(typeof(PalletContentLine), nameof(PalletContentLine.Quantity))]
    [InlineData(typeof(PalletContentLine), nameof(PalletContentLine.NetWeightKg))]
    public void Availability_Decimal_Properties_Are_Configured_As_Decimal_18_3(Type entityType, string propertyName)
    {
        // SQLite dwingt decimal-precisie niet af; deze modeltest bewaakt de afspraak voor Postgres.
        using var db = _factory.CreateContext();
        var property = db.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;

        Assert.Equal(18, property.GetPrecision());
        Assert.Equal(3, property.GetScale());
    }

    [Theory]
    [InlineData(typeof(ProductVariant), nameof(ProductVariant.AvailabilityUnit))]
    [InlineData(typeof(SupplyCommitmentLine), nameof(SupplyCommitmentLine.ExpectedUnit))]
    [InlineData(typeof(StockMovement), nameof(StockMovement.Unit))]
    [InlineData(typeof(StockReservation), nameof(StockReservation.Unit))]
    public void Availability_Unit_Properties_Are_Optional_With_Max_Length_32(Type entityType, string propertyName)
    {
        using var db = _factory.CreateContext();
        var property = db.Model.FindEntityType(entityType)!.FindProperty(propertyName)!;

        Assert.Equal(32, property.GetMaxLength());
        Assert.True(property.IsNullable);
    }

    // ---- Klantkoppeling en idempotentie van supply-commitments ----

    private async Task<int> AddCustomerAsync(string name = "Customer T")
    {
        await using var db = _factory.CreateContext();
        var customer = new Customer { Name = name };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private static SupplyCommitment NewCommitment(int vendorId, string route, int? customerId, Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        VendorId = vendorId,
        CustomerId = customerId,
        Route = route,
        Status = "Expected",
        Origin = "Kenya",
        Destination = "Turkey"
    };

    [Theory]
    [InlineData("Warehouse", false, true)]
    [InlineData("Warehouse", true, false)]
    [InlineData("DirectTrade", true, true)]
    [InlineData("DirectTrade", false, false)]
    public async Task SupplyCommitment_Customer_Link_Must_Match_Route(string route, bool withCustomer, bool accepted)
    {
        var graph = await SeedGraphAsync();
        var customerId = await AddCustomerAsync();

        await using var db = _factory.CreateContext();
        db.SupplyCommitments.Add(NewCommitment(graph.VendorId, route, withCustomer ? customerId : null));

        if (accepted)
        {
            await db.SaveChangesAsync();
            await using var verify = _factory.CreateContext();
            Assert.Equal(2, await verify.SupplyCommitments.CountAsync());
        }
        else
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task SupplyCommitment_Requires_Existing_Customer()
    {
        var graph = await SeedGraphAsync();

        await AssertSaveIsRejectedAsync(db =>
            db.SupplyCommitments.Add(NewCommitment(graph.VendorId, "DirectTrade", customerId: 999_999)));
    }

    [Fact]
    public async Task Deleting_Customer_With_SupplyCommitment_Is_Restricted()
    {
        var graph = await SeedGraphAsync();
        var customerId = await AddCustomerAsync();

        await using (var db = _factory.CreateContext())
        {
            db.SupplyCommitments.Add(NewCommitment(graph.VendorId, "DirectTrade", customerId));
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db => db.Customers.Remove(new Customer { Id = customerId }));

        await using var verify = _factory.CreateContext();
        Assert.True(await verify.Customers.AnyAsync(c => c.Id == customerId));
    }

    [Fact]
    public async Task SupplyCommitment_Duplicate_RequestId_Is_Rejected()
    {
        var graph = await SeedGraphAsync();
        var requestId = Guid.NewGuid();

        await using (var db = _factory.CreateContext())
        {
            db.SupplyCommitments.Add(NewCommitment(graph.VendorId, "Warehouse", null, requestId));
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db =>
            db.SupplyCommitments.Add(NewCommitment(graph.VendorId, "Warehouse", null, requestId)));

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.SupplyCommitments.CountAsync(c => c.RequestId == requestId));
    }

    [Fact]
    public void SupplyCommitment_Customer_And_RequestId_Metadata()
    {
        using var db = _factory.CreateContext();
        var entity = db.Model.FindEntityType(typeof(SupplyCommitment))!;

        var requestIdIndex = Assert.Single(entity.GetIndexes(), i => i.Properties.Count == 1 && i.Properties.Single().Name == nameof(SupplyCommitment.RequestId));
        Assert.True(requestIdIndex.IsUnique);
        Assert.Contains(entity.GetIndexes(), i => i.Properties.Count == 1 && i.Properties.Single().Name == nameof(SupplyCommitment.CustomerId));

        var customerFk = Assert.Single(entity.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Customer));
        Assert.Equal(DeleteBehavior.Restrict, customerFk.DeleteBehavior);
        Assert.True(entity.FindProperty(nameof(SupplyCommitment.CustomerId))!.IsNullable);
    }

    // ---- Pallet- en inhoudsregel-traceability ----

    private async Task<int> AddCommitmentLineAsync(SeededGraph graph, string lotNumber)
    {
        await using var db = _factory.CreateContext();
        var line = new SupplyCommitmentLine
        {
            SupplyCommitmentId = graph.CommitmentId,
            ProductVariantId = graph.ProductVariantId,
            ExpectedQuantity = 10m,
            ExpectedUnit = "box",
            LotNumber = lotNumber
        };
        db.SupplyCommitmentLines.Add(line);
        await db.SaveChangesAsync();
        return line.Id;
    }

    private async Task<int> AddPalletAsync(string reference, string sscc = null)
    {
        await using var db = _factory.CreateContext();
        var pallet = NewPallet(reference, sscc);
        db.Pallets.Add(pallet);
        await db.SaveChangesAsync();
        return pallet.Id;
    }

    private static Pallet NewPallet(string reference, string sscc = null) => new()
    {
        Reference = reference,
        Sscc = sscc,
        LogisticsStatus = "Received",
        LocationContext = "Warehouse"
    };

    private static PalletContentLine NewContentLine(int palletId, int sourceLineId, string lotNumber = "LOT-1") => new()
    {
        RequestId = Guid.NewGuid(),
        PalletId = palletId,
        SupplyCommitmentLineId = sourceLineId,
        LotNumber = lotNumber,
        Quantity = 40m,
        Unit = "box",
        NetWeightKg = 160.000m,
        QualityStatus = "Quarantine"
    };

    private async Task<int> AddContentLineAsync(int palletId, int sourceLineId, string lotNumber = "LOT-1")
    {
        await using var db = _factory.CreateContext();
        var contentLine = NewContentLine(palletId, sourceLineId, lotNumber);
        db.PalletContentLines.Add(contentLine);
        await db.SaveChangesAsync();
        return contentLine.Id;
    }

    [Fact]
    public async Task Pallet_Rejects_Duplicate_Reference_And_Duplicate_Sscc()
    {
        await AddPalletAsync("PAL-001", sscc: "000000000000000001");

        // Meerdere pallets zonder SSCC zijn toegestaan.
        await AddPalletAsync("PAL-002");
        await AddPalletAsync("PAL-003");

        await AssertSaveIsRejectedAsync(db => db.Pallets.Add(NewPallet("PAL-001")));
        await AssertSaveIsRejectedAsync(db => db.Pallets.Add(NewPallet("PAL-004", sscc: "000000000000000001")));

        await using var verify = _factory.CreateContext();
        Assert.Equal(3, await verify.Pallets.CountAsync());
    }

    [Theory]
    [InlineData("nullReference")]
    [InlineData("emptyReference")]
    [InlineData("whitespaceReference")]
    [InlineData("emptySscc")]
    [InlineData("whitespaceSscc")]
    public async Task Pallet_Rejects_Blank_Reference_Or_Blank_Sscc(string invalidCase)
    {
        await AssertSaveIsRejectedAsync(db =>
        {
            var pallet = NewPallet("PAL-001", sscc: "000000000000000001");
            switch (invalidCase)
            {
                case "nullReference": pallet.Reference = null; break;
                case "emptyReference": pallet.Reference = ""; break;
                case "whitespaceReference": pallet.Reference = "   "; break;
                case "emptySscc": pallet.Sscc = ""; break;
                case "whitespaceSscc": pallet.Sscc = "   "; break;
            }
            db.Pallets.Add(pallet);
        });

        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.Pallets.CountAsync());
    }

    [Fact]
    public async Task Pallet_Accepts_Null_Sscc()
    {
        var palletId = await AddPalletAsync("PAL-001", sscc: null);

        await using var verify = _factory.CreateContext();
        var saved = await verify.Pallets.SingleAsync(p => p.Id == palletId);
        Assert.Null(saved.Sscc);
        Assert.Equal("PAL-001", saved.Reference);
    }

    [Theory]
    [InlineData("missingPallet")]
    [InlineData("missingSourceLine")]
    [InlineData("nullLot")]
    [InlineData("emptyLot")]
    [InlineData("whitespaceLot")]
    [InlineData("nullUnit")]
    [InlineData("emptyUnit")]
    [InlineData("whitespaceUnit")]
    public async Task PalletContentLine_Requires_Pallet_SourceLine_Lot_Unit_And_Positive_Quantities(string invalidCase)
    {
        var graph = await SeedGraphAsync();
        var palletId = await AddPalletAsync("PAL-001");

        await AssertSaveIsRejectedAsync(db =>
        {
            var contentLine = NewContentLine(palletId, graph.CommitmentLineId);
            switch (invalidCase)
            {
                case "missingPallet": contentLine.PalletId = palletId + 1000; break;
                case "missingSourceLine": contentLine.SupplyCommitmentLineId = graph.CommitmentLineId + 1000; break;
                case "nullLot": contentLine.LotNumber = null; break;
                case "emptyLot": contentLine.LotNumber = ""; break;
                case "whitespaceLot": contentLine.LotNumber = "   "; break;
                case "nullUnit": contentLine.Unit = null; break;
                case "emptyUnit": contentLine.Unit = ""; break;
                case "whitespaceUnit": contentLine.Unit = "   "; break;
            }
            db.PalletContentLines.Add(contentLine);
        });

        await using var verify = _factory.CreateContext();
        Assert.Equal(0, await verify.PalletContentLines.CountAsync());
    }

    [Theory]
    [InlineData(0, 160)]
    [InlineData(-1, 160)]
    [InlineData(40, 0)]
    [InlineData(40, -1)]
    public async Task PalletContentLine_Rejects_Zero_Quantity_Or_NetWeight(int quantity, int netWeightKg)
    {
        var graph = await SeedGraphAsync();
        var palletId = await AddPalletAsync("PAL-001");

        await AssertSaveIsRejectedAsync(db =>
        {
            var contentLine = NewContentLine(palletId, graph.CommitmentLineId);
            contentLine.Quantity = quantity;
            contentLine.NetWeightKg = netWeightKg;
            db.PalletContentLines.Add(contentLine);
        });
    }

    [Fact]
    public async Task PalletContentLine_Accepts_Smallest_Positive_Quantity_And_NetWeight()
    {
        var graph = await SeedGraphAsync();
        var palletId = await AddPalletAsync("PAL-001");

        await using (var db = _factory.CreateContext())
        {
            var contentLine = NewContentLine(palletId, graph.CommitmentLineId);
            contentLine.Quantity = 0.001m;
            contentLine.NetWeightKg = 0.001m;
            db.PalletContentLines.Add(contentLine);
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        var saved = await verify.PalletContentLines.SingleAsync();
        Assert.Equal(0.001m, saved.Quantity);
        Assert.Equal(0.001m, saved.NetWeightKg);
    }

    [Fact]
    public async Task PalletContentLine_Duplicate_RequestId_Is_Rejected()
    {
        var graph = await SeedGraphAsync();
        var palletId = await AddPalletAsync("PAL-001");
        var requestId = Guid.NewGuid();

        await using (var db = _factory.CreateContext())
        {
            var contentLine = NewContentLine(palletId, graph.CommitmentLineId);
            contentLine.RequestId = requestId;
            db.PalletContentLines.Add(contentLine);
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db =>
        {
            var duplicate = NewContentLine(palletId, graph.CommitmentLineId, "LOT-2");
            duplicate.RequestId = requestId;
            db.PalletContentLines.Add(duplicate);
        });

        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.PalletContentLines.CountAsync());
    }

    [Fact]
    public void PalletContentLine_RequestId_Has_Unique_Index()
    {
        using var db = _factory.CreateContext();
        var entity = db.Model.FindEntityType(typeof(PalletContentLine))!;

        var index = Assert.Single(entity.GetIndexes(), i => i.Properties.Single().Name == nameof(PalletContentLine.RequestId));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public async Task Pallet_Can_Carry_Multiple_Lots_Of_Same_Variant_As_Separate_ContentLines()
    {
        var graph = await SeedGraphAsync();
        var sourceLineA = await AddCommitmentLineAsync(graph, "LOT-A");
        var sourceLineB = await AddCommitmentLineAsync(graph, "LOT-B");
        var palletId = await AddPalletAsync("PAL-MIXED");

        await AddContentLineAsync(palletId, sourceLineA, "LOT-A");
        await AddContentLineAsync(palletId, sourceLineB, "LOT-B");

        await using var verify = _factory.CreateContext();
        var lines = await verify.PalletContentLines
            .Include(c => c.SupplyCommitmentLine)
            .Where(c => c.PalletId == palletId)
            .OrderBy(c => c.LotNumber)
            .ToListAsync();

        Assert.Equal(2, lines.Count);
        Assert.Equal(new[] { "LOT-A", "LOT-B" }, lines.Select(c => c.LotNumber));
        Assert.Equal(new[] { sourceLineA, sourceLineB }, lines.Select(c => c.SupplyCommitmentLineId));
        Assert.All(lines, c => Assert.Equal(graph.ProductVariantId, c.SupplyCommitmentLine.ProductVariantId));
        Assert.All(lines, c => Assert.NotEqual(default, c.CreatedAt));
        Assert.NotEqual(default, (await verify.Pallets.SingleAsync(p => p.Id == palletId)).CreatedAt);
    }

    [Fact]
    public async Task SourceLine_Can_Be_Split_Across_Multiple_Pallets_With_Distinct_ContentLines()
    {
        var graph = await SeedGraphAsync();
        var palletA = await AddPalletAsync("PAL-A");
        var palletB = await AddPalletAsync("PAL-B");

        var contentA = await AddContentLineAsync(palletA, graph.CommitmentLineId);
        var contentB = await AddContentLineAsync(palletB, graph.CommitmentLineId);

        await using var verify = _factory.CreateContext();
        var lines = await verify.PalletContentLines
            .Where(c => c.SupplyCommitmentLineId == graph.CommitmentLineId)
            .ToListAsync();

        Assert.Equal(2, lines.Count);
        Assert.NotEqual(contentA, contentB);
        Assert.Equal(new[] { palletA, palletB }.OrderBy(id => id), lines.Select(c => c.PalletId).OrderBy(id => id));
    }

    [Fact]
    public async Task PalletAuditEvent_Requires_A_Pallet_Or_ContentLine_Target()
    {
        var graph = await SeedGraphAsync();
        var palletId = await AddPalletAsync("PAL-001");
        var contentLineId = await AddContentLineAsync(palletId, graph.CommitmentLineId);

        await AssertSaveIsRejectedAsync(db => db.PalletAuditEvents.Add(new PalletAuditEvent
        {
            EventType = "QualityStatusChanged",
            OccurredAt = DateTime.UtcNow
        }));

        await using (var db = _factory.CreateContext())
        {
            db.PalletAuditEvents.AddRange(
                new PalletAuditEvent { PalletId = palletId, EventType = "LocationChanged", OccurredAt = DateTime.UtcNow },
                new PalletAuditEvent { PalletContentLineId = contentLineId, EventType = "QualityStatusChanged", OldValue = "Quarantine", NewValue = "Approved", ActorReference = "office-1", OccurredAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(2, await verify.PalletAuditEvents.CountAsync());
    }

    [Theory]
    [InlineData("none")]
    [InlineData("existing")]
    [InlineData("missing")]
    public async Task StockMovement_And_Reservation_May_Link_Optional_Existing_ContentLine(string link)
    {
        var graph = await SeedGraphAsync();
        var palletId = await AddPalletAsync("PAL-001");
        var contentLineId = await AddContentLineAsync(palletId, graph.CommitmentLineId);
        int? target = link switch
        {
            "none" => null,
            "existing" => contentLineId,
            _ => contentLineId + 1000
        };

        void Arrange(AgfAdmin.Data.AppDbContext db)
        {
            db.StockMovements.Add(new StockMovement { Type = "Receipt", Quantity = 1m, SupplyCommitmentLineId = graph.CommitmentLineId, PalletContentLineId = target });
            db.StockReservations.Add(new StockReservation
            {
                OrderLineId = graph.OrderLineId,
                SupplyCommitmentLineId = graph.CommitmentLineId,
                PalletContentLineId = target,
                Quantity = 1m,
                Source = "Physical",
                Status = "Active"
            });
        }

        if (link == "missing")
        {
            await AssertSaveIsRejectedAsync(Arrange);
            return;
        }

        await using (var db = _factory.CreateContext())
        {
            Arrange(db);
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal(target, (await verify.StockMovements.SingleAsync()).PalletContentLineId);
        Assert.Equal(target, (await verify.StockReservations.SingleAsync()).PalletContentLineId);
    }

    [Theory]
    [InlineData("palletWithContentLine")]
    [InlineData("palletWithAuditEvent")]
    [InlineData("receiptWithPallet")]
    [InlineData("sourceLineWithContentLine")]
    [InlineData("contentLineWithMovement")]
    [InlineData("contentLineWithReservation")]
    [InlineData("contentLineWithAuditEvent")]
    public async Task Deleting_Pallet_Or_ContentLine_With_Traceability_Dependents_Is_Restricted(string scenario)
    {
        var graph = await SeedGraphAsync();
        int receiptId;
        await using (var db = _factory.CreateContext())
        {
            var receipt = new StockReceipt { RequestId = Guid.NewGuid(), SupplyCommitmentId = graph.CommitmentId };
            db.StockReceipts.Add(receipt);
            await db.SaveChangesAsync();
            receiptId = receipt.Id;
        }

        int palletId;
        await using (var db = _factory.CreateContext())
        {
            var pallet = NewPallet("PAL-001");
            pallet.StockReceiptId = scenario == "receiptWithPallet" ? receiptId : null;
            db.Pallets.Add(pallet);
            await db.SaveChangesAsync();
            palletId = pallet.Id;
        }

        var contentLineId = scenario is "palletWithAuditEvent" or "receiptWithPallet"
            ? (int?)null
            : await AddContentLineAsync(palletId, graph.CommitmentLineId);

        await using (var db = _factory.CreateContext())
        {
            switch (scenario)
            {
                case "palletWithAuditEvent":
                    db.PalletAuditEvents.Add(new PalletAuditEvent { PalletId = palletId, EventType = "LocationChanged", OccurredAt = DateTime.UtcNow });
                    break;
                case "contentLineWithMovement":
                    db.StockMovements.Add(new StockMovement { Type = "Receipt", Quantity = 1m, SupplyCommitmentLineId = graph.CommitmentLineId, PalletContentLineId = contentLineId });
                    break;
                case "contentLineWithReservation":
                    db.StockReservations.Add(new StockReservation
                    {
                        OrderLineId = graph.OrderLineId,
                        SupplyCommitmentLineId = graph.CommitmentLineId,
                        PalletContentLineId = contentLineId,
                        Quantity = 1m,
                        Source = "Physical",
                        Status = "Active"
                    });
                    break;
                case "contentLineWithAuditEvent":
                    db.PalletAuditEvents.Add(new PalletAuditEvent { PalletContentLineId = contentLineId, EventType = "QualityStatusChanged", OccurredAt = DateTime.UtcNow });
                    break;
            }
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db =>
        {
            switch (scenario)
            {
                case "palletWithContentLine":
                case "palletWithAuditEvent":
                    db.Pallets.Remove(new Pallet { Id = palletId });
                    break;
                case "receiptWithPallet":
                    db.StockReceipts.Remove(new StockReceipt { Id = receiptId });
                    break;
                case "sourceLineWithContentLine":
                    db.SupplyCommitmentLines.Remove(new SupplyCommitmentLine { Id = graph.CommitmentLineId });
                    break;
                default:
                    db.PalletContentLines.Remove(new PalletContentLine { Id = contentLineId.Value });
                    break;
            }
        });

        await using var verify = _factory.CreateContext();
        Assert.True(await verify.Pallets.AnyAsync(p => p.Id == palletId));
        Assert.True(await verify.StockReceipts.AnyAsync(r => r.Id == receiptId));
        Assert.True(await verify.SupplyCommitmentLines.AnyAsync(l => l.Id == graph.CommitmentLineId));
        if (contentLineId.HasValue)
        {
            Assert.True(await verify.PalletContentLines.AnyAsync(c => c.Id == contentLineId.Value));
        }
        switch (scenario)
        {
            case "palletWithAuditEvent":
            case "contentLineWithAuditEvent":
                Assert.Equal(1, await verify.PalletAuditEvents.CountAsync());
                break;
            case "contentLineWithMovement":
                Assert.Equal(1, await verify.StockMovements.CountAsync());
                break;
            case "contentLineWithReservation":
                Assert.Equal(1, await verify.StockReservations.CountAsync());
                break;
        }
    }

    // ---- Toestandsregels van pallets en kwaliteit (database-checks), audit-RequestId en versies ----

    [Theory]
    [InlineData("Received", "Warehouse", null, true)]
    [InlineData("Received", "Warehouse", "COLD-01", true)]
    [InlineData("Expected", "Warehouse", null, false)]
    [InlineData("InTransit", "Warehouse", null, false)]
    [InlineData("Dispatched", "Warehouse", null, false)]
    [InlineData("Delivered", "Warehouse", null, false)]
    [InlineData("Cancelled", "Warehouse", null, false)]
    [InlineData("Received", "Supplier", "COLD-01", false)]
    [InlineData("Dispatched", "InTransit", "COLD-01", false)]
    [InlineData("Received", "Warehouse", "   ", false)]
    [InlineData("Received", "Warehouse", "", false)]
    [InlineData("Expected", "Supplier", null, true)]
    [InlineData("Received", "Crossdock", null, true)]
    [InlineData("Dispatched", "InTransit", null, true)]
    [InlineData("Delivered", "Customer", null, true)]
    [InlineData("Delivered", "External", null, true)]
    [InlineData("Delivered", "Crossdock", null, false)]
    [InlineData("Delivered", "InTransit", null, false)]
    [InlineData("Delivered", "Supplier", null, false)]
    [InlineData("Cancelled", "Supplier", null, true)]
    public async Task Pallet_Status_Location_And_Bin_Combination_Is_Enforced_By_Database(string status, string location, string bin, bool accepted)
    {
        await using var db = _factory.CreateContext();
        var pallet = NewPallet("PAL-001");
        pallet.LogisticsStatus = status;
        pallet.LocationContext = location;
        pallet.WarehouseLocation = bin;
        db.Pallets.Add(pallet);

        if (accepted)
        {
            await db.SaveChangesAsync();
        }
        else
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Changing_A_Warehouse_Pallet_To_An_Invalid_Combination_Is_Rejected_By_Database()
    {
        var palletId = await AddPalletAsync("PAL-001");

        await using (var db = _factory.CreateContext())
        {
            (await db.Pallets.SingleAsync()).LogisticsStatus = "Dispatched";
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using var verify = _factory.CreateContext();
        Assert.Equal("Received", (await verify.Pallets.SingleAsync(p => p.Id == palletId)).LogisticsStatus);
    }

    [Theory]
    [InlineData("status", "Shipped")]
    [InlineData("status", "received")]
    [InlineData("location", "Truck")]
    [InlineData("location", "warehouse")]
    public async Task Pallet_Rejects_Unknown_Status_Or_Location_Values(string field, string value)
    {
        await AssertSaveIsRejectedAsync(db =>
        {
            var pallet = NewPallet("PAL-001");
            if (field == "status")
            {
                pallet.LogisticsStatus = value;
                pallet.LocationContext = "Supplier";
            }
            else
            {
                pallet.LocationContext = value;
            }
            db.Pallets.Add(pallet);
        });
    }

    [Theory]
    [InlineData("Quarantine", true)]
    [InlineData("Approved", true)]
    [InlineData("Blocked", true)]
    [InlineData("Rejected", true)]
    [InlineData("PartiallyApproved", false)]
    [InlineData("approved", false)]
    public async Task PalletContentLine_QualityStatus_Must_Be_An_Allowed_Value(string qualityStatus, bool accepted)
    {
        var graph = await SeedGraphAsync();
        var palletId = await AddPalletAsync("PAL-001");

        await using var db = _factory.CreateContext();
        var contentLine = NewContentLine(palletId, graph.CommitmentLineId);
        contentLine.QualityStatus = qualityStatus;
        db.PalletContentLines.Add(contentLine);

        if (accepted)
        {
            await db.SaveChangesAsync();
        }
        else
        {
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task PalletAuditEvent_RequestId_Is_Unique_But_Optional()
    {
        var palletId = await AddPalletAsync("PAL-001");
        var requestId = Guid.NewGuid();

        await using (var db = _factory.CreateContext())
        {
            db.PalletAuditEvents.AddRange(
                new PalletAuditEvent { PalletId = palletId, EventType = "PalletCreated", OccurredAt = DateTime.UtcNow },
                new PalletAuditEvent { PalletId = palletId, EventType = "PalletCreated", OccurredAt = DateTime.UtcNow },
                new PalletAuditEvent { PalletId = palletId, EventType = "LocationChanged", RequestId = requestId, OccurredAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        await AssertSaveIsRejectedAsync(db => db.PalletAuditEvents.Add(
            new PalletAuditEvent { PalletId = palletId, EventType = "LocationChanged", RequestId = requestId, OccurredAt = DateTime.UtcNow }));

        await using var verify = _factory.CreateContext();
        Assert.Equal(3, await verify.PalletAuditEvents.CountAsync());
    }

    [Fact]
    public void Pallet_State_Metadata_Has_Version_Tokens_And_Audit_RequestId_Index()
    {
        using var db = _factory.CreateContext();

        var pallet = db.Model.FindEntityType(typeof(Pallet))!;
        Assert.True(pallet.FindProperty(nameof(Pallet.Version))!.IsConcurrencyToken);
        var contentLine = db.Model.FindEntityType(typeof(PalletContentLine))!;
        Assert.True(contentLine.FindProperty(nameof(PalletContentLine.Version))!.IsConcurrencyToken);

        var auditEvent = db.Model.FindEntityType(typeof(PalletAuditEvent))!;
        Assert.True(auditEvent.FindProperty(nameof(PalletAuditEvent.RequestId))!.IsNullable);
        var index = Assert.Single(auditEvent.GetIndexes(), i => i.Properties.Single().Name == nameof(PalletAuditEvent.RequestId));
        Assert.True(index.IsUnique);
    }
}
