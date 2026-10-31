using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 19: artikelen verwijderen en archiveren (zie ArticleLifecycleService) ----
        //
        // Een artikel (ProductVariant) wordt alleen verwijderd als er nergens iets aan hangt: geen inkoop- of verkooporderregel, afwijking
        // bij ontvangst, inkoop- of oude factuurregel, oude bestelregel, oude voorraadregel of afvalregistratie (drie daarvan zouden in de
        // database anders stil meeverdwijnen). Ontvangsten hangen via hun inkooporderregel aan het artikel. Een productsoort wordt alleen
        // verwijderd zonder artikelen. Een nieuwe inkooporder-, verkooporder- of inkoopfactuurregel wijst nooit naar een gearchiveerd
        // artikel. De artikelhistorie (ArticleAuditEvents) is append-only.
        private static async Task CheckArticlesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            if (db.ChangeTracker.Entries<ArticleAuditEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            {
                throw new TraceabilityViolationException("Article audit events are append-only and cannot be modified or deleted.");
            }

            var deletedVariants = db.ChangeTracker.Entries<ProductVariant>().Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToList();
            if (deletedVariants.Count > 0)
            {
                var used = await AnyAsync(db.SupplyCommitmentLines.AsNoTracking().Where(l => deletedVariants.Contains(l.ProductVariantId)), async, cancellationToken)
                    || await AnyAsync(db.SalesOrderLines.AsNoTracking().Where(l => deletedVariants.Contains(l.ProductVariantId)), async, cancellationToken)
                    || await AnyAsync(db.SupplyReceiptDeviations.AsNoTracking().Where(d => deletedVariants.Contains(d.ExpectedProductVariantId) || deletedVariants.Contains(d.ReceivedProductVariantId)), async, cancellationToken)
                    || await AnyAsync(db.IncomingInvoiceLines.AsNoTracking().Where(l => l.ProductVariantId != null && deletedVariants.Contains(l.ProductVariantId.Value)), async, cancellationToken)
                    || await AnyAsync(db.Set<InvoiceLine>().AsNoTracking().Where(l => l.ProductVariantId != null && deletedVariants.Contains(l.ProductVariantId.Value)), async, cancellationToken)
                    || await AnyAsync(db.OrderLines.AsNoTracking().Where(l => deletedVariants.Contains(l.ProductVariantId)), async, cancellationToken)
                    || await AnyAsync(db.Inventory.AsNoTracking().Where(i => deletedVariants.Contains(i.ProductVariantId)), async, cancellationToken)
                    || await AnyAsync(db.WasteLogs.AsNoTracking().Where(w => deletedVariants.Contains(w.ProductVariantId)), async, cancellationToken);
                if (used)
                {
                    throw new TraceabilityViolationException("An article that is used (orders, receipts, deviations, invoices, inventory or waste) cannot be deleted; archive it instead.");
                }
            }

            var deletedProducts = db.ChangeTracker.Entries<Product>().Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToList();
            if (deletedProducts.Count > 0
                && await AnyAsync(db.ProductVariants.AsNoTracking().Where(v => deletedProducts.Contains(v.ProductId) && !deletedVariants.Contains(v.Id)), async, cancellationToken))
            {
                throw new TraceabilityViolationException("A product kind with articles cannot be deleted.");
            }

            var newVariantIds = db.ChangeTracker.Entries<SupplyCommitmentLine>().Where(e => e.State == EntityState.Added).Select(e => e.Entity.ProductVariantId)
                .Concat(db.ChangeTracker.Entries<SalesOrderLine>().Where(e => e.State == EntityState.Added).Select(e => e.Entity.ProductVariantId))
                .Concat(db.ChangeTracker.Entries<IncomingInvoiceLine>().Where(e => e.State == EntityState.Added && e.Entity.ProductVariantId != null).Select(e => e.Entity.ProductVariantId.Value))
                .Where(id => id > 0).Distinct().ToList();
            if (newVariantIds.Count == 0)
            {
                return;
            }
            // Een artikel dat in dezelfde save wordt gearchiveerd of teruggezet telt met zijn nieuwe stand.
            var tracked = db.ChangeTracker.Entries<ProductVariant>().Where(e => newVariantIds.Contains(e.Entity.Id)).ToDictionary(e => e.Entity.Id, e => e.Entity.IsArchived);
            var untracked = newVariantIds.Where(id => !tracked.ContainsKey(id)).ToList();
            if (tracked.Values.Any(archived => archived)
                || (untracked.Count > 0 && await AnyAsync(db.ProductVariants.AsNoTracking().Where(v => untracked.Contains(v.Id) && v.IsArchived), async, cancellationToken)))
            {
                throw new TraceabilityViolationException("A new order or invoice line cannot use an archived article; restore the article first.");
            }
        }
    }
}
