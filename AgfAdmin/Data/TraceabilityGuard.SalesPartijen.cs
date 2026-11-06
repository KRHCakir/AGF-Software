using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private sealed record StoredPartijKind(int Id, string Kind, int? SalesOrderId);

        // ---- Regel 24: verkoop-partijen (zie PartijRules.KindSales en AppDbContext.EnsureSalesPartijenAsync) ----
        //
        // Een verkoop-partij is één verkoop: precies één verkooporder, en elke verkooporder heeft er precies één. Ze ontstaat samen met de
        // order, verandert nooit van soort of order en verdwijnt nooit. Inkooporders, inkoopregels en palletregels horen alleen bij een
        // inkoop-partij (de verkoop volgt die via de reserveringen). Het nummer mag zonder reden worden aangepast (NumberChanged) zolang er
        // op de order niets gereserveerd of geleverd is en ze niet geannuleerd is; daarna alleen met reden (regel 23). Regel 23 blijft
        // voor inkoop-partijen ongewijzigd. Vergeleken met de opgeslagen waarden in de database, niet met OriginalValues.
        private static async Task CheckSalesPartijenAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var partijEntries = db.ChangeTracker.Entries<Partij>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            var addedOrders = db.ChangeTracker.Entries<SalesOrder>().Where(e => e.State == EntityState.Added).ToList();
            var numberChanges = db.ChangeTracker.Entries<PartijAuditEvent>()
                .Where(e => e.State == EntityState.Added && e.Entity.EventType == PartijRules.EventNumberChanged).Select(e => e.Entity).ToList();

            // Nieuwe partijen: soort uit de lijst; een verkoop-partij hoort bij een order die in dezelfde save ontstaat, een inkoop-partij bij geen.
            foreach (var entry in partijEntries.Where(e => e.State == EntityState.Added))
            {
                var partij = entry.Entity;
                var orderEntry = partij.SalesOrder != null ? db.Entry(partij.SalesOrder) : null;
                var hasOrder = partij.SalesOrder != null || entry.Property(p => p.SalesOrderId).CurrentValue != null;
                if (!PartijRules.IsKind(partij.Kind) || (partij.Kind == PartijRules.KindPurchase && hasOrder))
                {
                    throw new TraceabilityViolationException($"Lot '{partij.Number}' is a purchase lot or a sales lot; only a sales lot belongs to a sales order.");
                }
                if (partij.Kind == PartijRules.KindSales && (orderEntry == null || orderEntry.State != EntityState.Added))
                {
                    throw new TraceabilityViolationException($"Sales lot '{partij.Number}' is created together with its sales order, never added to an existing order.");
                }
            }

            // Elke nieuwe verkooporder heeft precies één verkoop-partij.
            foreach (var order in addedOrders)
            {
                var count = db.ChangeTracker.Entries<Partij>().Count(e => e.State == EntityState.Added && e.Entity.Kind == PartijRules.KindSales && e.Entity.SalesOrder == order.Entity);
                if (count != 1)
                {
                    throw new TraceabilityViolationException("A new sales order gets exactly one sales lot.");
                }
            }

            // Bestaande partijen: soort en order veranderen nooit, een verkoop-partij wordt nooit verwijderd.
            var existingIds = partijEntries.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id)
                .Concat(numberChanges.Select(e => e.PartijId)).Distinct().ToList();
            var stored = existingIds.Count == 0 ? new Dictionary<int, StoredPartijKind>()
                : (await ToListAsync(db.Partijen.AsNoTracking().Where(p => existingIds.Contains(p.Id)).Select(p => new StoredPartijKind(p.Id, p.Kind, p.SalesOrderId)), async, cancellationToken))
                    .ToDictionary(p => p.Id);
            foreach (var entry in partijEntries.Where(e => e.State != EntityState.Added))
            {
                if (!stored.TryGetValue(entry.Entity.Id, out var before))
                {
                    continue; // een niet-bestaande partij laten we over aan de database
                }
                if (entry.State == EntityState.Deleted && before.Kind == PartijRules.KindSales)
                {
                    throw new TraceabilityViolationException("A sales lot belongs to its sales order and is never deleted.");
                }
                if (entry.State == EntityState.Modified
                    && (!string.Equals(before.Kind, entry.Entity.Kind, StringComparison.Ordinal) || before.SalesOrderId != entry.Property(p => p.SalesOrderId).CurrentValue))
                {
                    throw new TraceabilityViolationException("The kind of a lot and the sales order of a sales lot never change.");
                }
            }

            // Nummer aangepast zonder reden: alleen een verkoop-partij, alleen zolang er op haar order niets gereserveerd of geleverd is.
            foreach (var change in numberChanges)
            {
                if (!stored.TryGetValue(change.PartijId, out var partij) || partij.Kind != PartijRules.KindSales || partij.SalesOrderId == null)
                {
                    throw new TraceabilityViolationException("Only a sales lot changes its number without a reason; a purchase lot needs a correction with a reason.");
                }
                if (await PartijService.SalesNumberFixedAsync(db, partij.SalesOrderId.Value, async, cancellationToken))
                {
                    throw new TraceabilityViolationException("Something has been reserved or delivered on this sales order: its lot number only changes through a correction with a reason.");
                }
            }

            await CheckPurchaseOnlyLinksAsync(db, async, cancellationToken);
        }

        // Inkooporders, inkoopregels en palletregels krijgen alleen een inkoop-partij.
        private static async Task CheckPurchaseOnlyLinksAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var linked = db.ChangeTracker.Entries<SupplyCommitment>()
                    .Where(e => e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(c => c.PartijId).IsModified))
                    .Select(e => (Partij: e.Entity.Partij, Id: e.Property(c => c.PartijId).CurrentValue))
                .Concat(db.ChangeTracker.Entries<SupplyCommitmentLine>()
                    .Where(e => e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(l => l.PartijId).IsModified))
                    .Select(e => (Partij: e.Entity.Partij, Id: e.Property(l => l.PartijId).CurrentValue)))
                .Concat(db.ChangeTracker.Entries<PalletContentLine>()
                    .Where(e => e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(c => c.PartijId).IsModified))
                    .Select(e => (Partij: e.Entity.Partij, Id: e.Property(c => c.PartijId).CurrentValue)))
                .Where(l => l.Partij != null || l.Id != null)
                .ToList();
            if (linked.Count == 0)
            {
                return;
            }
            if (linked.Any(l => l.Partij != null && l.Partij.Kind != PartijRules.KindPurchase))
            {
                throw new TraceabilityViolationException("A purchase order, order line or pallet line only belongs to a purchase lot.");
            }
            var trackedKinds = db.ChangeTracker.Entries<Partij>().Where(e => e.State != EntityState.Detached)
                .ToDictionary(e => e.Property(p => p.Id).CurrentValue, e => e.Entity.Kind);
            var ids = linked.Where(l => l.Partij == null).Select(l => l.Id.Value).Distinct().Where(id => !trackedKinds.ContainsKey(id)).ToList();
            var storedKinds = ids.Count == 0 ? new Dictionary<int, string>()
                : (await ToListAsync(db.Partijen.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Kind }), async, cancellationToken)).ToDictionary(p => p.Id, p => p.Kind);
            foreach (var link in linked.Where(l => l.Partij == null))
            {
                var kind = trackedKinds.TryGetValue(link.Id.Value, out var tracked) ? tracked : storedKinds.GetValueOrDefault(link.Id.Value);
                if (kind != null && kind != PartijRules.KindPurchase)
                {
                    throw new TraceabilityViolationException("A purchase order, order line or pallet line only belongs to a purchase lot.");
                }
            }
        }
    }
}
