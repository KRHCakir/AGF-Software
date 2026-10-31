using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Landenlijst en eigen aliassen. Een alias is alleen invoerhulp: opgeslagen en vergeleken wordt altijd het land.
    public sealed class CountryService
    {
        private readonly AppDbContext _db;

        public CountryService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<CountryReferenceDto> ReferenceAsync(CancellationToken cancellationToken = default) => new()
        {
            Countries = (await _db.Countries.AsNoTracking().ToListAsync(cancellationToken))
                .OrderBy(c => c.NameNl, StringComparer.Create(new System.Globalization.CultureInfo("nl-NL"), true))
                .Select(c => new CountryDto { Code = c.Code, NameNl = c.NameNl, NameEn = c.NameEn }).ToList(),
            Aliases = await _db.CountryAliases.AsNoTracking().OrderBy(a => a.Alias)
                .Select(a => new CountryAliasDto { Id = a.Id, Alias = a.Alias, CountryCode = a.CountryCode }).ToListAsync(cancellationToken)
        };

        // Dezelfde alias voor hetzelfde land opnieuw = 200 met de bestaande (idempotent); naar een ander land of de naam/code van een
        // ander land = 409; ongeldig = 400.
        public async Task<ServiceResult<CountryAliasDto>> AddAliasAsync(AddCountryAliasDto request, CancellationToken cancellationToken = default)
        {
            var code = string.IsNullOrWhiteSpace(request?.CountryCode) ? null : request.CountryCode.Trim().ToUpperInvariant();
            var key = CountryRules.Key(request?.Alias);
            var existing = await _db.CountryAliases.AsNoTracking().Select(a => new { a.Id, a.Alias, a.AliasKey, a.CountryCode }).ToListAsync(cancellationToken);
            var same = existing.FirstOrDefault(a => a.AliasKey == key && a.CountryCode == code);
            if (same != null)
            {
                return ServiceResult<CountryAliasDto>.Ok(new CountryAliasDto { Id = same.Id, Alias = same.Alias, CountryCode = same.CountryCode });
            }
            var conflict = CountryRules.AliasConflict(request?.Alias, code, existing.Select(a => (a.AliasKey, a.CountryCode)));
            if (conflict != null)
            {
                return key == null || request.Alias.Trim().Length > CountryRules.MaxAliasLength || !CountryRules.IsCode(code)
                    ? ServiceResult<CountryAliasDto>.Validation(new Dictionary<string, string[]> { [key == null || request.Alias.Trim().Length > CountryRules.MaxAliasLength ? "alias" : "countryCode"] = new[] { conflict } })
                    : ServiceResult<CountryAliasDto>.Conflict(conflict);
            }
            var actor = string.IsNullOrWhiteSpace(request.ActorReference) ? null : request.ActorReference.Trim();
            if (actor != null && actor.Length > PurchaseOrderRules.MaxActorLength) actor = actor[..PurchaseOrderRules.MaxActorLength];
            var alias = new CountryAlias { Alias = request.Alias.Trim(), AliasKey = key, CountryCode = code, ActorReference = actor };
            _db.CountryAliases.Add(alias);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gelijktijdig dezelfde alias: de unieke sleutel wint; opnieuw beoordelen (dan bestaat hij en volgt 200 of 409).
                _db.ChangeTracker.Clear();
                if (!await _db.CountryAliases.AnyAsync(a => a.AliasKey == key, cancellationToken)) throw;
                return await AddAliasAsync(request, cancellationToken);
            }
            return ServiceResult<CountryAliasDto>.Created(new CountryAliasDto { Id = alias.Id, Alias = alias.Alias, CountryCode = alias.CountryCode });
        }

        // Een alias verwijderen raakt geen gegevens: hij is alleen invoerhulp.
        public async Task<ServiceResult<CountryReferenceDto>> RemoveAliasAsync(int id, CancellationToken cancellationToken = default)
        {
            var alias = await _db.CountryAliases.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (alias == null)
            {
                return ServiceResult<CountryReferenceDto>.NotFound($"Alias {id} does not exist.");
            }
            _db.CountryAliases.Remove(alias);
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<CountryReferenceDto>.Ok(await ReferenceAsync(cancellationToken));
        }
    }
}
