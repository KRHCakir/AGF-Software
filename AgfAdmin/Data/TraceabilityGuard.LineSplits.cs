using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 39: een deels ontvangen inkooporderregel splitsen (feedback 22 november, zie PurchaseOrderService.Edit) ----
        //
        // 1. een regel met SplitFromLineId (het open deel) ontstaat alleen samen met een Edit-auditregel van haar order in dezelfde save, en
        //    wijst naar een opgeslagen regel van dezelfde order die deels ontvangen is (meer dan niets, minder dan verwacht vóór deze save);
        //    SplitFromLineId verandert daarna nooit;
        // 2. het vervallen restant (CancelledQuantity) staat nooit op een nieuwe regel, verandert alleen samen met een Edit-auditregel, daalt
        //    nooit en stijgt alleen met precies de verlaging van het verwachte aantal (besteld = verwacht + vervallen blijft gelijk), en het
        //    verwachte aantal komt daarbij nooit onder wat ontvangen is (dat bewaakt regel 29);
        // 3. ontvangen hoeveelheden veranderen nooit: aantal, eenheid, netto kg en gewichtsmethode van een opgeslagen palletregel liggen vast (een
        //    correctie, afsplitsing of afkeuring maakt altijd nieuwe regels). Verwijderen weigert de database al: elke palletregel heeft een
        //    auditregel (ContentLineAdded) met een restrictieve koppeling.
        private static async Task CheckLineSplitsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            await CheckReceivedQuantitiesAsync(db, async, cancellationToken);
            var entries = db.ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList();
            var added = entries.Where(e => e.State == EntityState.Added && (e.Entity.SplitFromLineId != null || e.Entity.SplitFromLine != null || e.Entity.CancelledQuantity != null)).ToList();
            var modified = entries.Where(e => e.State == EntityState.Modified).ToList();
            if (added.Count == 0 && modified.Count == 0) return;

            var edits = db.ChangeTracker.Entries<PurchaseOrderChange>().Where(e => e.State == EntityState.Added && e.Entity.Kind == PurchaseOrderDocumentRules.KindEdit)
                .Select(e => e.Entity.SupplyCommitmentId).ToHashSet();
            if (added.Any(e => e.Entity.CancelledQuantity != null))
            {
                throw new TraceabilityViolationException("A new purchase order line never starts with a cancelled remainder.");
            }

            var sourceIds = added.Select(e => e.Entity.SplitFromLine?.Id ?? e.Entity.SplitFromLineId!.Value).Distinct().ToList();
            // De bron zoals opgeslagen (vóór deze save): order, eenheid en verwacht aantal, en wat er geldig ontvangen is in de eenheid van de
            // regel (zoals de ontvangst telt: geen correctie- of afgesplitste inhoud, geen geannuleerde pallet). Deels ontvangen = 0 < ontvangen < verwacht.
            var sources = sourceIds.Count == 0 ? new Dictionary<int, (int OrderId, string Unit, decimal Expected)>()
                : (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => sourceIds.Contains(l.Id)).Select(l => new { l.Id, l.SupplyCommitmentId, l.ExpectedUnit, l.ExpectedQuantity }), async, cancellationToken))
                    .ToDictionary(l => l.Id, l => (OrderId: l.SupplyCommitmentId, Unit: l.ExpectedUnit, Expected: l.ExpectedQuantity));
            var receipts = sourceIds.Count == 0 ? new List<(int LineId, string Unit, decimal Quantity)>()
                : (await ToListAsync(db.PalletContentLines.AsNoTracking().Where(c => sourceIds.Contains(c.SupplyCommitmentLineId) && c.OriginCorrectionLineId == null
                        && c.OriginRejectionLineId == null && c.Pallet.LogisticsStatus != PalletRules.StatusCancelled)
                    .Select(c => new { c.SupplyCommitmentLineId, c.Unit, c.Quantity }), async, cancellationToken))
                    .Select(c => (LineId: c.SupplyCommitmentLineId, c.Unit, c.Quantity)).ToList();
            decimal ReceivedOn(int lineId, string unit) =>
                receipts.Where(r => r.LineId == lineId && (unit == null || string.Equals(r.Unit, unit, StringComparison.Ordinal))).Sum(r => r.Quantity);
            foreach (var entry in added)
            {
                var sourceId = entry.Entity.SplitFromLine?.Id ?? entry.Entity.SplitFromLineId!.Value;
                var orderId = entry.Property(l => l.SupplyCommitmentId).CurrentValue;
                if (!sources.TryGetValue(sourceId, out var source) || source.OrderId != orderId
                    || ReceivedOn(sourceId, source.Unit) is var received && (received <= 0m || received >= source.Expected))
                {
                    throw new TraceabilityViolationException("A split line is the open part of a stored, partly received line of the same purchase order.");
                }
                if (!edits.Contains(orderId))
                {
                    throw new TraceabilityViolationException($"Purchase order {orderId} is only split together with an Edit change record.");
                }
            }

            // Vergeleken met de opgeslagen waarden (ook bij een losgekoppelde Update).
            var modifiedIds = modified.Select(e => e.Entity.Id).ToList();
            var stored = modifiedIds.Count == 0 ? new Dictionary<int, (int? SplitFrom, decimal? Cancelled, decimal Expected)>()
                : (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => modifiedIds.Contains(l.Id))
                    .Select(l => new { l.Id, l.SplitFromLineId, l.CancelledQuantity, l.ExpectedQuantity }), async, cancellationToken))
                    .ToDictionary(l => l.Id, l => (SplitFrom: l.SplitFromLineId, Cancelled: l.CancelledQuantity, Expected: l.ExpectedQuantity));
            foreach (var entry in modified)
            {
                var line = entry.Entity;
                if (!stored.TryGetValue(line.Id, out var before)) continue;
                if (before.SplitFrom != line.SplitFromLineId)
                {
                    throw new TraceabilityViolationException($"Supply commitment line {line.Id} keeps the line it was split from.");
                }
                var cancelledBefore = before.Cancelled ?? 0m;
                var cancelledAfter = line.CancelledQuantity ?? 0m;
                if (cancelledBefore == cancelledAfter) continue;
                if (!edits.Contains(line.SupplyCommitmentId) || cancelledAfter < cancelledBefore || before.Expected - line.ExpectedQuantity != cancelledAfter - cancelledBefore)
                {
                    throw new TraceabilityViolationException($"The cancelled remainder of supply commitment line {line.Id} only grows, with an Edit, by exactly the reduction of what is expected.");
                }
            }
        }

        private static async Task CheckReceivedQuantitiesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var contents = db.ChangeTracker.Entries<PalletContentLine>().Where(e => e.State == EntityState.Modified).Select(e => e.Entity).ToList();
            if (contents.Count == 0) return;
            var ids = contents.Select(c => c.Id).ToList();
            var stored = (await ToListAsync(db.PalletContentLines.AsNoTracking().Where(c => ids.Contains(c.Id))
                    .Select(c => new { c.Id, c.Quantity, c.Unit, c.NetWeightKg, c.WeightMethod, c.NetKgPerPackage }), async, cancellationToken))
                .ToDictionary(c => c.Id);
            foreach (var content in contents)
            {
                if (!stored.TryGetValue(content.Id, out var before)) continue;
                if (before.Quantity != content.Quantity || !string.Equals(before.Unit, content.Unit, StringComparison.Ordinal) || before.NetWeightKg != content.NetWeightKg
                    || !string.Equals(before.WeightMethod, content.WeightMethod, StringComparison.Ordinal) || before.NetKgPerPackage != content.NetKgPerPackage)
                {
                    throw new TraceabilityViolationException($"Received quantities never change: pallet content line {content.Id} keeps its quantity, unit and net kg.");
                }
            }
        }
    }
}
