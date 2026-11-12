using AgfAdmin.Models;
using AgfAdmin.Services.DocumentAi;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 28: AI-lezen (stap 7a) ----
        //
        // 1. een bijlage bij een factuur (het originele document, met hash) wordt nooit gewijzigd of verwijderd;
        // 2. een AI-lezing wordt nooit verwijderd (het verbruik per maand en wat de AI zei blijven te zien);
        // 3. een goedgekeurde lezing verandert niet meer: vergeleken met de opgeslagen status in de database, niet met OriginalValues.
        private static async Task CheckAiReadingAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<DocumentAttachment>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("An attachment of an invoice is never changed or removed.");
            }
            var readings = db.ChangeTracker.Entries<AiExtraction>().Where(e => e.State is EntityState.Modified or EntityState.Deleted).ToList();
            if (readings.Count == 0) return;
            if (readings.Any(e => e.State == EntityState.Deleted))
            {
                throw new TraceabilityViolationException("An AI reading is never removed.");
            }
            var ids = readings.Select(e => e.Entity.Id).ToList();
            if (await AnyAsync(db.AiExtractions.AsNoTracking().Where(e => ids.Contains(e.Id) && e.Status == AiReadingRules.StatusApproved), async, cancellationToken))
            {
                throw new TraceabilityViolationException("An approved AI reading does not change.");
            }
        }
    }
}
