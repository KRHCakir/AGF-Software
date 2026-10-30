using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private sealed record StoredSupplyLine(int Id, int SupplyCommitmentId, int ProductVariantId, string ExpectedUnit, int? ReplacesLineId,
            decimal ExpectedQuantity, decimal? ExpectedNetWeightKg, decimal? ExpectedGrossWeightKg, string WeightSource,
            decimal? PalletCount, decimal? PackagesPerPallet, decimal? LoosePackages, string Route);

        // ---- Regel 18: afwijkende ontvangst (zie PalletService.Deviation.cs) ----
        //
        // Vergeleken met de opgeslagen waarden (ook bij een losgekoppelde Update). Een DirectTrade-toewijzing raakt alleen de versie
        // van een regel; dat blijft vrij. "Nooit onder wat al ontvangen is" bepaalt de service bij het verlagen: een gelijktijdige gewone
        // ontvangst kan een regel daarna nog boven verwacht brengen, en dat is (net als bij elke te ruime ontvangst) een toegestane stand.
        private static async Task CheckReceiptDeviationsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<SupplyReceiptDeviation>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Receipt deviations are append-only and cannot be modified or deleted.");
            }
            var deviations = db.ChangeTracker.Entries<SupplyReceiptDeviation>().Where(e => e.State == EntityState.Added).ToList();
            var lines = db.ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State is EntityState.Added or EntityState.Modified).ToList();
            if (deviations.Count == 0 && !lines.Any(e => e.State == EntityState.Modified || e.Entity.ReplacesLineId != null))
            {
                return;
            }

            int NewLineOf(EntityEntry<SupplyReceiptDeviation> d) => d.Property(x => x.NewLineId).CurrentValue;
            int OriginalOf(EntityEntry<SupplyReceiptDeviation> d) => d.Property(x => x.OriginalLineId).CurrentValue;

            var storedIds = lines.Where(e => e.State == EntityState.Modified).Select(e => e.Entity.Id)
                .Concat(deviations.Select(OriginalOf)).Distinct().ToList();
            var stored = (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => storedIds.Contains(l.Id))
                    .Select(l => new StoredSupplyLine(l.Id, l.SupplyCommitmentId, l.ProductVariantId, l.ExpectedUnit, l.ReplacesLineId,
                        l.ExpectedQuantity, l.ExpectedNetWeightKg, l.ExpectedGrossWeightKg, l.WeightSource, l.PalletCount, l.PackagesPerPallet, l.LoosePackages,
                        l.SupplyCommitment.Route)), async, cancellationToken))
                .ToDictionary(l => l.Id);

            foreach (var entry in lines.Where(e => e.State == EntityState.Modified))
            {
                var line = entry.Entity;
                var before = stored[line.Id];
                if (before.SupplyCommitmentId != line.SupplyCommitmentId || before.ProductVariantId != line.ProductVariantId
                    || !string.Equals(before.ExpectedUnit, line.ExpectedUnit, StringComparison.Ordinal) || before.ReplacesLineId != line.ReplacesLineId)
                {
                    throw new TraceabilityViolationException($"The order, product variant, unit and replaced line of supply commitment line {line.Id} are fixed.");
                }
                var shapeChanged = before.ExpectedQuantity != line.ExpectedQuantity || before.ExpectedNetWeightKg != line.ExpectedNetWeightKg
                    || before.ExpectedGrossWeightKg != line.ExpectedGrossWeightKg || !string.Equals(before.WeightSource, line.WeightSource, StringComparison.Ordinal)
                    || before.PalletCount != line.PalletCount || before.PackagesPerPallet != line.PackagesPerPallet || before.LoosePackages != line.LoosePackages;
                if (!shapeChanged)
                {
                    continue;
                }
                var own = deviations.Where(d => OriginalOf(d) == line.Id).ToList();
                if (own.Count != 1)
                {
                    throw new TraceabilityViolationException($"The expected quantity, kg and pallet input of supply commitment line {line.Id} only change together with exactly one new receipt deviation on that line.");
                }
            }

            var newLineIds = deviations.Select(NewLineOf).ToHashSet();
            foreach (var entry in lines.Where(e => e.State == EntityState.Added && e.Entity.ReplacesLineId != null))
            {
                if (!newLineIds.Contains(entry.Property(l => l.Id).CurrentValue))
                {
                    throw new TraceabilityViolationException("A replacement supply commitment line only exists together with its receipt deviation in the same save.");
                }
            }
            if (deviations.Count == 0)
            {
                return;
            }

            var trackedLines = lines.ToDictionary(e => e.Property(l => l.Id).CurrentValue);
            var contents = db.ChangeTracker.Entries<PalletContentLine>().Where(e => e.State == EntityState.Added).ToDictionary(e => e.Property(c => c.Id).CurrentValue);
            var variantIds = deviations.SelectMany(d => new[] { d.Entity.ExpectedProductVariantId, d.Entity.ReceivedProductVariantId }).Distinct().ToList();
            var variants = (await ToListAsync(db.ProductVariants.AsNoTracking().Where(v => variantIds.Contains(v.Id))
                    .Select(v => new ReceiptDeviationRules.VariantFacts(v.Id, v.ProductId, v.Product.Name, v.Name, v.Origin, v.SizeCategory, v.Packaging, v.PackagingUnit, v.TradeClass, v.Brand)),
                    async, cancellationToken))
                .ToDictionary(v => v.Id);
            var directOriginals = stored.Values.Where(l => l.Route == SupplyRules.RouteDirectTrade && deviations.Any(d => OriginalOf(d) == l.Id)).Select(l => l.Id).ToList();
            var assigned = (await ToListAsync(db.StockReservations.AsNoTracking()
                    .Where(r => directOriginals.Contains(r.SupplyCommitmentLineId) && r.SalesOrderLineId != null && r.Source == ReservationRules.SourceDirectTrade
                        && (r.Status == ReservationRules.StatusActive || r.Status == ReservationRules.StatusConsumed))
                    .Select(r => new { r.SupplyCommitmentLineId, r.Quantity }), async, cancellationToken))
                .GroupBy(r => r.SupplyCommitmentLineId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));

            foreach (var entry in deviations)
            {
                var deviation = entry.Entity;
                var originalId = OriginalOf(entry);
                if (!stored.TryGetValue(originalId, out var original))
                {
                    throw new TraceabilityViolationException("A receipt deviation refers to a stored supply commitment line as its original line.");
                }
                if (!trackedLines.TryGetValue(NewLineOf(entry), out var replacementEntry) || replacementEntry.State != EntityState.Added)
                {
                    throw new TraceabilityViolationException("A receipt deviation creates its replacement supply commitment line in the same save.");
                }
                var replacement = replacementEntry.Entity;
                if (original.SupplyCommitmentId != deviation.SupplyCommitmentId || replacement.SupplyCommitmentId != deviation.SupplyCommitmentId
                    || replacement.ReplacesLineId != originalId)
                {
                    throw new TraceabilityViolationException($"Receipt deviation lines must belong to order {deviation.SupplyCommitmentId}, and the replacement must refer to original line {originalId}.");
                }
                if (original.ProductVariantId != deviation.ExpectedProductVariantId || replacement.ProductVariantId != deviation.ReceivedProductVariantId
                    || !variants.TryGetValue(deviation.ExpectedProductVariantId, out var expected) || !variants.TryGetValue(deviation.ReceivedProductVariantId, out var received)
                    || expected.Id == received.Id || !ReceiptDeviationRules.IsSameArticle(expected, received))
                {
                    throw new TraceabilityViolationException("A receipt deviation is a different variant of the same article (product and variety) as the original line; another article is a new supply line.");
                }
                if (replacement.ExpectedQuantity != deviation.Quantity || !string.Equals(replacement.ExpectedUnit, deviation.Unit, StringComparison.Ordinal)
                    || (original.ExpectedUnit != null && !string.Equals(original.ExpectedUnit, deviation.Unit, StringComparison.Ordinal)))
                {
                    throw new TraceabilityViolationException("The replacement line expects exactly the received quantity, in the unit of the original line.");
                }
                if (deviation.ReducedQuantity < 0m)
                {
                    throw new TraceabilityViolationException($"The reduction of the receipt deviation on line {originalId} cannot be negative ({deviation.ReducedQuantity}).");
                }
                var currentOriginal = trackedLines.TryGetValue(originalId, out var originalEntry) ? originalEntry.Entity.ExpectedQuantity : original.ExpectedQuantity;
                if (currentOriginal != original.ExpectedQuantity - deviation.ReducedQuantity || currentOriginal < 0m)
                {
                    throw new TraceabilityViolationException($"Supply commitment line {originalId} must go down by exactly the deviation's reduction ({deviation.ReducedQuantity}).");
                }
                if (assigned.TryGetValue(originalId, out var allocation) && allocation > currentOriginal)
                {
                    throw new TraceabilityViolationException($"Supply commitment line {originalId} would drop below its customer assignments ({allocation}); move or release those first.");
                }
                if (contents.Values.Count(c => c.Property(x => x.SupplyCommitmentLineId).CurrentValue == NewLineOf(entry)) != 1)
                {
                    throw new TraceabilityViolationException("A receipt deviation adds exactly one new received content line on its replacement line.");
                }
                if (!contents.TryGetValue(entry.Property(d => d.PalletContentLineId).CurrentValue, out var contentEntry)
                    || contentEntry.Property(c => c.SupplyCommitmentLineId).CurrentValue != NewLineOf(entry)
                    || contentEntry.Entity.Quantity != deviation.Quantity || !string.Equals(contentEntry.Entity.Unit, deviation.Unit, StringComparison.Ordinal)
                    || contentEntry.Entity.RequestId != deviation.RequestId
                    || contentEntry.Entity.OriginCorrectionLineId != null || contentEntry.Entity.OriginRejectionLineId != null)
                {
                    throw new TraceabilityViolationException("A receipt deviation belongs to exactly one new received content line on its replacement line, with the same quantity, unit and request.");
                }
            }
        }
    }
}
