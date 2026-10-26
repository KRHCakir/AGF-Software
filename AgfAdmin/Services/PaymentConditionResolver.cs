using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Eén gedeelde vertaling van een termijn naar een betalingsconditie: de vrije termijn uit het relatieformulier (dagen, factuurdatum
    // of einde maand, optionele korting) en de kale termijn in dagen van legacy schrijvers (oude klantroutes, snelle klantaanmaak,
    // OrderController, seeds, rollen zonder relatie). Zoekt de ACTIEVE conditie met precies dezelfde betekenis (dagen, basis,
    // kortingspercentage en kortingsdagen; laagste sorteervolgorde, dan laagste id), anders een nieuwe met code "{n}D" / "{n}EM" /
    // "{n}D-{p}%{d}" (PaymentConditionRules.DefaultCode). Een inactieve conditie met dezelfde betekenis wordt nooit gekoppeld en nooit
    // stil heractiveerd (inactief zetten is een beheerbesluit); er komt dan een nieuwe actieve conditie, bij een bezette code met
    // achtervoegsel "-2", "-3", …. Daarnaast de enige plek naast RelationService die PaymentTermsDays (spiegel van
    // PaymentCondition.Days) op een rol zet: zie Assign. TraceabilityGuard regel 16 bewaakt dit.
    public static class PaymentConditionResolver
    {
        public static Task<PaymentCondition> ForDaysAsync(AppDbContext db, int days, CancellationToken cancellationToken = default) =>
            ForTermsCoreAsync(db, PaymentConditionRules.Terms.InvoiceDays(days), async: true, cancellationToken);

        public static PaymentCondition ForDays(AppDbContext db, int days) =>
            ForTermsCoreAsync(db, PaymentConditionRules.Terms.InvoiceDays(days), async: false, CancellationToken.None).GetAwaiter().GetResult();

        public static Task<PaymentCondition> ForTermsAsync(AppDbContext db, PaymentConditionRules.Terms terms, CancellationToken cancellationToken = default) =>
            ForTermsCoreAsync(db, terms, async: true, cancellationToken);

        // Rol koppelen aan een conditie: verwijzing en spiegel in één keer.
        public static void Assign(Customer customer, PaymentCondition condition)
        {
            customer.PaymentCondition = condition;
            if (condition.Id != 0) customer.PaymentConditionId = condition.Id;
            customer.PaymentTermsDays = condition.Days;
        }

        public static void Assign(Vendor vendor, PaymentCondition condition)
        {
            vendor.PaymentCondition = condition;
            if (condition.Id != 0) vendor.PaymentConditionId = condition.Id;
            vendor.PaymentTermsDays = condition.Days;
        }

        // Legacy: een rol op een kale termijn zetten (bijv. de oude klantroute of een test die "leverancier op 60 dagen" nabootst).
        public static async Task AssignDaysAsync(AppDbContext db, Customer customer, int days, CancellationToken cancellationToken = default) =>
            Assign(customer, await ForDaysAsync(db, days, cancellationToken));

        public static async Task AssignDaysAsync(AppDbContext db, Vendor vendor, int days, CancellationToken cancellationToken = default) =>
            Assign(vendor, await ForDaysAsync(db, days, cancellationToken));

        private static async Task<PaymentCondition> ForTermsCoreAsync(AppDbContext db, PaymentConditionRules.Terms terms, bool async, CancellationToken cancellationToken)
        {
            // Eerst een conditie die in deze save al is toegevoegd (twee nieuwe rollen met dezelfde termijn delen één nieuwe conditie).
            var pending = db.ChangeTracker.Entries<PaymentCondition>()
                .Where(e => e.State == EntityState.Added && e.Entity.IsActive && terms.Matches(e.Entity))
                .Select(e => e.Entity).FirstOrDefault();
            if (pending != null)
            {
                return pending;
            }
            var (days, basis, percent, discountDays) = (terms.Days, terms.Basis, terms.DiscountPercent, terms.DiscountDays);
            var query = db.PaymentConditions
                .Where(p => p.IsActive && p.Days == days && p.Basis == basis && p.DiscountPercent == percent && p.DiscountDays == discountDays)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id);
            var stored = async ? await query.FirstOrDefaultAsync(cancellationToken) : query.FirstOrDefault();
            if (stored != null)
            {
                return stored;
            }
            // Nieuwe conditie. Is de standaardcode al bezet (door een inactieve of anders bedoelde conditie), dan "-2", "-3", …
            // (de basiscode wordt zo nodig ingekort zodat de code binnen 16 tekens blijft; uniek is zij door de controle).
            var baseCode = PaymentConditionRules.DefaultCode(terms);
            var code = baseCode;
            for (var suffix = 2; await CodeTakenAsync(db, code, async, cancellationToken); suffix++)
            {
                var tail = $"-{suffix}";
                code = $"{baseCode[..Math.Min(baseCode.Length, PaymentConditionRules.MaxCodeLength - tail.Length)]}{tail}";
            }
            var created = new PaymentCondition
            {
                Code = code,
                CodeKey = PaymentConditionRules.CodeKey(code),
                DescriptionNl = PaymentConditionRules.DefaultDescriptionNl(terms),
                DescriptionEn = PaymentConditionRules.DefaultDescriptionEn(terms),
                Days = terms.Days,
                Basis = terms.Basis,
                DiscountPercent = terms.DiscountPercent,
                DiscountDays = terms.DiscountDays,
                IsActive = true,
                SortOrder = terms.Days,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.PaymentConditions.Add(created);
            return created;
        }

        private static async Task<bool> CodeTakenAsync(AppDbContext db, string code, bool async, CancellationToken cancellationToken)
        {
            var key = PaymentConditionRules.CodeKey(code);
            if (db.ChangeTracker.Entries<PaymentCondition>().Any(e => e.State != EntityState.Deleted && e.Entity.CodeKey == key))
            {
                return true;
            }
            var query = db.PaymentConditions.AsNoTracking().Where(p => p.CodeKey == key);
            return async ? await query.AnyAsync(cancellationToken) : query.Any();
        }
    }
}
