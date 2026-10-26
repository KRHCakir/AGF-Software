using System.Text.Json;
using System.Text.Json.Serialization;
using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Services
{
    // Relatiewerkplek: de enige schrijver van gedeelde bedrijfsgegevens (Relation + adressen + contacten) en van de spiegelkolommen
    // op Customer/Vendor. Rollen houden hun bestaande ID's; een relatie krijgt een tweede rol zonder tweede onderneming.
    // Idempotent aanmaken op RequestId (canonieke payload), bijwerken met verwachte versie (409 bij verouderde versie), nooit
    // automatisch samenvoegen; inactief in plaats van verwijderen zodra documenten naar een rol verwijzen.
    public sealed class RelationService
    {
        public const string SimilarNamesCode = "similar-names";
        public const string RoleCustomer = "customer";
        public const string RoleVendor = "vendor";
        public const int DefaultCustomerPaymentTermsDays = 30;
        public const int DefaultVendorPaymentTermsDays = 30;
        public const string DefaultVendorCurrency = "EUR";
        public const string DefaultCustomerCurrency = "EUR";

        private static readonly JsonSerializerOptions Canonical = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public const string BankAccountsMissingCode = "bank-account-omitted";
        public const int MaxBankAccountEvents = 50;
        public const int MaxActorLength = 100;

        // Waarschuwingscodes (RelationWarningDto.Code); de frontend vertaalt op code.
        public const string WarningEmailTypo = "email-typo";
        public const string WarningEmailNoMailServer = "email-no-mail-server";
        public const string WarningEmailDomainUnchecked = "email-domain-unchecked";
        public const string WarningDefaultBankAccountUnconfirmed = "default-bank-account-unconfirmed";
        public const string WarningInsuranceExpired = "insurance-expired";
        public const string WarningInsuranceExpiresSoon = "insurance-expires-soon";
        public const string WarningInsuranceGap = "insurance-gap";
        public const string WarningPaymentConditionInactive = "payment-condition-inactive";

        private readonly AppDbContext _db;
        private readonly IEmailDomainChecker _emailDomains;

        public RelationService(AppDbContext db, IEmailDomainChecker emailDomains = null)
        {
            _db = db;
            _emailDomains = emailDomains ?? NullEmailDomainChecker.Instance;
        }

        // Verzameling van niet-blokkerende signalen tijdens één opslag: waarschuwingen en te controleren e-maildomeinen (MX).
        private sealed class SaveSignals
        {
            public List<RelationWarningDto> Warnings { get; } = new();
            public List<(string Field, string Domain)> Domains { get; } = new();
        }

        // ---------------- Lezen ----------------

        public async Task<RelationListDto> ListAsync(string role, string query, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            page = Math.Max(1, page);
            pageSize = pageSize <= 0 ? RelationRules.DefaultPageSize : Math.Min(pageSize, RelationRules.MaxPageSize);
            var relations = _db.Relations.AsNoTracking().Include(r => r.Customer).Include(r => r.Vendor).AsQueryable();
            if (string.Equals(role, RoleCustomer, StringComparison.OrdinalIgnoreCase)) relations = relations.Where(r => r.Customer != null);
            else if (string.Equals(role, RoleVendor, StringComparison.OrdinalIgnoreCase)) relations = relations.Where(r => r.Vendor != null);
            if (!includeInactive) relations = relations.Where(r => r.IsActive);
            var term = (query ?? "").Trim().ToLowerInvariant();
            if (term != "")
            {
                relations = relations.Where(r => r.Name.ToLower().Contains(term)
                    || (r.TradeName != null && r.TradeName.ToLower().Contains(term))
                    || (r.SearchCodeKey != null && r.SearchCodeKey.Contains(term))
                    || (r.VatNumber != null && r.VatNumber.ToLower().Contains(term))
                    || (r.Email != null && r.Email.ToLower().Contains(term))
                    || (r.Phone != null && r.Phone.Contains(term))
                    || r.Addresses.Any(a => a.City != null && a.City.ToLower().Contains(term))
                    || r.Contacts.Any(c => c.Name.ToLower().Contains(term) || c.Channels.Any(ch => ch.Value.ToLower().Contains(term))));
            }
            var total = await relations.CountAsync(cancellationToken);
            // De server bepaalt de definitieve resultaten en hun volgorde: een exacte zoekcode eerst, daarna op naam. De werkplek filtert niet opnieuw.
            var pageItems = await relations
                .OrderByDescending(r => term != "" && r.SearchCodeKey == term).ThenBy(r => r.Name).ThenBy(r => r.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(r => new
                {
                    r.Id, r.SearchCode, r.Name, r.TradeName, r.IsActive, r.Email, r.Phone, r.VatNumber, r.Version,
                    CustomerId = r.Customer == null ? (int?)null : r.Customer.Id,
                    VendorId = r.Vendor == null ? (int?)null : r.Vendor.Id,
                    Main = r.Addresses.Where(a => a.IsActive).OrderByDescending(a => a.Purpose == AddressPurpose.Main).ThenByDescending(a => a.IsDefault).ThenBy(a => a.SortOrder).Select(a => new { a.City, a.CountryCode }).FirstOrDefault(),
                    PrimaryContact = r.Contacts.Where(c => c.IsActive).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.SortOrder).Select(c => c.Name).FirstOrDefault()
                })
                .ToListAsync(cancellationToken);
            return new RelationListDto
            {
                Total = total,
                Page = page,
                PageSize = pageSize,
                Items = pageItems.Select(r => new RelationListItemDto
                {
                    Id = r.Id, SearchCode = r.SearchCode, Name = r.Name, TradeName = r.TradeName, IsActive = r.IsActive, Email = r.Email, Phone = r.Phone,
                    VatNumber = r.VatNumber, Version = r.Version, CustomerId = r.CustomerId, VendorId = r.VendorId,
                    City = r.Main?.City, CountryCode = r.Main?.CountryCode, PrimaryContactName = r.PrimaryContact
                }).ToList()
            };
        }

        public async Task<ServiceResult<RelationDto>> GetAsync(int id, CancellationToken cancellationToken = default)
        {
            var relation = await Query(tracking: false).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
            return relation == null ? ServiceResult<RelationDto>.NotFound($"Relation {id} does not exist.") : ServiceResult<RelationDto>.Ok(await ToDtoAsync(relation, cancellationToken));
        }

        public async Task<RelationReferenceDto> ReferenceAsync(CancellationToken cancellationToken = default) => new()
        {
            Languages = RelationRules.Languages.ToList(),
            AddressPurposes = AddressPurpose.All.ToList(),
            DepartmentSuggestions = RelationRules.DepartmentSuggestions.ToList(),
            RegistrationTypeSuggestions = RelationRules.RegistrationTypeSuggestions.ToList(),
            LegalFormSuggestions = RelationRules.LegalFormSuggestions.ToList(),
            Currencies = RelationRules.Currencies.ToList(),
            VatVerificationStatuses = VatVerification.All.ToList(),
            GlobalGapDatabaseUrl = RelationRules.GlobalGapDatabaseUrl,
            GlobalGapPortalUrl = (await _db.CompanySettings.AsNoTracking().OrderBy(c => c.Id).Select(c => c.GlobalGapPortalUrl).FirstOrDefaultAsync(cancellationToken)) ?? RelationRules.DefaultGlobalGapPortalUrl,
            PaymentMethods = Models.PaymentMethods.All.ToList(),
            PaymentConditionBases = PaymentConditionBasis.All.ToList(),
            DeliveryTermKinds = Models.DeliveryTermKinds.All.ToList(),
            Incoterms = RoleTermsRules.Incoterms2020.ToList(),
            TradeConditions = TradeConditionKinds.All.ToList(),
            CreditInsurers = Models.CreditInsurers.All.ToList(),
            // Oude artikelstandaarden (vóór de verplaatsing naar leveranciers): blijven bewaard, worden handmatig toegewezen.
            LegacyArticleGgns = (await _db.ProductVariants.AsNoTracking().Include(v => v.Product).Where(v => v.DefaultGgn != null).OrderBy(v => v.Id).ToListAsync(cancellationToken))
                .Select(v => new LegacyArticleGgnDto { ProductVariantId = v.Id, Description = ArticleService.Describe(v), Ggn = v.DefaultGgn }).ToList()
        };

        // ---------------- Aanmaken en bijwerken ----------------

        public async Task<ServiceResult<RelationDto>> CreateAsync(SaveRelationDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            if (request?.RequestId == null || request.RequestId == Guid.Empty)
            {
                ValidationErrors.Add(errors, "requestId", "A client-generated, non-empty requestId is required.");
            }
            // Replay eerst: een identieke herhaling mag niet stuklopen op haar eigen zoekcode of een inmiddels gelijkende naam.
            var existing = request?.RequestId is { } requestId && requestId != Guid.Empty
                ? await Query(tracking: false).SingleOrDefaultAsync(r => r.RequestId == requestId, cancellationToken)
                : null;
            // Bij een bekende requestId telt alleen de vergelijking met de oorspronkelijke opdracht: de actuele beschikbaarheid van de
            // zoekcode (inmiddels door een andere relatie gebruikt) mag een ongewijzigde herhaling nooit laten stuklopen.
            var signals = new SaveSignals();
            var command = await NormalizeAsync(request, null, errors, signals, cancellationToken, existing?.Id, checkSearchCodeAvailability: existing == null);
            if (existing != null)
            {
                if (errors.Count > 0)
                {
                    return ServiceResult<RelationDto>.Conflict($"RequestId {request.RequestId} was already used for a different relation.");
                }
                return await ResultForRetryAsync(existing, JsonSerializer.Serialize(command, Canonical), request.RequestId!.Value, cancellationToken);
            }
            if (errors.Count > 0)
            {
                return ServiceResult<RelationDto>.Validation(ValidationErrors.ToArrays(errors));
            }

            var payload = JsonSerializer.Serialize(command, Canonical);

            if (!request.AcknowledgeSimilarNames)
            {
                var similar = await SimilarAsync(command.Name, null, cancellationToken);
                if (similar.Count > 0)
                {
                    var message = $"A relation with a similar name already exists ({similar.Count}); confirm explicitly to create another one, or add the missing role to the existing relation.";
                    return ServiceResult<RelationDto>.Conflict(message, new { message, code = SimilarNamesCode, similarRelations = similar });
                }
            }

            var conditions = await ResolveConditionsAsync(command, cancellationToken);
            await CheckEmailDomainsAsync(signals, cancellationToken);
            var relation = new Relation { RequestId = request.RequestId, RequestPayload = payload, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            Apply(relation, command, conditions);
            _db.Relations.Add(relation);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                _db.ChangeTracker.Clear();
                var concurrent = await Query(tracking: false).SingleOrDefaultAsync(r => r.RequestId == request.RequestId, cancellationToken);
                if (concurrent != null)
                {
                    return await ResultForRetryAsync(concurrent, payload, request.RequestId!.Value, cancellationToken);
                }
                return ServiceResult<RelationDto>.Conflict($"Search code '{command.SearchCode}' was taken by a concurrent change; choose another code.");
            }
            var created = await ToDtoAsync(await Query(tracking: false).SingleAsync(r => r.Id == relation.Id, cancellationToken), cancellationToken);
            created.Warnings.InsertRange(0, signals.Warnings);
            return ServiceResult<RelationDto>.Created(created);
        }

        public async Task<ServiceResult<RelationDto>> UpdateAsync(int id, SaveRelationDto request, CancellationToken cancellationToken = default)
        {
            var relation = await Query(tracking: true).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (relation == null)
            {
                return ServiceResult<RelationDto>.NotFound($"Relation {id} does not exist.");
            }
            var errors = new Dictionary<string, List<string>>();
            if (request?.ExpectedVersion == null)
            {
                ValidationErrors.Add(errors, "expectedVersion", "The relation version the change is based on is required.");
            }
            var signals = new SaveSignals();
            var command = await NormalizeAsync(request, relation, errors, signals, cancellationToken);
            if (errors.Count > 0)
            {
                return ServiceResult<RelationDto>.Validation(ValidationErrors.ToArrays(errors));
            }
            if (relation.Version != request.ExpectedVersion)
            {
                return ServiceResult<RelationDto>.Conflict($"Relation {id} was changed by someone else (version {relation.Version}, expected {request.ExpectedVersion}); reload and apply your change again.");
            }
            var conditions = await ResolveConditionsAsync(command, cancellationToken);
            await CheckEmailDomainsAsync(signals, cancellationToken);
            var previousDefaults = relation.BankAccounts.Where(a => a.IsDefault && a.IsActive).Select(a => a.Id).ToHashSet();
            Apply(relation, command, conditions);
            try
            {
                // Hooguit één actieve standaardrekening (gefilterde unieke index, per statement gecontroleerd): verhuist de standaard
                // naar een andere opgeslagen rekening, dan eerst de oude uitzetten en pas daarna de nieuwe aanzetten, in één transactie.
                // De relatieversie stijgt maar één keer (de tweede save raakt alleen de rekening).
                var newDefault = relation.BankAccounts.FirstOrDefault(a => a.IsDefault && a.IsActive && !previousDefaults.Contains(a.Id));
                if (newDefault != null && previousDefaults.Count > 0)
                {
                    await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
                    newDefault.IsDefault = false;
                    await _db.SaveChangesAsync(cancellationToken);
                    newDefault.IsDefault = true;
                    await _db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                else
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                return ServiceResult<RelationDto>.Conflict($"Relation {id} was changed by someone else; reload and apply your change again.");
            }
            catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
            {
                return ServiceResult<RelationDto>.Conflict($"Search code '{command.SearchCode}' was taken by a concurrent change; choose another code.");
            }
            _db.ChangeTracker.Clear();
            var updated = await ToDtoAsync(await Query(tracking: false).SingleAsync(r => r.Id == id, cancellationToken), cancellationToken);
            updated.Warnings.InsertRange(0, signals.Warnings);
            return ServiceResult<RelationDto>.Ok(updated);
        }

        // Bankrekening bevestigen (na controle bij de relatie, bijv. terugbellen op een bekend nummer): wie verplicht, verwachte versie
        // verplicht. Unconfirmed -> Confirmed met auditregel en versie +1; al bevestigd = 200 zonder wijziging. Een inactieve rekening
        // wordt niet bevestigd.
        public async Task<ServiceResult<RelationDto>> ConfirmBankAccountAsync(int id, int bankAccountId, ConfirmBankAccountDto request, CancellationToken cancellationToken = default)
        {
            var relation = await Query(tracking: true).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (relation == null)
            {
                return ServiceResult<RelationDto>.NotFound($"Relation {id} does not exist.");
            }
            var account = relation.BankAccounts.SingleOrDefault(a => a.Id == bankAccountId);
            if (account == null)
            {
                return ServiceResult<RelationDto>.NotFound($"Bank account {bankAccountId} does not belong to relation {id}.");
            }
            var errors = new Dictionary<string, List<string>>();
            if (request?.ExpectedVersion == null) ValidationErrors.Add(errors, "expectedVersion", "The relation version the change is based on is required.");
            var actor = ValidationErrors.OptionalText(request?.ActorReference, MaxActorLength, "actorReference", errors);
            if (actor == null && !errors.ContainsKey("actorReference")) ValidationErrors.Add(errors, "actorReference", "Vul in wie de bankrekening bevestigt.");
            if (!account.IsActive) ValidationErrors.Add(errors, "bankAccountId", "Een inactieve bankrekening kan niet worden bevestigd.");
            if (errors.Count > 0)
            {
                return ServiceResult<RelationDto>.Validation(ValidationErrors.ToArrays(errors));
            }
            if (relation.Version != request.ExpectedVersion)
            {
                return ServiceResult<RelationDto>.Conflict($"Relation {id} was changed by someone else (version {relation.Version}, expected {request.ExpectedVersion}); reload and apply your change again.");
            }
            if (account.ConfirmationStatus != BankAccountConfirmation.Confirmed)
            {
                account.ConfirmationStatus = BankAccountConfirmation.Confirmed;
                account.ConfirmedAt = DateTime.UtcNow;
                account.ConfirmedBy = actor;
                _db.RelationBankAccountEvents.Add(new RelationBankAccountEvent
                {
                    BankAccount = account, RelationId = relation.Id, Kind = BankAccountEventKind.Confirmed, ActorReference = actor, OccurredAt = DateTime.UtcNow,
                    OldIban = account.Iban, NewIban = account.Iban, OldAccountHolder = account.AccountHolder, NewAccountHolder = account.AccountHolder,
                    OldOtherAccountNumber = account.OtherAccountNumber, NewOtherAccountNumber = account.OtherAccountNumber
                });
                relation.UpdatedAt = DateTime.UtcNow; // versie +1 (centraal in AppDbContext)
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return ServiceResult<RelationDto>.Conflict($"Relation {id} was changed by someone else; reload and apply your change again.");
                }
            }
            _db.ChangeTracker.Clear();
            return ServiceResult<RelationDto>.Ok(await ToDtoAsync(await Query(tracking: false).SingleAsync(r => r.Id == id, cancellationToken), cancellationToken));
        }

        // Bedrijfsinstelling voor de relatiewerkplek: link naar de GLOBALG.A.P. Supply Chain Portal (absolute https-URL).
        public async Task<RelationSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default) =>
            new() { GlobalGapPortalUrl = (await SettingsAsync(cancellationToken)).GlobalGapPortalUrl };

        public async Task<ServiceResult<RelationSettingsDto>> UpdateSettingsAsync(RelationSettingsDto request, CancellationToken cancellationToken = default)
        {
            var errors = new Dictionary<string, List<string>>();
            var url = ValidationErrors.OptionalText(request?.GlobalGapPortalUrl, RelationRules.MaxPortalUrlLength, "globalGapPortalUrl", errors);
            if (url == null && !errors.ContainsKey("globalGapPortalUrl")) ValidationErrors.Add(errors, "globalGapPortalUrl", "De link naar de GLOBALG.A.P. Supply Chain Portal is verplicht.");
            else if (url != null && !RelationRules.IsHttpsUrl(url)) ValidationErrors.Add(errors, "globalGapPortalUrl", "Gebruik een volledige https-link (bijvoorbeeld https://…).");
            if (errors.Count > 0)
            {
                return ServiceResult<RelationSettingsDto>.Validation(ValidationErrors.ToArrays(errors));
            }
            var settings = await SettingsAsync(cancellationToken);
            settings.GlobalGapPortalUrl = url;
            await _db.SaveChangesAsync(cancellationToken);
            return ServiceResult<RelationSettingsDto>.Ok(new RelationSettingsDto { GlobalGapPortalUrl = settings.GlobalGapPortalUrl });
        }

        private async Task<CompanySettings> SettingsAsync(CancellationToken cancellationToken)
        {
            var settings = await _db.CompanySettings.OrderBy(c => c.Id).FirstOrDefaultAsync(cancellationToken);
            if (settings == null)
            {
                settings = new CompanySettings();
                _db.CompanySettings.Add(settings);
                await _db.SaveChangesAsync(cancellationToken);
            }
            return settings;
        }

        // Tweede rol toevoegen aan een bestaande relatie (idempotent: bestaat de rol al, dan 200 met de kaart). Nooit een tweede onderneming.
        public async Task<ServiceResult<RelationDto>> AddRoleAsync(int id, string role, CancellationToken cancellationToken = default)
        {
            var relation = await Query(tracking: true).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (relation == null)
            {
                return ServiceResult<RelationDto>.NotFound($"Relation {id} does not exist.");
            }
            var created = false;
            if (string.Equals(role, RoleCustomer, StringComparison.OrdinalIgnoreCase))
            {
                if (relation.Customer == null)
                {
                    relation.Customer = new Customer { Relation = relation, PaymentTermsDays = DefaultCustomerPaymentTermsDays, CreatedAt = DateTime.UtcNow };
                    created = true;
                }
            }
            else if (string.Equals(role, RoleVendor, StringComparison.OrdinalIgnoreCase))
            {
                if (relation.Vendor == null)
                {
                    relation.Vendor = new Vendor { Relation = relation, PaymentTermsDays = DefaultVendorPaymentTermsDays, DefaultCurrency = DefaultVendorCurrency, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
                    created = true;
                }
            }
            else
            {
                return ServiceResult<RelationDto>.Validation(new Dictionary<string, string[]> { ["role"] = new[] { $"Role must be '{RoleCustomer}' or '{RoleVendor}'." } });
            }
            if (created)
            {
                SyncMirror(relation);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception) when (DbErrors.IsUniqueViolation(exception))
                {
                    // Gelijktijdige toevoeging van dezelfde rol: de unieke index (één rol per soort per relatie) hield de tweede
                    // tegen. Het eindresultaat is wat de gebruiker wilde; antwoord met de bestaande rol (200), nooit een 500.
                    _db.ChangeTracker.Clear();
                    var reloaded = await Query(tracking: false).SingleAsync(r => r.Id == id, cancellationToken);
                    var exists = string.Equals(role, RoleCustomer, StringComparison.OrdinalIgnoreCase) ? reloaded.Customer != null : reloaded.Vendor != null;
                    if (!exists)
                    {
                        throw;
                    }
                    return ServiceResult<RelationDto>.Ok(await ToDtoAsync(reloaded, cancellationToken));
                }
            }
            _db.ChangeTracker.Clear();
            var dto = await ToDtoAsync(await Query(tracking: false).SingleAsync(r => r.Id == id, cancellationToken), cancellationToken);
            return created ? ServiceResult<RelationDto>.Created(dto) : ServiceResult<RelationDto>.Ok(dto);
        }

        // ---------------- Bestaande routes (legacy) via dezelfde bron ----------------

        // CustomerController POST / OrderController: klant op naam of met de oude platte velden. Maakt relatie + klantrol; geen samenvoeging.
        public async Task<Customer> CreateLegacyCustomerAsync(CreateCustomerDto dto, CancellationToken cancellationToken = default)
        {
            var relation = new Relation
            {
                Name = dto.Name.Trim(),
                VatNumber = RelationRules.NormalizeVat(dto.VATNumber),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Customer = new Customer { PaymentTermsDays = dto.PaymentTermsDays, CreatedAt = DateTime.UtcNow }
            };
            // Kale termijn (oude route, OrderController 14 dagen): via de gedeelde resolver naar een betalingsconditie.
            await PaymentConditionResolver.AssignDaysAsync(_db, relation.Customer, dto.PaymentTermsDays, cancellationToken);
            ApplyLegacyFlatFields(relation, dto.BillingAddress, dto.ShippingAddress, dto.ContactPerson, dto.Email, dto.Phone);
            SyncMirror(relation);
            _db.Relations.Add(relation);
            await _db.SaveChangesAsync(cancellationToken);
            return relation.Customer;
        }

        // CustomerController PUT / CustomersController PUT: de platte velden gaan naar de relatie (bron) en worden teruggespiegeld.
        public async Task<Customer> UpdateLegacyCustomerAsync(int customerId, CreateCustomerDto dto, CancellationToken cancellationToken = default)
        {
            var customer = await _db.Customers.Include(c => c.Relation).ThenInclude(r => r.Vendor).Include(c => c.Relation).ThenInclude(r => r.Addresses).Include(c => c.Relation).ThenInclude(r => r.Contacts).ThenInclude(c => c.Channels)
                .SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
            if (customer == null)
            {
                return null;
            }
            var relation = customer.Relation;
            if (!string.IsNullOrWhiteSpace(dto.Name)) relation.Name = dto.Name.Trim();
            var vat = RelationRules.NormalizeVat(dto.VATNumber);
            if (!string.Equals(vat, relation.VatNumber, StringComparison.Ordinal))
            {
                relation.VatNumber = vat;
                ResetVatVerification(relation);
            }
            if (customer.PaymentTermsDays != dto.PaymentTermsDays)
            {
                await PaymentConditionResolver.AssignDaysAsync(_db, customer, dto.PaymentTermsDays, cancellationToken);
            }
            ApplyLegacyFlatFields(relation, dto.BillingAddress, dto.ShippingAddress, dto.ContactPerson, dto.Email, dto.Phone);
            relation.UpdatedAt = DateTime.UtcNow;
            SyncMirror(relation);
            await _db.SaveChangesAsync(cancellationToken);
            return customer;
        }

        // Platte legacy-velden: vrije adrestekst wordt één adresregel per doel (land onbekend tot de gebruiker het kiest), contactpersoon
        // één contact met zijn telefoon/e-mail; zonder contactpersoon zijn e-mail en telefoon algemene bedrijfsgegevens.
        public static void ApplyLegacyFlatFields(Relation relation, string billingAddress, string shippingAddress, string contactPerson, string email, string phone)
        {
            UpsertLegacyAddress(relation, AddressPurpose.Billing, billingAddress);
            UpsertLegacyAddress(relation, AddressPurpose.Delivery, shippingAddress);
            var contactName = Blank(contactPerson);
            var mail = Blank(email);
            var tel = Blank(phone);
            var primary = relation.Contacts.Where(c => c.IsActive).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.SortOrder).FirstOrDefault();
            if (contactName != null)
            {
                if (primary == null)
                {
                    primary = new RelationContact { Relation = relation, IsPrimary = true, SortOrder = relation.Contacts.Count };
                    relation.Contacts.Add(primary);
                }
                primary.Name = contactName;
                ReplaceChannel(primary, ContactChannelKind.Email, mail);
                ReplaceChannel(primary, ContactChannelKind.Phone, tel);
            }
            else
            {
                relation.Email = mail;
                relation.Phone = tel;
            }
        }

        private static void UpsertLegacyAddress(Relation relation, string purpose, string text)
        {
            var value = Blank(text);
            var address = relation.Addresses.Where(a => a.Purpose == purpose && a.IsActive).OrderByDescending(a => a.IsDefault).ThenBy(a => a.SortOrder).FirstOrDefault();
            if (value == null)
            {
                if (address != null) relation.Addresses.Remove(address);
                return;
            }
            if (address == null)
            {
                address = new RelationAddress { Relation = relation, Purpose = purpose, IsDefault = true, SortOrder = relation.Addresses.Count };
                relation.Addresses.Add(address);
            }
            if (!string.Equals(FormatAddress(address), value, StringComparison.Ordinal))
            {
                address.Line1 = value.Length > RelationRules.MaxAddressLineLength ? value[..RelationRules.MaxAddressLineLength] : value;
                address.Line2 = null;
                address.PostalCode = null;
                address.City = null;
                address.Region = null;
            }
        }

        private static void ReplaceChannel(RelationContact contact, string kind, string value)
        {
            var existing = contact.Channels.Where(c => c.Kind == kind).OrderBy(c => c.SortOrder).FirstOrDefault();
            if (value == null)
            {
                if (existing != null) contact.Channels.Remove(existing);
                return;
            }
            if (existing == null)
            {
                existing = new RelationContactChannel { RelationContact = contact, Kind = kind, SortOrder = contact.Channels.Count };
                contact.Channels.Add(existing);
            }
            existing.Value = value;
        }

        // ---------------- Normalisatie en validatie ----------------

        // Canonieke, genormaliseerde opdracht: dit is wat wordt opgeslagen en wat een replay vergelijkt.
        public sealed record AddressCommand(int? Id, string Purpose, string Label, string Line1, string Line2, string PostalCode, string City, string Region, string CountryCode, bool IsDefault, bool IsActive);
        public sealed record ChannelCommand(int? Id, string Kind, string Label, string Value);
        public sealed record ContactCommand(int? Id, string Name, string Department, string JobTitle, bool IsPrimary, bool IsActive, string Note, List<ChannelCommand> Channels);
        public sealed record GgnCommand(int? Id, string Ggn, string ProducerName, bool IsActive, DateOnly? CheckedOn, string CheckedBy, DateOnly? CertificateValidUntil, bool HasGgnLabel);
        // Betalingsconditie: PaymentConditionId (gekozen of ongewijzigd) of LegacyDays (kale termijn -> resolver bij opslaan).
        public sealed record CustomerRoleCommand(int? PaymentConditionId, int? LegacyDays, RoleTermsRules.Terms Terms, string Currency, RoleTermsRules.Credit Credit);
        public sealed record VendorRoleCommand(int? PaymentConditionId, int? LegacyDays, string DefaultCurrency, RoleTermsRules.Terms Terms, List<GgnCommand> Ggns);
        public sealed record BankAccountCommand(int? Id, string Iban, string OtherAccountNumber, string Bic, string AccountHolder, string Currency, bool IsDefault, bool IsActive);
        public sealed record RelationCommand(
            string SearchCode, string Name, string TradeName, string PreferredLanguage, string Website, string Email, string Phone, string VatNumber,
            string RegistrationNumber, string RegistrationCountry, string RegistrationType, string LegalForm, string InternalNote, bool IsActive,
            CustomerRoleCommand Customer, VendorRoleCommand Vendor, List<AddressCommand> Addresses, List<ContactCommand> Contacts,
            List<BankAccountCommand> BankAccounts, string ActorReference);

        private async Task<RelationCommand> NormalizeAsync(SaveRelationDto request, Relation existing, Dictionary<string, List<string>> errors, SaveSignals signals, CancellationToken cancellationToken, int? selfId = null, bool checkSearchCodeAvailability = true)
        {
            var today = _db.BusinessCalendar.Today();
            selfId ??= existing?.Id;
            if (request == null)
            {
                ValidationErrors.Add(errors, "request", "A request body is required.");
                return null;
            }
            var name = ValidationErrors.RequiredText(request.Name, RelationRules.MaxNameLength, "name", errors);
            var searchCode = ValidationErrors.OptionalText(request.SearchCode, RelationRules.MaxSearchCodeLength, "searchCode", errors);
            if (searchCode != null)
            {
                if (!RelationRules.IsSearchCode(searchCode))
                {
                    ValidationErrors.Add(errors, "searchCode", "A search code consists of letters, digits, '.', '-' or '_' (max 32), without spaces.");
                }
                else if (checkSearchCodeAvailability)
                {
                    var key = RelationRules.SearchCodeKey(searchCode);
                    var taken = await _db.Relations.AsNoTracking().Where(r => r.SearchCodeKey == key && (selfId == null || r.Id != selfId)).Select(r => new { r.Id, r.Name }).FirstOrDefaultAsync(cancellationToken);
                    if (taken != null)
                    {
                        ValidationErrors.Add(errors, "searchCode", $"Search code '{searchCode}' is already used by relation #{taken.Id} ({taken.Name}); search codes are unique regardless of case.");
                    }
                }
            }
            var tradeName = ValidationErrors.OptionalText(request.TradeName, RelationRules.MaxNameLength, "tradeName", errors);
            var language = ValidationErrors.OptionalText(request.PreferredLanguage, 8, "preferredLanguage", errors)?.ToLowerInvariant();
            if (language != null && !RelationRules.IsLanguage(language))
            {
                ValidationErrors.Add(errors, "preferredLanguage", $"Preferred language must be empty or one of: {string.Join(", ", RelationRules.Languages)}.");
            }
            var website = ValidationErrors.OptionalText(request.Website, RelationRules.MaxWebsiteLength, "website", errors);
            var email = ValidationErrors.OptionalText(request.Email, RelationRules.MaxEmailLength, "email", errors);
            // Strikte vorm, tikfoutsuggestie en MX-controle alleen voor een nieuw of gewijzigd adres; een ongewijzigd opgeslagen adres blijft opslaanbaar.
            if (email != null && !string.Equals(email, existing?.Email, StringComparison.Ordinal))
            {
                CheckNewEmail(email, "email", errors, signals);
            }
            var phone = ValidationErrors.OptionalText(request.Phone, RelationRules.MaxPhoneLength, "phone", errors);
            var vat = RelationRules.NormalizeVat(request.VatNumber);
            // Btw-vorm per land alleen voor een nieuw of gewijzigd nummer (VatRules); een ongewijzigd opgeslagen nummer blijft staan.
            if (vat != null && !string.Equals(vat, existing?.VatNumber, StringComparison.Ordinal))
            {
                var problem = VatRules.Check(vat);
                if (problem != VatRules.VatProblem.None) ValidationErrors.Add(errors, "vatNumber", VatRules.Message(problem, vat));
            }
            var registrationNumber = ValidationErrors.OptionalText(request.RegistrationNumber, RelationRules.MaxRegistrationLength, "registrationNumber", errors);
            var registrationCountry = RelationRules.NormalizeCountry(request.RegistrationCountry);
            if (registrationCountry != null && !RelationRules.IsCountryCode(registrationCountry))
            {
                ValidationErrors.Add(errors, "registrationCountry", "Registration country must be a two-letter ISO country code.");
            }
            if (registrationNumber != null && registrationCountry == null)
            {
                ValidationErrors.Add(errors, "registrationCountry", "Choose the country of the registration number (KvK = NL).");
            }
            var registrationType = ValidationErrors.OptionalText(request.RegistrationType, RelationRules.MaxRegistrationTypeLength, "registrationType", errors);
            var legalForm = ValidationErrors.OptionalText(request.LegalForm, RelationRules.MaxLegalFormLength, "legalForm", errors);
            var note = ValidationErrors.OptionalText(request.InternalNote, RelationRules.MaxNoteLength, "internalNote", errors);
            var isActive = request.IsActive ?? existing?.IsActive ?? true;

            // Rollen: null laat een bestaande rol ongemoeid en maakt geen nieuwe; een bestaande rol wordt nooit verwijderd.
            CustomerRoleCommand customer = null;
            if (request.Customer != null)
            {
                var c = request.Customer;
                var (conditionId, legacyDays) = await NormalizeConditionAsync(c.PaymentConditionId, c.PaymentTermsDays, existing?.Customer?.PaymentConditionId, existing?.Customer?.PaymentTermsDays, DefaultCustomerPaymentTermsDays, "customer", errors, cancellationToken);
                var terms = RoleTermsRules.NormalizeTerms(new RoleTermsRules.Terms(c.PaymentMethod ?? existing?.Customer?.PaymentMethod, c.MandateReference, c.MandateSignedOn, c.DeliveryTermKind, c.Incoterm, c.IncotermPlace, c.DeliveryTermText, c.TradeConditions, c.TradeConditionsText), "customer", today, errors);
                var currency = (Blank(c.Currency) ?? existing?.Customer?.Currency ?? DefaultCustomerCurrency).ToUpperInvariant();
                if (!RoleTermsRules.IsCurrency(currency)) ValidationErrors.Add(errors, "customer.currency", "Valuta is een ISO-code van drie letters (bijvoorbeeld EUR).");
                var credit = RoleTermsRules.NormalizeCredit(new RoleTermsRules.Credit(c.CreditLimit, c.IsInsured, c.Insurer, c.InsurerText, c.InsuredLimit, c.InsurerReference, c.InsuredUntil), "customer", errors);
                customer = new CustomerRoleCommand(conditionId, legacyDays, terms, currency, credit);
            }
            else if (existing?.Customer != null)
            {
                var c = existing.Customer;
                customer = new CustomerRoleCommand(c.PaymentConditionId, null, TermsOf(c), c.Currency,
                    new RoleTermsRules.Credit(c.CreditLimit, c.IsInsured, c.Insurer, c.InsurerText, c.InsuredLimit, c.InsurerReference, c.InsuredUntil));
            }
            VendorRoleCommand vendor = null;
            if (request.Vendor != null)
            {
                var v = request.Vendor;
                var (conditionId, legacyDays) = await NormalizeConditionAsync(v.PaymentConditionId, v.PaymentTermsDays, existing?.Vendor?.PaymentConditionId, existing?.Vendor?.PaymentTermsDays, DefaultVendorPaymentTermsDays, "vendor", errors, cancellationToken);
                var currency = (Blank(v.DefaultCurrency) ?? existing?.Vendor?.DefaultCurrency ?? DefaultVendorCurrency).ToUpperInvariant();
                if (currency.Length != 3 || !currency.All(char.IsLetter)) ValidationErrors.Add(errors, "vendor.defaultCurrency", "Currency must be a three-letter ISO code.");
                var terms = RoleTermsRules.NormalizeTerms(new RoleTermsRules.Terms(v.PaymentMethod ?? existing?.Vendor?.PaymentMethod, v.MandateReference, v.MandateSignedOn, v.DeliveryTermKind, v.Incoterm, v.IncotermPlace, v.DeliveryTermText, v.TradeConditions, v.TradeConditionsText), "vendor", today, errors);
                var ggns = new List<GgnCommand>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var ggnList = v.Ggns ?? existing?.Vendor?.Ggns.OrderBy(g => g.SortOrder).Select(g => new VendorGgnDto { Id = g.Id, Ggn = g.Ggn, ProducerName = g.ProducerName, IsActive = g.IsActive, CheckedOn = g.CheckedOn, CheckedBy = g.CheckedBy, CertificateValidUntil = g.CertificateValidUntil, HasGgnLabel = g.HasGgnLabel }).ToList() ?? new List<VendorGgnDto>();
                if (ggnList.Count > RelationRules.MaxGgnsPerVendor) ValidationErrors.Add(errors, "vendor.ggns", $"At most {RelationRules.MaxGgnsPerVendor} GGNs per supplier.");
                for (var i = 0; i < ggnList.Count; i++)
                {
                    var item = ggnList[i];
                    var ggn = ArticleRules.NormalizeGgn(item?.Ggn);
                    if (!ArticleRules.IsGgn(ggn))
                    {
                        ValidationErrors.Add(errors, $"vendor.ggns[{i}].ggn", $"A GGN consists of exactly {ArticleRules.GgnLength} digits (leading zeros included); spaces are allowed as separators. A CoC number is not a GGN.");
                        continue;
                    }
                    if (!seen.Add(ggn)) ValidationErrors.Add(errors, $"vendor.ggns[{i}].ggn", $"GGN {ggn} is listed twice for this supplier.");
                    var producer = ValidationErrors.OptionalText(item.ProducerName, RelationRules.MaxProducerNameLength, $"vendor.ggns[{i}].producerName", errors);
                    // Controle in het portaal: datum niet in de toekomst en dan ook wie; geldigheid en GGN-label zoals ingevoerd.
                    var checkedBy = ValidationErrors.OptionalText(item.CheckedBy, MaxActorLength, $"vendor.ggns[{i}].checkedBy", errors);
                    if (item.CheckedOn != null && item.CheckedOn > today) ValidationErrors.Add(errors, $"vendor.ggns[{i}].checkedOn", "De controledatum kan niet in de toekomst liggen.");
                    if (item.CheckedOn != null && checkedBy == null && !errors.ContainsKey($"vendor.ggns[{i}].checkedBy")) ValidationErrors.Add(errors, $"vendor.ggns[{i}].checkedBy", "Vul in wie het certificaat heeft gecontroleerd.");
                    if (item.CheckedOn == null && checkedBy != null) ValidationErrors.Add(errors, $"vendor.ggns[{i}].checkedOn", "Vul in wanneer het certificaat is gecontroleerd.");
                    ggns.Add(new GgnCommand(item.Id, ggn, producer, item.IsActive, item.CheckedOn, checkedBy, item.CertificateValidUntil, item.HasGgnLabel));
                }
                vendor = new VendorRoleCommand(conditionId, legacyDays, currency, terms, ggns);
            }
            else if (existing?.Vendor != null)
            {
                var v = existing.Vendor;
                vendor = new VendorRoleCommand(v.PaymentConditionId, null, v.DefaultCurrency ?? DefaultVendorCurrency, TermsOf(v),
                    v.Ggns.OrderBy(g => g.SortOrder).Select(g => new GgnCommand(g.Id, g.Ggn, g.ProducerName, g.IsActive, g.CheckedOn, g.CheckedBy, g.CertificateValidUntil, g.HasGgnLabel)).ToList());
            }

            var addresses = NormalizeAddresses(request.Addresses, existing, errors);
            var contacts = NormalizeContacts(request.Contacts, existing, errors, signals);
            var actor = ValidationErrors.OptionalText(request.ActorReference, MaxActorLength, "actorReference", errors);
            var bankAccounts = NormalizeBankAccounts(request.BankAccounts, existing, errors);
            // Elke wijziging van bankgegevens wordt geaudit: dan is "wie" verplicht.
            if (actor == null && !errors.ContainsKey("actorReference") && bankAccounts.Any(b => BankChange(b, existing) != null))
            {
                ValidationErrors.Add(errors, "actorReference", "Vul in wie de bankgegevens wijzigt (actorReference).");
            }

            return errors.Count > 0 ? null : new RelationCommand(searchCode, name, tradeName, language, website, email, phone, vat,
                registrationNumber, registrationCountry, registrationType, legalForm, note, isActive, customer, vendor, addresses, contacts, bankAccounts, actor);
        }

        private static RoleTermsRules.Terms TermsOf(IRoleTerms r) =>
            new(r.PaymentMethod, r.MandateReference, r.MandateSignedOn, r.DeliveryTermKind, r.Incoterm, r.IncotermPlace, r.DeliveryTermText, r.TradeConditions, r.TradeConditionsText);

        // Betalingsconditie van een rol: een gekozen id (moet bestaan; een inactieve conditie alleen als die al gekoppeld was), anders een
        // legacy-termijn in dagen (alleen als die afwijkt van de huidige termijn, zodat een ongewijzigde kaart haar conditie houdt), anders
        // de huidige conditie, en voor een nieuwe rol de standaardtermijn.
        private async Task<(int? ConditionId, int? LegacyDays)> NormalizeConditionAsync(int? requestedId, int? requestedDays, int? currentId, int? currentDays, int defaultDays, string role, Dictionary<string, List<string>> errors, CancellationToken cancellationToken)
        {
            if (requestedId != null)
            {
                var condition = await _db.PaymentConditions.AsNoTracking().Where(p => p.Id == requestedId).Select(p => new { p.Id, p.IsActive }).SingleOrDefaultAsync(cancellationToken);
                if (condition == null)
                {
                    ValidationErrors.Add(errors, $"{role}.paymentConditionId", $"Betalingsconditie {requestedId} bestaat niet.");
                }
                else if (!condition.IsActive && condition.Id != currentId)
                {
                    ValidationErrors.Add(errors, $"{role}.paymentConditionId", "Deze betalingsconditie is inactief en kan niet opnieuw worden gekozen.");
                }
                return (requestedId, null);
            }
            if (requestedDays != null)
            {
                if (requestedDays < PaymentConditionRules.MinDays || requestedDays > PaymentConditionRules.MaxDays)
                {
                    ValidationErrors.Add(errors, $"{role}.paymentTermsDays", "Payment terms must be between 0 and 365 days.");
                    return (null, null);
                }
                return currentId != null && currentDays == requestedDays ? (currentId, null) : (null, requestedDays);
            }
            return currentId != null ? (currentId, null) : (null, defaultDays);
        }

        // Condities die bij het toepassen aan de rollen worden gekoppeld (een legacy-termijn wordt hier via de resolver opgezocht of aangemaakt).
        private sealed record RoleConditions(PaymentCondition Customer, PaymentCondition Vendor);

        private async Task<RoleConditions> ResolveConditionsAsync(RelationCommand command, CancellationToken cancellationToken)
        {
            async Task<PaymentCondition> Resolve(int? id, int? days) =>
                id != null ? await _db.PaymentConditions.SingleAsync(p => p.Id == id, cancellationToken)
                : days != null ? await PaymentConditionResolver.ForDaysAsync(_db, days.Value, cancellationToken)
                : null;
            return new RoleConditions(
                command.Customer == null ? null : await Resolve(command.Customer.PaymentConditionId, command.Customer.LegacyDays),
                command.Vendor == null ? null : await Resolve(command.Vendor.PaymentConditionId, command.Vendor.LegacyDays));
        }

        // Nieuw of gewijzigd e-mailadres: strikte vorm (fout), bekende tikfout (waarschuwing met suggestie), domein voor de MX-controle.
        private static void CheckNewEmail(string value, string field, Dictionary<string, List<string>> errors, SaveSignals signals)
        {
            if (!EmailRules.IsStrict(value))
            {
                ValidationErrors.Add(errors, field, EmailRules.InvalidMessage);
            }
            var suggestion = EmailRules.TypoSuggestion(value);
            if (suggestion != null)
            {
                signals.Warnings.Add(new RelationWarningDto { Code = WarningEmailTypo, Field = field, Message = $"Bedoelt u {suggestion}?", Suggestion = suggestion });
            }
            else if (EmailRules.IsStrict(value))
            {
                signals.Domains.Add((field, EmailRules.Domain(value)));
            }
        }

        // MX-controle per uniek domein (parallel, elk met de 2 s time-out van de controleur). Alleen waarschuwingen, nooit blokkeren.
        private async Task CheckEmailDomainsAsync(SaveSignals signals, CancellationToken cancellationToken)
        {
            var domains = signals.Domains.Select(d => d.Domain).Distinct(StringComparer.Ordinal).ToList();
            if (domains.Count == 0)
            {
                return;
            }
            var results = await Task.WhenAll(domains.Select(async d => (Domain: d, Status: await _emailDomains.CheckAsync(d, cancellationToken))));
            var byDomain = results.ToDictionary(r => r.Domain, r => r.Status, StringComparer.Ordinal);
            foreach (var (field, domain) in signals.Domains)
            {
                switch (byDomain[domain])
                {
                    case EmailDomainStatus.NoMailServer:
                        signals.Warnings.Add(new RelationWarningDto { Code = WarningEmailNoMailServer, Field = field, Message = $"Het domein {domain} heeft geen mailserver; controleer het e-mailadres." });
                        break;
                    case EmailDomainStatus.Unavailable:
                        signals.Warnings.Add(new RelationWarningDto { Code = WarningEmailDomainUnchecked, Field = field, Message = $"De mailserver van {domain} kon niet worden gecontroleerd (DNS niet bereikbaar)." });
                        break;
                }
            }
        }

        // Bankrekeningen: precies één van IBAN (geldig: vorm, land, lengte, mod-97) en ander rekeningnummer; BIC optioneel (8 of 11);
        // rekeninghouder verplicht; per relatie hooguit één actieve standaard (geen -> de eerste actieve wordt standaard); een inactieve
        // rekening is nooit standaard; dezelfde IBAN niet twee keer; een opgeslagen rekening mag niet ontbreken (nooit verwijderen).
        private static List<BankAccountCommand> NormalizeBankAccounts(List<RelationBankAccountDto> input, Relation existing, Dictionary<string, List<string>> errors)
        {
            var stored = existing?.BankAccounts.ToDictionary(a => a.Id) ?? new Dictionary<int, RelationBankAccount>();
            if (input == null)
            {
                return stored.Values.OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
                    .Select(a => new BankAccountCommand(a.Id, a.Iban, a.OtherAccountNumber, a.Bic, a.AccountHolder, a.Currency, a.IsDefault, a.IsActive)).ToList();
            }
            if (input.Count > BankAccountRules.MaxBankAccounts) ValidationErrors.Add(errors, "bankAccounts", $"Hooguit {BankAccountRules.MaxBankAccounts} bankrekeningen per relatie.");
            var result = new List<BankAccountCommand>();
            var seenIbans = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < input.Count; i++)
            {
                var a = input[i];
                var key = $"bankAccounts[{i}]";
                if (a == null)
                {
                    ValidationErrors.Add(errors, key, "Een bankrekening is verplicht.");
                    continue;
                }
                if (a.Id != null && !stored.ContainsKey(a.Id.Value)) ValidationErrors.Add(errors, $"{key}.id", $"Bankrekening {a.Id} hoort niet bij deze relatie.");
                var iban = BankAccountRules.NormalizeIban(a.Iban);
                var other = ValidationErrors.OptionalText(a.OtherAccountNumber, BankAccountRules.MaxOtherAccountNumberLength, $"{key}.otherAccountNumber", errors);
                if (iban == null && other == null && !errors.ContainsKey($"{key}.otherAccountNumber"))
                {
                    ValidationErrors.Add(errors, $"{key}.iban", "Vul een IBAN in, of een ander rekeningnummer voor een rekening buiten SEPA.");
                }
                else if (iban != null && other != null)
                {
                    ValidationErrors.Add(errors, $"{key}.otherAccountNumber", "Vul óf een IBAN óf een ander rekeningnummer in, niet allebei.");
                }
                if (iban != null)
                {
                    var problem = BankAccountRules.CheckIban(iban);
                    if (problem != BankAccountRules.IbanProblem.None) ValidationErrors.Add(errors, $"{key}.iban", BankAccountRules.IbanMessage(problem, iban));
                    else if (a.IsActive && !seenIbans.Add(iban)) ValidationErrors.Add(errors, $"{key}.iban", $"IBAN {iban} staat al bij deze relatie.");
                }
                var bic = BankAccountRules.NormalizeBic(a.Bic);
                if (bic != null && !BankAccountRules.IsBic(bic)) ValidationErrors.Add(errors, $"{key}.bic", "Een BIC heeft 8 of 11 tekens: 4 letters bank, 2 letters land, 2 tekens plaats en eventueel 3 tekens filiaal.");
                var holder = ValidationErrors.OptionalText(a.AccountHolder, BankAccountRules.MaxAccountHolderLength, $"{key}.accountHolder", errors);
                if (holder == null && !errors.ContainsKey($"{key}.accountHolder")) ValidationErrors.Add(errors, $"{key}.accountHolder", "De rekeninghouder is verplicht.");
                var currency = (Blank(a.Currency) ?? (a.Id != null && stored.TryGetValue(a.Id.Value, out var old) ? old.Currency : DefaultCustomerCurrency)).ToUpperInvariant();
                if (!RoleTermsRules.IsCurrency(currency)) ValidationErrors.Add(errors, $"{key}.currency", "Valuta is een ISO-code van drie letters (bijvoorbeeld EUR).");
                if (a.IsDefault && !a.IsActive) ValidationErrors.Add(errors, $"{key}.isDefault", "Een inactieve bankrekening kan geen standaardrekening zijn.");
                result.Add(new BankAccountCommand(a.Id, iban, other, bic, holder, currency, a.IsDefault && a.IsActive, a.IsActive));
            }
            var missing = stored.Keys.Where(id => !input.Any(a => a?.Id == id)).OrderBy(id => id).ToList();
            foreach (var id in missing)
            {
                ValidationErrors.Add(errors, "bankAccounts", $"Bankrekening {id} ontbreekt in de opdracht. Bankrekeningen worden nooit verwijderd: zet inactief in plaats van verwijderen.");
            }
            var active = result.Where(x => x.IsActive).ToList();
            if (active.Count(x => x.IsDefault) > 1) ValidationErrors.Add(errors, "bankAccounts", "Er kan maar één standaardrekening zijn.");
            else if (active.Count > 0 && !active.Any(x => x.IsDefault)) result[result.IndexOf(active[0])] = active[0] with { IsDefault = true };
            return result;
        }

        // Soort auditregel die deze rekeningopdracht oplevert (null = geen auditwaardige wijziging): nieuw -> Added; IBAN, houder of ander
        // nummer gewijzigd -> Changed (en weer Unconfirmed); actief -> inactief -> Deactivated; inactief -> actief -> Changed.
        private static string BankChange(BankAccountCommand command, Relation existing)
        {
            var old = command.Id == null ? null : existing?.BankAccounts.FirstOrDefault(a => a.Id == command.Id);
            if (old == null) return BankAccountEventKind.Added;
            if (IdentityChanged(old, command)) return BankAccountEventKind.Changed;
            if (old.IsActive && !command.IsActive) return BankAccountEventKind.Deactivated;
            if (!old.IsActive && command.IsActive) return BankAccountEventKind.Changed;
            return null;
        }

        private static bool IdentityChanged(RelationBankAccount old, BankAccountCommand command) =>
            !string.Equals(old.Iban, command.Iban, StringComparison.Ordinal) || !string.Equals(old.AccountHolder, command.AccountHolder, StringComparison.Ordinal)
            || !string.Equals(old.OtherAccountNumber, command.OtherAccountNumber, StringComparison.Ordinal);

        private static List<AddressCommand> NormalizeAddresses(List<RelationAddressDto> input, Relation existing, Dictionary<string, List<string>> errors)
        {
            if (input == null)
            {
                return existing?.Addresses.OrderBy(a => a.SortOrder).Select(a => new AddressCommand(a.Id, a.Purpose, a.Label, a.Line1, a.Line2, a.PostalCode, a.City, a.Region, a.CountryCode, a.IsDefault, a.IsActive)).ToList() ?? new List<AddressCommand>();
            }
            if (input.Count > RelationRules.MaxAddresses) ValidationErrors.Add(errors, "addresses", $"At most {RelationRules.MaxAddresses} addresses.");
            var result = new List<AddressCommand>();
            var stored = existing?.Addresses.ToDictionary(a => a.Id) ?? new Dictionary<int, RelationAddress>();
            for (var i = 0; i < input.Count; i++)
            {
                var a = input[i];
                var key = $"addresses[{i}]";
                var purpose = Blank(a?.Purpose);
                if (!RelationRules.IsAddressPurpose(purpose))
                {
                    ValidationErrors.Add(errors, $"{key}.purpose", $"Address purpose must be one of: {string.Join(", ", AddressPurpose.All)}.");
                    continue;
                }
                var label = ValidationErrors.OptionalText(a.Label, RelationRules.MaxAddressLabelLength, $"{key}.label", errors);
                var line1 = ValidationErrors.OptionalText(a.Line1, RelationRules.MaxAddressLineLength, $"{key}.line1", errors);
                var line2 = ValidationErrors.OptionalText(a.Line2, RelationRules.MaxAddressLineLength, $"{key}.line2", errors);
                var postal = ValidationErrors.OptionalText(a.PostalCode, RelationRules.MaxPostalCodeLength, $"{key}.postalCode", errors);
                var city = ValidationErrors.OptionalText(a.City, RelationRules.MaxCityLength, $"{key}.city", errors);
                var region = ValidationErrors.OptionalText(a.Region, RelationRules.MaxCityLength, $"{key}.region", errors);
                var country = RelationRules.NormalizeCountry(a.CountryCode);
                if (line1 == null && city == null && postal == null)
                {
                    ValidationErrors.Add(errors, $"{key}.line1", "An address needs at least a street line, postal code or city.");
                }
                if (country != null && !RelationRules.IsCountryCode(country))
                {
                    ValidationErrors.Add(errors, $"{key}.countryCode", "Country must be a two-letter ISO country code.");
                }
                // Land verplicht voor nieuwe en gewijzigde adressen; een onveranderd legacy-adres (vrije tekst, land onbekend) mag blijven.
                if (country == null)
                {
                    var unchanged = a.Id != null && stored.TryGetValue(a.Id.Value, out var old) && old.CountryCode == null
                        && string.Equals(old.Line1, line1, StringComparison.Ordinal) && string.Equals(old.Line2, line2, StringComparison.Ordinal)
                        && string.Equals(old.PostalCode, postal, StringComparison.Ordinal) && string.Equals(old.City, city, StringComparison.Ordinal) && string.Equals(old.Region, region, StringComparison.Ordinal);
                    if (!unchanged) ValidationErrors.Add(errors, $"{key}.countryCode", "Choose the country of this address (also for foreign addresses; no Dutch postal code format is required).");
                }
                if (a.Id != null && !stored.ContainsKey(a.Id.Value)) ValidationErrors.Add(errors, $"{key}.id", $"Address {a.Id} does not belong to this relation.");
                result.Add(new AddressCommand(a.Id, purpose, label, line1, line2, postal, city, region, country, a.IsDefault, a.IsActive));
            }
            // Per doel precies één standaard onder de actieve adressen: geen enkele -> de eerste wordt standaard; meer dan één -> fout.
            foreach (var purpose in AddressPurpose.All)
            {
                var group = result.Where(x => x.Purpose == purpose && x.IsActive).ToList();
                if (group.Count(x => x.IsDefault) > 1) ValidationErrors.Add(errors, "addresses", $"Only one default {purpose.ToLowerInvariant()} address is allowed.");
                else if (group.Count > 0 && !group.Any(x => x.IsDefault))
                {
                    var first = group[0];
                    result[result.IndexOf(first)] = first with { IsDefault = true };
                }
            }
            return result;
        }

        private static List<ContactCommand> NormalizeContacts(List<RelationContactDto> input, Relation existing, Dictionary<string, List<string>> errors, SaveSignals signals)
        {
            if (input == null)
            {
                return existing?.Contacts.OrderBy(c => c.SortOrder).Select(c => new ContactCommand(c.Id, c.Name, c.Department, c.JobTitle, c.IsPrimary, c.IsActive, c.Note,
                    c.Channels.OrderBy(ch => ch.SortOrder).Select(ch => new ChannelCommand(ch.Id, ch.Kind, ch.Label, ch.Value)).ToList())).ToList() ?? new List<ContactCommand>();
            }
            if (input.Count > RelationRules.MaxContacts) ValidationErrors.Add(errors, "contacts", $"At most {RelationRules.MaxContacts} contacts.");
            var stored = existing?.Contacts.ToDictionary(c => c.Id) ?? new Dictionary<int, RelationContact>();
            var storedChannels = existing?.Contacts.SelectMany(c => c.Channels).ToDictionary(ch => ch.Id) ?? new Dictionary<int, RelationContactChannel>();
            var result = new List<ContactCommand>();
            for (var i = 0; i < input.Count; i++)
            {
                var c = input[i];
                var key = $"contacts[{i}]";
                var name = ValidationErrors.RequiredText(c?.Name, RelationRules.MaxContactNameLength, $"{key}.name", errors);
                if (c == null) continue;
                var department = ValidationErrors.OptionalText(c.Department, RelationRules.MaxDepartmentLength, $"{key}.department", errors);
                var job = ValidationErrors.OptionalText(c.JobTitle, RelationRules.MaxJobTitleLength, $"{key}.jobTitle", errors);
                var note = ValidationErrors.OptionalText(c.Note, RelationRules.MaxContactNoteLength, $"{key}.note", errors);
                if (c.Id != null && !stored.ContainsKey(c.Id.Value)) ValidationErrors.Add(errors, $"{key}.id", $"Contact {c.Id} does not belong to this relation.");
                var channels = new List<ChannelCommand>();
                var list = c.Channels ?? new List<ContactChannelDto>();
                if (list.Count > RelationRules.MaxChannelsPerContact) ValidationErrors.Add(errors, $"{key}.channels", $"At most {RelationRules.MaxChannelsPerContact} phone numbers and e-mail addresses per contact.");
                for (var j = 0; j < list.Count; j++)
                {
                    var ch = list[j];
                    var chKey = $"{key}.channels[{j}]";
                    var kind = Blank(ch?.Kind);
                    if (!RelationRules.IsChannelKind(kind))
                    {
                        ValidationErrors.Add(errors, $"{chKey}.kind", $"Channel kind must be one of: {string.Join(", ", ContactChannelKind.All)}.");
                        continue;
                    }
                    var value = ValidationErrors.RequiredText(ch.Value, RelationRules.MaxChannelValueLength, $"{chKey}.value", errors);
                    // Strikte e-mailcontrole alleen voor een nieuw of gewijzigd kanaal (zelfde regel als de relatie-e-mail).
                    if (value != null && kind == ContactChannelKind.Email
                        && !(ch.Id != null && storedChannels.TryGetValue(ch.Id.Value, out var storedChannel) && storedChannel.Kind == kind && string.Equals(storedChannel.Value, value, StringComparison.Ordinal)))
                    {
                        CheckNewEmail(value, $"{chKey}.value", errors, signals);
                    }
                    if (value != null && kind == ContactChannelKind.Phone && value.Length > RelationRules.MaxPhoneLength) ValidationErrors.Add(errors, $"{chKey}.value", $"A phone number may contain at most {RelationRules.MaxPhoneLength} characters.");
                    var label = ValidationErrors.OptionalText(ch.Label, RelationRules.MaxChannelLabelLength, $"{chKey}.label", errors);
                    channels.Add(new ChannelCommand(ch.Id, kind, label, value));
                }
                result.Add(new ContactCommand(c.Id, name, department, job, c.IsPrimary, c.IsActive, note, channels));
            }
            if (result.Count(x => x.IsPrimary && x.IsActive) > 1) ValidationErrors.Add(errors, "contacts", "Only one primary contact is allowed.");
            return result;
        }

        // ---------------- Toepassen ----------------

        private void Apply(Relation relation, RelationCommand command, RoleConditions conditions)
        {
            relation.SearchCode = command.SearchCode;
            relation.SearchCodeKey = RelationRules.SearchCodeKey(command.SearchCode);
            relation.Name = command.Name;
            relation.TradeName = command.TradeName;
            relation.PreferredLanguage = command.PreferredLanguage;
            relation.Website = command.Website;
            relation.Email = command.Email;
            relation.Phone = command.Phone;
            if (relation.Id == 0 || !string.Equals(relation.VatNumber, command.VatNumber, StringComparison.Ordinal))
            {
                relation.VatNumber = command.VatNumber;
                ResetVatVerification(relation);
            }
            relation.RegistrationNumber = command.RegistrationNumber;
            relation.RegistrationCountry = command.RegistrationCountry;
            relation.RegistrationType = command.RegistrationType;
            relation.LegalForm = command.LegalForm;
            relation.InternalNote = command.InternalNote;
            relation.IsActive = command.IsActive;
            relation.UpdatedAt = DateTime.UtcNow;

            if (command.Customer != null)
            {
                relation.Customer ??= new Customer { Relation = relation, CreatedAt = DateTime.UtcNow };
                var c = relation.Customer;
                // Conditie en termijnspiegel samen (regel 16).
                PaymentConditionResolver.Assign(c, conditions.Customer);
                ApplyTerms(c, command.Customer.Terms);
                c.Currency = command.Customer.Currency;
                var credit = command.Customer.Credit;
                c.CreditLimit = credit.CreditLimit; c.IsInsured = credit.IsInsured; c.Insurer = credit.Insurer; c.InsurerText = credit.InsurerText;
                c.InsuredLimit = credit.InsuredLimit; c.InsurerReference = credit.InsurerReference; c.InsuredUntil = credit.InsuredUntil;
            }
            if (command.Vendor != null)
            {
                relation.Vendor ??= new Vendor { Relation = relation, CreatedAt = DateTime.UtcNow };
                PaymentConditionResolver.Assign(relation.Vendor, conditions.Vendor);
                relation.Vendor.DefaultCurrency = command.Vendor.DefaultCurrency;
                ApplyTerms(relation.Vendor, command.Vendor.Terms);
                relation.Vendor.UpdatedAt = DateTime.UtcNow;
                SyncSet(relation.Vendor.Ggns, command.Vendor.Ggns, g => g.Id, g => g.Id,
                    (g, c, i) => { g.Ggn = c.Ggn; g.ProducerName = c.ProducerName; g.IsActive = c.IsActive; g.SortOrder = i; g.CheckedOn = c.CheckedOn; g.CheckedBy = c.CheckedBy; g.CertificateValidUntil = c.CertificateValidUntil; g.HasGgnLabel = c.HasGgnLabel; },
                    (c, i) => new VendorGgn { Vendor = relation.Vendor, Ggn = c.Ggn, ProducerName = c.ProducerName, IsActive = c.IsActive, SortOrder = i, CheckedOn = c.CheckedOn, CheckedBy = c.CheckedBy, CertificateValidUntil = c.CertificateValidUntil, HasGgnLabel = c.HasGgnLabel });
            }
            ApplyBankAccounts(relation, command.BankAccounts, command.ActorReference);
            SyncSet(relation.Addresses, command.Addresses, a => a.Id, c => c.Id,
                (a, c, i) => { a.Purpose = c.Purpose; a.Label = c.Label; a.Line1 = c.Line1; a.Line2 = c.Line2; a.PostalCode = c.PostalCode; a.City = c.City; a.Region = c.Region; a.CountryCode = c.CountryCode; a.IsDefault = c.IsDefault; a.IsActive = c.IsActive; a.SortOrder = i; },
                (c, i) => new RelationAddress { Relation = relation, Purpose = c.Purpose, Label = c.Label, Line1 = c.Line1, Line2 = c.Line2, PostalCode = c.PostalCode, City = c.City, Region = c.Region, CountryCode = c.CountryCode, IsDefault = c.IsDefault, IsActive = c.IsActive, SortOrder = i });
            SyncSet(relation.Contacts, command.Contacts, c => c.Id, c => c.Id,
                (contact, c, i) =>
                {
                    contact.Name = c.Name; contact.Department = c.Department; contact.JobTitle = c.JobTitle; contact.IsPrimary = c.IsPrimary; contact.IsActive = c.IsActive; contact.Note = c.Note; contact.SortOrder = i;
                    SyncSet(contact.Channels, c.Channels, ch => ch.Id, x => x.Id, (ch, x, j) => { ch.Kind = x.Kind; ch.Label = x.Label; ch.Value = x.Value; ch.SortOrder = j; },
                        (x, j) => new RelationContactChannel { RelationContact = contact, Kind = x.Kind, Label = x.Label, Value = x.Value, SortOrder = j });
                },
                (c, i) =>
                {
                    var contact = new RelationContact { Relation = relation, Name = c.Name, Department = c.Department, JobTitle = c.JobTitle, IsPrimary = c.IsPrimary, IsActive = c.IsActive, Note = c.Note, SortOrder = i };
                    var j = 0;
                    foreach (var x in c.Channels) contact.Channels.Add(new RelationContactChannel { RelationContact = contact, Kind = x.Kind, Label = x.Label, Value = x.Value, SortOrder = j++ });
                    return contact;
                });
            SyncMirror(relation);
        }

        private static void ApplyTerms(IRoleTerms role, RoleTermsRules.Terms terms)
        {
            role.PaymentMethod = terms.PaymentMethod;
            role.MandateReference = terms.MandateReference;
            role.MandateSignedOn = terms.MandateSignedOn;
            role.DeliveryTermKind = terms.DeliveryTermKind;
            role.Incoterm = terms.Incoterm;
            role.IncotermPlace = terms.IncotermPlace;
            role.DeliveryTermText = terms.DeliveryTermText;
            role.TradeConditions = terms.TradeConditions;
            role.TradeConditionsText = terms.TradeConditionsText;
        }

        // Bankrekeningen bijwerken zonder ooit te verwijderen (NormalizeBankAccounts eist dat elke opgeslagen rekening meekomt). Nieuwe
        // of in IBAN/houder/nummer gewijzigde rekeningen worden Unconfirmed; elke auditwaardige wijziging krijgt een auditregel met "wie".
        private void ApplyBankAccounts(Relation relation, List<BankAccountCommand> commands, string actor)
        {
            var byId = relation.BankAccounts.Where(a => a.Id != 0).ToDictionary(a => a.Id);
            for (var i = 0; i < commands.Count; i++)
            {
                var c = commands[i];
                var kind = BankChange(c, relation);
                if (c.Id != null && byId.TryGetValue(c.Id.Value, out var account))
                {
                    var (oldIban, oldHolder, oldOther) = (account.Iban, account.AccountHolder, account.OtherAccountNumber);
                    if (IdentityChanged(account, c))
                    {
                        account.ConfirmationStatus = BankAccountConfirmation.Unconfirmed;
                        account.ConfirmedAt = null;
                        account.ConfirmedBy = null;
                    }
                    account.Iban = c.Iban; account.OtherAccountNumber = c.OtherAccountNumber; account.Bic = c.Bic; account.AccountHolder = c.AccountHolder;
                    account.Currency = c.Currency; account.IsDefault = c.IsDefault; account.IsActive = c.IsActive; account.SortOrder = i;
                    if (kind != null) AddBankEvent(relation, account, kind, actor, oldIban, oldHolder, oldOther);
                }
                else
                {
                    account = new RelationBankAccount
                    {
                        Relation = relation, Iban = c.Iban, OtherAccountNumber = c.OtherAccountNumber, Bic = c.Bic, AccountHolder = c.AccountHolder, Currency = c.Currency,
                        IsDefault = c.IsDefault, IsActive = c.IsActive, SortOrder = i, ConfirmationStatus = BankAccountConfirmation.Unconfirmed, CreatedAt = DateTime.UtcNow
                    };
                    relation.BankAccounts.Add(account);
                    AddBankEvent(relation, account, BankAccountEventKind.Added, actor, null, null, null);
                }
            }
        }

        private void AddBankEvent(Relation relation, RelationBankAccount account, string kind, string actor, string oldIban, string oldHolder, string oldOther)
        {
            var bankEvent = new RelationBankAccountEvent
            {
                BankAccount = account, Relation = relation, Kind = kind, ActorReference = actor, OccurredAt = DateTime.UtcNow,
                OldIban = oldIban, NewIban = account.Iban, OldAccountHolder = oldHolder, NewAccountHolder = account.AccountHolder,
                OldOtherAccountNumber = oldOther, NewOtherAccountNumber = account.OtherAccountNumber
            };
            account.Events.Add(bankEvent);
            if (relation.Id != 0) bankEvent.RelationId = relation.Id;
        }

        // Bestaande rijen (op Id) bijwerken, nieuwe toevoegen, weggelaten verwijderen; rijen worden door niets anders gerefereerd.
        private static void SyncSet<TEntity, TCommand>(ICollection<TEntity> entities, List<TCommand> commands, Func<TEntity, int> entityId, Func<TCommand, int?> commandId,
            Action<TEntity, TCommand, int> update, Func<TCommand, int, TEntity> create)
        {
            var byId = entities.ToDictionary(entityId);
            var keep = new HashSet<int>();
            for (var i = 0; i < commands.Count; i++)
            {
                var id = commandId(commands[i]);
                if (id != null && byId.TryGetValue(id.Value, out var entity))
                {
                    update(entity, commands[i], i);
                    keep.Add(id.Value);
                }
                else
                {
                    entities.Add(create(commands[i], i));
                }
            }
            foreach (var stale in entities.Where(e => entityId(e) != 0 && !keep.Contains(entityId(e))).ToList())
            {
                entities.Remove(stale);
            }
        }

        private static void ResetVatVerification(Relation relation)
        {
            // Een gewijzigd nummer erft nooit de status van het vorige nummer; zonder nummer is verificatie niet van toepassing.
            relation.VatVerificationStatus = relation.VatNumber == null ? VatVerification.NotApplicable : VatVerification.NotChecked;
            relation.VatVerifiedNumber = null;
            relation.VatVerifiedAt = null;
        }

        // Spiegelkolommen op de rollen: uitsluitend hier geschreven. Legacy lezers en documentweergaven (Customer.Name, Vendor.Name)
        // blijven werken; dit is géén tweede wijzigbare bron.
        public static void SyncMirror(Relation relation)
        {
            var billing = DefaultAddress(relation, AddressPurpose.Billing) ?? DefaultAddress(relation, AddressPurpose.Main);
            var delivery = DefaultAddress(relation, AddressPurpose.Delivery) ?? DefaultAddress(relation, AddressPurpose.Main);
            var primary = relation.Contacts.Where(c => c.IsActive).OrderByDescending(c => c.IsPrimary).ThenBy(c => c.SortOrder).FirstOrDefault();
            var email = primary?.Channels.Where(c => c.Kind == ContactChannelKind.Email).OrderBy(c => c.SortOrder).Select(c => c.Value).FirstOrDefault() ?? relation.Email;
            var phone = primary?.Channels.Where(c => c.Kind == ContactChannelKind.Phone).OrderBy(c => c.SortOrder).Select(c => c.Value).FirstOrDefault() ?? relation.Phone;
            if (relation.Customer != null)
            {
                var c = relation.Customer;
                c.Name = relation.Name;
                c.VATNumber = relation.VatNumber;
                c.BillingAddress = FormatAddress(billing);
                c.ShippingAddress = FormatAddress(delivery);
                c.ContactPerson = primary?.Name;
                c.Email = email;
                c.Phone = phone;
            }
            if (relation.Vendor != null)
            {
                var v = relation.Vendor;
                v.Name = relation.Name;
                v.VATNumber = relation.VatNumber;
                v.BillingAddress = FormatAddress(billing);
                v.Email = relation.Email ?? email;
            }
        }

        private static RelationAddress DefaultAddress(Relation relation, string purpose) =>
            relation.Addresses.Where(a => a.IsActive && a.Purpose == purpose).OrderByDescending(a => a.IsDefault).ThenBy(a => a.SortOrder).FirstOrDefault();

        public static string FormatAddress(RelationAddress address)
        {
            if (address == null) return null;
            var parts = new[] { address.Line1, address.Line2, string.Join(" ", new[] { address.PostalCode, address.City }.Where(p => !string.IsNullOrWhiteSpace(p))), address.Region, address.CountryCode }
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        // ---------------- Replay, gelijkende namen, DTO ----------------

        private async Task<ServiceResult<RelationDto>> ResultForRetryAsync(Relation existing, string payload, Guid requestId, CancellationToken cancellationToken)
        {
            if (!string.Equals(existing.RequestPayload, payload, StringComparison.Ordinal))
            {
                return ServiceResult<RelationDto>.Conflict($"RequestId {requestId} was already used for a different relation.");
            }
            return ServiceResult<RelationDto>.Ok(await ToDtoAsync(existing, cancellationToken));
        }

        public async Task<List<SimilarRelationDto>> SimilarAsync(string name, int? excludeId, CancellationToken cancellationToken)
        {
            var target = CustomerService.NormalizeName(name);
            var all = await _db.Relations.AsNoTracking().Where(r => excludeId == null || r.Id != excludeId)
                .Select(r => new { r.Id, r.SearchCode, r.Name, r.TradeName, CustomerId = r.Customer == null ? (int?)null : r.Customer.Id, VendorId = r.Vendor == null ? (int?)null : r.Vendor.Id })
                .ToListAsync(cancellationToken);
            return all.Where(r => CustomerService.IsSimilar(target, CustomerService.NormalizeName(r.Name)) || (r.TradeName != null && CustomerService.IsSimilar(target, CustomerService.NormalizeName(r.TradeName))))
                .OrderBy(r => r.Name).Take(10)
                .Select(r => new SimilarRelationDto { Id = r.Id, SearchCode = r.SearchCode, Name = r.Name, CustomerId = r.CustomerId, VendorId = r.VendorId }).ToList();
        }

        private IQueryable<Relation> Query(bool tracking)
        {
            var query = _db.Relations
                .Include(r => r.Customer).ThenInclude(c => c.PaymentCondition)
                .Include(r => r.Vendor).ThenInclude(v => v.Ggns)
                .Include(r => r.Vendor).ThenInclude(v => v.PaymentCondition)
                .Include(r => r.BankAccounts)
                .Include(r => r.Addresses)
                .Include(r => r.Contacts).ThenInclude(c => c.Channels)
                .AsSplitQuery();
            return tracking ? query : query.AsNoTracking();
        }

        public static PaymentConditionSummaryDto ToSummary(PaymentCondition p) => p == null ? null : new PaymentConditionSummaryDto
        {
            Id = p.Id, Code = p.Code, DescriptionNl = p.DescriptionNl, DescriptionEn = p.DescriptionEn, Days = p.Days, Basis = p.Basis,
            DiscountPercent = p.DiscountPercent, DiscountDays = p.DiscountDays, IsActive = p.IsActive
        };

        // Blijvende signalen op de kaart (ook bij GET): onbevestigde standaardrekening, verzekering verlopen of bijna verlopen, verzekerde
        // limiet lager dan de interne limiet, inactieve betalingsconditie op een rol.
        public static List<RelationWarningDto> StandingWarnings(Relation r, DateOnly today)
        {
            var warnings = new List<RelationWarningDto>();
            var accounts = r.BankAccounts.OrderBy(a => a.SortOrder).ThenBy(a => a.Id).ToList();
            var defaultIndex = accounts.FindIndex(a => a.IsDefault && a.IsActive);
            if (defaultIndex >= 0 && accounts[defaultIndex].ConfirmationStatus != BankAccountConfirmation.Confirmed)
            {
                warnings.Add(new RelationWarningDto { Code = WarningDefaultBankAccountUnconfirmed, Field = $"bankAccounts[{defaultIndex}]", Message = "De standaardbankrekening is nog niet bevestigd; controleer haar bij de relatie voordat u betaalt of incasseert." });
            }
            if (r.Customer is { IsInsured: true } c)
            {
                if (RoleTermsRules.InsuranceExpired(c.InsuredUntil, today))
                    warnings.Add(new RelationWarningDto { Code = WarningInsuranceExpired, Field = "customer.insuredUntil", Message = $"De kredietverzekering is verlopen op {c.InsuredUntil:dd-MM-yyyy}." });
                else if (RoleTermsRules.InsuranceExpiresSoon(c.InsuredUntil, today))
                    warnings.Add(new RelationWarningDto { Code = WarningInsuranceExpiresSoon, Field = "customer.insuredUntil", Message = $"De kredietverzekering verloopt op {c.InsuredUntil:dd-MM-yyyy} (binnen {RoleTermsRules.InsuranceExpiryWarningDays} dagen)." });
                if (RoleTermsRules.InsuranceGap(c.CreditLimit, c.IsInsured, c.InsuredLimit) is { } gap)
                    warnings.Add(new RelationWarningDto { Code = WarningInsuranceGap, Field = "customer.insuredLimit", Message = $"De verzekerde limiet ligt EUR {CreditExposureService.Amount(gap)} onder de interne kredietlimiet." });
            }
            if (r.Customer?.PaymentCondition is { IsActive: false })
                warnings.Add(new RelationWarningDto { Code = WarningPaymentConditionInactive, Field = "customer.paymentConditionId", Message = "De betalingsconditie van de klantrol is inactief; kies bij gelegenheid een actieve conditie." });
            if (r.Vendor?.PaymentCondition is { IsActive: false })
                warnings.Add(new RelationWarningDto { Code = WarningPaymentConditionInactive, Field = "vendor.paymentConditionId", Message = "De betalingsconditie van de leveranciersrol is inactief; kies bij gelegenheid een actieve conditie." });
            return warnings;
        }

        private async Task<RelationDto> ToDtoAsync(Relation r, CancellationToken cancellationToken)
        {
            var events = (await _db.RelationBankAccountEvents.AsNoTracking().Where(e => e.RelationId == r.Id)
                    .OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Take(MaxBankAccountEvents).ToListAsync(cancellationToken))
                .Select(e => new RelationBankAccountEventDto
                {
                    Id = e.Id, BankAccountId = e.RelationBankAccountId, Kind = e.Kind, ActorReference = e.ActorReference, OccurredAt = DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc),
                    OldIban = e.OldIban, NewIban = e.NewIban, OldAccountHolder = e.OldAccountHolder, NewAccountHolder = e.NewAccountHolder,
                    OldOtherAccountNumber = e.OldOtherAccountNumber, NewOtherAccountNumber = e.NewOtherAccountNumber
                }).ToList();
            var usage = new RelationUsageDto();
            if (r.Customer != null)
            {
                var id = r.Customer.Id;
                usage.SalesOrders = await _db.SalesOrders.CountAsync(o => o.CustomerId == id, cancellationToken);
                usage.LegacyOrders = await _db.Orders.CountAsync(o => o.CustomerId == id, cancellationToken);
                usage.DirectTradeCommitments = await _db.SupplyCommitments.CountAsync(c => c.CustomerId == id, cancellationToken);
                if (usage.LegacyOrders > 0) usage.DefaultsInUse.Add("customerPaymentTermsDaysLegacyOrders");
            }
            if (r.Vendor != null)
            {
                var id = r.Vendor.Id;
                usage.PurchaseInvoices = await _db.IncomingInvoices.CountAsync(i => i.VendorId == id, cancellationToken);
                usage.SupplyCommitments = await _db.SupplyCommitments.CountAsync(c => c.VendorId == id, cancellationToken);
                // Inkoopfactuur: betaaltermijn en valuta worden bij invoer voorgesteld en per document vastgelegd (nooit met terugwerkende kracht).
                usage.DefaultsInUse.Add("vendorPaymentTermsDaysPurchaseInvoice");
                usage.DefaultsInUse.Add("vendorDefaultCurrencyPurchaseInvoice");
                // De basis van de leveranciersconditie (factuurdatum / einde maand) bepaalt de voorgestelde vervaldatum.
                usage.DefaultsInUse.Add("vendorPaymentConditionPurchaseInvoice");
                if (r.Vendor.Ggns.Any(g => g.IsActive)) usage.DefaultsInUse.Add("vendorGgnReceipt");
            }
            return new RelationDto
            {
                Id = r.Id,
                RequestId = r.RequestId?.ToString(),
                SearchCode = r.SearchCode,
                Name = r.Name,
                TradeName = r.TradeName,
                PreferredLanguage = r.PreferredLanguage,
                Website = r.Website,
                Email = r.Email,
                Phone = r.Phone,
                VatNumber = r.VatNumber,
                VatVerificationStatus = r.VatVerificationStatus,
                VatVerifiedNumber = r.VatVerifiedNumber,
                VatVerifiedAt = r.VatVerifiedAt,
                RegistrationNumber = r.RegistrationNumber,
                RegistrationCountry = r.RegistrationCountry,
                RegistrationType = r.RegistrationType,
                LegalForm = r.LegalForm,
                InternalNote = r.InternalNote,
                IsActive = r.IsActive,
                CreatedAt = DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                UpdatedAt = DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc),
                Version = r.Version,
                Customer = r.Customer == null ? null : new CustomerRoleDto
                {
                    CustomerId = r.Customer.Id, PaymentConditionId = r.Customer.PaymentConditionId, PaymentCondition = ToSummary(r.Customer.PaymentCondition),
                    PaymentTermsDays = r.Customer.PaymentTermsDays, PaymentMethod = r.Customer.PaymentMethod, MandateReference = r.Customer.MandateReference,
                    MandateSignedOn = r.Customer.MandateSignedOn, Currency = r.Customer.Currency, DeliveryTermKind = r.Customer.DeliveryTermKind, Incoterm = r.Customer.Incoterm,
                    IncotermPlace = r.Customer.IncotermPlace, DeliveryTermText = r.Customer.DeliveryTermText, TradeConditions = r.Customer.TradeConditions,
                    TradeConditionsText = r.Customer.TradeConditionsText, CreditLimit = r.Customer.CreditLimit, IsInsured = r.Customer.IsInsured, Insurer = r.Customer.Insurer,
                    InsurerText = r.Customer.InsurerText, InsuredLimit = r.Customer.InsuredLimit, InsurerReference = r.Customer.InsurerReference, InsuredUntil = r.Customer.InsuredUntil
                },
                Vendor = r.Vendor == null ? null : new VendorRoleDto
                {
                    VendorId = r.Vendor.Id, PaymentConditionId = r.Vendor.PaymentConditionId, PaymentCondition = ToSummary(r.Vendor.PaymentCondition),
                    PaymentTermsDays = r.Vendor.PaymentTermsDays, DefaultCurrency = r.Vendor.DefaultCurrency, PaymentMethod = r.Vendor.PaymentMethod,
                    MandateReference = r.Vendor.MandateReference, MandateSignedOn = r.Vendor.MandateSignedOn, DeliveryTermKind = r.Vendor.DeliveryTermKind, Incoterm = r.Vendor.Incoterm,
                    IncotermPlace = r.Vendor.IncotermPlace, DeliveryTermText = r.Vendor.DeliveryTermText, TradeConditions = r.Vendor.TradeConditions, TradeConditionsText = r.Vendor.TradeConditionsText,
                    Ggns = r.Vendor.Ggns.OrderBy(g => g.SortOrder).ThenBy(g => g.Id).Select(g => new VendorGgnDto
                    {
                        Id = g.Id, Ggn = g.Ggn, ProducerName = g.ProducerName, IsActive = g.IsActive, CheckedOn = g.CheckedOn, CheckedBy = g.CheckedBy,
                        CertificateValidUntil = g.CertificateValidUntil, HasGgnLabel = g.HasGgnLabel
                    }).ToList()
                },
                BankAccounts = r.BankAccounts.OrderBy(a => a.SortOrder).ThenBy(a => a.Id).Select(a => new RelationBankAccountDto
                {
                    Id = a.Id, Iban = a.Iban, OtherAccountNumber = a.OtherAccountNumber, Bic = a.Bic, AccountHolder = a.AccountHolder, Currency = a.Currency,
                    IsDefault = a.IsDefault, IsActive = a.IsActive, ConfirmationStatus = a.ConfirmationStatus,
                    ConfirmedAt = a.ConfirmedAt == null ? null : DateTime.SpecifyKind(a.ConfirmedAt.Value, DateTimeKind.Utc), ConfirmedBy = a.ConfirmedBy
                }).ToList(),
                BankAccountEvents = events,
                Warnings = StandingWarnings(r, _db.BusinessCalendar.Today()),
                Addresses = r.Addresses.OrderBy(a => a.SortOrder).ThenBy(a => a.Id).Select(a => new RelationAddressDto
                {
                    Id = a.Id, Purpose = a.Purpose, Label = a.Label, Line1 = a.Line1, Line2 = a.Line2, PostalCode = a.PostalCode, City = a.City, Region = a.Region, CountryCode = a.CountryCode, IsDefault = a.IsDefault, IsActive = a.IsActive
                }).ToList(),
                Contacts = r.Contacts.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).Select(c => new RelationContactDto
                {
                    Id = c.Id, Name = c.Name, Department = c.Department, JobTitle = c.JobTitle, IsPrimary = c.IsPrimary, IsActive = c.IsActive, Note = c.Note,
                    Channels = c.Channels.OrderBy(ch => ch.SortOrder).ThenBy(ch => ch.Id).Select(ch => new ContactChannelDto { Id = ch.Id, Kind = ch.Kind, Label = ch.Label, Value = ch.Value }).ToList()
                }).ToList(),
                Usage = usage
            };
        }

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
