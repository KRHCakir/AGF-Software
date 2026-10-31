using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private sealed record StoredExpectedShown(int Id, bool Shown, DateTime? At, string By);

        // ---- Regel 20: verwachte kenmerken vooraf getoond (zie PalletService, blinde invoer) ----
        //
        // Of de gebruiker de verwachting van de bestelling vóór het invullen zag, met tijdstip en medewerker, wordt bij het aanmaken van
        // een inhoudsregel vastgelegd en verandert daarna nooit meer. Vergeleken met de opgeslagen waarden in de database, niet met de
        // OriginalValues: bij een losgekoppelde Update zijn die gelijk aan de nieuwe waarden en zou een wijziging onzichtbaar blijven.
        private static async Task CheckExpectedShownAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var changed = db.ChangeTracker.Entries<PalletContentLine>()
                .Where(e => e.State == EntityState.Modified && (e.Property(c => c.ExpectedShownBeforeEntry).IsModified
                    || e.Property(c => c.ExpectedShownAt).IsModified || e.Property(c => c.ExpectedShownBy).IsModified))
                .Select(e => e.Entity).ToList();
            if (changed.Count == 0)
            {
                return;
            }
            var ids = changed.Select(c => c.Id).ToList();
            var stored = (await ToListAsync(db.PalletContentLines.AsNoTracking().Where(c => ids.Contains(c.Id))
                    .Select(c => new StoredExpectedShown(c.Id, c.ExpectedShownBeforeEntry, c.ExpectedShownAt, c.ExpectedShownBy)), async, cancellationToken))
                .ToDictionary(s => s.Id);
            foreach (var line in changed)
            {
                if (!stored.TryGetValue(line.Id, out var before))
                {
                    continue; // een niet-bestaande regel laten we over aan de database
                }
                if (before.Shown != line.ExpectedShownBeforeEntry || before.At != line.ExpectedShownAt || !string.Equals(before.By, line.ExpectedShownBy, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"Whether the expected attributes were shown before entry is recorded once for content line {line.Id} and cannot change.");
                }
            }
        }
    }
}
