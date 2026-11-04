using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private sealed record StoredSalesOrderNumber(int Id, string OrderNumber, int OrderYear, int OrderSequence);

        // ---- Regel 22: verkoopordernummer (zie SalesSupplyRules.FormatNumber) ----
        //
        // Een nieuwe verkooporder heeft een nummer dat exact bij jaar en volgnummer hoort (AppDbContext kent het toe). Het nummer van een
        // opgeslagen order (nummer, jaar en volgnummer) verandert nooit: klant en loods noemen de order bij dit nummer. Vergeleken met de
        // opgeslagen waarde in de database, niet met OriginalValues (bij een losgekoppelde Update zijn die gelijk aan de nieuwe waarde).
        private static async Task CheckSalesOrderNumbersAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            foreach (var entry in db.ChangeTracker.Entries<SalesOrder>().Where(e => e.State == EntityState.Added))
            {
                var order = entry.Entity;
                if (order.OrderSequence <= 0 || !string.Equals(order.OrderNumber, SalesSupplyRules.FormatNumber(order.OrderYear, order.OrderSequence), StringComparison.Ordinal))
                {
                    throw new TraceabilityViolationException($"A new sales order gets the number that belongs to its year and sequence, not '{order.OrderNumber}'.");
                }
            }

            var modified = db.ChangeTracker.Entries<SalesOrder>().Where(e => e.State == EntityState.Modified).ToList();
            if (modified.Count == 0)
            {
                return;
            }
            var ids = modified.Select(e => e.Entity.Id).ToList();
            var stored = (await ToListAsync(db.SalesOrders.AsNoTracking().Where(o => ids.Contains(o.Id))
                    .Select(o => new StoredSalesOrderNumber(o.Id, o.OrderNumber, o.OrderYear, o.OrderSequence)), async, cancellationToken))
                .ToDictionary(s => s.Id);
            foreach (var entry in modified)
            {
                var order = entry.Entity;
                if (stored.TryGetValue(order.Id, out var before)
                    && (!string.Equals(before.OrderNumber, order.OrderNumber, StringComparison.Ordinal) || before.OrderYear != order.OrderYear || before.OrderSequence != order.OrderSequence))
                {
                    throw new TraceabilityViolationException($"The number of sales order {before.OrderNumber} never changes.");
                }
            }
        }
    }
}
