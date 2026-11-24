using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        private static async Task CheckLotColorsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var changed = db.ChangeTracker.Entries<Partij>().Where(e => e.State == EntityState.Modified && e.Property(p => p.Color).IsModified).ToList();
            var added = db.ChangeTracker.Entries<Partij>().Where(e => e.State == EntityState.Added && e.Entity.Color != null).ToList();
            if (added.Count > 0)
            {
                throw new TraceabilityViolationException("A new lot starts without a color; the color is set afterwards, with a history line.");
            }
            if (changed.Count == 0)
            {
                return;
            }
            // Vergeleken met de opgeslagen kleur in de database, niet met OriginalValues (die kunnen buiten de service om gezet zijn). LotColorService
            // schrijft de kleur zelf voorwaardelijk (alleen als de oude kleur nog klopt), zodat een gelijktijdige wijziging een conflict is.
            var ids = changed.Select(e => e.Entity.Id).ToList();
            var stored = (await ToListAsync(db.Partijen.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Color }), async, cancellationToken))
                .ToDictionary(p => p.Id, p => p.Color);
            var events = db.ChangeTracker.Entries<PartijAuditEvent>().Where(e => e.State == EntityState.Added && e.Entity.EventType == Services.PartijRules.EventColorChanged).Select(e => e.Entity).ToList();
            foreach (var entry in changed)
            {
                if (!stored.TryGetValue(entry.Entity.Id, out var before) || before == entry.Entity.Color) continue;
                if (!events.Any(e => e.PartijId == entry.Entity.Id && e.OldValue == before && e.NewValue == entry.Entity.Color))
                {
                    throw new TraceabilityViolationException($"The color of lot {entry.Entity.Number} only changes with a history line (old and new color).");
                }
            }
        }

        // ---- Regel 43: partij op de verkooporderregel (feedback 22 november, zie SalesOrderService en StockReservationService.ChangeLotAsync) ----
        //
        // De gevraagde partij van een verkooporderregel is een inkoop-partij (CheckPurchaseOnlyLinksAsync, regel 24) en wordt alleen bij het
        // aanmaken van de regel gezet; daarna verandert ze niet. Welke partij er werkelijk meegaat, staat in de reserveringen: een andere
        // partij bij het picken is een vrijgave plus een nieuwe reservering, met hun eigen auditregels. Vergeleken met de database.
        //
        // De kleur van een partij verandert alleen samen met een historieregel ColorChanged met precies de oude en de nieuwe kleur.
        private static async Task CheckLineLotsAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            await CheckLotColorsAsync(db, async, cancellationToken);
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
