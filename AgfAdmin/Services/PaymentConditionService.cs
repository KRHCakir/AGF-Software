using System.Text.Json;
using System.Text.Json.Serialization;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Stamgegevens betalingscondities. Code hoofdletterongevoelig uniek; idempotent aanmaken op RequestId (zoals relaties); bijwerken met
    // verwachte versie; verwijderen alleen zolang geen rol de conditie gebruikt (anders 409: zet inactief). Een gewijzigd aantal dagen
    // wordt in dezelfde save doorgezet naar de PaymentTermsDays-spiegel van alle rollen met deze conditie (regel 16); bestaande
    // documenten (inkoopfacturen) houden hun eigen opgeslagen termijn en vervaldatum.
    public sealed class PaymentConditionService
    {
        public const string InUseCode = "payment-condition-in-use";

        private static readonly JsonSerializerOptions Canonical = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        private readonly AppDbContext _db;

        public PaymentConditionService(AppDbContext db) => _db = db;

        private sealed record Command(string Code, string DescriptionNl, string DescriptionEn, int Days, string Basis, decimal? DiscountPercent, int? DiscountDays, bool IsActive, int SortOrder);

        public async Task<IReadOnlyList<PaymentConditionDto>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
        {
            var conditions = await _db.PaymentConditions.AsNoTracking().Where(p => includeInactive || p.IsActive)
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Code).ToListAsync(cancellationToken);
            var usage = await UsageAsync(conditions.Select(p => p.Id).ToList(), cancellationToken);
            return conditions.Select(p => ToDto(p, usage)).ToList();
        }

        public async Task<ServiceResult<PaymentConditionDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var condition = await _db.PaymentConditions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            return condition == null
                ? ServiceResult<PaymentConditionDto>.NotFound($"Betalingsconditie {id} bestaat niet.")
                : ServiceResult<PaymentConditionDto>.Ok(ToDto(condition, await UsageAsync(new List<int> { id }, cancellationToken)));
        }

        public async Task<ServiceResult<PaymentConditionDto>> CreateAsync(SavePaymentConditionDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request?.RequestId == null || request.RequestId == Guid.Empty)
            {
                ValidationErrors.Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }
            var existing = request?.RequestId is { } requestId && requestId != Guid.Empty
                ? await _db.PaymentConditions.AsNoTracking().SingleOrDefaultAsync(p => p.RequestId == requestId, cancellationToken)
                : null;
            var command = await NormalizeAsync(request, null, errors, existing == null, existing?.Id, cancellationToken);
            if (existing != null)
            {
                return errors.Count == 0 && string.Equals(existing.RequestPayload, JsonSerializer.Serialize(command, Canonical), StringComparison.Ordinal)
                    ? ServiceResult<PaymentConditionDto>.Ok(ToDto(existing, await UsageAsync(new List<int> { existing.Id }, cancellationToken)))
                    : ServiceResult<PaymentConditionDto>.Conflict($"RequestId {request.RequestId} was already used for a different payment condition.");
            }
            if (errors.Count > 0)
            {
                return ServiceResult<PaymentConditionDto>.Validation(ValidationErrors.ToArrays(errors));
            }
            var condition = new PaymentCondition { RequestId = request.RequestId, RequestPayload = JsonSerializer.Serialize(command, Canonical), CreatedAt = DateTime.UtcNow };
            Apply(condition, command);
            _db.PaymentConditions.Add(condition);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                _db.ChangeTracker.Clear();
                var concurrent = await _db.PaymentConditions.AsNoTracking().SingleOrDefaultAsync(p => p.RequestId == request.RequestId, cancellationToken);
                if (concurrent != null && string.Equals(concurrent.RequestPayload, condition.RequestPayload, StringComparison.Ordinal))
                {
                    return ServiceResult<PaymentConditionDto>.Ok(ToDto(concurrent, await UsageAsync(new List<int> { concurrent.Id }, cancellationToken)));
                }
                return ServiceResult<PaymentConditionDto>.Conflict($"Code '{command.Code}' is intussen door een andere betalingsconditie in gebruik genomen; kies een andere code.");
            }
            return ServiceResult<PaymentConditionDto>.Created(ToDto(condition, await UsageAsync(new List<int> { condition.Id }, cancellationToken)));
        }

        public async Task<ServiceResult<PaymentConditionDto>> UpdateAsync(int id, SavePaymentConditionDto request, CancellationToken cancellationToken = default)
        {
            var condition = await _db.PaymentConditions.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (condition == null)
            {
                return ServiceResult<PaymentConditionDto>.NotFound($"Betalingsconditie {id} bestaat niet.");
            }
            var errors = new Dictionary<string, List<string>>();
            if (request?.ExpectedVersion == null)
            {
                ValidationErrors.Add(errors, "expectedVersion", "The payment condition version the change is based on is required.");
            }
            var command = await NormalizeAsync(request, condition, errors, true, id, cancellationToken);
            if (errors.Count > 0)
            {
                return ServiceResult<PaymentConditionDto>.Validation(ValidationErrors.ToArrays(errors));
            }
            if (condition.Version != request.ExpectedVersion)
            {
                return ServiceResult<PaymentConditionDto>.Conflict($"Betalingsconditie {id} is intussen gewijzigd (versie {condition.Version}, verwacht {request.ExpectedVersion}); laad opnieuw en pas de wijziging opnieuw toe.");
            }
            if (condition.Days != command.Days)
            {
                // Spiegel van alle rollen met deze conditie in dezelfde save (regel 16).
                foreach (var customer in await _db.Customers.Where(c => c.PaymentConditionId == id).ToListAsync(cancellationToken)) customer.PaymentTermsDays = command.Days;
                foreach (var vendor in await _db.Vendors.Where(v => v.PaymentConditionId == id).ToListAsync(cancellationToken)) vendor.PaymentTermsDays = command.Days;
            }
            Apply(condition, command);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<PaymentConditionDto>.Conflict($"Betalingsconditie {id} is intussen gewijzigd; laad opnieuw en pas de wijziging opnieuw toe.");
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                return ServiceResult<PaymentConditionDto>.Conflict($"Code '{command.Code}' is intussen door een andere betalingsconditie in gebruik genomen; kies een andere code.");
            }
            return ServiceResult<PaymentConditionDto>.Ok(ToDto(condition, await UsageAsync(new List<int> { id }, cancellationToken)));
        }

        // Verwijderen alleen als geen klant- of leveranciersrol de conditie gebruikt; anders 409 met het advies om haar inactief te zetten.
        public async Task<ServiceResult<bool>> DeleteAsync(int id, CancellationToken cancellationToken = default)
        {
            var condition = await _db.PaymentConditions.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (condition == null)
            {
                return ServiceResult<bool>.NotFound($"Betalingsconditie {id} bestaat niet.");
            }
            var customers = await _db.Customers.CountAsync(c => c.PaymentConditionId == id, cancellationToken);
            var vendors = await _db.Vendors.CountAsync(v => v.PaymentConditionId == id, cancellationToken);
            if (customers + vendors > 0)
            {
                var message = $"Betalingsconditie {condition.Code} is in gebruik bij {customers} klant(en) en {vendors} leverancier(s) en kan niet worden verwijderd. Zet haar inactief in plaats van verwijderen.";
                return ServiceResult<bool>.Conflict(message, new { message, code = InUseCode, customerCount = customers, vendorCount = vendors });
            }
            _db.PaymentConditions.Remove(condition);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gelijktijdig in gebruik genomen: de FK (Restrict) hield de verwijdering tegen.
                var message = $"Betalingsconditie {condition.Code} is intussen in gebruik genomen en kan niet worden verwijderd. Zet haar inactief in plaats van verwijderen.";
                return ServiceResult<bool>.Conflict(message, new { message, code = InUseCode });
            }
            return ServiceResult<bool>.Ok(true);
        }

        private async Task<Command> NormalizeAsync(SavePaymentConditionDto request, PaymentCondition existing, Dictionary<string, List<string>> errors, bool checkCodeAvailability, int? selfId, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
                return null;
            }
            var code = ValidationErrors.OptionalText(request.Code, PaymentConditionRules.MaxCodeLength + 64, "code", errors);
            var nl = ValidationErrors.OptionalText(request.DescriptionNl, PaymentConditionRules.MaxDescriptionLength, "descriptionNl", errors);
            var en = ValidationErrors.OptionalText(request.DescriptionEn, PaymentConditionRules.MaxDescriptionLength, "descriptionEn", errors);
            var basis = string.IsNullOrWhiteSpace(request.Basis) ? null : request.Basis.Trim();
            PaymentConditionRules.Validate(code, nl, en, request.Days, basis, request.DiscountPercent, request.DiscountDays, errors);
            if (code != null && PaymentConditionRules.IsCode(code) && checkCodeAvailability)
            {
                var key = PaymentConditionRules.CodeKey(code);
                var taken = await _db.PaymentConditions.AsNoTracking().Where(p => p.CodeKey == key && (selfId == null || p.Id != selfId)).Select(p => new { p.Id, p.Code }).FirstOrDefaultAsync(cancellationToken);
                if (taken != null)
                {
                    ValidationErrors.Add(errors, "code", $"Code '{code}' wordt al gebruikt door betalingsconditie {taken.Code} (#{taken.Id}); codes zijn uniek ongeacht hoofdletters.");
                }
            }
            if (request.SortOrder is < 0 or > 100_000) ValidationErrors.Add(errors, "sortOrder", "Sorteervolgorde ligt tussen 0 en 100000.");
            return errors.Count > 0 ? null : new Command(code, nl, en, request.Days!.Value, basis, request.DiscountPercent, request.DiscountDays,
                request.IsActive ?? existing?.IsActive ?? true, request.SortOrder ?? existing?.SortOrder ?? request.Days!.Value);
        }

        private static void Apply(PaymentCondition condition, Command command)
        {
            condition.Code = command.Code;
            condition.CodeKey = PaymentConditionRules.CodeKey(command.Code);
            condition.DescriptionNl = command.DescriptionNl;
            condition.DescriptionEn = command.DescriptionEn;
            condition.Days = command.Days;
            condition.Basis = command.Basis;
            condition.DiscountPercent = command.DiscountPercent;
            condition.DiscountDays = command.DiscountDays;
            condition.IsActive = command.IsActive;
            condition.SortOrder = command.SortOrder;
            condition.UpdatedAt = DateTime.UtcNow;
        }

        private async Task<Dictionary<int, (int Customers, int Vendors)>> UsageAsync(List<int> ids, CancellationToken cancellationToken)
        {
            var customers = await _db.Customers.AsNoTracking().Where(c => ids.Contains(c.PaymentConditionId)).GroupBy(c => c.PaymentConditionId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            var vendors = await _db.Vendors.AsNoTracking().Where(v => ids.Contains(v.PaymentConditionId)).GroupBy(v => v.PaymentConditionId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
            return ids.ToDictionary(id => id, id => (customers.FirstOrDefault(c => c.Key == id)?.Count ?? 0, vendors.FirstOrDefault(v => v.Key == id)?.Count ?? 0));
        }

        private static PaymentConditionDto ToDto(PaymentCondition p, Dictionary<int, (int Customers, int Vendors)> usage) => new()
        {
            Id = p.Id, Code = p.Code, DescriptionNl = p.DescriptionNl, DescriptionEn = p.DescriptionEn, Days = p.Days, Basis = p.Basis,
            DiscountPercent = p.DiscountPercent, DiscountDays = p.DiscountDays, IsActive = p.IsActive, SortOrder = p.SortOrder, Version = p.Version,
            CustomerCount = usage.TryGetValue(p.Id, out var u) ? u.Customers : 0, VendorCount = usage.TryGetValue(p.Id, out var w) ? w.Vendors : 0
        };
    }
}
