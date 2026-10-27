using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Inkoopordernummer: centraal toegekend bij opslaan (elke schrijver), per jaar doorlopend, uniek; en de nummering bij de migratie.
public class PurchaseOrderNumberTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<int> SeedVendorAsync()
    {
        await using var db = _factory.CreateContext();
        var vendor = new Vendor { Name = "Nature's Pride" };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync();
        return vendor.Id;
    }

    private static SupplyCommitment Commitment(int vendorId, DateTime createdAt) => new()
    {
        RequestId = Guid.NewGuid(), VendorId = vendorId, Route = "Warehouse", Status = "Expected", Origin = "Peru", CreatedAt = createdAt
    };

    [Fact]
    public async Task OrderNumber_RunsPerYearOfCreation_InAmsterdam_AndIsUnique()
    {
        var vendorId = await SeedVendorAsync();
        await using (var db = _factory.CreateContext())
        {
            db.SupplyCommitments.Add(Commitment(vendorId, new DateTime(2025, 12, 31, 22, 30, 0, DateTimeKind.Utc))); // 31-12 23:30 in Amsterdam: nog 2025
            db.SupplyCommitments.Add(Commitment(vendorId, new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc))); // 1-1 00:30 in Amsterdam: al 2026
            await db.SaveChangesAsync();
        }
        await using (var db = _factory.CreateContext())
        {
            // Twee nieuwe in één save: opeenvolgend, na het hoogste opgeslagen nummer van dat jaar.
            db.SupplyCommitments.Add(Commitment(vendorId, new DateTime(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc)));
            db.SupplyCommitments.Add(Commitment(vendorId, new DateTime(2026, 10, 27, 9, 1, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();
        }

        await using var verify = _factory.CreateContext();
        var numbers = await verify.SupplyCommitments.OrderBy(c => c.Id).Select(c => new { c.OrderNumber, c.OrderYear, c.OrderSequence, c.OrderedOn }).ToListAsync();
        Assert.Equal(new[] { "IO-2025-0001", "IO-2026-0001", "IO-2026-0002", "IO-2026-0003" }, numbers.Select(n => n.OrderNumber));
        Assert.Equal(new[] { 2025, 2026, 2026, 2026 }, numbers.Select(n => n.OrderYear));
        Assert.Equal(new DateOnly(2025, 12, 31), numbers[0].OrderedOn);
        Assert.Equal(new DateOnly(2026, 1, 1), numbers[1].OrderedOn);

        // De database weigert een dubbel nummer of volgnummer, ook als een schrijver het zelf invult.
        await using var duplicate = _factory.CreateContext();
        var copy = Commitment(vendorId, DateTime.UtcNow);
        copy.OrderNumber = "IO-2026-0002";
        copy.OrderYear = 2026;
        copy.OrderSequence = 9;
        duplicate.SupplyCommitments.Add(copy);
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [Fact]
    public void FormatNumber_HasAtLeastFourDigits()
    {
        Assert.Equal("IO-2026-0001", PurchaseOrderRules.FormatNumber(2026, 1));
        Assert.Equal("IO-2026-12345", PurchaseOrderRules.FormatNumber(2026, 12345));
    }

    // Spiegel van de migratie AddPurchaseOrders: bestaande orders genummerd op CreatedAt (dan Id), per jaar in Europe/Amsterdam,
    // zonder gaten; OrderedOn = aanmaakdatum. De SQL zelf is daarnaast op een kopie van de smoke-database gecontroleerd.
    [Fact]
    public void MigrationNumbering_OrdersByCreatedAtPerYear_WithoutGaps()
    {
        var existing = new[]
        {
            (Id: 7, CreatedAt: new DateTime(2026, 10, 18, 16, 11, 0, DateTimeKind.Utc)),
            (Id: 3, CreatedAt: new DateTime(2026, 10, 18, 16, 11, 0, DateTimeKind.Utc)),
            (Id: 1, CreatedAt: new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc)),
            (Id: 2, CreatedAt: new DateTime(2025, 12, 31, 22, 30, 0, DateTimeKind.Utc)),
            (Id: 9, CreatedAt: new DateTime(2026, 10, 26, 13, 9, 3, DateTimeKind.Utc))
        };
        var numbered = PurchaseOrderRules.InitialNumbers(existing).ToDictionary(n => n.Id);
        Assert.Equal("IO-2025-0001", numbered[2].OrderNumber);
        Assert.Equal(new DateOnly(2025, 12, 31), numbered[2].OrderedOn);
        Assert.Equal("IO-2026-0001", numbered[1].OrderNumber);
        Assert.Equal(new DateOnly(2026, 1, 1), numbered[1].OrderedOn);
        Assert.Equal("IO-2026-0002", numbered[3].OrderNumber);
        Assert.Equal("IO-2026-0003", numbered[7].OrderNumber);
        Assert.Equal("IO-2026-0004", numbered[9].OrderNumber);

        // De migratie zelf: één backfill-UPDATE in dezelfde volgorde en tijdzone, vóór de unieke indexen en de check op het volgnummer.
        var operations = new AgfAdmin.Migrations.AddPurchaseOrders().UpOperations.ToList();
        var backfill = operations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>().Single(o => o.Sql.Contains("UPDATE \"SupplyCommitments\""));
        Assert.Contains("ROW_NUMBER() OVER (PARTITION BY EXTRACT(YEAR FROM (\"CreatedAt\" AT TIME ZONE 'Europe/Amsterdam')) ORDER BY \"CreatedAt\", \"Id\")", backfill.Sql);
        Assert.Contains("lpad(n.sequence::text, 4, '0')", backfill.Sql);
        Assert.Contains("CAST((\"CreatedAt\" AT TIME ZONE 'Europe/Amsterdam') AS date)", backfill.Sql);
        var uniqueNumber = operations.FindIndex(o => o is Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation { Name: "IX_SupplyCommitments_OrderNumber" });
        Assert.True(operations.IndexOf(backfill) < uniqueNumber);
        Assert.True(operations.IndexOf(backfill) < operations.FindIndex(o => o is Microsoft.EntityFrameworkCore.Migrations.Operations.AddCheckConstraintOperation { Name: "CK_SupplyCommitments_OrderSequence_Positive" }));
    }

}
