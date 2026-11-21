using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 34: het GGN van het product op een inkoopregel (certificaten deel 2, zie ProductGgnRules) ----
        //
        // Het GGN van een opgeslagen inkoopregel verandert alleen samen met een auditregel van die order in dezelfde save: bij een Open order
        // een Edit, bij een definitieve order een Correction (met reden, regel 29). Zo is elke wijziging van het GGN na het aanmaken terug te
        // vinden, met oud en nieuw. Vergeleken met de opgeslagen waarde in de database, niet met OriginalValues.
        private static async Task CheckProductGgnAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var lines = db.ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State == EntityState.Modified).ToList();
            if (lines.Count == 0) return;
            var ids = lines.Select(e => e.Entity.Id).ToList();
            var stored = (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => ids.Contains(l.Id))
                    .Select(l => new { l.Id, l.Ggn, l.SupplyCommitmentId, l.SupplyCommitment.DocumentStatus }), async, cancellationToken))
                .ToDictionary(l => l.Id);
            var changes = db.ChangeTracker.Entries<PurchaseOrderChange>().Where(e => e.State == EntityState.Added)
                .GroupBy(e => e.Entity.SupplyCommitmentId).ToDictionary(g => g.Key, g => g.First().Entity.Kind);
            foreach (var entry in lines)
            {
                if (!stored.TryGetValue(entry.Entity.Id, out var before) || string.Equals(before.Ggn, entry.Entity.Ggn, StringComparison.Ordinal)) continue;
                var needed = before.DocumentStatus == PurchaseOrderDocumentRules.StatusDefinitive ? PurchaseOrderDocumentRules.KindCorrection : PurchaseOrderDocumentRules.KindEdit;
                if (!changes.TryGetValue(before.SupplyCommitmentId, out var kind) || kind != needed)
                {
                    throw new TraceabilityViolationException(
                        $"The GGN of purchase order line {before.Id} only changes with an {(needed == PurchaseOrderDocumentRules.KindEdit ? "edit" : "correction with a reason")} of the order.");
                }
            }
        }
    }
}
