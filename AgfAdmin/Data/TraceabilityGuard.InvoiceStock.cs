using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 46: voorraad van een directe factuur (verkoopfactuur 1) ----
        //
        // 1. een nieuwe actieve factuurreservering hoort bij een productregel (niet weggehaald) van een directe factuur die een concept is en
        //    blijft, en komt uit de partij, het artikel en de eenheid van die regel; de eigenaar van een reservering verandert nooit;
        // 2. afboeken gaat met precies één afboekregel (SalesInvoiceStockLine) per verbruikte reservering: een exacte kopie, met haar
        //    Dispatch-mutatie (regel 8 telt de mutaties en de afgeboekte totalen); afboekregels zijn append-only;
        // 3. wordt de factuur definitief, dan staat er geen actieve reservering meer op haar regels en is van elke productregel precies het
        //    aantal afgeboekt; vervalt het concept, dan staat er geen actieve reservering meer.
        private static async Task CheckInvoiceStockAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<SalesInvoiceStockLine>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Stock lines of a sales invoice are append-only.");
            }
            foreach (var entry in db.ChangeTracker.Entries<StockReservation>().Where(e => e.State == EntityState.Modified && e.Property(r => r.InvoiceLineId).IsModified))
            {
                if (entry.Property(r => r.InvoiceLineId).OriginalValue != entry.Property(r => r.InvoiceLineId).CurrentValue)
                {
                    throw new TraceabilityViolationException("The invoice line of a reservation never changes.");
                }
            }

            var (added, changedLines, closing) = InvoiceStockChanges(db);

            await LockInvoiceRowsAsync(db, added, closing, changedLines, async, cancellationToken);

            if (changedLines.Count > 0)
            {
                await CheckChangedInvoiceLinesAsync(db, changedLines, async, cancellationToken);
            }
            if (added.Count > 0)
            {
                await CheckNewInvoiceReservationsAsync(db, added, async, cancellationToken);
            }
            foreach (var invoice in closing)
            {
                await CheckClosingInvoiceStockAsync(db, invoice, async, cancellationToken);
            }
        }

        // Wat regel 46 controleert: nieuwe actieve factuurreserveringen, gewijzigde factuurregels en een directe factuur die wordt afgesloten.
        private static (List<StockReservation> Added, List<InvoiceLine> ChangedLines, List<Invoice> Closing) InvoiceStockChanges(AppDbContext db)
        {
            var added = db.ChangeTracker.Entries<StockReservation>()
                .Where(e => e.State == EntityState.Added && IsInvoiceReservation(e.Entity) && e.Entity.Status == ReservationRules.StatusActive)
                .Select(e => e.Entity).ToList();
            var changedLines = db.ChangeTracker.Entries<InvoiceLine>()
                .Where(e => e.State == EntityState.Modified && e.Entity.Id > 0
                    && (e.Property(l => l.PartijId).IsModified || e.Property(l => l.ProductVariantId).IsModified || e.Property(l => l.Unit).IsModified
                        || e.Property(l => l.LineKind).IsModified || e.Property(l => l.RemovedAt).IsModified))
                .Select(e => e.Entity).ToList();
            var closing = db.ChangeTracker.Entries<Invoice>()
                .Where(e => e.State == EntityState.Modified && e.Entity.Kind == SalesInvoiceRules.KindDirect && e.Property(i => i.Status).IsModified
                    && e.Entity.Status is SalesInvoiceRules.StatusFinal or SalesInvoiceRules.StatusDiscarded)
                .Select(e => e.Entity).ToList();
            return (added, changedLines, closing);
        }

        // Regel 46 zet factuurrijen vast; dat slot werkt alleen binnen een transactie. AppDbContext opent er daarom zelf een als de schrijver
        // dat niet deed (zoals bij regel 41), zodat de controles nooit zonder slot lezen, ook niet voor een toekomstige schrijver.
        internal static bool NeedsInvoiceStockTransaction(AppDbContext db)
        {
            if (!db.Database.IsNpgsql())
            {
                return false;
            }
            var (added, changedLines, closing) = InvoiceStockChanges(db);
            return added.Count > 0 || changedLines.Count > 0 || closing.Count > 0;
        }

        // Zet de factuurrijen vast (FOR UPDATE, PostgreSQL, binnen de transactie van de schrijfactie) vóór de controles lezen: reserveren, regels
        // wijzigen en afsluiten van dezelfde factuur wachten zo op elkaar en zien daarna de echte toestand. Vaste volgorde tegen deadlocks.
        // Zonder transactie zou het slot meteen vervallen; SQLite (tests) serialiseert schrijvers zelf.
        private static async Task LockInvoiceRowsAsync(AppDbContext db, List<StockReservation> added, List<Invoice> closing, List<InvoiceLine> changedLines, bool async, CancellationToken cancellationToken)
        {
            if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction == null)
            {
                return;
            }
            var ids = closing.Select(i => i.Id).ToList();
            ids.AddRange(changedLines.Select(l => l.Invoice?.Id ?? l.InvoiceId));
            var unresolved = new List<int>();
            foreach (var reservation in added)
            {
                var line = reservation.InvoiceLine;
                if (line != null)
                {
                    ids.Add(line.Invoice?.Id ?? line.InvoiceId);
                }
                else if (reservation.InvoiceLineId != null)
                {
                    unresolved.Add(reservation.InvoiceLineId.Value);
                }
            }
            if (unresolved.Count > 0)
            {
                ids.AddRange(await ToListAsync(db.InvoiceLines.AsNoTracking().Where(l => unresolved.Contains(l.Id)).Select(l => l.InvoiceId), async, cancellationToken));
            }
            foreach (var id in ids.Where(id => id > 0).Distinct().OrderBy(id => id))
            {
                if (async)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Invoices\" WHERE \"Id\" = {id} FOR UPDATE", cancellationToken);
                }
                else
                {
                    db.Database.ExecuteSqlInterpolated($"SELECT 1 FROM \"Invoices\" WHERE \"Id\" = {id} FOR UPDATE");
                }
            }
        }

        // Een conceptregel met een actieve reservering houdt haar partij, artikel, eenheid en soort: eerst vrijgeven, dan wijzigen.
        private static async Task CheckChangedInvoiceLinesAsync(AppDbContext db, List<InvoiceLine> changedLines, bool async, CancellationToken cancellationToken)
        {
            var ids = changedLines.Select(l => l.Id).Distinct().ToList();
            var stored = await ToListAsync(db.StockReservations.AsNoTracking().Where(r => r.InvoiceLineId != null && ids.Contains(r.InvoiceLineId.Value))
                .Select(r => new { r.Id, LineId = r.InvoiceLineId.Value, r.Status }), async, cancellationToken);
            var tracked = db.ChangeTracker.Entries<StockReservation>().Where(e => e.State != EntityState.Deleted && IsInvoiceReservation(e.Entity)).ToList();
            var active = stored.Where(r => tracked.All(e => e.Entity.Id != r.Id)).Select(r => (r.LineId, r.Status))
                .Concat(tracked.Select(e => (LineId: e.Entity.InvoiceLine?.Id ?? e.Entity.InvoiceLineId ?? 0, e.Entity.Status)))
                .Where(r => r.Status is ReservationRules.StatusActive or ReservationRules.StatusConsumed).Select(r => r.LineId).ToHashSet();
            foreach (var line in changedLines.Where(l => active.Contains(l.Id)))
            {
                throw new TraceabilityViolationException($"Invoice line {line.Id} holds or has booked out stock: release it before the lot, article, unit or kind of the line changes or the line is removed.");
            }
        }

        private static async Task CheckNewInvoiceReservationsAsync(AppDbContext db, List<StockReservation> added, bool async, CancellationToken cancellationToken)
        {
            var trackedLines = db.ChangeTracker.Entries<InvoiceLine>().Select(e => e.Entity).ToList();
            var storedLineIds = added.Where(r => r.InvoiceLine == null).Select(r => r.InvoiceLineId!.Value).Where(id => trackedLines.All(l => l.Id != id)).Distinct().ToList();
            var storedLines = storedLineIds.Count == 0 ? new List<InvoiceLine>()
                : await ToListAsync(db.InvoiceLines.AsNoTracking().Where(l => storedLineIds.Contains(l.Id)), async, cancellationToken);
            var lines = trackedLines.Concat(storedLines).ToList();
            InvoiceLine LineOf(StockReservation r) => r.InvoiceLine ?? lines.FirstOrDefault(l => l.Id == r.InvoiceLineId);

            var trackedInvoices = db.ChangeTracker.Entries<Invoice>().ToList();
            var invoiceIds = lines.Select(l => l.Invoice?.Id ?? l.InvoiceId).Where(id => id > 0).Distinct().ToList();
            var storedStatus = invoiceIds.Count == 0 ? new Dictionary<int, (string Kind, string Status)>()
                : (await ToListAsync(db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).Select(i => new { i.Id, i.Kind, i.Status }), async, cancellationToken))
                    .ToDictionary(i => i.Id, i => (i.Kind, i.Status));

            var contentIds = added.Select(r => r.PalletContentLine?.Id ?? r.PalletContentLineId ?? 0).Where(id => id > 0).Distinct().ToList();
            var contents = (await ToListAsync(db.PalletContentLines.AsNoTracking().Where(c => contentIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.PartijId, c.SupplyCommitmentLine.ProductVariantId, c.Unit }), async, cancellationToken))
                .ToDictionary(c => c.Id);

            foreach (var reservation in added)
            {
                var line = LineOf(reservation);
                if (line == null)
                {
                    continue; // bestaat niet: de FK weigert dit
                }
                var invoice = line.Invoice ?? trackedInvoices.FirstOrDefault(e => e.Entity.Id == line.InvoiceId)?.Entity;
                var (kind, status) = invoice != null ? (invoice.Kind, invoice.Status) : storedStatus.TryGetValue(line.InvoiceId, out var s) ? s : (null, null);
                var stored = invoice != null && invoice.Id > 0 && storedStatus.TryGetValue(invoice.Id, out var before) ? before.Status : SalesInvoiceRules.StatusConcept;
                if (kind != SalesInvoiceRules.KindDirect || status != SalesInvoiceRules.StatusConcept || stored != SalesInvoiceRules.StatusConcept
                    || line.LineKind != SalesInvoiceRules.LineProduct || line.RemovedAt != null)
                {
                    throw new TraceabilityViolationException("Stock is only held for a product line of a direct invoice that is a concept.");
                }
                var contentId = reservation.PalletContentLine?.Id ?? reservation.PalletContentLineId ?? 0;
                if (contents.TryGetValue(contentId, out var content)
                    && (content.PartijId == null || content.PartijId != line.PartijId || content.ProductVariantId != line.ProductVariantId
                        || !string.Equals(content.Unit, line.Unit, StringComparison.Ordinal)))
                {
                    throw new TraceabilityViolationException($"A reservation of invoice line {line.Id} comes from the lot, article and unit of that line.");
                }
            }
        }

        private static async Task CheckClosingInvoiceStockAsync(AppDbContext db, Invoice invoice, bool async, CancellationToken cancellationToken)
        {
            var stored = await ToListAsync(db.StockReservations.AsNoTracking().Where(r => r.InvoiceLineId != null && r.InvoiceLine.InvoiceId == invoice.Id)
                .Select(r => new { r.Id, LineId = r.InvoiceLineId.Value, r.Status, r.Quantity }), async, cancellationToken);
            var tracked = db.ChangeTracker.Entries<StockReservation>().Where(e => e.State != EntityState.Deleted && IsInvoiceReservation(e.Entity)).ToList();
            var all = stored.Where(r => tracked.All(e => e.Entity.Id != r.Id)).Select(r => (r.LineId, r.Status, r.Quantity))
                .Concat(tracked.Where(e => (e.Entity.InvoiceLine?.InvoiceId ?? 0) == invoice.Id || stored.Any(r => r.Id == e.Entity.Id))
                    .Select(e => (LineId: e.Entity.InvoiceLine?.Id ?? e.Entity.InvoiceLineId ?? 0, e.Entity.Status, e.Entity.Quantity)))
                .ToList();
            if (all.Any(r => r.Status == ReservationRules.StatusActive))
            {
                throw new TraceabilityViolationException($"Sales invoice {invoice.Id} still holds stock: release or consume it before the invoice is {invoice.Status.ToLowerInvariant()}.");
            }
            if (invoice.Status != SalesInvoiceRules.StatusFinal)
            {
                if (all.Any(r => r.Status == ReservationRules.StatusConsumed))
                {
                    throw new TraceabilityViolationException($"Sales invoice {invoice.Id} has stock booked out and cannot be discarded.");
                }
                return;
            }
            var storedLines = await ToListAsync(db.InvoiceLines.AsNoTracking().Where(l => l.InvoiceId == invoice.Id && l.LineKind == SalesInvoiceRules.LineProduct)
                .Select(l => new { l.Id, l.Quantity, l.RemovedAt }), async, cancellationToken);
            // Actuele toestand: opgeslagen regels, overschreven door de tracker (gewijzigd of toegevoegd; verwijderd valt weg).
            var trackedLines = db.ChangeTracker.Entries<InvoiceLine>().ToList();
            var lines = storedLines.Where(l => trackedLines.All(e => e.Entity.Id != l.Id)).Select(l => (l.Id, l.Quantity, l.RemovedAt))
                .Concat(trackedLines.Where(e => e.State != EntityState.Deleted && e.Entity.LineKind == SalesInvoiceRules.LineProduct
                        && (e.Entity.Invoice?.Id ?? e.Entity.InvoiceId) == invoice.Id)
                    .Select(e => (e.Entity.Id, e.Entity.Quantity, e.Entity.RemovedAt)))
                .ToList();
            foreach (var line in lines)
            {
                var consumed = all.Where(r => r.LineId == line.Id && r.Status == ReservationRules.StatusConsumed).Sum(r => r.Quantity);
                if (consumed != (line.RemovedAt == null ? line.Quantity : 0m))
                {
                    throw new TraceabilityViolationException($"Product line {line.Id} of sales invoice {invoice.Id} becomes final with {consumed} booked out instead of its quantity.");
                }
            }
        }

        // Afboekregels van een factuur: elk een exacte kopie van één reservering die nu verbruikt wordt, met haar Dispatch-mutatie.
        private static async Task CheckInvoiceStockLinesAsync(AppDbContext db, List<SalesInvoiceStockLine> stockLines, List<StockReservation> consumed, List<StockMovement> movements, bool async, CancellationToken cancellationToken)
        {
            // Eigenaar (factuur) van de factuurregels: uit de tracker, anders uit de database.
            var lineIds = stockLines.Where(l => l.InvoiceLine == null).Select(l => l.InvoiceLineId).Where(id => id > 0).Distinct().ToList();
            var storedOwners = lineIds.Count == 0 ? new Dictionary<int, int>()
                : (await ToListAsync(db.InvoiceLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).Select(l => new { l.Id, l.InvoiceId }), async, cancellationToken))
                    .ToDictionary(l => l.Id, l => l.InvoiceId);
            var seen = new HashSet<StockReservation>();
            foreach (var line in stockLines)
            {
                var reservation = line.StockReservation ?? consumed.FirstOrDefault(r => r.Id == line.StockReservationId);
                if (reservation == null || !consumed.Contains(reservation) || !seen.Add(reservation))
                {
                    throw new TraceabilityViolationException("An invoice stock line books out exactly one reservation of a direct invoice that is consumed in the same save.");
                }
                var movement = line.StockMovement ?? movements.FirstOrDefault(m => m.Id == line.StockMovementId && m.Id != 0);
                var lineId = line.InvoiceLine?.Id ?? line.InvoiceLineId;
                var ownerId = line.InvoiceLine != null ? (line.InvoiceLine.Invoice?.Id ?? line.InvoiceLine.InvoiceId) : storedOwners.TryGetValue(lineId, out var owner) ? owner : 0;
                if (ownerId > 0 && (line.Invoice?.Id ?? line.InvoiceId) != ownerId)
                {
                    throw new TraceabilityViolationException("An invoice stock line belongs to the invoice of its invoice line.");
                }
                var reservationLine = reservation.InvoiceLine?.Id ?? reservation.InvoiceLineId;
                if (movement == null || (movement.StockReservation ?? null) != reservation && movement.StockReservationId != reservation.Id
                    || line.Quantity != reservation.Quantity || !string.Equals(line.Unit, reservation.Unit, StringComparison.Ordinal)
                    || line.NetWeightKg != reservation.NetWeightKg || (line.PalletContentLine?.Id ?? line.PalletContentLineId) != reservation.PalletContentLineId
                    || (line.InvoiceLine != null && reservation.InvoiceLine != null ? line.InvoiceLine != reservation.InvoiceLine : lineId != reservationLine))
                {
                    throw new TraceabilityViolationException("An invoice stock line copies its reservation exactly and points at its dispatch movement.");
                }
            }
        }
    }
}
