using AgfAdmin.Controllers;
using AgfAdmin.Data;
using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Controllers;

public class IncomingInvoicesControllerTests
{
    private static readonly DateTime OriginalUpdatedAt = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static AppDbContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AppDbContext(options);
    }

    private static IncomingInvoicesController CreateController(AppDbContext db) =>
        new(db, new UnusedEmailIngestionService());

    private static async Task<(string DatabaseName, int InvoiceId)> SeedInvoiceAsync(string status)
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var db = CreateContext(databaseName);

        var invoice = new IncomingInvoice
        {
            InvoiceNumber = "SUP-001",
            InvoiceDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            TotalAmount = 100.00m,
            VATAmount = 21.00m,
            Status = status,
            CreatedAt = OriginalUpdatedAt,
            UpdatedAt = OriginalUpdatedAt
        };
        db.IncomingInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (databaseName, invoice.Id);
    }

    private static async Task<IncomingInvoice> LoadInvoiceAsync(string databaseName, int id)
    {
        await using var db = CreateContext(databaseName);
        return await db.IncomingInvoices.AsNoTracking().SingleAsync(i => i.Id == id);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Processing")]
    [InlineData("NeedsReview")]
    public async Task Approve_From_Reviewable_Status_Sets_Approved_And_UpdatedAt(string initialStatus)
    {
        var (databaseName, id) = await SeedInvoiceAsync(initialStatus);

        await using (var db = CreateContext(databaseName))
        {
            var result = await CreateController(db).ApproveInvoice(id);
            Assert.IsType<OkObjectResult>(result);
        }

        var invoice = await LoadInvoiceAsync(databaseName, id);
        Assert.Equal("Approved", invoice.Status);
        Assert.True(invoice.UpdatedAt > OriginalUpdatedAt);
    }

    [Fact]
    public async Task Reject_From_NeedsReview_Sets_Rejected_And_UpdatedAt()
    {
        var (databaseName, id) = await SeedInvoiceAsync("NeedsReview");

        await using (var db = CreateContext(databaseName))
        {
            var result = await CreateController(db).RejectInvoice(id);
            Assert.IsType<OkObjectResult>(result);
        }

        var invoice = await LoadInvoiceAsync(databaseName, id);
        Assert.Equal("Rejected", invoice.Status);
        Assert.True(invoice.UpdatedAt > OriginalUpdatedAt);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    [InlineData("Error")]
    public async Task Approve_From_Terminal_Status_Returns_Conflict_And_Keeps_Status(string initialStatus)
    {
        var (databaseName, id) = await SeedInvoiceAsync(initialStatus);

        await using (var db = CreateContext(databaseName))
        {
            var result = await CreateController(db).ApproveInvoice(id);
            Assert.IsType<ConflictObjectResult>(result);
        }

        var invoice = await LoadInvoiceAsync(databaseName, id);
        Assert.Equal(initialStatus, invoice.Status);
        Assert.Equal(OriginalUpdatedAt, invoice.UpdatedAt);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    [InlineData("Error")]
    public async Task Reject_From_Terminal_Status_Returns_Conflict_And_Keeps_Status(string initialStatus)
    {
        var (databaseName, id) = await SeedInvoiceAsync(initialStatus);

        await using (var db = CreateContext(databaseName))
        {
            var result = await CreateController(db).RejectInvoice(id);
            Assert.IsType<ConflictObjectResult>(result);
        }

        var invoice = await LoadInvoiceAsync(databaseName, id);
        Assert.Equal(initialStatus, invoice.Status);
        Assert.Equal(OriginalUpdatedAt, invoice.UpdatedAt);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public async Task Update_When_Approved_Or_Rejected_Returns_Conflict_And_Leaves_Fields_Unchanged(string initialStatus)
    {
        var (databaseName, id) = await SeedInvoiceAsync(initialStatus);

        await using (var db = CreateContext(databaseName))
        {
            var body = new IncomingInvoice
            {
                Id = id,
                InvoiceNumber = "CHANGED",
                TotalAmount = 999.99m,
                VATAmount = 1.11m,
                Status = "NeedsReview"
            };

            var result = await CreateController(db).UpdateIncomingInvoice(id, body);
            Assert.IsType<ConflictObjectResult>(result);
        }

        var invoice = await LoadInvoiceAsync(databaseName, id);
        Assert.Equal(initialStatus, invoice.Status);
        Assert.Equal("SUP-001", invoice.InvoiceNumber);
        Assert.Equal(100.00m, invoice.TotalAmount);
        Assert.Equal(21.00m, invoice.VATAmount);
        Assert.Equal(OriginalUpdatedAt, invoice.UpdatedAt);
    }

    [Fact]
    public async Task Update_Ignores_Status_From_Body()
    {
        var (databaseName, id) = await SeedInvoiceAsync("NeedsReview");
        var dueDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var db = CreateContext(databaseName))
        {
            var body = new IncomingInvoice
            {
                Id = id,
                InvoiceNumber = "SUP-001-CORRECTED",
                InvoiceDate = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc),
                DueDate = dueDate,
                TotalAmount = 121.00m,
                VATAmount = 21.00m,
                Status = "Approved",
                CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            var result = await CreateController(db).UpdateIncomingInvoice(id, body);
            Assert.IsType<NoContentResult>(result);
        }

        var invoice = await LoadInvoiceAsync(databaseName, id);
        Assert.Equal("NeedsReview", invoice.Status);
        Assert.Equal("SUP-001-CORRECTED", invoice.InvoiceNumber);
        Assert.Equal(dueDate, invoice.DueDate);
        Assert.Equal(121.00m, invoice.TotalAmount);
        Assert.Equal(OriginalUpdatedAt, invoice.CreatedAt);
        Assert.True(invoice.UpdatedAt > OriginalUpdatedAt);
    }

    [Fact]
    public async Task Approve_Does_Not_Change_Inventory()
    {
        var (databaseName, id) = await SeedInvoiceAsync("NeedsReview");
        int inventoryId;
        await using (var db = CreateContext(databaseName))
        {
            var inventory = new Inventory { Location = "Warehouse A", Quantity = 10, ProductVariantId = 1 };
            db.Inventory.Add(inventory);
            await db.SaveChangesAsync();
            inventoryId = inventory.Id;
        }

        await using (var db = CreateContext(databaseName))
        {
            await CreateController(db).ApproveInvoice(id);
        }

        await using var verify = CreateContext(databaseName);
        var stock = Assert.Single(await verify.Inventory.ToListAsync());
        Assert.Equal(inventoryId, stock.Id);
        Assert.Equal(10, stock.Quantity);
    }

    private sealed class UnusedEmailIngestionService : IEmailIngestionService
    {
        public Task CheckForNewEmailsAsync() => throw new NotSupportedException();

        public Task<IncomingInvoice> ProcessEmailAttachmentAsync(string filePath, string senderEmail) =>
            throw new NotSupportedException();
    }
}
