using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private sealed record StoredTradeClass(int Id, string TradeClass);

        // ---- Regel 21: handelsklasse uit de vaste lijst (zie ArticleRules.NormalizeTradeClass) ----
        //
        // Een nieuw artikel krijgt een klasse uit de vaste lijst (Extra, I, II) of geen klasse; wordt de klasse van een artikel gewijzigd,
        // dan ook. Een oude, niet herkende waarde ("klasse A") blijft staan zolang niemand de klasse wijzigt; zo'n artikel staat in
        // "Maat en klasse opschonen". Vergeleken met de opgeslagen waarde in de database, niet met OriginalValues (bij een losgekoppelde
        // Update zijn die gelijk aan de nieuwe waarde). Leeg ("") telt als geen klasse.
        private static async Task CheckTradeClassAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            static bool Allowed(string value) => string.IsNullOrEmpty(value) || ArticleRules.IsTradeClass(value);

            var entries = db.ChangeTracker.Entries<ProductVariant>()
                .Where(e => (e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(v => v.TradeClass).IsModified)) && !Allowed(e.Entity.TradeClass))
                .ToList();
            if (entries.Count == 0)
            {
                return;
            }
            if (entries.Any(e => e.State == EntityState.Added))
            {
                throw new TraceabilityViolationException("A new article gets a class from the list (Extra, I, II) or no class.");
            }
            var ids = entries.Select(e => e.Entity.Id).ToList();
            var stored = (await ToListAsync(db.ProductVariants.AsNoTracking().Where(v => ids.Contains(v.Id)).Select(v => new StoredTradeClass(v.Id, v.TradeClass)), async, cancellationToken))
                .ToDictionary(s => s.Id, s => s.TradeClass);
            foreach (var entry in entries)
            {
                if (!stored.TryGetValue(entry.Entity.Id, out var before) || !string.Equals(before, entry.Entity.TradeClass, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"The class of article {entry.Entity.Id} can only be changed to Extra, I or II.");
                }
            }
        }
    }
}
