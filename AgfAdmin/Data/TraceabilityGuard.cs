using AgfAdmin.Models;
using AgfAdmin.Services;
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
    // 3. PalletAuditEvents zijn append-only;
    // 4. inhoud met een DirectTrade-bron staat nooit op een pallet met LocationContext Warehouse.
    // Er wordt nooit automatisch omgekoppeld. Een niet-bestaande inhoudsregel laten we over aan de FK in de database.
    // Let op: ExecuteUpdate/ExecuteDelete en ruwe SQL gaan buiten de change tracker om en dus ook buiten deze guard.
    internal static class TraceabilityGuard
    {
        public static void Validate(AppDbContext db) =>
            // Met async = false voert de kern alleen synchrone EF-aanroepen uit en is de taak al voltooid.
            ValidateCoreAsync(db, async: false, CancellationToken.None).GetAwaiter().GetResult();

        public static Task ValidateAsync(AppDbContext db, CancellationToken cancellationToken) =>
            ValidateCoreAsync(db, async: true, cancellationToken);

        private sealed record SourceLink(string Entity, int ContentLineId, int SupplyCommitmentLineId);

        private static async Task ValidateCoreAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.AutoDetectChangesEnabled)
            {
                db.ChangeTracker.DetectChanges();
            }

            // Regel 3: een auditrecord mag nooit worden gewijzigd of verwijderd.
            if (db.ChangeTracker.Entries<PalletAuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Pallet audit events are append-only and cannot be modified or deleted.");
            }

            await CheckSourceLinksAsync(db, async, cancellationToken);
            await CheckNoDirectTradeInWarehouseAsync(db, async, cancellationToken);
        }

        // ---- Regels 1 en 2 ----

        private static async Task CheckSourceLinksAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var trackedContentSources = new Dictionary<int, int>();
            var modifiedContentSources = new Dictionary<int, int>();

            // Waarden via de entry, zodat ook tijdelijke sleutels van nieuwe entiteiten in dezelfde save meetellen.
            foreach (var entry in db.ChangeTracker.Entries<PalletContentLine>())
            {
                if (entry.State is EntityState.Deleted or EntityState.Detached)
                {
                    continue;
                }

                var id = entry.Property(c => c.Id).CurrentValue;
                var sourceLineId = entry.Property(c => c.SupplyCommitmentLineId).CurrentValue;
                trackedContentSources[id] = sourceLineId;

                if (entry.State == EntityState.Modified && entry.Property(c => c.SupplyCommitmentLineId).IsModified)
                {
                    modifiedContentSources[id] = sourceLineId;
                }
            }

            var links = new List<SourceLink>();
            foreach (var entry in db.ChangeTracker.Entries<StockMovement>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                var contentLineId = entry.Property(m => m.PalletContentLineId).CurrentValue;
                if (contentLineId != null)
                {
                    links.Add(new SourceLink(nameof(StockMovement), contentLineId.Value, entry.Property(m => m.SupplyCommitmentLineId).CurrentValue));
                }
            }
            foreach (var entry in db.ChangeTracker.Entries<StockReservation>().Where(e => e.State is EntityState.Added or EntityState.Modified))
            {
                var contentLineId = entry.Property(r => r.PalletContentLineId).CurrentValue;
                if (contentLineId != null)
                {
                    links.Add(new SourceLink(nameof(StockReservation), contentLineId.Value, entry.Property(r => r.SupplyCommitmentLineId).CurrentValue));
                }
            }

            var idsToLoad = modifiedContentSources.Keys
                .Concat(links.Select(l => l.ContentLineId).Where(id => !trackedContentSources.ContainsKey(id)))
                .Distinct()
                .ToList();
            var stored = idsToLoad.Count == 0
                ? new Dictionary<int, int>()
                : (await ToListAsync(
                        db.PalletContentLines.AsNoTracking()
                            .Where(c => idsToLoad.Contains(c.Id))
                            .Select(c => new { c.Id, c.SupplyCommitmentLineId }),
                        async, cancellationToken))
                    .ToDictionary(c => c.Id, c => c.SupplyCommitmentLineId);

            foreach (var (contentLineId, newSourceLineId) in modifiedContentSources)
            {
                if (stored.TryGetValue(contentLineId, out var storedSourceLineId) && storedSourceLineId != newSourceLineId)
                {
                    throw new TraceabilityViolationException(
                        $"The source supply commitment line of pallet content line {contentLineId} cannot be changed.");
                }
            }

            foreach (var link in links)
            {
                var known = trackedContentSources.TryGetValue(link.ContentLineId, out var contentSourceLineId)
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

        // ---- Regel 4 ----

        private static async Task CheckNoDirectTradeInWarehouseAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var trackedPalletLocations = db.ChangeTracker.Entries<Pallet>()
                .Where(e => e.State is not (EntityState.Deleted or EntityState.Detached))
                .ToDictionary(e => e.Property(p => p.Id).CurrentValue, e => e.Property(p => p.LocationContext).CurrentValue);

            var warehousePalletIds = db.ChangeTracker.Entries<Pallet>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified
                    && e.Property(p => p.LocationContext).CurrentValue == PalletRules.LocationWarehouse)
                .Select(e => e.Property(p => p.Id).CurrentValue)
                .ToHashSet();

            // Te controleren paren (pallet, bronregel): nieuwe inhoud op elke pallet, en gevolgde inhoud op een pallet die
            // (opnieuw) in het magazijn staat.
            var pairs = db.ChangeTracker.Entries<PalletContentLine>()
                .Where(e => e.State is not (EntityState.Deleted or EntityState.Detached))
                .Select(e => (
                    State: e.State,
                    PalletId: e.Property(c => c.PalletId).CurrentValue,
                    SourceLineId: e.Property(c => c.SupplyCommitmentLineId).CurrentValue))
                .Where(c => c.State == EntityState.Added || warehousePalletIds.Contains(c.PalletId))
                .Select(c => (c.PalletId, c.SourceLineId))
                .ToList();

            if (warehousePalletIds.Count == 0 && pairs.Count == 0)
            {
                return;
            }

            // Opgeslagen inhoud van pallets die naar het magazijn gaan.
            if (warehousePalletIds.Count > 0)
            {
                var ids = warehousePalletIds.ToList();
                var storedDirectTrade = await ToListAsync(
                    db.PalletContentLines.AsNoTracking()
                        .Where(c => ids.Contains(c.PalletId) && c.SupplyCommitmentLine.SupplyCommitment.Route == SupplyRules.RouteDirectTrade)
                        .Select(c => c.PalletId),
                    async, cancellationToken);
                if (storedDirectTrade.Count > 0)
                {
                    throw DirectTradeInWarehouse(storedDirectTrade[0]);
                }
            }

            if (pairs.Count == 0)
            {
                return;
            }

            var missingPalletIds = pairs.Select(p => p.PalletId).Where(id => !trackedPalletLocations.ContainsKey(id)).Distinct().ToList();
            var palletLocations = new Dictionary<int, string>(trackedPalletLocations);
            if (missingPalletIds.Count > 0)
            {
                foreach (var pallet in await ToListAsync(
                    db.Pallets.AsNoTracking().Where(p => missingPalletIds.Contains(p.Id)).Select(p => new { p.Id, p.LocationContext }),
                    async, cancellationToken))
                {
                    palletLocations[pallet.Id] = pallet.LocationContext;
                }
            }

            var warehousePairs = pairs.Where(p => palletLocations.TryGetValue(p.PalletId, out var location) && location == PalletRules.LocationWarehouse).ToList();
            if (warehousePairs.Count == 0)
            {
                return;
            }

            var routes = TrackedSourceRoutes(db);
            var missingSourceIds = warehousePairs.Select(p => p.SourceLineId).Where(id => !routes.ContainsKey(id)).Distinct().ToList();
            if (missingSourceIds.Count > 0)
            {
                foreach (var source in await ToListAsync(
                    db.SupplyCommitmentLines.AsNoTracking()
                        .Where(l => missingSourceIds.Contains(l.Id))
                        .Select(l => new { l.Id, l.SupplyCommitment.Route }),
                    async, cancellationToken))
                {
                    routes[source.Id] = source.Route;
                }
            }

            foreach (var (palletId, sourceLineId) in warehousePairs)
            {
                if (routes.TryGetValue(sourceLineId, out var route) && route == SupplyRules.RouteDirectTrade)
                {
                    throw DirectTradeInWarehouse(palletId);
                }
            }
        }

        // Route per gevolgde bronregel, via de gevolgde toezegging (ook met tijdelijke sleutels in dezelfde save).
        private static Dictionary<int, string> TrackedSourceRoutes(AppDbContext db)
        {
            var commitmentRoutes = db.ChangeTracker.Entries<SupplyCommitment>()
                .Where(e => e.State is not (EntityState.Deleted or EntityState.Detached))
                .ToDictionary(e => e.Property(c => c.Id).CurrentValue, e => e.Property(c => c.Route).CurrentValue);

            var routes = new Dictionary<int, string>();
            foreach (var entry in db.ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State is not (EntityState.Deleted or EntityState.Detached)))
            {
                if (commitmentRoutes.TryGetValue(entry.Property(l => l.SupplyCommitmentId).CurrentValue, out var route))
                {
                    routes[entry.Property(l => l.Id).CurrentValue] = route;
                }
            }
            return routes;
        }

        private static TraceabilityViolationException DirectTradeInWarehouse(int palletId) =>
            new($"Pallet {palletId} cannot hold DirectTrade content while its location context is {PalletRules.LocationWarehouse}.");

        private static async Task<List<T>> ToListAsync<T>(IQueryable<T> query, bool async, CancellationToken cancellationToken) =>
            async ? await query.ToListAsync(cancellationToken) : query.ToList();
    }
}
