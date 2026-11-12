using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private sealed record StoredOrderDocument(int Id, string Route, int VendorId, string SupplierReference, DateOnly OrderedOn, DateTime? ExpectedAt, string Origin,
            string Note, string SupplierLotReference, string OrderChannel, string DocumentStatus, DateTime? FinalizedAt, string FinalizedBy, string FinalizedVia);

        private sealed record StoredOrderLine(int Id, int SupplyCommitmentId, int ProductVariantId, string ExpectedUnit, decimal ExpectedQuantity, decimal? ExpectedNetWeightKg,
            decimal? ExpectedGrossWeightKg, string WeightSource, decimal? PalletCount, decimal? PackagesPerPallet, decimal? LoosePackages, decimal? UnitPrice, int? ReplacesLineId);

        // ---- Regel 29: inkooporder aanpassen, definitief maken en corrigeren (zie PurchaseOrderService.Edit.cs) ----
        //
        // Wat regel 18 van een opgeslagen orderregel vastlegt (artikel, eenheid, prijs, aantal, kg, palletinvoer) verandert buiten een
        // afwijkende ontvangst alleen samen met precies één nieuwe auditregel van die order in dezelfde save, nooit samen met een afwijking
        // op die order (die blijft onder regel 18):
        //   - Edit: alleen een Open order voor het eigen magazijn. Met een Edit gelden in die save de grenzen na ontvangst: een regel
        //     waaraan iets hangt (ontvangst, afwijking, vervanging, reservering, afkeuring) wordt niet verwijderd en houdt artikel en
        //     eenheid, het aantal gaat nooit onder wat ontvangen is, en de leverancier ligt vast zodra er iets ontvangen is.
        //   - Finalize: Open naar Definitive, met wie, wanneer en waardoor; verder verandert er in die save niets aan de order.
        //   - Correction: alleen een Definitive order, met reden, en alleen de prijs per regel, het ordernummer van de leverancier en de
        //     notitie. Een definitieve order verandert in haar kopvelden (leverancier, ordernummer leverancier, besteldatum, verwacht,
        //     herkomst, notitie, lot van de leverancier, kanaal) en regels (toevoegen, verwijderen, wijzigen) alleen zo.
        // Een nieuwe order is Open; Definitive gaat nooit terug en wie, wanneer en waardoor liggen dan vast. Auditregels zijn append-only.
        // Kopvelden en het toevoegen of verwijderen van regels van een Open order bewaakte de guard niet en blijft hij niet bewaken (de
        // service schrijft er wel altijd een auditregel bij). Vergeleken met de opgeslagen waarden (ook bij een losgekoppelde Update).
        private static async Task CheckPurchaseOrderChangesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<PurchaseOrderChange>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Purchase order changes are append-only and cannot be modified or deleted.");
            }
            var addedOrders = db.ChangeTracker.Entries<SupplyCommitment>().Where(e => e.State == EntityState.Added).ToList();
            foreach (var order in addedOrders.Select(e => e.Entity))
            {
                if (order.DocumentStatus != PurchaseOrderDocumentRules.StatusOpen || order.FinalizedAt != null || order.FinalizedBy != null || order.FinalizedVia != null)
                {
                    throw new TraceabilityViolationException("A new purchase order starts Open; it can only become definitive afterwards.");
                }
            }

            var changes = new Dictionary<int, PurchaseOrderChange>();
            foreach (var change in db.ChangeTracker.Entries<PurchaseOrderChange>().Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            {
                if (!PurchaseOrderDocumentRules.Kinds.Contains(change.Kind, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(change.ActorReference)
                    || (change.Kind == PurchaseOrderDocumentRules.KindCorrection && string.IsNullOrWhiteSpace(change.Reason)))
                {
                    throw new TraceabilityViolationException("A purchase order change has a known kind and an actor, and a correction has a reason.");
                }
                if (!changes.TryAdd(change.SupplyCommitmentId, change))
                {
                    throw new TraceabilityViolationException($"Purchase order {change.SupplyCommitmentId} gets at most one change per save.");
                }
            }

            var newOrderKeys = addedOrders.Select(e => e.Property(c => c.Id).CurrentValue).ToHashSet();
            var lineEntries = db.ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State is EntityState.Modified or EntityState.Deleted
                || (e.State == EntityState.Added && e.Entity.ReplacesLineId == null && !newOrderKeys.Contains(e.Property(l => l.SupplyCommitmentId).CurrentValue))).ToList();
            var modifiedOrders = db.ChangeTracker.Entries<SupplyCommitment>().Where(e => e.State == EntityState.Modified).ToList();
            if (changes.Count == 0 && modifiedOrders.Count == 0 && lineEntries.Count == 0)
            {
                return;
            }

            var storedLineIds = lineEntries.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id).ToList();
            var storedLines = storedLineIds.Count == 0 ? new Dictionary<int, StoredOrderLine>()
                : (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => storedLineIds.Contains(l.Id))
                    .Select(l => new StoredOrderLine(l.Id, l.SupplyCommitmentId, l.ProductVariantId, l.ExpectedUnit, l.ExpectedQuantity, l.ExpectedNetWeightKg, l.ExpectedGrossWeightKg,
                        l.WeightSource, l.PalletCount, l.PackagesPerPallet, l.LoosePackages, l.UnitPrice, l.ReplacesLineId)), async, cancellationToken)).ToDictionary(l => l.Id);

            // Regels onder een afwijking in dezelfde save horen bij regel 18; de rest van de regelwijzigingen vraagt een auditregel.
            var deviationOrders = db.ChangeTracker.Entries<SupplyReceiptDeviation>().Where(e => e.State == EntityState.Added)
                .Select(e => e.Property(d => d.SupplyCommitmentId).CurrentValue).ToHashSet();
            var deviationLines = db.ChangeTracker.Entries<SupplyReceiptDeviation>().Where(e => e.State == EntityState.Added)
                .Select(e => e.Property(d => d.OriginalLineId).CurrentValue).ToHashSet();
            var edited = new List<(EntityEntry<SupplyCommitmentLine> Entry, StoredOrderLine Before, int OrderId)>();
            foreach (var entry in lineEntries)
            {
                if (entry.State == EntityState.Added)
                {
                    edited.Add((entry, null, entry.Property(l => l.SupplyCommitmentId).CurrentValue));
                    continue;
                }
                if (!storedLines.TryGetValue(entry.Entity.Id, out var before))
                {
                    continue; // bestaat niet (meer): de database weigert dit zelf
                }
                if (entry.State == EntityState.Deleted || (!deviationLines.Contains(before.Id) && LineDiffers(entry.Entity, before)))
                {
                    edited.Add((entry, before, before.SupplyCommitmentId));
                }
            }

            var orderIds = modifiedOrders.Select(e => e.Entity.Id).Concat(changes.Keys).Concat(edited.Select(e => e.OrderId)).Distinct().ToList();
            var stored = (await ToListAsync(db.SupplyCommitments.AsNoTracking().Where(c => orderIds.Contains(c.Id))
                .Select(c => new StoredOrderDocument(c.Id, c.Route, c.VendorId, c.SupplierReference, c.OrderedOn, c.ExpectedAt, c.Origin, c.Note, c.SupplierLotReference, c.OrderChannel,
                    c.DocumentStatus, c.FinalizedAt, c.FinalizedBy, c.FinalizedVia)), async, cancellationToken)).ToDictionary(c => c.Id);
            var trackedOrders = modifiedOrders.ToDictionary(e => e.Entity.Id, e => e.Entity);

            foreach (var orderId in orderIds)
            {
                if (!stored.TryGetValue(orderId, out var before))
                {
                    if (changes.ContainsKey(orderId))
                    {
                        throw new TraceabilityViolationException($"A purchase order change belongs to a stored purchase order, not {orderId}.");
                    }
                    continue;
                }
                trackedOrders.TryGetValue(orderId, out var current);
                changes.TryGetValue(orderId, out var change);
                var fields = current == null ? new List<string>() : ChangedDocumentFields(current, before);
                var lines = edited.Where(e => e.OrderId == orderId).ToList();
                var after = current?.DocumentStatus ?? before.DocumentStatus;

                if (before.DocumentStatus == PurchaseOrderDocumentRules.StatusDefinitive
                    && (after != PurchaseOrderDocumentRules.StatusDefinitive || current != null && (current.FinalizedAt != before.FinalizedAt
                        || current.FinalizedBy != before.FinalizedBy || current.FinalizedVia != before.FinalizedVia)))
                {
                    throw new TraceabilityViolationException($"Purchase order {orderId} is definitive: it never becomes Open again, and who, when and how stay fixed.");
                }
                var finalizing = before.DocumentStatus == PurchaseOrderDocumentRules.StatusOpen && after == PurchaseOrderDocumentRules.StatusDefinitive;
                if (finalizing != (change?.Kind == PurchaseOrderDocumentRules.KindFinalize))
                {
                    throw new TraceabilityViolationException($"Purchase order {orderId} becomes definitive exactly together with its Finalize change, and only then.");
                }
                if (finalizing && (current.FinalizedAt == null || string.IsNullOrWhiteSpace(current.FinalizedBy)
                    || (current.FinalizedVia != PurchaseOrderDocumentRules.ViaManual && current.FinalizedVia != PurchaseOrderDocumentRules.ViaInvoice)))
                {
                    throw new TraceabilityViolationException($"Making purchase order {orderId} definitive records who, when and how.");
                }
                if (change != null && deviationOrders.Contains(orderId))
                {
                    throw new TraceabilityViolationException($"Purchase order {orderId} is never finalized, edited or corrected in the same save as a receipt deviation.");
                }
                if (finalizing && (fields.Count > 0 || lines.Count > 0))
                {
                    throw new TraceabilityViolationException($"Making purchase order {orderId} definitive changes nothing else in the same save.");
                }
                if (fields.Count == 0 && lines.Count == 0)
                {
                    continue;
                }
                if (before.DocumentStatus == PurchaseOrderDocumentRules.StatusOpen)
                {
                    if (change == null)
                    {
                        // Kopvelden, nieuwe en verwijderde regels: zoals altijd; wat regel 18 vastlegt, vraagt een Edit.
                        if (lines.Any(l => l.Entry.State == EntityState.Modified))
                        {
                            throw new TraceabilityViolationException($"The lines of purchase order {orderId} only change together with a receipt deviation or an Edit change record.");
                        }
                        continue;
                    }
                    if (change.Kind != PurchaseOrderDocumentRules.KindEdit || before.Route != SupplyRules.RouteWarehouse)
                    {
                        throw new TraceabilityViolationException($"Only an open warehouse purchase order is edited (order {orderId}).");
                    }
                    await CheckEditLimitsAsync(db, orderId, fields, lines, async, cancellationToken);
                    continue;
                }
                // Definitief: alleen een correctie, alleen prijs, ordernummer van de leverancier en notitie.
                if (change?.Kind != PurchaseOrderDocumentRules.KindCorrection
                    || fields.Any(f => f != nameof(SupplyCommitment.SupplierReference) && f != nameof(SupplyCommitment.Note))
                    || lines.Any(l => l.Entry.State != EntityState.Modified || !OnlyPriceDiffers(l.Entry.Entity, l.Before)))
                {
                    throw new TraceabilityViolationException(
                        $"Definitive purchase order {orderId} is only corrected, with a reason, in its prices, the supplier's order number and the note.");
                }
            }
        }

        private static async Task CheckEditLimitsAsync(AppDbContext db, int orderId, List<string> fields, List<(EntityEntry<SupplyCommitmentLine> Entry, StoredOrderLine Before, int OrderId)> lines,
            bool async, CancellationToken cancellationToken)
        {
            if (fields.Contains(nameof(SupplyCommitment.VendorId))
                && await AnyAsync(db.PalletContentLines.AsNoTracking().Where(c => c.SupplyCommitmentLine.SupplyCommitmentId == orderId), async, cancellationToken))
            {
                throw new TraceabilityViolationException($"The supplier of purchase order {orderId} is fixed once goods have been received.");
            }
            var storedIds = lines.Where(l => l.Before != null).Select(l => l.Before.Id).ToList();
            if (storedIds.Count == 0)
            {
                return;
            }
            var inUse = (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => storedIds.Contains(l.Id) && (l.ReplacesLineId != null
                    || db.PalletContentLines.Any(c => c.SupplyCommitmentLineId == l.Id) || db.SupplyReceiptDeviations.Any(d => d.OriginalLineId == l.Id || d.NewLineId == l.Id)
                    || db.SupplyCommitmentLines.Any(r => r.ReplacesLineId == l.Id) || db.StockReservations.Any(r => r.SupplyCommitmentLineId == l.Id)
                    || db.SupplyRejectionLines.Any(r => r.SupplyCommitmentLineId == l.Id)))
                .Select(l => l.Id), async, cancellationToken)).ToHashSet();
            var received = (await ToListAsync(db.PalletContentLines.AsNoTracking().Where(c => storedIds.Contains(c.SupplyCommitmentLineId)
                    && c.OriginCorrectionLineId == null && c.OriginRejectionLineId == null && c.Pallet.LogisticsStatus != PalletRules.StatusCancelled)
                .Select(c => new { c.SupplyCommitmentLineId, c.Quantity, c.Unit }), async, cancellationToken));
            foreach (var (entry, before, _) in lines.Where(l => l.Before != null))
            {
                var line = entry.Entity;
                if (line.SupplyCommitmentId != before.SupplyCommitmentId || line.ReplacesLineId != before.ReplacesLineId)
                {
                    throw new TraceabilityViolationException($"Supply commitment line {before.Id} stays on its order and keeps what it replaces.");
                }
                if (!inUse.Contains(before.Id))
                {
                    continue;
                }
                if (entry.State == EntityState.Deleted)
                {
                    throw new TraceabilityViolationException($"Supply commitment line {before.Id} has receipts or history and is never removed.");
                }
                if (line.ProductVariantId != before.ProductVariantId || !string.Equals(line.ExpectedUnit, before.ExpectedUnit, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"The article and unit of supply commitment line {before.Id} are fixed once something hangs on it.");
                }
                var receivedQuantity = received.Where(r => r.SupplyCommitmentLineId == before.Id && (before.ExpectedUnit == null || string.Equals(r.Unit, before.ExpectedUnit, StringComparison.Ordinal)))
                    .Sum(r => r.Quantity);
                if (line.ExpectedQuantity < receivedQuantity)
                {
                    throw new TraceabilityViolationException($"Supply commitment line {before.Id} never expects less than the {receivedQuantity} already received.");
                }
            }
        }

        private static bool LineDiffers(SupplyCommitmentLine line, StoredOrderLine before) =>
            line.SupplyCommitmentId != before.SupplyCommitmentId || line.ReplacesLineId != before.ReplacesLineId || line.ProductVariantId != before.ProductVariantId
            || !string.Equals(line.ExpectedUnit, before.ExpectedUnit, StringComparison.Ordinal) || line.UnitPrice != before.UnitPrice || ShapeDiffers(line, before);

        private static bool ShapeDiffers(SupplyCommitmentLine line, StoredOrderLine before) =>
            line.ExpectedQuantity != before.ExpectedQuantity || line.ExpectedNetWeightKg != before.ExpectedNetWeightKg || line.ExpectedGrossWeightKg != before.ExpectedGrossWeightKg
            || !string.Equals(line.WeightSource, before.WeightSource, StringComparison.Ordinal) || line.PalletCount != before.PalletCount
            || line.PackagesPerPallet != before.PackagesPerPallet || line.LoosePackages != before.LoosePackages;

        private static bool OnlyPriceDiffers(SupplyCommitmentLine line, StoredOrderLine before) =>
            line.SupplyCommitmentId == before.SupplyCommitmentId && line.ReplacesLineId == before.ReplacesLineId && line.ProductVariantId == before.ProductVariantId
            && string.Equals(line.ExpectedUnit, before.ExpectedUnit, StringComparison.Ordinal) && !ShapeDiffers(line, before);

        private static List<string> ChangedDocumentFields(SupplyCommitment order, StoredOrderDocument before)
        {
            var fields = new List<string>();
            void Check(bool changed, string name) { if (changed) fields.Add(name); }
            Check(!string.Equals(order.Route, before.Route, StringComparison.Ordinal), nameof(SupplyCommitment.Route));
            Check(order.VendorId != before.VendorId, nameof(SupplyCommitment.VendorId));
            Check(!string.Equals(order.SupplierReference, before.SupplierReference, StringComparison.Ordinal), nameof(SupplyCommitment.SupplierReference));
            Check(order.OrderedOn != before.OrderedOn, nameof(SupplyCommitment.OrderedOn));
            Check(order.ExpectedAt != before.ExpectedAt, nameof(SupplyCommitment.ExpectedAt));
            Check(!string.Equals(order.Origin, before.Origin, StringComparison.Ordinal), nameof(SupplyCommitment.Origin));
            Check(!string.Equals(order.Note, before.Note, StringComparison.Ordinal), nameof(SupplyCommitment.Note));
            Check(!string.Equals(order.SupplierLotReference, before.SupplierLotReference, StringComparison.Ordinal), nameof(SupplyCommitment.SupplierLotReference));
            Check(!string.Equals(order.OrderChannel, before.OrderChannel, StringComparison.Ordinal), nameof(SupplyCommitment.OrderChannel));
            return fields;
        }
    }
}
