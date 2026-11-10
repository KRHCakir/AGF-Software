using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 27: emballage (zie SalesOrderService.Packaging) ----
        //
        // 1. een emballageregel op een verkooporder verwijst altijd naar een artikel van de soort Packaging, nooit naar een kostensoort;
        // 2. een opgeslagen emballageregel verandert nooit en wordt nooit verwijderd: artikel, aantal, prijs en btw zijn wat de factuur
        //    later overneemt;
        // 3. de soort van een kostenartikel verandert nooit (vergeleken met de opgeslagen waarde in de database).
        private static async Task CheckPackagingAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var lines = db.ChangeTracker.Entries<SalesOrderPackagingLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            if (lines.Any(e => e.State != EntityState.Added))
            {
                throw new TraceabilityViolationException("A packaging line on a sales order never changes and is never removed.");
            }

            var articles = db.ChangeTracker.Entries<CostArticle>().Where(e => e.State == EntityState.Modified).ToDictionary(e => e.Entity.Id, e => e.Entity);
            if (articles.Count > 0)
            {
                var ids = articles.Keys.ToList();
                var stored = await ToListAsync(db.CostArticles.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.Kind }), async, cancellationToken);
                if (stored.Any(c => !string.Equals(c.Kind, articles[c.Id].Kind, StringComparison.Ordinal)))
                {
                    throw new TraceabilityViolationException("The kind of a cost article never changes.");
                }
            }

            if (lines.Count == 0)
            {
                return;
            }
            var articleIds = lines.Select(e => e.Entity.CostArticleId).Distinct().ToList();
            var kinds = (await ToListAsync(db.CostArticles.AsNoTracking().Where(c => articleIds.Contains(c.Id)).Select(c => new { c.Id, c.Kind }), async, cancellationToken))
                .ToDictionary(c => c.Id, c => c.Kind);
            foreach (var line in lines.Select(e => e.Entity))
            {
                var kind = line.CostArticle?.Kind ?? kinds.GetValueOrDefault(line.CostArticleId);
                if (kind != CostArticleRules.KindPackaging)
                {
                    throw new TraceabilityViolationException($"A packaging line on a sales order needs a packaging article, not article {line.CostArticleId}.");
                }
            }
        }
    }
}
