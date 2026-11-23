using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 38: dienstregels op een verkooporder (feedback 22 november, zie SalesOrderService.Services) ----
        //
        // 1. een dienstregel verwijst altijd naar een kostenartikel van de soort Cost (Dienst), nooit naar emballage;
        // 2. een opgeslagen dienstregel verandert nooit en wordt nooit verwijderd: artikel, aantal, prijs, korting en btw zijn wat de factuur
        //    later overneemt.
        private static async Task CheckServiceLinesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var lines = db.ChangeTracker.Entries<SalesOrderServiceLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            if (lines.Count == 0) return;
            if (lines.Any(e => e.State != EntityState.Added))
            {
                throw new TraceabilityViolationException("A service line on a sales order never changes and is never removed.");
            }
            var articleIds = lines.Select(e => e.Entity.CostArticleId).Distinct().ToList();
            var kinds = (await ToListAsync(db.CostArticles.AsNoTracking().Where(c => articleIds.Contains(c.Id)).Select(c => new { c.Id, c.Kind }), async, cancellationToken))
                .ToDictionary(c => c.Id, c => c.Kind);
            foreach (var line in lines.Select(e => e.Entity))
            {
                var kind = line.CostArticle?.Kind ?? kinds.GetValueOrDefault(line.CostArticleId);
                if (kind != CostArticleRules.KindCost)
                {
                    throw new TraceabilityViolationException($"A service line on a sales order needs a service article, not article {line.CostArticleId}.");
                }
            }
        }
    }
}
