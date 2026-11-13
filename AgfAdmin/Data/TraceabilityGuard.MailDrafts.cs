using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 32: mailconcepten (documenten deel 3, zie MailDraftService) ----
        //
        // Een mailconcept verdwijnt nooit (Verwijderen zet het op Discarded). Een verstuurd concept is het bewijs van wat naar wie ging:
        // daarna verandert er niets meer aan. Bij welk document en welke order het hoort ligt vanaf het begin vast. Vergeleken met de
        // opgeslagen waarden in de database.
        private static async Task CheckMailDraftsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<MailDraft>().Any(e => e.State == EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Mail drafts are never deleted; removing one marks it discarded.");
            }
            var modified = db.ChangeTracker.Entries<MailDraft>().Where(e => e.State == EntityState.Modified).ToList();
            if (modified.Count == 0) return;
            var ids = modified.Select(e => e.Entity.Id).ToList();
            var stored = (await ToListAsync(db.MailDrafts.AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.Status, d.SalesOrderId, d.DocumentId, d.DocumentKind }), async, cancellationToken))
                .ToDictionary(d => d.Id);
            foreach (var entry in modified)
            {
                if (!stored.TryGetValue(entry.Entity.Id, out var before)) continue;
                if (before.Status == DocumentMailRules.StatusSent)
                {
                    throw new TraceabilityViolationException($"Mail {before.Id} was sent: what went to whom stays as it was.");
                }
                if (before.SalesOrderId != entry.Entity.SalesOrderId || before.DocumentId != entry.Entity.DocumentId || before.DocumentKind != entry.Entity.DocumentKind)
                {
                    throw new TraceabilityViolationException($"Mail {before.Id} belongs to one document version; that never changes.");
                }
            }
        }
    }
}
