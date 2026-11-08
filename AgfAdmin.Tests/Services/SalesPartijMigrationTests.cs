using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Migrations;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Partij P2: vaste tekst en aantal cijfers in het nummerformaat (5{JJ}{MM}{volgnr} = 52611002), en de migratie AddSalesPartijen: elke
// bestaande verkooporder een verkoop-partij op volgorde van aanmaken, niets bestaands gewijzigd (SQLite-variant van dezelfde SQL).
public sealed class SalesPartijMigrationTests : IDisposable
{
    private readonly SalesBeforeArrivalScenario _s = new();

    public SalesPartijMigrationTests() => _s.Factory.Calendar = new FixedBusinessCalendar(new DateOnly(2026, 11, 5));

    public void Dispose() => _s.Dispose();

    private async Task<ServiceResult<PartijSummaryDto>> PurchaseAsync(string number = null)
    {
        await using var db = _s.Factory.CreateContext();
        return await new PartijService(db).CreateAsync(new CreatePartijDto { RequestId = Guid.NewGuid(), Number = number });
    }

    [Theory]
    [InlineData("5{JJ}{MM}{volgnr}", 2, 3, "52611002")]
    [InlineData("5{JJ}{MM}{volgnr}", 45, 2, "5261145")]
    [InlineData("5{JJ}{MM}{volgnr}", 100, 3, "52611100")]
    [InlineData("{JJ}{MM}{volgnr}", 40, 2, "261140")]
    [InlineData("P-{JJJJ}/{volgnr}", 7, 1, "P-2026/7")]
    [InlineData("{JJ}{MM}{volgnr}", 1234, 3, "26111234")]
    public void Fixed_Text_And_The_Number_Of_Digits_Shape_The_Number(string format, int sequence, int digits, string expected)
    {
        Assert.Null(PartijRules.FormatError(format));
        Assert.Equal(expected, PartijRules.Format(format, new DateOnly(2026, 11, 5), sequence, digits));
    }

    [Fact]
    public async Task The_Migration_Numbers_Existing_Orders_In_Order_Of_Creation_And_Changes_Nothing_Else()
    {
        await _s.ArrangeAsync();
        var purchase = (await PurchaseAsync("2610251")).Value;
        await using (var db = _s.Factory.CreateContext())
        {
            foreach (var createdAt in new[] { new DateTime(2026, 10, 20, 9, 0, 0), new DateTime(2025, 12, 1, 9, 0, 0), new DateTime(2026, 11, 2, 9, 0, 0), new DateTime(2026, 10, 19, 9, 0, 0) })
            {
                db.SalesOrders.Add(new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = _s.CustomerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "seed", CreatedAt = DateTime.SpecifyKind(createdAt, DateTimeKind.Utc) });
            }
            await db.SaveChangesAsync();
            // De situatie vóór de migratie: orders zonder verkoop-partij (buiten de guard om, zoals de oude database).
            await db.Database.ExecuteSqlRawAsync(@"DELETE FROM ""Partijen"" WHERE ""Kind"" = 'Sales'");
        }

        // Twee keer uitvoeren: de tweede keer heeft elke order al een verkoop-partij en gebeurt er niets.
        for (var run = 0; run < 2; run++)
        {
            await using var db = _s.Factory.CreateContext();
            await db.Database.ExecuteSqlRawAsync(AddSalesPartijen.BackfillSalesPartijenSql(postgres: false));
        }

        await using var verify = _s.Factory.CreateContext();
        var lots = await verify.Partijen.Where(p => p.Kind == "Sales").Include(p => p.SalesOrder).ToListAsync();
        Assert.Equal(4, lots.Count);
        Assert.Equal(new[] { ("2512001", "2025", 1), ("2610001", "2026", 1), ("2610002", "2026", 2), ("2611003", "2026", 3) },
            lots.OrderBy(p => p.SalesOrder.CreatedAt).Select(p => (p.Number, p.SequencePeriod, p.Sequence ?? 0)));
        Assert.All(lots, p => Assert.Equal((p.NumberKey, "Migratie", "Open"), (PartijRules.NumberKey(p.Number), p.CreatedBy, p.Status)));
        // De inkoop-partij blijft zoals ze was.
        Assert.Equal(("2610251", "Purchase", (int?)null), await verify.Partijen.Where(p => p.Id == purchase.Id).Select(p => ValueTuple.Create(p.Number, p.Kind, p.SalesOrderId)).SingleAsync());
    }
}
