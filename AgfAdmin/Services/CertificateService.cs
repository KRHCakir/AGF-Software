using System.Security.Cryptography;
using System.Text;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Security;
using AgfAdmin.Services.DocumentAi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgfAdmin.Services
{
    // Certificaten deel 1: de certificaten van een relatie of van het eigen bedrijf (relatie leeg), hun bestanden (elke upload een nieuwe
    // versie), de bijna verlopen certificaten en de bedrijfsinstellingen (dagen vooraf, eigen soorten). Opslaan vervangt de hele lijst met
    // de versie die de gebruiker zag, zoals bij de documentontvangers. Wie controleerde en wie uploadde komt uit CurrentUser.
    public sealed partial class CertificateService(AppDbContext db, ICurrentUser user = null, IDocumentStore store = null, IOptions<DocumentAiOptions> options = null)
    {
        private const int CertificateLockClass = 0x43455231; // eigen lockruimte voor het opslaan van certificaten
        private const int SettingsLockClass = 0x43455332; // bedrijfsbrede lock voor de certificaatinstellingen (eigen soorten, dagen vooraf)
        public const string WarningProductsMissing = "products-missing";
        public const string WarningGgnCheckDigit = "ggn-check-digit";

        private readonly ICurrentUser _user = user ?? CurrentUser.Default;
        private long MaxFileBytes => options?.Value.MaxFileBytes ?? new DocumentAiOptions().MaxFileBytes;

        public async Task<CertificateReferenceDto> ReferenceAsync(CancellationToken cancellationToken = default)
        {
            var settings = await SettingsAsync(tracking: false, cancellationToken);
            return new CertificateReferenceDto
            {
                Types = CertificateRules.Types.Select(t => new CertificateTypeDto
                {
                    Code = t.Code, Name = t.Name, Description = t.Description, TypicalHolder = t.TypicalHolder, ProductsRequired = t.ProductsRequired, GgnNumber = t.GgnNumber,
                }).ToList(),
                CustomTypes = CertificateRules.ParseCustomTypes(settings.CustomCertificateTypes).ToList(),
                WarningDays = settings.CertificateWarningDays,
                GlobalGapPortalUrl = settings.GlobalGapPortalUrl,
                GlobalGapDatabaseUrl = RelationRules.GlobalGapDatabaseUrl,
                MaxFileBytes = MaxFileBytes,
            };
        }

        public async Task<CertificateSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            var settings = await SettingsAsync(tracking: false, cancellationToken);
            return new CertificateSettingsDto { WarningDays = settings.CertificateWarningDays, CustomTypes = CertificateRules.ParseCustomTypes(settings.CustomCertificateTypes).ToList() };
        }

        public async Task<ServiceResult<CertificateSettingsDto>> UpdateSettingsAsync(CertificateSettingsDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request == null || request.WarningDays < CertificateRules.MinWarningDays || request.WarningDays > CertificateRules.MaxWarningDays)
            {
                ValidationErrors.Add(errors, "warningDays", $"Choose between {CertificateRules.MinWarningDays} and {CertificateRules.MaxWarningDays} days.");
            }
            var names = CertificateRules.ParseCustomTypes(string.Join("\n", (request?.CustomTypes ?? new List<string>()).Select(n => (n ?? "").Replace('\n', ' '))));
            if (names.Count > CertificateRules.MaxCustomTypes) ValidationErrors.Add(errors, "customTypes", $"At most {CertificateRules.MaxCustomTypes} own certificate types.");
            if (names.Any(n => n.Length > CertificateRules.MaxCustomTypeLength)) ValidationErrors.Add(errors, "customTypes", $"A certificate type has at most {CertificateRules.MaxCustomTypeLength} characters.");
            if (errors.Count > 0) return ServiceResult<CertificateSettingsDto>.Validation(ValidationErrors.ToArrays(errors));
            // Lezen en schrijven achter dezelfde bedrijfsbrede lock als het toevoegen van eigen soorten bij het opslaan van certificaten.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await LockSettingsAsync(cancellationToken);
            var settings = await SettingsAsync(tracking: true, cancellationToken);
            settings.CertificateWarningDays = request!.WarningDays;
            settings.CustomCertificateTypes = CertificateRules.FormatCustomTypes(names);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ServiceResult<CertificateSettingsDto>.Ok(new CertificateSettingsDto { WarningDays = settings.CertificateWarningDays, CustomTypes = names.ToList() });
        }

        public async Task<ServiceResult<CertificateListDto>> GetForRelationAsync(int relationId, CancellationToken cancellationToken = default)
        {
            if (!await db.Relations.AnyAsync(r => r.Id == relationId, cancellationToken)) return ServiceResult<CertificateListDto>.NotFound($"Relation {relationId} does not exist.");
            return ServiceResult<CertificateListDto>.Ok(await ListAsync(relationId, cancellationToken));
        }

        public async Task<CertificateListDto> GetOwnAsync(CancellationToken cancellationToken = default) => await ListAsync(null, cancellationToken);

        public async Task<ServiceResult<CertificateListDto>> SaveForRelationAsync(int relationId, SaveCertificatesDto request, CancellationToken cancellationToken = default)
        {
            if (!await db.Relations.AnyAsync(r => r.Id == relationId, cancellationToken)) return ServiceResult<CertificateListDto>.NotFound($"Relation {relationId} does not exist.");
            return await SaveAsync(relationId, request, cancellationToken);
        }

        public Task<ServiceResult<CertificateListDto>> SaveOwnAsync(SaveCertificatesDto request, CancellationToken cancellationToken = default) => SaveAsync(null, request, cancellationToken);

        private async Task<ServiceResult<CertificateListDto>> SaveAsync(int? relationId, SaveCertificatesDto request, CancellationToken cancellationToken)
        {
            // Versiecontrole en opslaan in één transactie, per eigenaar achter een lock (eigen bedrijf = 0), zoals bij de documentontvangers.
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({CertificateLockClass}, {relationId ?? 0})", cancellationToken);
            }
            var stored = await StoredAsync(relationId, tracking: true, cancellationToken);
            if (request?.ExpectedVersion != VersionOf(stored))
            {
                return ServiceResult<CertificateListDto>.Conflict("The certificates were changed by someone else; reload and try again.", new { code = "stale", version = VersionOf(stored) });
            }
            var today = db.BusinessCalendar.Today();
            var errors = new Dictionary<string, List<string>>();
            var warnings = new List<CertificateWarningDto>();
            var items = request.Certificates ?? new List<SaveCertificateDto>();
            if (items.Count > CertificateRules.MaxCertificatesPerOwner) ValidationErrors.Add(errors, "certificates", $"At most {CertificateRules.MaxCertificatesPerOwner} certificates.");
            var productIds = items.SelectMany(i => i?.ProductIds ?? new List<int>()).Distinct().ToList();
            var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(cancellationToken);
            var seenIds = new HashSet<int>();
            var seenNumbers = new HashSet<string>(StringComparer.Ordinal);
            var normalized = new List<(SaveCertificateDto Item, string Type, string CustomName, string Number, string Body, string Producer, List<int> Products)>();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i] ?? new SaveCertificateDto();
                var key = $"certificates[{i}]";
                if (item.Id is int existingId && !seenIds.Add(existingId)) ValidationErrors.Add(errors, $"{key}.id", "This certificate is listed twice.");
                if (item.Id is int knownId && stored.All(c => c.Id != knownId)) ValidationErrors.Add(errors, $"{key}.id", "This certificate is not in the list; reload and try again.");
                var type = item.TypeCode?.Trim();
                if (!CertificateRules.IsKnownType(type)) ValidationErrors.Add(errors, $"{key}.typeCode", "Choose a certificate type from the list, or an own type.");
                string customName = null;
                if (type == CertificateRules.Custom)
                {
                    customName = ValidationErrors.OptionalText(item.CustomTypeName, CertificateRules.MaxCustomTypeLength, $"{key}.customTypeName", errors);
                    if (customName == null && !errors.ContainsKey($"{key}.customTypeName")) ValidationErrors.Add(errors, $"{key}.customTypeName", "Fill in the name of the own certificate type.");
                }
                var number = CertificateRules.NormalizeNumber(type, item.Number);
                if (number == null) ValidationErrors.Add(errors, $"{key}.number", "Fill in the certificate number.");
                else if (CertificateRules.IsGgnType(type) && !ArticleRules.IsGgn(number))
                {
                    ValidationErrors.Add(errors, $"{key}.number", $"A GGN consists of exactly {ArticleRules.GgnLength} digits (leading zeros included); spaces are allowed as separators. A CoC number is not a GGN.");
                }
                else if (number.Length > CertificateRules.MaxNumberLength) ValidationErrors.Add(errors, $"{key}.number", $"A certificate number has at most {CertificateRules.MaxNumberLength} characters.");
                else
                {
                    if (CertificateRules.IsGgnType(type) && !CertificateRules.HasValidCheckDigit(number))
                    {
                        warnings.Add(new CertificateWarningDto { Code = WarningGgnCheckDigit, Field = $"{key}.number", Message = $"The check digit of GGN {number} does not match; check the number for a typo." });
                    }
                    // Twee certificaten van dezelfde soort mogen (twee GGN's), maar niet twee keer hetzelfde nummer van dezelfde soort.
                    if (!seenNumbers.Add($"{type}|{customName?.ToLowerInvariant()}|{CertificateRules.NumberKey(number)}")) ValidationErrors.Add(errors, $"{key}.number", "This certificate is already in the list.");
                }
                var body = ValidationErrors.OptionalText(item.CertificationBody, CertificateRules.MaxBodyLength, $"{key}.certificationBody", errors);
                var producer = ValidationErrors.OptionalText(item.ProducerName, RelationRules.MaxProducerNameLength, $"{key}.producerName", errors);
                if (item.ValidFrom != null && item.ValidUntil != null && item.ValidFrom > item.ValidUntil) ValidationErrors.Add(errors, $"{key}.validUntil", "The end date is before the start date.");
                if (item.CheckedOn != null && item.CheckedOn > today) ValidationErrors.Add(errors, $"{key}.checkedOn", "The check date cannot be in the future.");
                var ids = (item.ProductIds ?? new List<int>()).Distinct().ToList();
                if (ids.Count > CertificateRules.MaxProductsPerCertificate) ValidationErrors.Add(errors, $"{key}.productIds", $"At most {CertificateRules.MaxProductsPerCertificate} products.");
                if (ids.Any(id => !products.Contains(id))) ValidationErrors.Add(errors, $"{key}.productIds", "Choose existing products.");
                if (type != null && CertificateRules.Find(type)?.ProductsRequired == true && ids.Count == 0)
                {
                    warnings.Add(new CertificateWarningDto { Code = WarningProductsMissing, Field = $"{key}.productIds", Message = "Fill in for which products this GGN is valid; until then the purchase check cannot use it." });
                }
                normalized.Add((item, type, customName, number, body, producer, ids));
            }
            // Een opgeslagen certificaat verdwijnt nooit uit de lijst: archiveren.
            var listed = items.Where(i => i?.Id != null).Select(i => i.Id.Value).ToHashSet();
            foreach (var missing in stored.Where(c => !listed.Contains(c.Id)))
            {
                ValidationErrors.Add(errors, "certificates", $"Certificate {CertificateRules.TypeName(missing.TypeCode, missing.CustomTypeName)} {missing.Number} cannot be removed; archive it instead.");
            }
            // Certificaten deel 2: Goedkeuren van een AI-voorstel in dezelfde opslag.
            var approval = await CertificateAiApproval.CheckAsync(db, request.AiApproval, relationId, items.Count, errors, cancellationToken);
            if (errors.Count > 0) return ServiceResult<CertificateListDto>.Validation(ValidationErrors.ToArrays(errors));

            var now = DateTime.UtcNow;
            var actor = _user.Actor(CertificateRules.MaxActorLength);
            var rows = new List<RelationCertificate>();
            for (var i = 0; i < normalized.Count; i++)
            {
                var (item, type, customName, number, body, producer, ids) = normalized[i];
                var row = item.Id == null ? null : stored.Single(c => c.Id == item.Id);
                if (row == null)
                {
                    row = new RelationCertificate { RelationId = relationId, CreatedAt = now, CreatedBy = actor };
                    db.RelationCertificates.Add(row);
                }
                rows.Add(row);
                // Gecontroleerd door: wie de controledatum nu zet of wijzigt (CurrentUser); een ongewijzigde datum houdt wie het toen deed.
                var checkChanged = row.CheckedOn != item.CheckedOn;
                if (item.CheckedOn == null) row.CheckedBy = null;
                else if (checkChanged) row.CheckedBy = actor;
                row.CheckedOn = item.CheckedOn;
                var changed = row.Id == 0 || checkChanged || row.TypeCode != type || row.CustomTypeName != customName || row.Number != number || row.CertificationBody != body
                    || row.ValidFrom != item.ValidFrom || row.ValidUntil != item.ValidUntil || row.ProducerName != producer || row.HasGgnLabel != item.HasGgnLabel
                    || row.IsActive != item.IsActive || row.SortOrder != i
                    || !row.Products.Select(p => p.ProductId).OrderBy(x => x).SequenceEqual(ids.OrderBy(x => x));
                row.TypeCode = type;
                row.CustomTypeName = customName;
                row.Number = number;
                row.NumberKey = CertificateRules.NumberKey(number);
                row.CertificationBody = body;
                row.ValidFrom = item.ValidFrom;
                row.ValidUntil = item.ValidUntil;
                row.ProducerName = producer;
                row.HasGgnLabel = item.HasGgnLabel;
                row.IsActive = item.IsActive;
                row.SortOrder = i;
                // "Producten nog invullen": een GGN zonder producten (ook de overgenomen oude GGN's), tot er producten zijn.
                row.ProductsPending = CertificateRules.Find(type)?.ProductsRequired == true && ids.Count == 0;
                foreach (var gone in row.Products.Where(p => !ids.Contains(p.ProductId)).ToList()) row.Products.Remove(gone);
                foreach (var id in ids.Where(id => row.Products.All(p => p.ProductId != id))) row.Products.Add(new RelationCertificateProduct { ProductId = id });
                if (changed)
                {
                    row.UpdatedAt = now;
                    row.UpdatedBy = actor;
                }
            }
            await AddCustomTypesAsync(normalized.Where(n => n.Type == CertificateRules.Custom).Select(n => n.CustomName), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            if (approval != null)
            {
                var refused = await CertificateAiApproval.ApplyAsync(db, approval, rows[approval.Index], normalized[approval.Index].Item, actor, now, cancellationToken);
                if (refused != null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    db.ChangeTracker.Clear();
                    return ServiceResult<CertificateListDto>.Conflict(refused, new { code = "aiClosed", message = refused });
                }
                await db.SaveChangesAsync(cancellationToken);
            }
            // Het antwoord is precies wat deze opslag schreef: nog binnen de transactie (en de lock) gelezen.
            var dto = await ListAsync(relationId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            db.ChangeTracker.Clear();
            dto.Warnings = warnings;
            return ServiceResult<CertificateListDto>.Ok(dto);
        }

        private async Task LockSettingsAsync(CancellationToken cancellationToken)
        {
            if (db.Database.IsNpgsql())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({SettingsLockClass}, 0)", cancellationToken);
            }
        }

        // "Eigen soort toevoegen": een nieuwe eigen soort komt in de bedrijfslijst, zodat hij de volgende keer in de keuzelijst staat.
        private async Task AddCustomTypesAsync(IEnumerable<string> names, CancellationToken cancellationToken)
        {
            var wanted = names.Where(n => n != null).ToList();
            if (wanted.Count == 0) return;
            // Bedrijfsbreed: de lock per relatie volstaat niet, want de lijst met eigen soorten is voor alle relaties gedeeld.
            // Volgorde altijd eerst de lock van de eigenaar, dan deze; UpdateSettingsAsync neemt alleen deze, dus geen deadlock.
            await LockSettingsAsync(cancellationToken);
            var settings = await SettingsAsync(tracking: true, cancellationToken);
            var list = CertificateRules.ParseCustomTypes(settings.CustomCertificateTypes).ToList();
            foreach (var name in wanted)
            {
                if (list.Count < CertificateRules.MaxCustomTypes && !list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
            }
            settings.CustomCertificateTypes = CertificateRules.FormatCustomTypes(list);
        }

        // ---- bestanden ----

        public async Task<ServiceResult<CertificateFileDto>> UploadAsync(int certificateId, string fileName, byte[] bytes, CancellationToken cancellationToken = default)
        {
            if (store == null) throw new InvalidOperationException("No document store configured.");
            if (!await db.RelationCertificates.AnyAsync(c => c.Id == certificateId, cancellationToken)) return ServiceResult<CertificateFileDto>.NotFound($"Certificate {certificateId} does not exist.");
            var (document, error) = UploadedDocuments.Prepare(fileName, bytes, MaxFileBytes);
            if (document == null)
            {
                return ServiceResult<CertificateFileDto>.Validation(new Dictionary<string, string[]> { ["file"] = new[] { UploadMessage(error) } });
            }
            var saved = new List<string>();
            try
            {
                var key = await store.SaveAsync(document.Original, document.Extension, cancellationToken);
                saved.Add(key);
                string displayKey = null;
                if (document.Display != null)
                {
                    displayKey = await store.SaveAsync(document.Display, ".jpg", cancellationToken);
                    saved.Add(displayKey);
                }
                var name = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "certificaat" + document.Extension : fileName.Trim());
                // Volgende versie: na een gelijktijdige upload weigert de unieke index (certificaat, versie); dan opnieuw met de volgende.
                for (var attempt = 1; ; attempt++)
                {
                    var version = (await db.RelationCertificateFiles.Where(f => f.RelationCertificateId == certificateId).Select(f => (int?)f.Version).MaxAsync(cancellationToken) ?? 0) + 1;
                    var file = new RelationCertificateFile
                    {
                        RelationCertificateId = certificateId, Version = version, FileName = name.Length > 255 ? name[^255..] : name, MediaType = document.MediaType,
                        SizeBytes = document.Original.LongLength, Sha256 = document.Sha256, StorageKey = key, DisplayStorageKey = displayKey, DisplayMediaType = document.DisplayMediaType,
                        CreatedAt = DateTime.UtcNow, CreatedBy = _user.Actor(CertificateRules.MaxActorLength),
                    };
                    db.RelationCertificateFiles.Add(file);
                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        saved.Clear();
                        return ServiceResult<CertificateFileDto>.Created(FileDto(file));
                    }
                    catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception) && attempt < 5)
                    {
                        db.Entry(file).State = EntityState.Detached;
                    }
                }
            }
            finally
            {
                // Niet opgeslagen (fout of afgebroken): de bestanden zonder record weer weg.
                foreach (var key in saved) store.Delete(key);
            }
        }

        public async Task<(byte[] Bytes, string MediaType, string FileName)?> FileAsync(int certificateId, int fileId, CancellationToken cancellationToken = default)
        {
            if (store == null) return null;
            var file = await db.RelationCertificateFiles.AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId && f.RelationCertificateId == certificateId, cancellationToken);
            if (file == null) return null;
            // Met een displayversie (bv. HEIC als JPEG) past de extensie van de bestandsnaam bij de aangeboden inhoud.
            var fileName = file.DisplayStorageKey != null && file.DisplayMediaType == "image/jpeg" ? Path.ChangeExtension(file.FileName, ".jpg") : file.FileName;
            return (await store.ReadAsync(file.DisplayStorageKey ?? file.StorageKey, cancellationToken), file.DisplayMediaType ?? file.MediaType, fileName);
        }

        private static string UploadMessage(string error) => error switch
        {
            UploadedDocuments.Empty => "The file is empty.",
            UploadedDocuments.TooLarge => "The file is too large.",
            UploadedDocuments.Unsupported => "Upload a PDF or a photo (JPEG, PNG or HEIC).",
            UploadedDocuments.Encrypted => "This PDF is protected with a password; upload a version without protection.",
            _ => "The file cannot be read.",
        };

        // Certificaten deel 2: het eigen CoC-nummer in de koptekst van de laadbon en de transportopdracht. Gekozen: de soort GLOBALG.A.P.
        // Chain of Custody (CoC) bij de eigen certificaten, het eerste actieve certificaat in de volgorde van de lijst dat op die dag niet
        // verlopen is. Geen CoC (of alleen verlopen of gearchiveerd): niets in de koptekst.
        public static async Task<string> OwnCocNumberAsync(AppDbContext db, DateOnly today, CancellationToken cancellationToken) =>
            await db.RelationCertificates.AsNoTracking()
                .Where(c => c.RelationId == null && c.IsActive && c.TypeCode == CertificateRules.GlobalGapCoc && (c.ValidUntil == null || c.ValidUntil >= today))
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(c => c.Number).FirstOrDefaultAsync(cancellationToken);

        public static async Task<int> WarningDaysAsync(AppDbContext db, CancellationToken cancellationToken) =>
            await db.CompanySettings.AsNoTracking().OrderBy(c => c.Id).Select(c => (int?)c.CertificateWarningDays).FirstOrDefaultAsync(cancellationToken) ?? CertificateRules.DefaultWarningDays;

        // ---- intern ----

        private async Task<CertificateListDto> ListAsync(int? relationId, CancellationToken cancellationToken)
        {
            var stored = await StoredAsync(relationId, tracking: false, cancellationToken);
            var warningDays = await WarningDaysAsync(db, cancellationToken);
            var today = db.BusinessCalendar.Today();
            var productIds = stored.SelectMany(c => c.Products.Select(p => p.ProductId)).Distinct().ToList();
            var names = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
            return new CertificateListDto
            {
                Version = VersionOf(stored),
                WarningDays = warningDays,
                Certificates = stored.Select(c => new CertificateDto
                {
                    Id = c.Id, TypeCode = c.TypeCode, CustomTypeName = c.CustomTypeName, TypeName = CertificateRules.TypeName(c.TypeCode, c.CustomTypeName), Number = c.Number,
                    CertificationBody = c.CertificationBody, ValidFrom = c.ValidFrom, ValidUntil = c.ValidUntil, ProductsPending = c.ProductsPending, ProducerName = c.ProducerName,
                    HasGgnLabel = c.HasGgnLabel, CheckedOn = c.CheckedOn, CheckedBy = c.CheckedBy, IsActive = c.IsActive,
                    Status = CertificateRules.Status(c.ValidUntil, today, warningDays), DaysLeft = c.ValidUntil is DateOnly until ? CertificateRules.DaysLeft(until, today) : null,
                    Products = c.Products.Select(p => new CertificateProductDto { Id = p.ProductId, Name = names.GetValueOrDefault(p.ProductId) }).OrderBy(p => p.Name).ToList(),
                    Files = c.Files.OrderByDescending(f => f.Version).Select(FileDto).ToList(),
                }).ToList(),
            };
        }

        private async Task<List<RelationCertificate>> StoredAsync(int? relationId, bool tracking, CancellationToken cancellationToken)
        {
            var query = db.RelationCertificates.Include(c => c.Products).Include(c => c.Files).Where(c => c.RelationId == relationId);
            // Voor het tonen ook de goedgekeurde AI-lezing per bestandsversie (wie, wanneer, model); bij opslaan niet nodig.
            if (!tracking) query = query.Include(c => c.Files).ThenInclude(f => f.AiExtraction);
            return await (tracking ? query : query.AsNoTracking()).AsSplitQuery().OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(cancellationToken);
        }

        private async Task<CompanySettings> SettingsAsync(bool tracking, CancellationToken cancellationToken)
        {
            var query = tracking ? db.CompanySettings : db.CompanySettings.AsNoTracking();
            var settings = await query.OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
            if (settings != null) return settings;
            settings = new CompanySettings();
            if (!tracking) return settings;
            db.CompanySettings.Add(settings);
            return settings;
        }

        // Het aantal aangepaste velden; onleesbaar (zou niet mogen) telt als 0, zodat de lijst altijd te tonen blijft.
        private static int AdjustedCount(string json)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            try { return System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonArray changed ? changed.Count : 0; }
            catch (System.Text.Json.JsonException) { return 0; }
        }

        private static CertificateFileDto FileDto(RelationCertificateFile f) => new()
        {
            Id = f.Id, Version = f.Version, FileName = f.FileName, MediaType = f.MediaType, SizeBytes = f.SizeBytes, Sha256 = f.Sha256,
            CreatedAt = DateTime.SpecifyKind(f.CreatedAt, DateTimeKind.Utc), CreatedBy = f.CreatedBy,
            Ai = f.AiExtraction is { ApprovedAt: DateTime approvedAt } ai ? new CertificateFileAiDto
            {
                ApprovedBy = ai.ApprovedBy, ApprovedAt = DateTime.SpecifyKind(approvedAt, DateTimeKind.Utc), Model = ai.Model, ModelName = AiModelNames.Display(ai.Model),
                ModelVersion = ai.ModelVersion, AdjustedFields = AdjustedCount(ai.AdjustmentsJson),
            } : null,
        };

        // De versie van een lijst: een korte vingerafdruk van wat er staat (leeg = "0"); bestanden tellen niet mee (die komen er los bij).
        public static string VersionOf(IReadOnlyCollection<RelationCertificate> stored)
        {
            if (stored.Count == 0) return "0";
            var text = string.Join("\n", stored.OrderBy(c => c.Id).Select(c =>
                $"{c.Id}|{c.TypeCode}|{c.CustomTypeName}|{c.Number}|{c.CertificationBody}|{c.ValidFrom}|{c.ValidUntil}|{c.ProducerName}|{c.HasGgnLabel}|{c.CheckedOn}|{c.CheckedBy}|{c.IsActive}|{c.SortOrder}|"
                + string.Join(",", c.Products.Select(p => p.ProductId).OrderBy(id => id))));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();
        }
    }
}
