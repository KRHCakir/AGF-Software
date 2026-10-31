using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 20: verwachte kenmerken vooraf getoond (zie PalletService, blinde invoer) ----
        //
        // Of de gebruiker de verwachting van de bestelling vóór het invullen zag, met tijdstip en medewerker, wordt bij het aanmaken van
        // een inhoudsregel vastgelegd en verandert daarna nooit meer (vergeleken met de opgeslagen waarden, ook bij een losgekoppelde Update).
        private static void CheckExpectedShown(AppDbContext db)
        {
            foreach (var entry in db.ChangeTracker.Entries<PalletContentLine>().Where(e => e.State == EntityState.Modified))
            {
                if (entry.Property(c => c.ExpectedShownBeforeEntry).IsModified && entry.Property(c => c.ExpectedShownBeforeEntry).OriginalValue != entry.Entity.ExpectedShownBeforeEntry
                    || entry.Property(c => c.ExpectedShownAt).IsModified && entry.Property(c => c.ExpectedShownAt).OriginalValue != entry.Entity.ExpectedShownAt
                    || entry.Property(c => c.ExpectedShownBy).IsModified && entry.Property(c => c.ExpectedShownBy).OriginalValue != entry.Entity.ExpectedShownBy)
                {
                    throw new TraceabilityViolationException($"Whether the expected attributes were shown before entry is recorded once for content line {entry.Entity.Id} and cannot change.");
                }
            }
        }
    }
}
