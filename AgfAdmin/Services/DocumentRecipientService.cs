using System.Globalization;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Documentontvangers (documenten deel 3): e-mailadressen met een naam, per adres de documentsoorten en Aan of Cc. Van een relatie
    // (eventueel alleen voor één afleveradres) of van de loods. Opslaan vervangt de hele lijst, met de versie die de gebruiker zag. Een
    // bekende tikfout in het domein geeft een waarschuwing met suggestie (zoals bij de relatie), nooit een blokkade.
    public sealed class DocumentRecipientService(AppDbContext db, ICurrentUser user = null)
    {
        private const int RecipientLockClass = 0x41504d31; // eigen lockruimte voor het opslaan van documentontvangers

        private readonly ICurrentUser _user = user ?? CurrentUser.Default;

        public async Task<ServiceResult<DocumentRecipientsDto>> GetForRelationAsync(int relationId, CancellationToken cancellationToken = default)
        {
            var relation = await db.Relations.AsNoTracking().Include(r => r.Addresses).SingleOrDefaultAsync(r => r.Id == relationId, cancellationToken);
            if (relation == null) return ServiceResult<DocumentRecipientsDto>.NotFound($"Relation {relationId} does not exist.");
            return ServiceResult<DocumentRecipientsDto>.Ok(Dto(await StoredAsync(DocumentMailRules.OwnerRelation, relationId, cancellationToken), await VersionAsync(DocumentMailRules.OwnerRelation, relationId, cancellationToken), relation));
        }

        public async Task<DocumentRecipientsDto> GetWarehouseAsync(CancellationToken cancellationToken = default) =>
            Dto(await StoredAsync(DocumentMailRules.OwnerWarehouse, null, cancellationToken), await VersionAsync(DocumentMailRules.OwnerWarehouse, null, cancellationToken), null);

        public async Task<ServiceResult<DocumentRecipientsDto>> SaveForRelationAsync(int relationId, SaveDocumentRecipientsDto request, CancellationToken cancellationToken = default)
        {
            var relation = await db.Relations.AsNoTracking().Include(r => r.Addresses).SingleOrDefaultAsync(r => r.Id == relationId, cancellationToken);
            if (relation == null) return ServiceResult<DocumentRecipientsDto>.NotFound($"Relation {relationId} does not exist.");
            return await SaveAsync(DocumentMailRules.OwnerRelation, relation, request, cancellationToken);
        }

        public Task<ServiceResult<DocumentRecipientsDto>> SaveWarehouseAsync(SaveDocumentRecipientsDto request, CancellationToken cancellationToken = default) =>
            SaveAsync(DocumentMailRules.OwnerWarehouse, null, request, cancellationToken);

        // De ontvangers voor één documentsoort: die van het afleveradres als daar iemand voor deze soort staat, anders die van de relatie
        // (adres leeg). Voor de loods: de loodsadressen.
        public static (List<string> To, List<string> Cc) Resolve(IReadOnlyCollection<DocumentRecipient> all, int? addressId, string kind)
        {
            var flag = DocumentMailRules.Flag(kind);
            var forKind = all.Where(r => (r.Kinds & flag) != 0).ToList();
            var chosen = addressId != null && forKind.Any(r => r.RelationAddressId == addressId)
                ? forKind.Where(r => r.RelationAddressId == addressId).ToList()
                : forKind.Where(r => r.RelationAddressId == null).ToList();
            var ordered = chosen.OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToList();
            var to = ordered.Where(r => r.Role == DocumentMailRules.RoleTo).Select(r => r.Email).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var cc = ordered.Where(r => r.Role == DocumentMailRules.RoleCc).Select(r => r.Email).Where(e => !to.Contains(e, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return (to, cc);
        }

        public async Task<(List<string> To, List<string> Cc)> ResolveAsync(string ownerKind, int? relationId, int? addressId, string kind, CancellationToken cancellationToken = default) =>
            Resolve(await StoredAsync(ownerKind, relationId, cancellationToken), addressId, kind);

        private async Task<ServiceResult<DocumentRecipientsDto>> SaveAsync(string ownerKind, Relation relation, SaveDocumentRecipientsDto request, CancellationToken cancellationToken)
        {
            var relationId = relation?.Id;
            // Versiecontrole en opslaan in één transactie, per eigenaar achter een lock (loods = 0): twee gelijktijdige opslagen worden
            // na elkaar uitgevoerd, de tweede ziet de nieuwe versie en krijgt een 409. SQLite (tests) serialiseert schrijvers zelf.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (db.Database.IsNpgsql())
            {
                var lockKey = relationId ?? 0;
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({RecipientLockClass}, {lockKey})", cancellationToken);
            }
            var stored = await StoredAsync(ownerKind, relationId, cancellationToken, tracking: true);
            var versionRow = await db.DocumentRecipientListVersions.SingleOrDefaultAsync(v => v.OwnerKind == ownerKind && v.OwnerId == (relationId ?? 0), cancellationToken);
            var currentVersion = (versionRow?.Version ?? 0).ToString(CultureInfo.InvariantCulture);
            if (request?.ExpectedVersion != currentVersion)
            {
                return ServiceResult<DocumentRecipientsDto>.Conflict("The e-mail addresses were changed by someone else; reload and try again.", new { code = "stale", version = currentVersion });
            }
            var errors = new Dictionary<string, List<string>>();
            var warnings = new List<RelationWarningDto>();
            var items = request.Recipients ?? new List<DocumentRecipientDto>();
            if (items.Count > DocumentMailRules.MaxRecipientsPerOwner) ValidationErrors.Add(errors, "recipients", $"At most {DocumentMailRules.MaxRecipientsPerOwner} e-mail addresses.");
            var normalized = new List<(DocumentRecipientDto Item, string Email, string Name, string Role, int Kinds)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var ids = new HashSet<int>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i] ?? new DocumentRecipientDto();
                var key = $"recipients[{i}]";
                // Een bestaande regel kan maar één keer in de lijst staan (anders zou de laatste stil de eerste vervangen).
                if (item.Id is int existingId && !ids.Add(existingId)) ValidationErrors.Add(errors, $"{key}.id", "This address is listed twice.");
                var email = (item.Email ?? "").Trim();
                if (email.Length == 0 || email.Length > EmailRules.MaxLength || !EmailRules.IsStrict(email)) ValidationErrors.Add(errors, $"{key}.email", EmailRules.InvalidMessage);
                else if (EmailRules.TypoSuggestion(email) is { } suggestion)
                {
                    warnings.Add(new RelationWarningDto { Code = RelationService.WarningEmailTypo, Field = $"{key}.email", Message = $"Bedoelt u {suggestion}?", Suggestion = suggestion });
                }
                var name = ValidationErrors.OptionalText(item.Name, DocumentMailRules.MaxNameLength, $"{key}.name", errors);
                var role = DocumentMailRules.Roles.FirstOrDefault(r => string.Equals(r, item.Role?.Trim() ?? DocumentMailRules.RoleTo, StringComparison.OrdinalIgnoreCase));
                if (role == null) ValidationErrors.Add(errors, $"{key}.role", "Choose To or Cc.");
                var kinds = 0;
                foreach (var kind in item.Kinds ?? new List<string>())
                {
                    var flag = DocumentMailRules.Flag(kind);
                    if (flag == 0) ValidationErrors.Add(errors, $"{key}.kinds", $"Unknown document kind '{kind}'.");
                    kinds |= flag;
                }
                if (kinds == 0) ValidationErrors.Add(errors, $"{key}.kinds", "Choose at least one document.");
                if (item.RelationAddressId != null)
                {
                    var address = relation?.Addresses.SingleOrDefault(a => a.Id == item.RelationAddressId);
                    var unchanged = stored.Any(r => r.Id == item.Id && r.RelationAddressId == item.RelationAddressId);
                    if (address == null || address.Purpose != AddressPurpose.Delivery || (!address.IsActive && !unchanged)) ValidationErrors.Add(errors, $"{key}.relationAddressId", "Choose one of this relation's active delivery addresses, or the whole relation.");
                }
                if (email.Length > 0 && !seen.Add($"{email}|{item.RelationAddressId}")) ValidationErrors.Add(errors, $"{key}.email", "This address is listed twice.");
                normalized.Add((item, email, name, role, kinds));
            }
            if (errors.Count > 0) return ServiceResult<DocumentRecipientsDto>.Validation(ValidationErrors.ToArrays(errors));

            var now = DateTime.UtcNow;
            var actor = _user.Actor(SalesOrderDocumentRules.MaxActorLength);
            var keep = new HashSet<int>();
            for (var i = 0; i < normalized.Count; i++)
            {
                var (item, email, name, role, kinds) = normalized[i];
                var row = item.Id == null ? null : stored.SingleOrDefault(r => r.Id == item.Id);
                if (row == null)
                {
                    row = new DocumentRecipient { OwnerKind = ownerKind, RelationId = relationId, CreatedAt = now, CreatedBy = actor };
                    db.DocumentRecipients.Add(row);
                }
                else keep.Add(row.Id);
                row.Email = email;
                row.Name = name;
                row.Role = role;
                row.Kinds = kinds;
                row.RelationAddressId = item.RelationAddressId;
                row.SortOrder = i;
                row.UpdatedAt = now;
                row.UpdatedBy = actor;
            }
            db.DocumentRecipients.RemoveRange(stored.Where(r => !keep.Contains(r.Id)));
            // De versie loopt bij elke opslag op en blijft bestaan, ook als de lijst leeg wordt: een eerder gezien nummer komt nooit terug.
            if (versionRow == null) db.DocumentRecipientListVersions.Add(new DocumentRecipientListVersion { OwnerKind = ownerKind, OwnerId = relationId ?? 0, Version = 1 });
            else versionRow.Version++;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var dto = Dto(await StoredAsync(ownerKind, relationId, cancellationToken), await VersionAsync(ownerKind, relationId, cancellationToken), relation);
            dto.Warnings = warnings;
            return ServiceResult<DocumentRecipientsDto>.Ok(dto);
        }

        private async Task<List<DocumentRecipient>> StoredAsync(string ownerKind, int? relationId, CancellationToken cancellationToken, bool tracking = false)
        {
            var query = db.DocumentRecipients.Where(r => r.OwnerKind == ownerKind && r.RelationId == relationId);
            return await (tracking ? query : query.AsNoTracking()).OrderBy(r => r.SortOrder).ThenBy(r => r.Id).ToListAsync(cancellationToken);
        }

        private static DocumentRecipientsDto Dto(List<DocumentRecipient> stored, string version, Relation relation) => new()
        {
            Version = version,
            Recipients = stored.Select(r => new DocumentRecipientDto { Id = r.Id, Name = r.Name, Email = r.Email, Role = r.Role, Kinds = DocumentMailRules.Names(r.Kinds).ToList(), RelationAddressId = r.RelationAddressId }).ToList(),
            Addresses = relation?.Addresses.Where(a => a.Purpose == AddressPurpose.Delivery && (a.IsActive || stored.Any(r => r.RelationAddressId == a.Id))).OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
                .Select(a => new RecipientAddressOptionDto { Id = a.Id, Purpose = a.Purpose, Text = TransportOrderBuilder.AddressText(a) ?? a.Label }).ToList() ?? new List<RecipientAddressOptionDto>(),
        };

        // De versie van een lijst: een blijvende teller per eigenaar (nog nooit opgeslagen = "0"), zodat opslaan een gelijktijdige wijziging herkent.
        private async Task<string> VersionAsync(string ownerKind, int? relationId, CancellationToken cancellationToken)
        {
            var ownerId = relationId ?? 0;
            var version = await db.DocumentRecipientListVersions.AsNoTracking().Where(v => v.OwnerKind == ownerKind && v.OwnerId == ownerId).Select(v => (long?)v.Version).SingleOrDefaultAsync(cancellationToken);
            return (version ?? 0).ToString(CultureInfo.InvariantCulture);
        }
    }
}
