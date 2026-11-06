using AgfAdmin.Migrations;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Infrastructure;

// De standaardsoorten van de kostenartikelen met dezelfde SQL als de migratie AddCostArticles (de testdatabase wordt uit het model gemaakt).
public static class CostArticleTestData
{
    public static async Task<Dictionary<string, int>> SeedStandardAsync(SqliteAppDbContextFactory factory)
    {
        await using var db = factory.CreateContext();
        await db.Database.ExecuteSqlRawAsync(AddCostArticles.SeedStandardSql);
        return await db.CostArticles.AsNoTracking().ToDictionaryAsync(c => c.Name, c => c.Id);
    }
}
