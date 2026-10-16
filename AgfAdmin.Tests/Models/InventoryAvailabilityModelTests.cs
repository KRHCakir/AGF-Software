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
        var commitment = new SupplyCommitment { Vendor = vendor, Route = "Warehouse", Status = "Expected", Origin = "Peru" };
        var commitmentLine = new SupplyCommitmentLine { SupplyCommitment = commitment, ProductVariant = variant, ExpectedQuantity = 3 };

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
            ExpectedQuantity = 1
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
            ExpectedQuantity = 1
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
            Quantity = 1,
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
            Quantity = 1,
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
                Quantity = 1,
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
        Assert.Equal(1, saved.Quantity);
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
                db.StockMovements.Add(new StockMovement { Type = "Correction", Quantity = 1, SupplyCommitmentLineId = graph.CommitmentLineId, Reason = "Test" });
            }
            else
            {
                db.StockReservations.Add(new StockReservation
                {
                    OrderLineId = graph.OrderLineId,
                    SupplyCommitmentLineId = graph.CommitmentLineId,
                    Quantity = 1,
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
            db.StockMovements.Add(new StockMovement { Type = "Receipt", Quantity = 3, SupplyCommitmentLineId = graph.CommitmentLineId, StockReceipt = receipt });
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
                Quantity = 1,
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
}
