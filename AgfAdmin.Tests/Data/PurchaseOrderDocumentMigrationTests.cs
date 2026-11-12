using AgfAdmin.Migrations;
using AgfAdmin.Models;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Data;

// Migratie AddPurchaseOrderDocumentStatus (stap 4a): een nieuwe order is Open; een bestaande order met een factuurnummer wordt
// Definitive via Invoice, met tijdstip en medewerker van het eerste factuurnummer; een order met alleen een creditnota blijft Open.
public class PurchaseOrderDocumentMigrationTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();
    private PurchaseOrderSeed _seed;

    public void Dispose() => _factory.Dispose();

    private async Task<(int OrderId, int LineId)> OrderAsync()
    {
        _seed ??= await PurchaseOrderTestData.SeedAsync(_factory);
        var order = await PurchaseOrderTestData.CreateOrderAsync(_factory, PurchaseOrderTestData.OrderRequest(_seed));
        return (order.Id, order.Lines.Single().Id);
    }

    [Fact]
    public async Task Migration_Makes_Orders_With_An_Invoice_Definitive_And_Leaves_The_Rest_Open()
    {
        var invoiced = await OrderAsync();
        var creditOnly = await OrderAsync();
        var plain = await OrderAsync();
        var first = new DateTime(2026, 11, 3, 9, 0, 0, DateTimeKind.Utc);
        await using (var db = _factory.CreateContext())
        {
            db.SupplyCommitmentInvoiceRefs.AddRange(
                InvoiceRef(invoiced.OrderId, "Invoice", "F-2", first.AddDays(1), "Piet"),
                InvoiceRef(invoiced.OrderId, "Invoice", "F-1", first, "Jan"),
                InvoiceRef(creditOnly.OrderId, "CreditNote", "C-1", first, "Jan"));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(AddPurchaseOrderDocumentStatus.FinalizeInvoicedOrdersSql);
        }

        await using var check = _factory.CreateContext();
        var orders = await check.SupplyCommitments.AsNoTracking().ToDictionaryAsync(c => c.Id);
        Assert.Equal(("Definitive", "Invoice", "Jan", first), (orders[invoiced.OrderId].DocumentStatus, orders[invoiced.OrderId].FinalizedVia, orders[invoiced.OrderId].FinalizedBy,
            DateTime.SpecifyKind(orders[invoiced.OrderId].FinalizedAt!.Value, DateTimeKind.Utc)));
        Assert.Equal(("Open", null), (orders[creditOnly.OrderId].DocumentStatus, orders[creditOnly.OrderId].FinalizedVia));
        Assert.Equal(("Open", null), (orders[plain.OrderId].DocumentStatus, orders[plain.OrderId].FinalizedAt));
    }

    [Fact]
    public async Task The_Database_Refuses_Definitive_Without_How_Or_Open_With_Finalized_Data()
    {
        var order = await OrderAsync();
        await using var db = _factory.CreateContext();
        // Rechtstreeks in de database (buiten de guard om): ook een lege FinalizedVia bij Definitive weigert de check-constraint.
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"SupplyCommitments\" SET \"DocumentStatus\" = 'Definitive', \"FinalizedAt\" = '2026-11-12', \"FinalizedBy\" = 'Jan', \"FinalizedVia\" = NULL WHERE \"Id\" = {order.OrderId}"));
        await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
            $"UPDATE \"SupplyCommitments\" SET \"FinalizedBy\" = 'Jan' WHERE \"Id\" = {order.OrderId}"));
        var stored = await db.SupplyCommitments.SingleAsync(c => c.Id == order.OrderId);
        Assert.Throws<ArgumentException>(() => AgfAdmin.Services.PurchaseOrderDocumentRules.Finalize(stored, "Telefoon", "Jan", DateTime.UtcNow, Guid.NewGuid()));
    }

    private static SupplyCommitmentInvoiceRef InvoiceRef(int orderId, string kind, string number, DateTime at, string actor) => new()
    {
        RequestId = Guid.NewGuid(), SupplyCommitmentId = orderId, Kind = kind, InvoiceNumber = number, InvoiceNumberKey = number.ToLowerInvariant(), ActorReference = actor, CreatedAt = at,
    };
}
