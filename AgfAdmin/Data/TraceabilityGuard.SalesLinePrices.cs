using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 36: prijs, korting en btw van een verkooporderregel (feedback 22 november, zie OrderLineMath) ----
        //
        // Wat een opgeslagen verkooporderregel aan prijs, korting en btw heeft, is wat de factuur later overneemt; het verandert nooit (een
        // verkooporder heeft nog geen bewerkscherm voor regels). Een reservering raakt de regel wel aan (Version), maar nooit deze velden.
        // Vergeleken met de opgeslagen waarde in de database, niet met OriginalValues.
        private static async Task CheckSalesLinePricesAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var lines = db.ChangeTracker.Entries<SalesOrderLine>().Where(e => e.State == EntityState.Modified).ToList();
            if (lines.Count == 0) return;
            var ids = lines.Select(e => e.Entity.Id).ToList();
            var stored = (await ToListAsync(db.SalesOrderLines.AsNoTracking().Where(l => ids.Contains(l.Id))
                    .Select(l => new { l.Id, l.UnitPrice, l.DiscountPercentage, l.VatPercentage }), async, cancellationToken))
                .ToDictionary(l => l.Id);
            foreach (var line in lines.Select(e => e.Entity))
            {
                if (stored.TryGetValue(line.Id, out var before)
                    && (before.UnitPrice != line.UnitPrice || before.DiscountPercentage != line.DiscountPercentage || before.VatPercentage != line.VatPercentage))
                {
                    throw new TraceabilityViolationException($"The price, discount and VAT of sales order line {line.Id} never change once stored.");
                }
            }
        }
    }
}
