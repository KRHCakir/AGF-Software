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

        private static readonly JsonSerializerOptions Canonical = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        private readonly AppDbContext _db;

        public RelationService(AppDbContext db) => _db = db;

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
            var command = await NormalizeAsync(request, null, errors, cancellationToken, existing?.Id, checkSearchCodeAvailability: existing == null);
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

            var relation = new Relation { RequestId = request.RequestId, RequestPayload = payload, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            Apply(relation, command);
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
            return ServiceResult<RelationDto>.Created(await ToDtoAsync(await Query(tracking: false).SingleAsync(r => r.Id == relation.Id, cancellationToken), cancellationToken));
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
            var command = await NormalizeAsync(request, relation, errors, cancellationToken);
            if (errors.Count > 0)
            {
                return ServiceResult<RelationDto>.Validation(ValidationErrors.ToArrays(errors));
            }
            if (relation.Version != request.ExpectedVersion)
            {
                return ServiceResult<RelationDto>.Conflict($"Relation {id} was changed by someone else (version {relation.Version}, expected {request.ExpectedVersion}); reload and apply your change again.");
            }
            Apply(relation, command);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
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
            return ServiceResult<RelationDto>.Ok(await ToDtoAsync(await Query(tracking: false).SingleAsync(r => r.Id == id, cancellationToken), cancellationToken));
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
            customer.PaymentTermsDays = dto.PaymentTermsDays;
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
        public sealed record GgnCommand(int? Id, string Ggn, string ProducerName, bool IsActive);
        public sealed record CustomerRoleCommand(int PaymentTermsDays);
        public sealed record VendorRoleCommand(int PaymentTermsDays, string DefaultCurrency, List<GgnCommand> Ggns);
        public sealed record RelationCommand(
            string SearchCode, string Name, string TradeName, string PreferredLanguage, string Website, string Email, string Phone, string VatNumber,
            string RegistrationNumber, string RegistrationCountry, string RegistrationType, string LegalForm, string InternalNote, bool IsActive,
            CustomerRoleCommand Customer, VendorRoleCommand Vendor, List<AddressCommand> Addresses, List<ContactCommand> Contacts);

        private async Task<RelationCommand> NormalizeAsync(SaveRelationDto request, Relation existing, Dictionary<string, List<string>> errors, CancellationToken cancellationToken, int? selfId = null, bool checkSearchCodeAvailability = true)
        {
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
            if (email != null && !RelationRules.LooksLikeEmail(email))
            {
                ValidationErrors.Add(errors, "email", "This does not look like an e-mail address.");
            }
            var phone = ValidationErrors.OptionalText(request.Phone, RelationRules.MaxPhoneLength, "phone", errors);
            var vat = RelationRules.NormalizeVat(request.VatNumber);
            if (vat != null && !RelationRules.IsVatShape(vat))
            {
                ValidationErrors.Add(errors, "vatNumber", "A VAT number starts with a two-letter country code (XI for Northern Ireland) followed by letters and digits; spaces, dots and dashes are ignored. This is a format check only, not a VIES verification.");
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
                var terms = request.Customer.PaymentTermsDays ?? existing?.Customer?.PaymentTermsDays ?? DefaultCustomerPaymentTermsDays;
                if (terms < 0 || terms > 365) ValidationErrors.Add(errors, "customer.paymentTermsDays", "Payment terms must be between 0 and 365 days.");
                customer = new CustomerRoleCommand(terms);
            }
            else if (existing?.Customer != null)
            {
                customer = new CustomerRoleCommand(existing.Customer.PaymentTermsDays);
            }
            VendorRoleCommand vendor = null;
            if (request.Vendor != null)
            {
                var terms = request.Vendor.PaymentTermsDays ?? existing?.Vendor?.PaymentTermsDays ?? DefaultVendorPaymentTermsDays;
                if (terms < 0 || terms > 365) ValidationErrors.Add(errors, "vendor.paymentTermsDays", "Payment terms must be between 0 and 365 days.");
                var currency = (Blank(request.Vendor.DefaultCurrency) ?? existing?.Vendor?.DefaultCurrency ?? DefaultVendorCurrency).ToUpperInvariant();
                if (currency.Length != 3 || !currency.All(char.IsLetter)) ValidationErrors.Add(errors, "vendor.defaultCurrency", "Currency must be a three-letter ISO code.");
                var ggns = new List<GgnCommand>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var ggnList = request.Vendor.Ggns ?? existing?.Vendor?.Ggns.OrderBy(g => g.SortOrder).Select(g => new VendorGgnDto { Id = g.Id, Ggn = g.Ggn, ProducerName = g.ProducerName, IsActive = g.IsActive }).ToList() ?? new List<VendorGgnDto>();
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
                    ggns.Add(new GgnCommand(item.Id, ggn, producer, item.IsActive));
                }
                vendor = new VendorRoleCommand(terms, currency, ggns);
            }
            else if (existing?.Vendor != null)
            {
                vendor = new VendorRoleCommand(existing.Vendor.PaymentTermsDays, existing.Vendor.DefaultCurrency ?? DefaultVendorCurrency,
                    existing.Vendor.Ggns.OrderBy(g => g.SortOrder).Select(g => new GgnCommand(g.Id, g.Ggn, g.ProducerName, g.IsActive)).ToList());
            }

            var addresses = NormalizeAddresses(request.Addresses, existing, errors);
            var contacts = NormalizeContacts(request.Contacts, existing, errors);

            return errors.Count > 0 ? null : new RelationCommand(searchCode, name, tradeName, language, website, email, phone, vat,
                registrationNumber, registrationCountry, registrationType, legalForm, note, isActive, customer, vendor, addresses, contacts);
        }

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

        private static List<ContactCommand> NormalizeContacts(List<RelationContactDto> input, Relation existing, Dictionary<string, List<string>> errors)
        {
            if (input == null)
            {
                return existing?.Contacts.OrderBy(c => c.SortOrder).Select(c => new ContactCommand(c.Id, c.Name, c.Department, c.JobTitle, c.IsPrimary, c.IsActive, c.Note,
                    c.Channels.OrderBy(ch => ch.SortOrder).Select(ch => new ChannelCommand(ch.Id, ch.Kind, ch.Label, ch.Value)).ToList())).ToList() ?? new List<ContactCommand>();
            }
            if (input.Count > RelationRules.MaxContacts) ValidationErrors.Add(errors, "contacts", $"At most {RelationRules.MaxContacts} contacts.");
            var stored = existing?.Contacts.ToDictionary(c => c.Id) ?? new Dictionary<int, RelationContact>();
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
                    if (value != null && kind == ContactChannelKind.Email && !RelationRules.LooksLikeEmail(value)) ValidationErrors.Add(errors, $"{chKey}.value", "This does not look like an e-mail address.");
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

        private static void Apply(Relation relation, RelationCommand command)
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
                relation.Customer.PaymentTermsDays = command.Customer.PaymentTermsDays;
            }
            if (command.Vendor != null)
            {
                relation.Vendor ??= new Vendor { Relation = relation, CreatedAt = DateTime.UtcNow };
                relation.Vendor.PaymentTermsDays = command.Vendor.PaymentTermsDays;
                relation.Vendor.DefaultCurrency = command.Vendor.DefaultCurrency;
                relation.Vendor.UpdatedAt = DateTime.UtcNow;
                SyncSet(relation.Vendor.Ggns, command.Vendor.Ggns, g => g.Id, g => g.Id,
                    (g, c, i) => { g.Ggn = c.Ggn; g.ProducerName = c.ProducerName; g.IsActive = c.IsActive; g.SortOrder = i; },
                    (c, i) => new VendorGgn { Vendor = relation.Vendor, Ggn = c.Ggn, ProducerName = c.ProducerName, IsActive = c.IsActive, SortOrder = i });
            }
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
                .Include(r => r.Customer)
                .Include(r => r.Vendor).ThenInclude(v => v.Ggns)
                .Include(r => r.Addresses)
                .Include(r => r.Contacts).ThenInclude(c => c.Channels)
                .AsSplitQuery();
            return tracking ? query : query.AsNoTracking();
        }

        private async Task<RelationDto> ToDtoAsync(Relation r, CancellationToken cancellationToken)
        {
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
                Customer = r.Customer == null ? null : new CustomerRoleDto { CustomerId = r.Customer.Id, PaymentTermsDays = r.Customer.PaymentTermsDays },
                Vendor = r.Vendor == null ? null : new VendorRoleDto
                {
                    VendorId = r.Vendor.Id, PaymentTermsDays = r.Vendor.PaymentTermsDays, DefaultCurrency = r.Vendor.DefaultCurrency,
                    Ggns = r.Vendor.Ggns.OrderBy(g => g.SortOrder).ThenBy(g => g.Id).Select(g => new VendorGgnDto { Id = g.Id, Ggn = g.Ggn, ProducerName = g.ProducerName, IsActive = g.IsActive }).ToList()
                },
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
