using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private sealed record StoredPartij(int Id, string Number, string SequencePeriod, int? Sequence, string CreatedBy, DateTime CreatedAt, string Kind);
        private sealed record StoredPartijLink(int Id, int? PartijId, int OwnerId);

        // ---- Regel 23: partijen (zie PartijService en PartijRules) ----
        //
        // De partij is de basis voor kosten en resultaat (P2, P3): een palletregel valt nooit stil in een andere partij, een nummer
        // verandert nooit zonder historie en een partij met koppelingen verdwijnt nooit. Vergeleken met de opgeslagen waarden in de
        // database, niet met OriginalValues (bij een losgekoppelde Update zijn die gelijk aan de nieuwe waarden). Foreign keys worden via
        // de change tracker gelezen, zodat een partij of regel die in dezelfde save ontstaat (tijdelijke sleutel) gewoon meetelt.
        private static async Task CheckPartijenAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<PartijAuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Lot audit events are append-only and cannot be modified or deleted.");
            }

            await CheckPartijRecordsAsync(db, async, cancellationToken);

            var trackedLines = db.ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State != EntityState.Detached)
                .ToDictionary(e => e.Property(l => l.Id).CurrentValue);
            var trackedOrders = db.ChangeTracker.Entries<SupplyCommitment>().Where(e => e.State != EntityState.Detached)
                .ToDictionary(e => e.Property(c => c.Id).CurrentValue);

            await CheckContentPartijenAsync(db, trackedLines, async, cancellationToken);
            await CheckOrderPartijenAsync(db, trackedLines, trackedOrders, async, cancellationToken);
        }

        private static async Task CheckPartijRecordsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var entries = db.ChangeTracker.Entries<Partij>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            if (entries.Count == 0)
            {
                return;
            }
            foreach (var entry in entries.Where(e => e.State != EntityState.Deleted))
            {
                var partij = entry.Entity;
                if (string.IsNullOrWhiteSpace(partij.Number) || partij.Number != partij.Number.Trim() || partij.NumberKey != PartijRules.NumberKey(partij.Number))
                {
                    throw new TraceabilityViolationException($"Lot '{partij.Number}' needs a trimmed number and the key that belongs to it.");
                }
            }

            var ids = entries.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id).ToList();
            var stored = ids.Count == 0 ? new Dictionary<int, StoredPartij>()
                : (await ToListAsync(db.Partijen.AsNoTracking().Where(p => ids.Contains(p.Id))
                    .Select(p => new StoredPartij(p.Id, p.Number, p.SequencePeriod, p.Sequence, p.CreatedBy, p.CreatedAt, p.Kind)), async, cancellationToken)).ToDictionary(p => p.Id);
            // Een correctie met reden; bij een verkoop-partij (opgeslagen soort) ook een aanpassing zonder reden, die regel 24 alleen
            // toestaat zolang er op haar order niets gereserveerd of geleverd is.
            var corrections = db.ChangeTracker.Entries<PartijAuditEvent>()
                .Where(e => e.State == EntityState.Added
                    && ((e.Entity.EventType == PartijRules.EventNumberCorrected && !string.IsNullOrWhiteSpace(e.Entity.Reason))
                        || (e.Entity.EventType == PartijRules.EventNumberChanged && stored.TryGetValue(e.Entity.PartijId, out var target) && target.Kind == PartijRules.KindSales)))
                .Select(e => e.Entity).ToList();

            foreach (var entry in entries.Where(e => e.State == EntityState.Modified))
            {
                var partij = entry.Entity;
                if (!stored.TryGetValue(partij.Id, out var before))
                {
                    continue; // een niet-bestaande partij laten we over aan de database
                }
                if (before.SequencePeriod != partij.SequencePeriod || before.Sequence != partij.Sequence
                    || !string.Equals(before.CreatedBy, partij.CreatedBy, StringComparison.Ordinal) || before.CreatedAt != partij.CreatedAt)
                {
                    throw new TraceabilityViolationException($"The sequence and creator of lot {before.Number} never change.");
                }
                if (!string.Equals(before.Number, partij.Number, StringComparison.Ordinal)
                    && !corrections.Any(c => c.PartijId == partij.Id && c.OldValue == before.Number && c.NewValue == partij.Number))
                {
                    throw new TraceabilityViolationException($"The number of lot {before.Number} only changes through a correction with a reason and an audit event.");
                }
            }

            var deleted = entries.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToList();
            if (deleted.Count > 0)
            {
                var linked = await AnyAsync(db.SupplyCommitments.AsNoTracking().Where(c => c.PartijId != null && deleted.Contains(c.PartijId.Value)), async, cancellationToken)
                    || await AnyAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => l.PartijId != null && deleted.Contains(l.PartijId.Value)), async, cancellationToken)
                    || await AnyAsync(db.PalletContentLines.AsNoTracking().Where(c => c.PartijId != null && deleted.Contains(c.PartijId.Value)), async, cancellationToken);
                if (linked)
                {
                    throw new TraceabilityViolationException("A lot that a purchase order, order line or pallet line refers to cannot be deleted.");
                }
            }
        }

        // Nieuwe palletregels: exact de partij van de bronregel (correctie- en afgesplitste inhoud: regels 9 en 17), met het partijnummer
        // als LotNumber. Bestaande palletregels: alleen van leeg naar de partij van hun bronregel (het gat uit de migratie dichten).
        private static async Task CheckContentPartijenAsync(AppDbContext db, Dictionary<int, EntityEntry<SupplyCommitmentLine>> trackedLines, bool async, CancellationToken cancellationToken)
        {
            var added = db.ChangeTracker.Entries<PalletContentLine>()
                .Where(e => e.State == EntityState.Added && e.Entity.OriginCorrectionLine == null && e.Entity.OriginCorrectionLineId == null
                    && e.Entity.OriginRejectionLine == null && e.Entity.OriginRejectionLineId == null)
                .ToList();
            var modified = db.ChangeTracker.Entries<PalletContentLine>()
                .Where(e => e.State == EntityState.Modified && e.Property(c => c.PartijId).IsModified).ToList();
            if (added.Count == 0 && modified.Count == 0)
            {
                return;
            }

            var modifiedIds = modified.Select(e => e.Entity.Id).ToList();
            var storedContent = modifiedIds.Count == 0 ? new Dictionary<int, StoredPartijLink>()
                : (await ToListAsync(db.PalletContentLines.AsNoTracking().Where(c => modifiedIds.Contains(c.Id))
                    .Select(c => new StoredPartijLink(c.Id, c.PartijId, c.SupplyCommitmentLineId)), async, cancellationToken)).ToDictionary(c => c.Id);
            var sourceIds = added.Select(e => e.Property(c => c.SupplyCommitmentLineId).CurrentValue)
                .Concat(storedContent.Values.Select(c => c.OwnerId)).Where(id => !trackedLines.ContainsKey(id)).Distinct().ToList();
            var storedSources = sourceIds.Count == 0 ? new Dictionary<int, int?>()
                : (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => sourceIds.Contains(l.Id)).Select(l => new { l.Id, l.PartijId }), async, cancellationToken))
                    .ToDictionary(l => l.Id, l => l.PartijId);
            int? SourcePartij(int lineId) => trackedLines.TryGetValue(lineId, out var line) ? line.Property(l => l.PartijId).CurrentValue : storedSources.GetValueOrDefault(lineId);

            var numbers = await PartijNumbersAsync(db, added.Select(e => e.Property(c => c.PartijId).CurrentValue), async, cancellationToken);
            foreach (var entry in added)
            {
                var partij = entry.Property(c => c.PartijId).CurrentValue;
                var source = SourcePartij(entry.Property(c => c.SupplyCommitmentLineId).CurrentValue);
                if (partij != source)
                {
                    throw new TraceabilityViolationException("A new pallet line belongs to exactly the lot of its purchase order line.");
                }
                if (partij != null && numbers.TryGetValue(partij.Value, out var number) && !string.Equals(entry.Entity.LotNumber, number, StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"A new pallet line of lot {number} carries that number as its lot number, not '{entry.Entity.LotNumber}'.");
                }
            }
            foreach (var entry in modified)
            {
                if (!storedContent.TryGetValue(entry.Entity.Id, out var before) || before.PartijId == entry.Entity.PartijId)
                {
                    continue;
                }
                if (before.PartijId != null || entry.Entity.PartijId != SourcePartij(before.OwnerId))
                {
                    throw new TraceabilityViolationException($"The lot of pallet line {entry.Entity.Id} is only set once, to the lot of its purchase order line, and never changed.");
                }
            }
        }

        // Orders en orderregels: een partij wordt gezet (leeg naar partij) of, zolang er op de order niets ontvangen is, vervangen; een
        // nieuwe regel heeft de partij van de kop (een vervangende regel na een afwijking die van de oorspronkelijke regel); en een order
        // met partij heeft die partij op al haar regels.
        private static async Task CheckOrderPartijenAsync(AppDbContext db, Dictionary<int, EntityEntry<SupplyCommitmentLine>> trackedLines,
            Dictionary<int, EntityEntry<SupplyCommitment>> trackedOrders, bool async, CancellationToken cancellationToken)
        {
            var orders = trackedOrders.Values.Where(e => e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(c => c.PartijId).IsModified)).ToList();
            var lines = trackedLines.Values.Where(e => e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(l => l.PartijId).IsModified)).ToList();
            if (orders.Count == 0 && lines.Count == 0)
            {
                return;
            }

            // Opgeslagen partij van gewijzigde orders en regels: een vervanging (partij naar andere of leeg) mag alleen zonder ontvangst.
            var changedOrderIds = orders.Where(e => e.State == EntityState.Modified).Select(e => e.Entity.Id).ToList();
            var changedLineIds = lines.Where(e => e.State == EntityState.Modified).Select(e => e.Entity.Id).ToList();
            var storedOrders = changedOrderIds.Count == 0 ? new Dictionary<int, int?>()
                : (await ToListAsync(db.SupplyCommitments.AsNoTracking().Where(c => changedOrderIds.Contains(c.Id)).Select(c => new { c.Id, c.PartijId }), async, cancellationToken)).ToDictionary(c => c.Id, c => c.PartijId);
            var storedLines = changedLineIds.Count == 0 ? new Dictionary<int, int?>()
                : (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => changedLineIds.Contains(l.Id)).Select(l => new { l.Id, l.PartijId }), async, cancellationToken)).ToDictionary(l => l.Id, l => l.PartijId);
            foreach (var entry in orders.Where(e => e.State == EntityState.Modified))
            {
                if (storedOrders.TryGetValue(entry.Entity.Id, out var before) && before != null && before != entry.Property(c => c.PartijId).CurrentValue
                    && await AnyAsync(db.PalletContentLines.AsNoTracking().Where(c => c.SupplyCommitmentLine.SupplyCommitmentId == entry.Entity.Id), async, cancellationToken))
                {
                    throw new TraceabilityViolationException($"The lot of purchase order {entry.Entity.OrderNumber} is fixed once goods have been received; correct the lot number instead.");
                }
            }
            foreach (var entry in lines.Where(e => e.State == EntityState.Modified))
            {
                if (storedLines.TryGetValue(entry.Entity.Id, out var before) && before != null && before != entry.Property(l => l.PartijId).CurrentValue
                    && await AnyAsync(db.PalletContentLines.AsNoTracking().Where(c => c.SupplyCommitmentLineId == entry.Entity.Id), async, cancellationToken))
                {
                    throw new TraceabilityViolationException($"The lot of purchase order line {entry.Entity.Id} is fixed once goods have been received.");
                }
            }

            // Vervangende regel: de partij van de oorspronkelijke regel.
            var replacedIds = lines.Where(e => e.State == EntityState.Added && e.Entity.ReplacesLine == null && e.Entity.ReplacesLineId != null)
                .Select(e => e.Entity.ReplacesLineId.Value).Where(id => !trackedLines.ContainsKey(id)).ToList();
            var storedReplaced = replacedIds.Count == 0 ? new Dictionary<int, int?>()
                : (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => replacedIds.Contains(l.Id)).Select(l => new { l.Id, l.PartijId }), async, cancellationToken)).ToDictionary(l => l.Id, l => l.PartijId);
            foreach (var entry in lines.Where(e => e.State == EntityState.Added && (e.Entity.ReplacesLine != null || e.Entity.ReplacesLineId != null)))
            {
                var replacedId = entry.Property(l => l.ReplacesLineId).CurrentValue.Value;
                var replaced = trackedLines.TryGetValue(replacedId, out var line) ? line.Property(l => l.PartijId).CurrentValue : storedReplaced.GetValueOrDefault(replacedId);
                if (entry.Property(l => l.PartijId).CurrentValue != replaced)
                {
                    throw new TraceabilityViolationException("A replacement line after a receipt deviation keeps the lot of the original line.");
                }
            }

            // Kop en regels: een order met partij heeft die partij op al haar regels (opgeslagen regels met de gevolgde wijzigingen erover).
            var orderIds = orders.Select(e => e.Property(c => c.Id).CurrentValue)
                .Concat(lines.Select(e => e.Property(l => l.SupplyCommitmentId).CurrentValue)).Distinct().ToList();
            var storedHeaders = (await ToListAsync(db.SupplyCommitments.AsNoTracking().Where(c => orderIds.Contains(c.Id)).Select(c => new { c.Id, c.PartijId }), async, cancellationToken))
                .ToDictionary(c => c.Id, c => c.PartijId);
            var storedOrderLines = (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => orderIds.Contains(l.SupplyCommitmentId))
                    .Select(l => new StoredPartijLink(l.Id, l.PartijId, l.SupplyCommitmentId)), async, cancellationToken));
            foreach (var orderId in orderIds)
            {
                var header = trackedOrders.TryGetValue(orderId, out var order) ? order.Property(c => c.PartijId).CurrentValue : storedHeaders.GetValueOrDefault(orderId);
                if (header == null)
                {
                    continue;
                }
                var current = storedOrderLines.Where(l => l.OwnerId == orderId && !trackedLines.ContainsKey(l.Id)).Select(l => l.PartijId)
                    .Concat(trackedLines.Values.Where(e => e.State != EntityState.Deleted && e.Property(l => l.SupplyCommitmentId).CurrentValue == orderId)
                        .Select(e => e.Property(l => l.PartijId).CurrentValue));
                if (current.Any(partij => partij != header))
                {
                    throw new TraceabilityViolationException("Every line of a purchase order with a lot belongs to that lot.");
                }
            }
        }

        // Huidig nummer per partij-id: een gevolgde partij (ook een nieuwe, met tijdelijke sleutel) gaat voor de opgeslagen waarde.
        private static async Task<Dictionary<int, string>> PartijNumbersAsync(AppDbContext db, IEnumerable<int?> partijIds, bool async, CancellationToken cancellationToken)
        {
            var ids = partijIds.Where(id => id != null).Select(id => id.Value).Distinct().ToList();
            var numbers = new Dictionary<int, string>();
            if (ids.Count == 0)
            {
                return numbers;
            }
            foreach (var entry in db.ChangeTracker.Entries<Partij>().Where(e => e.State != EntityState.Detached && ids.Contains(e.Property(p => p.Id).CurrentValue)))
            {
                numbers[entry.Property(p => p.Id).CurrentValue] = entry.Entity.Number;
            }
            var missing = ids.Where(id => !numbers.ContainsKey(id)).ToList();
            if (missing.Count > 0)
            {
                foreach (var stored in await ToListAsync(db.Partijen.AsNoTracking().Where(p => missing.Contains(p.Id)).Select(p => new { p.Id, p.Number }), async, cancellationToken))
                {
                    numbers[stored.Id] = stored.Number;
                }
            }
            return numbers;
        }
    }
}
