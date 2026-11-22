using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 35: de oude herkomsttekst van een inkooporder (feedback 22 november) ----
        //
        // OriginLegacyText bewaart de herkomsttekst van vóór de omzetting naar een landcode, voor de audit. Alleen de migratie
        // OriginCountryOnPurchaseOrder vult hem; de applicatie zet hem nooit bij een nieuwe order en wijzigt of wist hem nooit.
        // Vergeleken met de opgeslagen waarde in de database, niet met OriginalValues.
        private static async Task CheckOriginLegacyTextAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var entries = db.ChangeTracker.Entries<SupplyCommitment>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList();
            if (entries.Any(e => e.State == EntityState.Added && e.Entity.OriginLegacyText != null))
            {
                throw new TraceabilityViolationException("The old origin text of a purchase order is only kept by the migration; a new order has none.");
            }
            var modified = entries.Where(e => e.State == EntityState.Modified).ToList();
            if (modified.Count == 0) return;
            var ids = modified.Select(e => e.Entity.Id).ToList();
            var stored = (await ToListAsync(db.SupplyCommitments.AsNoTracking().Where(c => ids.Contains(c.Id)).Select(c => new { c.Id, c.OriginLegacyText }), async, cancellationToken))
                .ToDictionary(c => c.Id, c => c.OriginLegacyText);
            foreach (var entry in modified)
            {
                if (stored.TryGetValue(entry.Entity.Id, out var before) && !string.Equals(before, entry.Entity.OriginLegacyText, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"The old origin text of purchase order {entry.Entity.Id} never changes.");
                }
            }
        }
    }
}
