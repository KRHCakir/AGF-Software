using AgfAdmin.Models;
using AgfAdmin.Services;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Data
{
    internal static partial class TraceabilityGuard
    {
        // ---- Regel 41: partij is de sleutel (feedback 22 november, zie PartijService en PalletService.ApplyPartijAsync) ----
        //
        // Een nieuw of gewijzigd partijnummer bestaat niet al als partij van de andere soort: inkoop en verkoop delen nooit een nummer.
        // Bestaande dubbele nummers van vóór deze regel blijven staan; alleen een nieuw of gewijzigd nummer telt.
        //
        // Dat nieuwe voorraad altijd een partij heeft, regelt de enige schrijver van ontvangen palletregels (PalletService.ApplyPartijAsync,
        // ook via het ontvangstscherm): een order zonder partij krijgt er bij de eerste ontvangst een. Hier staat het niet, omdat voorraad van
        // vóór de partij (meerdere lotteksten op één orderregel) als gegeven blijft bestaan en regel 23 die regels per orderregel bewaakt.
        private const int LotNumberLockClass = 41;

        // Genormaliseerde sleutels van nieuwe of gewijzigde partijnummers; gesorteerd zodat gelijktijdige schrijvers de locks in dezelfde volgorde nemen.
        internal static List<string> LotNumberKeysToLock(AppDbContext db) =>
            db.ChangeTracker.Entries<Partij>()
                .Where(e => e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(p => p.NumberKey).IsModified))
                .Select(e => e.Entity.NumberKey).Where(k => k != null).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();

        // Transactielock per nummer (PostgreSQL): de uniekheid over soorten is anders niet atomair, want de unieke index geldt per soort.
        // Moet binnen de transactie van de schrijfactie staan; de lock vervalt bij commit of rollback. SQLite (tests) serialiseert schrijvers zelf.
        internal static async Task LockLotNumbersAsync(AppDbContext db, List<string> keys, bool async, CancellationToken cancellationToken)
        {
            foreach (var key in keys)
            {
                if (async)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({LotNumberLockClass}, hashtext({key}))", cancellationToken);
                }
                else
                {
                    db.Database.ExecuteSqlInterpolated($"SELECT pg_advisory_xact_lock({LotNumberLockClass}, hashtext({key}))");
                }
            }
        }

        private static async Task CheckLotNumberingAsync(AppDbContext db, bool async, CancellationToken cancellationToken)
        {
            var numbered = db.ChangeTracker.Entries<Partij>()
                .Where(e => e.State == EntityState.Added || (e.State == EntityState.Modified && e.Property(p => p.NumberKey).IsModified))
                .Select(e => e.Entity).ToList();
            if (numbered.Count > 0)
            {
                foreach (var group in numbered.GroupBy(p => p.NumberKey).Where(g => g.Select(p => p.Kind).Distinct().Count() > 1))
                {
                    throw new TraceabilityViolationException(PartijRules.OtherKindMessage(group.First().Number, PartijRules.KindSales));
                }
                var keys = numbered.Select(p => p.NumberKey).Where(k => k != null).Distinct().ToList();
                var stored = await ToListAsync(db.Partijen.AsNoTracking().Where(p => keys.Contains(p.NumberKey)).Select(p => new { p.Id, p.Kind, p.NumberKey }), async, cancellationToken);
                foreach (var partij in numbered)
                {
                    var clash = stored.FirstOrDefault(p => p.NumberKey == partij.NumberKey && p.Kind != partij.Kind && p.Id != partij.Id);
                    if (clash != null)
                    {
                        throw new TraceabilityViolationException(PartijRules.OtherKindMessage(partij.Number, clash.Kind));
                    }
                }
            }

        }
    }
}
