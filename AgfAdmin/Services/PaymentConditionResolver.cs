using AgfAdmin.Data;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Eén gedeelde vertaling van een kale termijn in dagen (legacy schrijvers: oude klantroutes, snelle klantaanmaak, OrderController,
    // seeds, rollen zonder relatie) naar een betalingsconditie: de bestaande conditie "n dagen na factuurdatum zonder korting"
    // (actief eerst, dan laagste sorteervolgorde), anders een nieuwe "{n}D". Daarnaast de enige plek naast RelationService die
    // PaymentTermsDays (spiegel van PaymentCondition.Days) op een rol zet: zie Assign. TraceabilityGuard regel 16 bewaakt dit.
    public static class PaymentConditionResolver
    {
        public static Task<PaymentCondition> ForDaysAsync(AppDbContext db, int days, CancellationToken cancellationToken = default) =>
            ForDaysCoreAsync(db, days, async: true, cancellationToken);

        public static PaymentCondition ForDays(AppDbContext db, int days) =>
            ForDaysCoreAsync(db, days, async: false, CancellationToken.None).GetAwaiter().GetResult();

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

        private static async Task<PaymentCondition> ForDaysCoreAsync(AppDbContext db, int days, bool async, CancellationToken cancellationToken)
        {
            // Eerst een conditie die in deze save al is toegevoegd (twee nieuwe rollen met dezelfde termijn delen één nieuwe conditie).
            var pending = db.ChangeTracker.Entries<PaymentCondition>()
                .Where(e => e.State == EntityState.Added && Matches(e.Entity, days))
                .Select(e => e.Entity).FirstOrDefault();
            if (pending != null)
            {
                return pending;
            }
            var query = db.PaymentConditions.Where(p => p.Days == days && p.Basis == PaymentConditionBasis.InvoiceDate && p.DiscountPercent == null && p.DiscountDays == null)
                .OrderByDescending(p => p.IsActive).ThenBy(p => p.SortOrder).ThenBy(p => p.Id);
            var stored = async ? await query.FirstOrDefaultAsync(cancellationToken) : query.FirstOrDefault();
            if (stored != null)
            {
                return stored;
            }
            // Nieuwe conditie. Is de standaardcode al bezet door een conditie met een andere betekenis, dan "{n}D-2", "{n}D-3", ….
            var code = PaymentConditionRules.DefaultCodeForDays(days);
            for (var suffix = 2; await CodeTakenAsync(db, code, async, cancellationToken); suffix++)
            {
                code = $"{PaymentConditionRules.DefaultCodeForDays(days)}-{suffix}";
            }
            var created = new PaymentCondition
            {
                Code = code,
                CodeKey = PaymentConditionRules.CodeKey(code),
                DescriptionNl = PaymentConditionRules.DefaultDescriptionNl(days),
                DescriptionEn = PaymentConditionRules.DefaultDescriptionEn(days),
                Days = days,
                Basis = PaymentConditionBasis.InvoiceDate,
                IsActive = true,
                SortOrder = days,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.PaymentConditions.Add(created);
            return created;
        }

        private static bool Matches(PaymentCondition p, int days) =>
            p.Days == days && p.Basis == PaymentConditionBasis.InvoiceDate && p.DiscountPercent == null && p.DiscountDays == null;

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
