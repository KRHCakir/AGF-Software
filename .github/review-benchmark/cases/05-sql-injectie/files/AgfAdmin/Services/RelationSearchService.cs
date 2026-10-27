using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Zoekt relaties op een deel van de naam, voor het zoekveld bovenaan de relatielijst.
    public sealed class RelationSearchService(AppDbContext db)
    {
        public Task<List<Customer>> SearchAsync(string term, CancellationToken ct)
        {
            var cleaned = term.Trim();
            return db.Customers
                .FromSqlRaw($"SELECT * FROM \"Customers\" WHERE \"Name\" ILIKE '%{cleaned}%'")
                .OrderBy(c => c.Name)
                .Take(50)
                .ToListAsync(ct);
        }
    }
}
