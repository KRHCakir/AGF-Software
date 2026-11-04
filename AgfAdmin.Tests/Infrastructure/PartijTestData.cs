using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Infrastructure;

// Partij rechtstreeks in de database, voor tests die de nummering of de service niet zelf toetsen.
public static class PartijTestData
{
    public static async Task<int> CreateAsync(SqliteAppDbContextFactory factory, string number)
    {
        await using var db = factory.CreateContext();
        var partij = new Partij { Number = number, NumberKey = PartijRules.NumberKey(number), CreatedBy = "Test" };
        db.Partijen.Add(partij);
        await db.SaveChangesAsync();
        return partij.Id;
    }

    // Koppelt een order (kop en alle regels) aan de partij, zoals een nieuwe order met partij dat doet.
    public static async Task LinkOrderAsync(SqliteAppDbContextFactory factory, int orderId, int partijId)
    {
        await using var db = factory.CreateContext();
        var order = await db.SupplyCommitments.Include(c => c.Lines).SingleAsync(c => c.Id == orderId);
        order.PartijId = partijId;
        foreach (var line in order.Lines)
        {
            line.PartijId = partijId;
        }
        await db.SaveChangesAsync();
    }
}
