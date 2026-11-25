using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 44: verkoopfactuur (verkoopfactuur 1; alleen het nieuwe model, Invoice.Kind gevuld; oude facturen blijven ongemoeid) ----
        //
        // 1. een factuur van het nieuwe model wordt nooit verwijderd; soort, klant, sleutel, aanmaker en aanmaakmoment veranderen nooit, en
        //    een oude factuur wordt nooit omgezet naar het nieuwe model;
        // 2. een nieuwe factuur is een concept zonder nummer; daarna alleen Concept -> Definitief of Concept -> Vervallen;
        // 3. een definitieve of vervallen factuur verandert nooit meer (ook in de database: trigger uit de migratie AddSalesInvoices);
        // 4. regels worden nooit verwijderd; ze ontstaan en veranderen alleen zolang de factuur in de database een concept is en dat in
        //    deze save blijft (Definitief maken legt eerst de momentopname van de regels vast en zet daarna, in een tweede save, de status);
        //    waar een regel vandaan komt (soort, levering, orderregel, dienst- of emballageregel, artikel) verandert nooit; een uit het
        //    concept gehaalde regel verandert nooit meer;
        // 5. een omzetsoort wordt nooit verwijderd;
        // 6. een PDF-versie (SalesInvoiceDocument) hoort bij een definitieve factuur, met opeenvolgende versies vanaf 1; ze wordt nooit
        //    verwijderd en haar inhoud verandert nooit; alleen het moment van versturen wordt één keer vastgelegd.
        // Vergeleken met de opgeslagen waarden in de database, niet met OriginalValues.
        private sealed record StoredInvoice(int Id, string Kind, string Status, int? CustomerId, Guid? RequestId, string RequestFingerprint, string CreatedBy, DateTime CreatedAt);
        private sealed record StoredInvoiceLine(int Id, int InvoiceId, string LineKind, int? SalesDeliveryId, int? SalesOrderLineId, int? SalesOrderServiceLineId,
            int? SalesOrderPackagingLineId, int? CostArticleId, int? ProductVariantId, DateTime? RemovedAt);

        private static async Task CheckSalesInvoicesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            await CheckSalesInvoiceDocumentsAsync(db, async, cancellationToken);
            if (db.ChangeTracker.Entries<RevenueType>().Any(e => e.State == EntityState.Deleted))
            {
                throw new TraceabilityViolationException("A revenue type is never deleted; set it inactive instead.");
            }

            var invoiceEntries = db.ChangeTracker.Entries<Invoice>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            var lineEntries = db.ChangeTracker.Entries<InvoiceLine>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            if (invoiceEntries.Count == 0 && lineEntries.Count == 0)
            {
                return;
            }

            // De opgeslagen kop van elke bestaande factuur die hier (of via een regel) geraakt wordt.
            var invoiceIds = invoiceEntries.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id)
                .Concat(lineEntries.Where(e => !IsNewInvoice(db, e)).Select(e => e.State == EntityState.Added ? e.Property(l => l.InvoiceId).CurrentValue : e.Property(l => l.InvoiceId).OriginalValue))
                .Distinct().ToList();
            var stored = invoiceIds.Count == 0 ? new Dictionary<int, StoredInvoice>()
                : (await ToListAsync(db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id))
                    .Select(i => new StoredInvoice(i.Id, i.Kind, i.Status, i.CustomerId, i.RequestId, i.RequestFingerprint, i.CreatedBy, i.CreatedAt)), async, cancellationToken))
                    .ToDictionary(i => i.Id);

            foreach (var entry in invoiceEntries)
            {
                var invoice = entry.Entity;
                if (entry.State == EntityState.Added)
                {
                    if (invoice.Kind != null && (invoice.Status != SalesInvoiceRules.StatusConcept || invoice.InvoiceNumber != null || invoice.InvoiceYear != null
                        || invoice.InvoiceSequence != null || invoice.FinalizedAt != null || invoice.DiscardedAt != null))
                    {
                        throw new TraceabilityViolationException("A new sales invoice is always a concept without a number.");
                    }
                    continue;
                }
                if (!stored.TryGetValue(invoice.Id, out var before))
                {
                    continue;
                }
                if (before.Kind == null)
                {
                    if (entry.State == EntityState.Modified && invoice.Kind != null)
                    {
                        throw new TraceabilityViolationException("An old invoice is never turned into a sales invoice of the new model.");
                    }
                    continue;
                }
                if (entry.State == EntityState.Deleted)
                {
                    throw new TraceabilityViolationException("A sales invoice is never deleted; a concept is discarded instead.");
                }
                if (before.Status != SalesInvoiceRules.StatusConcept)
                {
                    throw new TraceabilityViolationException($"Sales invoice {invoice.Id} is {before.Status.ToLowerInvariant()} and never changes; correct it with a credit invoice.");
                }
                if (invoice.Kind != before.Kind || invoice.CustomerId != before.CustomerId || invoice.RequestId != before.RequestId || invoice.RequestFingerprint != before.RequestFingerprint
                    || invoice.CreatedBy != before.CreatedBy || invoice.CreatedAt != before.CreatedAt)
                {
                    throw new TraceabilityViolationException("The kind, customer, key, request and creator of a sales invoice never change.");
                }
                if (invoice.Status == SalesInvoiceRules.StatusConcept && (invoice.InvoiceNumber != null || invoice.InvoiceYear != null || invoice.InvoiceSequence != null || invoice.FinalizedAt != null))
                {
                    throw new TraceabilityViolationException("A concept has no invoice number.");
                }
                if (invoice.Status == SalesInvoiceRules.StatusFinal
                    && lineEntries.Any(l => l.State != EntityState.Unchanged && l.Property(x => x.InvoiceId).CurrentValue == invoice.Id))
                {
                    throw new TraceabilityViolationException("The lines of a sales invoice are recorded before it becomes final, never in the same save.");
                }
            }

            var lineIds = lineEntries.Where(e => e.State != EntityState.Added).Select(e => e.Entity.Id).ToList();
            var storedLines = lineIds.Count == 0 ? new Dictionary<int, StoredInvoiceLine>()
                : (await ToListAsync(db.InvoiceLines.AsNoTracking().Where(l => lineIds.Contains(l.Id))
                    .Select(l => new StoredInvoiceLine(l.Id, l.InvoiceId, l.LineKind, l.SalesDeliveryId, l.SalesOrderLineId, l.SalesOrderServiceLineId,
                        l.SalesOrderPackagingLineId, l.CostArticleId, l.ProductVariantId, l.RemovedAt)), async, cancellationToken))
                    .ToDictionary(l => l.Id);

            foreach (var entry in lineEntries)
            {
                if (IsNewInvoice(db, entry))
                {
                    continue;
                }
                var invoiceId = entry.State == EntityState.Added ? entry.Property(l => l.InvoiceId).CurrentValue : entry.Property(l => l.InvoiceId).OriginalValue;
                if (entry.State != EntityState.Added && storedLines.TryGetValue(entry.Entity.Id, out var storedLine))
                {
                    invoiceId = storedLine.InvoiceId;
                }
                if (!stored.TryGetValue(invoiceId, out var invoice) || invoice.Kind == null)
                {
                    continue;
                }
                if (entry.State == EntityState.Deleted)
                {
                    throw new TraceabilityViolationException("A line of a sales invoice is never deleted; it is removed from the concept instead.");
                }
                var current = db.ChangeTracker.Entries<Invoice>().FirstOrDefault(e => e.Entity.Id == invoiceId)?.Entity.Status ?? invoice.Status;
                if (invoice.Status != SalesInvoiceRules.StatusConcept || current != SalesInvoiceRules.StatusConcept)
                {
                    throw new TraceabilityViolationException($"The lines of sales invoice {invoiceId} only change while it is a concept.");
                }
                if (entry.State == EntityState.Modified && storedLines.TryGetValue(entry.Entity.Id, out var before))
                {
                    var line = entry.Entity;
                    if (line.InvoiceId != before.InvoiceId || line.LineKind != before.LineKind || line.SalesDeliveryId != before.SalesDeliveryId
                        || line.SalesOrderLineId != before.SalesOrderLineId || line.SalesOrderServiceLineId != before.SalesOrderServiceLineId
                        || line.SalesOrderPackagingLineId != before.SalesOrderPackagingLineId || line.CostArticleId != before.CostArticleId
                        || line.ProductVariantId != before.ProductVariantId)
                    {
                        throw new TraceabilityViolationException("Where a sales invoice line comes from never changes; remove the line and add a new one.");
                    }
                    if (before.RemovedAt != null)
                    {
                        throw new TraceabilityViolationException("A line removed from a concept never changes again.");
                    }
                }
            }
        }

        private static async Task CheckSalesInvoiceDocumentsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var entries = db.ChangeTracker.Entries<SalesInvoiceDocument>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            if (entries.Count == 0)
            {
                return;
            }
            if (entries.Any(e => e.State == EntityState.Deleted))
            {
                throw new TraceabilityViolationException("A sales invoice document is never deleted.");
            }
            foreach (var entry in entries.Where(e => e.State == EntityState.Modified))
            {
                var changed = entry.Properties.Where(p => p.IsModified && !Equals(p.OriginalValue, p.CurrentValue)).Select(p => p.Metadata.Name).ToList();
                if (changed.Any(name => name is not (nameof(SalesInvoiceDocument.SentAt) or nameof(SalesInvoiceDocument.SentBy)))
                    || entry.Property(d => d.SentAt).OriginalValue != null && changed.Count > 0)
                {
                    throw new TraceabilityViolationException("A sales invoice document never changes; only the moment it was sent is recorded, once.");
                }
            }
            foreach (var group in entries.Where(e => e.State == EntityState.Added).GroupBy(e => e.Entity.InvoiceId))
            {
                var invoiceId = group.Key;
                var tracked = db.ChangeTracker.Entries<Invoice>().FirstOrDefault(e => e.Entity.Id == invoiceId);
                var stored = await FirstOrDefaultAsync(db.Invoices.AsNoTracking().Where(i => i.Id == invoiceId).Select(i => new { i.Kind, i.Status, i.InvoiceNumber }), async, cancellationToken);
                if (stored == null || stored.Kind == null || stored.Status != SalesInvoiceRules.StatusFinal || tracked != null && tracked.State == EntityState.Modified && tracked.Entity.Status != SalesInvoiceRules.StatusFinal)
                {
                    throw new TraceabilityViolationException("A document is only made for a final sales invoice.");
                }
                var last = (await ToListAsync(db.SalesInvoiceDocuments.AsNoTracking().Where(d => d.InvoiceId == invoiceId).Select(d => d.Version), async, cancellationToken)).DefaultIfEmpty(0).Max();
                foreach (var entry in group.OrderBy(e => e.Entity.Version))
                {
                    if (entry.Entity.Version != ++last || entry.Entity.Number != stored.InvoiceNumber || entry.Entity.SentAt != null)
                    {
                        throw new TraceabilityViolationException("Document versions of a sales invoice follow each other from 1, carry the invoice number and are not sent yet.");
                    }
                }
            }
        }

        private static bool IsNewInvoice(AppDbContext db, EntityEntry<InvoiceLine> entry) =>
            entry.Entity.Invoice != null && db.Entry(entry.Entity.Invoice).State == EntityState.Added;
    }
}
