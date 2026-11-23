using AgfAdmin.Models;
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
            var used = await AnyAsync(db.Pallets.AsNoTracking().Where(p => p.StockLocationId != null && changed.Contains(p.StockLocationId.Value)), async, cancellationToken)
                || await AnyAsync(db.SupplyCommitments.AsNoTracking().Where(c => c.ReceiveLocationId != null && changed.Contains(c.ReceiveLocationId.Value)), async, cancellationToken);
            if (used)
            {
                throw new TraceabilityViolationException("The kind of a stock location used by pallets or purchase orders never changes.");
            }
        }
    }
}
