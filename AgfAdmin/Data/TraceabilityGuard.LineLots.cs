using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 43: partij op de verkooporderregel (feedback 22 november, zie SalesOrderService en StockReservationService.ChangeLotAsync) ----
        //
        // De gevraagde partij van een verkooporderregel is een inkoop-partij (CheckPurchaseOnlyLinksAsync, regel 24) en wordt alleen bij het
        // aanmaken van de regel gezet; daarna verandert ze niet. Welke partij er werkelijk meegaat, staat in de reserveringen: een andere
        // partij bij het picken is een vrijgave plus een nieuwe reservering, met hun eigen auditregels. Vergeleken met de database.
        private static async Task CheckLineLotsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var modified = db.ChangeTracker.Entries<SalesOrderLine>().Where(e => e.State == EntityState.Modified && e.Property(l => l.PartijId).IsModified).ToList();
            if (modified.Count == 0)
            {
                return;
            }
            var ids = modified.Select(e => e.Entity.Id).ToList();
            var stored = (await ToListAsync(db.SalesOrderLines.AsNoTracking().Where(l => ids.Contains(l.Id)).Select(l => new { l.Id, l.PartijId }), async, cancellationToken))
                .ToDictionary(l => l.Id, l => l.PartijId);
            if (modified.Any(e => stored.TryGetValue(e.Entity.Id, out var before) && before != e.Property(l => l.PartijId).CurrentValue))
            {
                throw new TraceabilityViolationException("The requested lot of a sales order line is set when the line is made and never changes; change the lot of a reservation instead.");
            }
        }
    }
}
