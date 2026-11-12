using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 30: een inkoopfactuur gekoppeld aan de bon (stap 4b, zie PurchaseInvoiceOrderLinks) ----
        //
        // Een factuurregel factureert hoogstens één orderregel, en alleen een orderregel van een order van dezelfde leverancier als de
        // factuur. Koppeling, de prijs op de bon bij het invoeren en de keuze bij een prijsverschil liggen vast na opslaan: het verschil
        // blijft zo herleidbaar, ook als de order later wordt aangepast. Vergeleken met de opgeslagen waarden in de database.
        private static async Task CheckInvoiceOrderLinksAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var modifiedLines = db.ChangeTracker.Entries<IncomingInvoiceLine>().Where(e => e.State == EntityState.Modified).ToList();
            if (modifiedLines.Count > 0)
            {
                var ids = modifiedLines.Select(e => e.Entity.Id).ToList();
                var stored = (await ToListAsync(db.IncomingInvoiceLines.AsNoTracking().Where(l => ids.Contains(l.Id))
                    .Select(l => new { l.Id, l.SupplyCommitmentLineId, l.OrderUnitPrice, l.IncomingInvoiceId }), async, cancellationToken)).ToDictionary(l => l.Id);
                foreach (var entry in modifiedLines)
                {
                    if (stored.TryGetValue(entry.Entity.Id, out var before)
                        && (before.SupplyCommitmentLineId != entry.Entity.SupplyCommitmentLineId || before.OrderUnitPrice != entry.Entity.OrderUnitPrice))
                    {
                        throw new TraceabilityViolationException($"Invoice line {entry.Entity.Id} keeps its purchase order line and the price that was on it.");
                    }
                    if (stored.TryGetValue(entry.Entity.Id, out var linkedBefore) && linkedBefore.SupplyCommitmentLineId != null && linkedBefore.IncomingInvoiceId != entry.Entity.IncomingInvoiceId)
                    {
                        throw new TraceabilityViolationException($"Invoice line {entry.Entity.Id} is linked to a purchase order line and stays on its invoice.");
                    }
                }
            }

            var modifiedInvoices = db.ChangeTracker.Entries<IncomingInvoice>().Where(e => e.State == EntityState.Modified).ToList();
            if (modifiedInvoices.Count > 0)
            {
                var ids = modifiedInvoices.Select(e => e.Entity.Id).ToList();
                var stored = (await ToListAsync(db.IncomingInvoices.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.OrderPriceDecision, i.VendorId }), async, cancellationToken))
                    .ToDictionary(i => i.Id);
                foreach (var entry in modifiedInvoices)
                {
                    if (stored.TryGetValue(entry.Entity.Id, out var before) && !string.Equals(before.OrderPriceDecision, entry.Entity.OrderPriceDecision, StringComparison.Ordinal))
                    {
                        throw new TraceabilityViolationException($"Invoice {entry.Entity.Id} keeps what was decided about a price difference with its purchase orders.");
                    }
                    if (stored.TryGetValue(entry.Entity.Id, out var prior) && prior.VendorId != entry.Entity.VendorId)
                    {
                        var invoiceId = entry.Entity.Id;
                        var newVendor = entry.Entity.VendorId;
                        var orderVendors = await ToListAsync(db.IncomingInvoiceLines.AsNoTracking()
                            .Where(l => l.IncomingInvoiceId == invoiceId && l.SupplyCommitmentLineId != null)
                            .Select(l => l.SupplyCommitmentLine!.SupplyCommitment.VendorId), async, cancellationToken);
                        if (orderVendors.Any(v => v != newVendor))
                        {
                            throw new TraceabilityViolationException($"Invoice {invoiceId} keeps the supplier of the purchase orders its lines are linked to.");
                        }
                    }
                }
            }

            foreach (var entry in db.ChangeTracker.Entries<IncomingInvoice>().Where(e => e.State == EntityState.Added))
            {
                var decision = entry.Entity.OrderPriceDecision;
                if (decision != null && !PurchaseInvoiceOrderLinkRules.Decisions.Contains(decision, StringComparer.Ordinal))
                {
                    throw new TraceabilityViolationException($"A price difference with the purchase order is either {PurchaseInvoiceOrderLinkRules.AdjustOrder} or {PurchaseInvoiceOrderLinkRules.RecordDifference}.");
                }
            }

            var linked = db.ChangeTracker.Entries<IncomingInvoiceLine>().Where(e => e.State == EntityState.Added && e.Entity.SupplyCommitmentLineId != null).Select(e => e.Entity).ToList();
            if (linked.Count == 0) return;
            var orderLineIds = linked.Select(l => l.SupplyCommitmentLineId!.Value).Distinct().ToList();
            var vendors = (await ToListAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => orderLineIds.Contains(l.Id))
                .Select(l => new { l.Id, l.SupplyCommitment.VendorId }), async, cancellationToken)).ToDictionary(l => l.Id, l => l.VendorId);
            foreach (var line in linked)
            {
                var invoiceVendor = line.IncomingInvoice?.VendorId
                    ?? db.ChangeTracker.Entries<IncomingInvoice>().Select(e => e.Entity).FirstOrDefault(i => i.Id == line.IncomingInvoiceId)?.VendorId
                    ?? (await ToListAsync(db.IncomingInvoices.AsNoTracking().Where(i => i.Id == line.IncomingInvoiceId).Select(i => i.VendorId), async, cancellationToken)).FirstOrDefault();
                if (!vendors.TryGetValue(line.SupplyCommitmentLineId!.Value, out var orderVendor) || orderVendor != invoiceVendor)
                {
                    throw new TraceabilityViolationException($"An invoice line is only linked to a purchase order line of the invoice's own supplier (order line {line.SupplyCommitmentLineId}).");
                }
            }
        }
    }
}
