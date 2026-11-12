using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 31: bevestigen en de laadbon (documenten deel 1, zie LoadingSlipService) ----
        //
        // Wie en wanneer een verkooporder bevestigde ligt vast zodra het gezet is. Een document bij een order is een versie die nooit
        // verandert of verdwijnt: alleen "verstuurd/doorgegeven" (SentAt, SentBy) wordt één keer gezet. Een nieuwe versie hoort bij een
        // bevestigde order en volgt de vorige op (1, 2, 3 per order en soort). Vergeleken met de opgeslagen waarden in de database.
        private static async Task CheckSalesOrderDocumentsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<SalesOrderDocument>().Any(e => e.State == EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Documents of a sales order are never deleted; a change gives a new version.");
            }
            var modified = db.ChangeTracker.Entries<SalesOrderDocument>().Where(e => e.State == EntityState.Modified).ToList();
            if (modified.Count > 0)
            {
                var ids = modified.Select(e => e.Entity.Id).ToList();
                var stored = (await ToListAsync(db.SalesOrderDocuments.AsNoTracking().Where(d => ids.Contains(d.Id)), async, cancellationToken)).ToDictionary(d => d.Id);
                foreach (var entry in modified)
                {
                    var now = entry.Entity;
                    if (!stored.TryGetValue(now.Id, out var before)) continue;
                    var same = before.SalesOrderId == now.SalesOrderId && before.Kind == now.Kind && before.Number == now.Number && before.Version == now.Version
                        && before.Reason == now.Reason && before.SnapshotJson == now.SnapshotJson && before.ContentHash == now.ContentHash
                        && before.CreatedAt == now.CreatedAt && before.CreatedBy == now.CreatedBy;
                    var sentOnce = before.SentAt == null || (before.SentAt == now.SentAt && before.SentBy == now.SentBy);
                    if (!same || !sentOnce)
                    {
                        throw new TraceabilityViolationException($"Version {before.Version} of document {before.Number} never changes; only passing it on is recorded, once.");
                    }
                }
            }

            var orders = db.ChangeTracker.Entries<SalesOrder>().Where(e => e.State == EntityState.Modified).ToList();
            if (orders.Count > 0)
            {
                var ids = orders.Select(e => e.Entity.Id).ToList();
                var stored = (await ToListAsync(db.SalesOrders.AsNoTracking().Where(o => ids.Contains(o.Id)).Select(o => new { o.Id, o.ConfirmedAt, o.ConfirmedBy }), async, cancellationToken))
                    .ToDictionary(o => o.Id);
                foreach (var entry in orders)
                {
                    if (stored.TryGetValue(entry.Entity.Id, out var before) && before.ConfirmedAt != null
                        && (before.ConfirmedAt != entry.Entity.ConfirmedAt || before.ConfirmedBy != entry.Entity.ConfirmedBy))
                    {
                        throw new TraceabilityViolationException($"Sales order {entry.Entity.OrderNumber} is confirmed: who and when stay fixed.");
                    }
                }
            }

            var added = db.ChangeTracker.Entries<SalesOrderDocument>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            if (added.Count == 0) return;
            var orderIds = added.Select(d => d.SalesOrderId).Distinct().ToList();
            var confirmed = (await ToListAsync(db.SalesOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.ConfirmedAt }), async, cancellationToken))
                .ToDictionary(o => o.Id, o => o.ConfirmedAt);
            var versions = await ToListAsync(db.SalesOrderDocuments.AsNoTracking().Where(d => orderIds.Contains(d.SalesOrderId)).Select(d => new { d.SalesOrderId, d.Kind, d.Version }), async, cancellationToken);
            foreach (var group in added.GroupBy(d => (d.SalesOrderId, d.Kind)))
            {
                var tracked = db.ChangeTracker.Entries<SalesOrder>().Select(e => e.Entity).FirstOrDefault(o => o.Id == group.Key.SalesOrderId);
                if ((tracked?.ConfirmedAt ?? confirmed.GetValueOrDefault(group.Key.SalesOrderId)) == null)
                {
                    throw new TraceabilityViolationException($"A document of sales order {group.Key.SalesOrderId} belongs to a confirmed order.");
                }
                var next = versions.Where(v => v.SalesOrderId == group.Key.SalesOrderId && v.Kind == group.Key.Kind).Select(v => v.Version).DefaultIfEmpty(0).Max() + 1;
                foreach (var document in group.OrderBy(d => d.Version))
                {
                    if (document.Version != next++)
                    {
                        throw new TraceabilityViolationException($"Document {document.Number} gets its versions in order: {next - 1} comes next, not {document.Version}.");
                    }
                    if (document.SentAt != null)
                    {
                        throw new TraceabilityViolationException($"A new version of document {document.Number} has not been passed on yet.");
                    }
                }
            }
        }
    }
}
