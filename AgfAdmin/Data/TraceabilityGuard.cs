using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    public sealed class TraceabilityViolationException : InvalidOperationException
    {
        public TraceabilityViolationException(string message) : base(message) {}
    }

    // Draait vóór elke SaveChanges/SaveChangesAsync, zodat geen enkele writer de regels kan vergeten:
    // 1. een StockMovement/StockReservation met PalletContentLineId heeft dezelfde SupplyCommitmentLineId als die regel;
    // 2. de bronregel van een bestaande PalletContentLine ligt vast;
    // 3. PalletAuditEvents zijn append-only.
    // Er wordt nooit automatisch omgekoppeld. Een niet-bestaande inhoudsregel laten we over aan de FK in de database.
    // Let op: ExecuteUpdate/ExecuteDelete en ruwe SQL gaan buiten de change tracker om en dus ook buiten deze guard.
    internal static class TraceabilityGuard
    {
        public static void Validate(AppDbContext db)
        {
            var plan = Collect(db);
            if (plan.StoredIdsToLoad.Count == 0)
            {
                Check(plan, new Dictionary<int, int>());
                return;
            }

            var stored = db.PalletContentLines
                .AsNoTracking()
                .Where(c => plan.StoredIdsToLoad.Contains(c.Id))
                .Select(c => new { c.Id, c.SupplyCommitmentLineId })
                .ToDictionary(c => c.Id, c => c.SupplyCommitmentLineId);
            Check(plan, stored);
        }

        public static async Task ValidateAsync(AppDbContext db, CancellationToken cancellationToken)
        {
            var plan = Collect(db);
            if (plan.StoredIdsToLoad.Count == 0)
            {
                Check(plan, new Dictionary<int, int>());
                return;
            }

            var stored = await db.PalletContentLines
                .AsNoTracking()
                .Where(c => plan.StoredIdsToLoad.Contains(c.Id))
                .Select(c => new { c.Id, c.SupplyCommitmentLineId })
                .ToDictionaryAsync(c => c.Id, c => c.SupplyCommitmentLineId, cancellationToken);
            Check(plan, stored);
        }

        private sealed record SourceLink(string Entity, int ContentLineId, int SupplyCommitmentLineId);

        private sealed class Plan
        {
            public List<SourceLink> Links { get; } = new();
            public Dictionary<int, int> TrackedContentSources { get; } = new();
            public Dictionary<int, int> ModifiedContentSources { get; } = new();
            public List<int> StoredIdsToLoad { get; } = new();
        }

        private static Plan Collect(AppDbContext db)
        {
            if (db.ChangeTracker.AutoDetectChangesEnabled)
            {
                db.ChangeTracker.DetectChanges();
            }

            // Append-only: een auditrecord mag nooit worden gewijzigd of verwijderd.
            if (db.ChangeTracker.Entries<PalletAuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Pallet audit events are append-only and cannot be modified or deleted.");
            }

            var plan = new Plan();

            // Waarden via de entry, zodat ook tijdelijke sleutels van nieuwe entiteiten in dezelfde save meetellen.
            foreach (var entry in db.ChangeTracker.Entries<PalletContentLine>())
            {
                if (entry.State == EntityState.Deleted || entry.State == EntityState.Detached)
                {
                    continue;
                }

                var id = entry.Property(c => c.Id).CurrentValue;
                var sourceLineId = entry.Property(c => c.SupplyCommitmentLineId).CurrentValue;
                plan.TrackedContentSources[id] = sourceLineId;

                if (entry.State == EntityState.Modified && entry.Property(c => c.SupplyCommitmentLineId).IsModified)
                {
                    plan.ModifiedContentSources[id] = sourceLineId;
                }
            }

            foreach (var entry in db.ChangeTracker.Entries<StockMovement>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                var contentLineId = entry.Property(m => m.PalletContentLineId).CurrentValue;
                if (contentLineId != null)
                {
                    plan.Links.Add(new SourceLink(nameof(StockMovement), contentLineId.Value, entry.Property(m => m.SupplyCommitmentLineId).CurrentValue));
                }
            }

            foreach (var entry in db.ChangeTracker.Entries<StockReservation>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                var contentLineId = entry.Property(r => r.PalletContentLineId).CurrentValue;
                if (contentLineId != null)
                {
                    plan.Links.Add(new SourceLink(nameof(StockReservation), contentLineId.Value, entry.Property(r => r.SupplyCommitmentLineId).CurrentValue));
                }
            }

            plan.StoredIdsToLoad.AddRange(plan.ModifiedContentSources.Keys);
            plan.StoredIdsToLoad.AddRange(plan.Links
                .Select(l => l.ContentLineId)
                .Where(id => !plan.TrackedContentSources.ContainsKey(id)));

            return plan;
        }

        private static void Check(Plan plan, IReadOnlyDictionary<int, int> stored)
        {
            foreach (var (contentLineId, newSourceLineId) in plan.ModifiedContentSources)
            {
                if (stored.TryGetValue(contentLineId, out var storedSourceLineId) && storedSourceLineId != newSourceLineId)
                {
                    throw new TraceabilityViolationException(
                        $"The source supply commitment line of pallet content line {contentLineId} cannot be changed.");
                }
            }

            foreach (var link in plan.Links)
            {
                var known = plan.TrackedContentSources.TryGetValue(link.ContentLineId, out var contentSourceLineId)
                    || stored.TryGetValue(link.ContentLineId, out contentSourceLineId);
                if (!known)
                {
                    continue; // bestaat niet: de FK-constraint weigert dit bij het opslaan
                }

                if (contentSourceLineId != link.SupplyCommitmentLineId)
                {
                    throw new TraceabilityViolationException(
                        $"{link.Entity} references pallet content line {link.ContentLineId}, which belongs to supply commitment line " +
                        $"{contentSourceLineId}, not {link.SupplyCommitmentLineId}.");
                }
            }
        }
    }
}
