using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 40: voorraadlocaties (feedback 22 november, zie StockLocationService en ReceiptService) ----
        //
        // 1. een voorraadlocatie wordt nooit verwijderd (inactief zetten);
        // 2. de locatie van een pallet wordt alleen bij aanmaken gezet en verandert daarna nooit (waar de voorraad ontvangen is, blijft
        //    herleidbaar; verplaatsen is een latere stap met een eigen auditregel);
        // 3. de soort van een locatie waar een pallet of inkooporder naar wijst, verandert nooit (Onderweg blijft Onderweg in de historie).
        private static async Task CheckStockLocationsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<StockLocation>().Any(e => e.State == EntityState.Deleted))
            {
                throw new TraceabilityViolationException("A stock location is never deleted; make it inactive instead.");
            }
            var pallets = db.ChangeTracker.Entries<Pallet>().Where(e => e.State == EntityState.Modified).Select(e => e.Entity).ToList();
            if (pallets.Count > 0)
            {
                var ids = pallets.Select(p => p.Id).ToList();
                var stored = (await ToListAsync(db.Pallets.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.StockLocationId }), async, cancellationToken))
                    .ToDictionary(p => p.Id, p => p.StockLocationId);
                foreach (var pallet in pallets)
                {
                    if (stored.TryGetValue(pallet.Id, out var before) && before != pallet.StockLocationId)
                    {
                        throw new TraceabilityViolationException($"Pallet {pallet.Reference} keeps the stock location it was received at.");
                    }
                }
            }
            var locations = db.ChangeTracker.Entries<StockLocation>().Where(e => e.State == EntityState.Modified).Select(e => e.Entity).ToList();
            if (locations.Count == 0) return;
            var locationIds = locations.Select(l => l.Id).ToList();
            var kinds = (await ToListAsync(db.StockLocations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).Select(l => new { l.Id, l.Kind }), async, cancellationToken))
                .ToDictionary(l => l.Id, l => l.Kind);
            var changed = locations.Where(l => kinds.TryGetValue(l.Id, out var kind) && !string.Equals(kind, l.Kind, StringComparison.Ordinal)).Select(l => l.Id).ToList();
            if (changed.Count == 0) return;
            // Ook verwijzingen die in dezelfde save nieuw of gewijzigd zijn (nog niet opgeslagen).
            var pending = db.ChangeTracker.Entries<Pallet>().Where(e => e.State is EntityState.Added or EntityState.Modified)
                    .Any(e => e.Entity.StockLocationId != null && changed.Contains(e.Entity.StockLocationId.Value))
                || db.ChangeTracker.Entries<SupplyCommitment>().Where(e => e.State is EntityState.Added or EntityState.Modified)
                    .Any(e => e.Entity.ReceiveLocationId != null && changed.Contains(e.Entity.ReceiveLocationId.Value));
            var used = pending || await AnyAsync(db.Pallets.AsNoTracking().Where(p => p.StockLocationId != null && changed.Contains(p.StockLocationId.Value)), async, cancellationToken)
                || await AnyAsync(db.SupplyCommitments.AsNoTracking().Where(c => c.ReceiveLocationId != null && changed.Contains(c.ReceiveLocationId.Value)), async, cancellationToken);
            if (used)
            {
                throw new TraceabilityViolationException("The kind of a stock location used by pallets or purchase orders never changes.");
            }
        }
    
        // ---- Regel 47: ontvangstlocatie (november 2026, zie StockLocationService) ----
        //
        // 1. Onderweg / direct naar klant is er altijd: de laatste actieve locatie van de soort InTransit wordt niet inactief en verandert
        //    niet van soort;
        // 2. de standaard ontvangstlocatie (CompanySettings.DefaultReceiveLocationId) is leeg (de ingebouwde Eigen loods) of een actieve
        //    locatie van de soort OwnWarehouse, en blijft dat zolang ze de standaard is.
        private static async Task CheckReceiveLocationsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var modified = db.ChangeTracker.Entries<StockLocation>().Where(e => e.State == EntityState.Modified).ToList();
            var settings = db.ChangeTracker.Entries<CompanySettings>().Where(e => e.State is EntityState.Added or EntityState.Modified).Select(e => e.Entity).ToList();
            if (modified.Count == 0 && settings.Count == 0) return;

            bool ActiveKind(StockLocation l, string kind) => l.IsActive && string.Equals(l.Kind, kind, StringComparison.Ordinal);
            var leaving = modified.Where(e => e.Property(l => l.IsActive).OriginalValue && string.Equals(e.Property(l => l.Kind).OriginalValue, StockLocationRules.KindInTransit, StringComparison.Ordinal)
                    && !ActiveKind(e.Entity, StockLocationRules.KindInTransit)).Select(e => e.Entity.Id).ToList();
            if (leaving.Count > 0)
            {
                var tracked = db.ChangeTracker.Entries<StockLocation>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList();
                var trackedIds = tracked.Where(e => e.Entity.Id > 0).Select(e => e.Entity.Id).ToList();
                var another = tracked.Any(e => ActiveKind(e.Entity, StockLocationRules.KindInTransit))
                    || await AnyAsync(db.StockLocations.AsNoTracking().Where(l => !trackedIds.Contains(l.Id) && l.IsActive && l.Kind == StockLocationRules.KindInTransit), async, cancellationToken);
                if (!another)
                {
                    throw new TraceabilityViolationException("Onderweg / direct naar klant is always available: the last active in-transit location stays active and in transit.");
                }
            }

            var defaults = settings.Select(c => c.DefaultReceiveLocationId).Where(id => id != null).Select(id => id.Value).ToList();
            var storedDefault = await FirstOrDefaultAsync(db.CompanySettings.AsNoTracking().OrderBy(c => c.Id).Select(c => c.DefaultReceiveLocationId), async, cancellationToken);
            var settingsChanged = settings.Count > 0;
            var check = settingsChanged ? defaults : storedDefault is { } d && modified.Any(e => e.Entity.Id == d) ? new List<int> { d } : new List<int>();
            foreach (var id in check.Distinct())
            {
                var entry = db.ChangeTracker.Entries<StockLocation>().FirstOrDefault(e => e.Entity.Id == id);
                var ok = entry != null ? ActiveKind(entry.Entity, StockLocationRules.KindOwnWarehouse)
                    : await AnyAsync(db.StockLocations.AsNoTracking().Where(l => l.Id == id && l.IsActive && l.Kind == StockLocationRules.KindOwnWarehouse), async, cancellationToken);
                if (!ok)
                {
                    throw new TraceabilityViolationException("The default receive location is your own warehouse or an active location of the kind own warehouse.");
                }
            }
        }
}
}
