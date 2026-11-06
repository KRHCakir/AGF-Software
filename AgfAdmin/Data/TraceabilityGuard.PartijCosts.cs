using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private sealed record StoredPartijCost(int Id, int IncomingInvoiceLineId, int PartijId, decimal AmountExclVat, string Kind, string CreatedBy, DateTime CreatedAt);

        // ---- Regel 25: kosten op partijen (zie PartijCostService) ----
        //
        // De kosten per partij zijn de basis voor het resultaat (P3): een verdeling verandert nooit zonder historie en telt nooit op tot meer
        // dan het regelbedrag excl. btw. Elke nieuwe, gewijzigde of weggehaalde kostenregel heeft in dezelfde save een auditregel met het
        // oude en nieuwe bedrag; factuurregel, partij, soort en aanmaker veranderen nooit; de historie is append-only; een partij met kosten
        // verdwijnt nooit. Vergeleken met de opgeslagen waarden in de database, niet met OriginalValues.
        private static async Task CheckPartijCostsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<PartijCostAuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("The history of lot costs is append-only and cannot be modified or deleted.");
            }
            var entries = db.ChangeTracker.Entries<PartijCost>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            var deletedPartijen = db.ChangeTracker.Entries<Partij>().Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToList();
            if (deletedPartijen.Count > 0 && await AnyAsync(db.PartijCosts.AsNoTracking().Where(c => deletedPartijen.Contains(c.PartijId)), async, cancellationToken))
            {
                throw new TraceabilityViolationException("A lot with costs cannot be deleted.");
            }
            if (entries.Count == 0)
            {
                return;
            }

            var events = db.ChangeTracker.Entries<PartijCostAuditEvent>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            var ids = entries.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id).ToList();
            var stored = ids.Count == 0 ? new Dictionary<int, StoredPartijCost>()
                : (await ToListAsync(db.PartijCosts.AsNoTracking().Where(c => ids.Contains(c.Id))
                    .Select(c => new StoredPartijCost(c.Id, c.IncomingInvoiceLineId, c.PartijId, c.AmountExclVat, c.Kind, c.CreatedBy, c.CreatedAt)), async, cancellationToken))
                    .ToDictionary(c => c.Id);
            bool Audited(int lineId, int partijId, string type, decimal? oldAmount, decimal? newAmount) =>
                events.Any(e => e.IncomingInvoiceLineId == lineId && e.PartijId == partijId && e.EventType == type && e.OldAmount == oldAmount && e.NewAmount == newAmount);

            foreach (var entry in entries)
            {
                var cost = entry.Entity;
                var lineId = entry.Property(c => c.IncomingInvoiceLineId).CurrentValue;
                var partijId = entry.Property(c => c.PartijId).CurrentValue;
                if (entry.State != EntityState.Deleted && !PartijCostRules.IsAmount(cost.AmountExclVat))
                {
                    throw new TraceabilityViolationException("A cost on a lot is a positive amount excl. VAT with at most two decimals.");
                }
                if (entry.State == EntityState.Added)
                {
                    if (!Audited(lineId, partijId, PartijCostRules.EventAllocated, null, cost.AmountExclVat))
                    {
                        throw new TraceabilityViolationException("A new cost on a lot needs an audit event with its amount.");
                    }
                    continue;
                }
                if (!stored.TryGetValue(cost.Id, out var before))
                {
                    continue; // een niet-bestaande kostenregel laten we over aan de database
                }
                if (entry.State == EntityState.Deleted)
                {
                    if (!Audited(before.IncomingInvoiceLineId, before.PartijId, PartijCostRules.EventRemoved, before.AmountExclVat, null))
                    {
                        throw new TraceabilityViolationException("Removing a cost from a lot needs an audit event with the old amount.");
                    }
                    continue;
                }
                if (before.IncomingInvoiceLineId != lineId || before.PartijId != partijId || !string.Equals(before.Kind, cost.Kind, StringComparison.Ordinal)
                    || !string.Equals(before.CreatedBy, cost.CreatedBy, StringComparison.Ordinal) || before.CreatedAt != cost.CreatedAt)
                {
                    throw new TraceabilityViolationException("The invoice line, lot, kind and creator of a cost on a lot never change.");
                }
                if (before.AmountExclVat != cost.AmountExclVat && !Audited(lineId, partijId, PartijCostRules.EventChanged, before.AmountExclVat, cost.AmountExclVat))
                {
                    throw new TraceabilityViolationException("Changing a cost on a lot needs an audit event with the old and new amount.");
                }
            }

            // Per factuurregel: de verdeling (opgeslagen regels met de gevolgde wijzigingen erover) is nooit meer dan het regelbedrag.
            var lineIds = entries.Select(e => e.Property(c => c.IncomingInvoiceLineId).CurrentValue).Concat(stored.Values.Select(s => s.IncomingInvoiceLineId)).Distinct().ToList();
            var lineTotals = (await ToListAsync(db.IncomingInvoiceLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, l.LineTotal }), async, cancellationToken))
                .ToDictionary(l => l.Id, l => l.LineTotal);
            var storedCosts = await ToListAsync(db.PartijCosts.AsNoTracking().Where(c => lineIds.Contains(c.IncomingInvoiceLineId))
                .Select(c => new { c.Id, c.IncomingInvoiceLineId, c.AmountExclVat }), async, cancellationToken);
            var tracked = db.ChangeTracker.Entries<PartijCost>().Where(e => e.State != EntityState.Detached).ToList();
            var trackedIds = tracked.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id).ToHashSet();
            foreach (var lineId in lineIds)
            {
                var sum = storedCosts.Where(c => c.IncomingInvoiceLineId == lineId && !trackedIds.Contains(c.Id)).Sum(c => c.AmountExclVat)
                    + tracked.Where(e => e.State != EntityState.Deleted && e.Property(c => c.IncomingInvoiceLineId).CurrentValue == lineId).Sum(e => e.Entity.AmountExclVat);
                if (lineTotals.TryGetValue(lineId, out var lineTotal) && sum > lineTotal)
                {
                    throw new TraceabilityViolationException($"The costs on lots of invoice line {lineId} ({sum:0.00}) exceed the line amount excl. VAT ({lineTotal:0.00}).");
                }
            }
        }
    }
}
