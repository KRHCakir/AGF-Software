using AgfAdmin.Migrations;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Infrastructure;

// De standaardsoorten van de kostenartikelen en de standaard-emballage met dezelfde SQL als de migraties AddCostArticles en AddPackagingArticles (de testdatabase wordt uit het model gemaakt).
public static class CostArticleTestData
{
    public static async Task<Dictionary<string, int>> SeedStandardAsync(SqliteAppDbContextFactory factory)
    {
        await using var db = factory.CreateContext();
        await db.Database.ExecuteSqlRawAsync(AddCostArticles.SeedStandardSql);
        await db.Database.ExecuteSqlRawAsync(AddPackagingArticles.SeedStandardSql);
        return await db.CostArticles.AsNoTracking().ToDictionaryAsync(c => c.Name, c => c.Id);
    }
}
