using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 37: de leveringsconditie van een order (feedback 22 november, zie DeliveryTermRules) ----
        //
        // De leveringsconditie van een opgeslagen verkooporder verandert alleen samen met een auditregel DeliveryTermChanged van die order
        // in dezelfde save, met de werkelijke oude en nieuwe waarde; die van een opgeslagen inkooporder alleen samen met een Edit van die order
        // die het veld deliveryTerm vastlegt (een definitieve order houdt haar, regel 29). Zo is elke wijziging terug te vinden. Vergeleken met
        // de opgeslagen waarde in de database, niet met OriginalValues.
        private static async Task CheckDeliveryTermsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var sales = db.ChangeTracker.Entries<SalesOrder>().Where(e => e.State == EntityState.Modified).ToList();
            if (sales.Count > 0)
            {
                var ids = sales.Select(e => e.Entity.Id).ToList();
                var stored = (await ToListAsync(db.SalesOrders.AsNoTracking().Where(o => ids.Contains(o.Id))
                        .Select(o => new { o.Id, o.DeliveryTermKind, o.Incoterm, o.IncotermPlace, o.DeliveryTermText }), async, cancellationToken))
                    .ToDictionary(o => o.Id, o => new DeliveryTermRules.DeliveryTerm(o.DeliveryTermKind, o.Incoterm, o.IncotermPlace, o.DeliveryTermText));
                var events = db.ChangeTracker.Entries<SalesOrderAuditEvent>().Where(e => e.State == EntityState.Added && e.Entity.EventType == ReservationRules.DeliveryTermChangedEvent)
                    .Select(e => e.Entity).ToList();
                foreach (var order in sales.Select(e => e.Entity))
                {
                    if (!stored.TryGetValue(order.Id, out var before)) continue;
                    var after = DeliveryTermRules.Of(order);
                    if (before == after) continue;
                    if (!events.Any(e => e.SalesOrderId == order.Id && e.OldValue == DeliveryTermRules.Describe(before) && e.NewValue == DeliveryTermRules.Describe(after)))
                    {
                        throw new TraceabilityViolationException($"The delivery term of sales order {order.Id} only changes together with a DeliveryTermChanged audit event.");
                    }
                }
            }

            var purchases = db.ChangeTracker.Entries<SupplyCommitment>().Where(e => e.State == EntityState.Modified).ToList();
            if (purchases.Count == 0) return;
            var purchaseIds = purchases.Select(e => e.Entity.Id).ToList();
            var storedPurchases = (await ToListAsync(db.SupplyCommitments.AsNoTracking().Where(c => purchaseIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.DeliveryTermKind, c.Incoterm, c.IncotermPlace, c.DeliveryTermText }), async, cancellationToken))
                .ToDictionary(c => c.Id, c => new DeliveryTermRules.DeliveryTerm(c.DeliveryTermKind, c.Incoterm, c.IncotermPlace, c.DeliveryTermText));
            var changes = db.ChangeTracker.Entries<PurchaseOrderChange>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
            foreach (var order in purchases.Select(e => e.Entity))
            {
                if (!storedPurchases.TryGetValue(order.Id, out var before)) continue;
                var after = DeliveryTermRules.Of(order);
                if (before == after) continue;
                var audited = changes.Any(c => c.SupplyCommitmentId == order.Id && c.Kind == PurchaseOrderDocumentRules.KindEdit
                    && PurchaseOrderDocumentRules.Deserialize(c.ChangesJson).Any(f => f.Field == PurchaseOrderDocumentRules.FieldDeliveryTerm
                        && f.Old == DeliveryTermRules.Describe(before) && f.New == DeliveryTermRules.Describe(after)));
                if (!audited)
                {
                    throw new TraceabilityViolationException($"The delivery term of purchase order {order.Id} only changes with an edit of the order.");
                }
            }
        }
    }
}
