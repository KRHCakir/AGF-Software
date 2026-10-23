using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Relatiewerkplek: één onderneming met klant- en/of leveranciersrol, bestaande ID's behouden, gedeelde gegevens met één bron,
// idempotent aanmaken, versiebewaking, inactief in plaats van verwijderen, leveranciers-GGN's en de oude schrijfroutes via dezelfde bron.
public class RelationServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<ServiceResult<RelationDto>> CreateAsync(SaveRelationDto request)
    {
        await using var db = _factory.CreateContext();
        return await new RelationService(db).CreateAsync(request);
    }

    private async Task<ServiceResult<RelationDto>> UpdateAsync(int id, SaveRelationDto request)
    {
        await using var db = _factory.CreateContext();
        return await new RelationService(db).UpdateAsync(id, request);
    }

    private async Task<ServiceResult<RelationDto>> GetAsync(int id)
    {
        await using var db = _factory.CreateContext();
        return await new RelationService(db).GetAsync(id);
    }

    private async Task<RelationListDto> ListAsync(string role = null, string query = null, bool includeInactive = false)
    {
        await using var db = _factory.CreateContext();
        return await new RelationService(db).ListAsync(role, query, includeInactive, 1, 50);
    }

    // PUT vervangt de gedeelde gegevens volledig (zoals het formulier ze meestuurt): een wijziging vertrekt dus altijd van de huidige kaart.
    private static SaveRelationDto From(RelationDto c) => new()
    {
        ExpectedVersion = c.Version, SearchCode = c.SearchCode, Name = c.Name, TradeName = c.TradeName, PreferredLanguage = c.PreferredLanguage, Website = c.Website, Email = c.Email, Phone = c.Phone,
        VatNumber = c.VatNumber, RegistrationNumber = c.RegistrationNumber, RegistrationCountry = c.RegistrationCountry, RegistrationType = c.RegistrationType, LegalForm = c.LegalForm, InternalNote = c.InternalNote, IsActive = c.IsActive,
        Customer = c.Customer == null ? null : new SaveCustomerRoleDto { PaymentTermsDays = c.Customer.PaymentTermsDays },
        Vendor = c.Vendor == null ? null : new SaveVendorRoleDto { PaymentTermsDays = c.Vendor.PaymentTermsDays, DefaultCurrency = c.Vendor.DefaultCurrency, Ggns = c.Vendor.Ggns },
        Addresses = c.Addresses, Contacts = c.Contacts
    };

    private static SaveRelationDto Minimal(string name = "Fruitimport Zuid") => new() { RequestId = Guid.NewGuid(), Name = name };

    private static SaveRelationDto Full() => new()
    {
        RequestId = Guid.NewGuid(),
        SearchCode = "FRUITZ",
        Name = "Fruitimport Zuid B.V.",
        TradeName = "FruitZuid",
        PreferredLanguage = "nl",
        Website = "https://fruitzuid.example",
        Email = "info@fruitzuid.example",
        Phone = "+31 77 123 4567",
        VatNumber = "NL 8123.45.678.B01",
        RegistrationNumber = "12345678",
        RegistrationCountry = "nl",
        RegistrationType = "KvK",
        LegalForm = "BV",
        InternalNote = "Betaalt altijd op tijd",
        Customer = new SaveCustomerRoleDto { PaymentTermsDays = 21 },
        Vendor = new SaveVendorRoleDto { PaymentTermsDays = 45, DefaultCurrency = "eur", Ggns = new() { new() { Ggn = "4049929000001", ProducerName = "Finca Uno" }, new() { Ggn = "4049 929 000002" } } },
        Addresses = new()
        {
            new() { Purpose = "Main", Line1 = "Marktweg 1", PostalCode = "5912 AB", City = "Venlo", CountryCode = "nl", IsDefault = true },
            new() { Purpose = "Billing", Line1 = "Postbus 12", PostalCode = "5900 AA", City = "Venlo", CountryCode = "NL" },
            new() { Purpose = "Delivery", Label = "Magazijn Venlo", Line1 = "Loswal 3", City = "Venlo", CountryCode = "NL", IsDefault = true },
            new() { Purpose = "Delivery", Label = "Depot Antwerpen", Line1 = "Kaai 100", PostalCode = "2000", City = "Antwerpen", CountryCode = "BE" },
        },
        Contacts = new()
        {
            new() { Name = "Anja Peeters", Department = "inkoop", IsPrimary = true, Channels = new() { new() { Kind = "Phone", Label = "mobiel", Value = "06-11111111" }, new() { Kind = "Phone", Label = "kantoor", Value = "077-1111111" }, new() { Kind = "Email", Label = "werk", Value = "anja@fruitzuid.example" } } },
            new() { Name = "Bert Jansen", Department = "transport", Channels = new() { new() { Kind = "Email", Value = "bert@fruitzuid.example" } } },
        }
    };

    [Fact]
    public async Task A_Relation_Exists_With_Only_A_Name_Active_Without_Roles_And_Vat_Verification_Not_Applicable()
    {
        var created = await CreateAsync(Minimal());
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        var dto = created.Value;
        Assert.Equal(("Fruitimport Zuid", true, (CustomerRoleDto)null, (VendorRoleDto)null), (dto.Name, dto.IsActive, dto.Customer, dto.Vendor));
        Assert.Equal(VatVerification.NotApplicable, dto.VatVerificationStatus);
        Assert.Empty(dto.Addresses);
        Assert.Empty(dto.Contacts);
        Assert.Equal(0, dto.Version);
        var missing = await CreateAsync(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "  " });
        Assert.Equal(ServiceResultStatus.Validation, missing.Status);
        Assert.Contains("name", missing.Errors.Keys);
    }

    [Fact]
    public async Task A_Full_Relation_With_Both_Roles_Addresses_Contacts_And_Ggns_Round_Trips_And_Mirrors_To_The_Roles()
    {
        var created = await CreateAsync(Full());
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        var dto = created.Value;
        Assert.Equal(("FRUITZ", "Fruitimport Zuid B.V.", "FruitZuid", "nl", "NL812345678B01", "NL", VatVerification.NotChecked), (dto.SearchCode, dto.Name, dto.TradeName, dto.PreferredLanguage, dto.VatNumber, dto.RegistrationCountry, dto.VatVerificationStatus));
        Assert.Equal(21, dto.Customer!.PaymentTermsDays);
        Assert.Equal((45, "EUR"), (dto.Vendor!.PaymentTermsDays, dto.Vendor.DefaultCurrency));
        Assert.Equal(new[] { "4049929000001", "4049929000002" }, dto.Vendor.Ggns.Select(g => g.Ggn));
        Assert.Equal(4, dto.Addresses.Count);
        Assert.Equal(new[] { "Main", "Billing", "Delivery", "Delivery" }, dto.Addresses.Select(a => a.Purpose));
        Assert.True(dto.Addresses[1].IsDefault, "the only billing address becomes the default");
        Assert.Equal(new[] { "NL", "NL", "NL", "BE" }, dto.Addresses.Select(a => a.CountryCode));
        Assert.Equal(2, dto.Contacts.Count);
        Assert.Equal(3, dto.Contacts[0].Channels.Count);
        Assert.Contains("vendorPaymentTermsDaysPurchaseInvoice", dto.Usage.DefaultsInUse);
        Assert.Contains("vendorGgnReceipt", dto.Usage.DefaultsInUse);

        // Spiegel op de rollen: dezelfde onderneming, bestaande lezers zien naam, btw, adres en contact.
        await using var db = _factory.CreateContext();
        var customer = await db.Customers.SingleAsync();
        var vendor = await db.Vendors.SingleAsync();
        Assert.Equal(customer.RelationId, vendor.RelationId);
        Assert.Equal(1, await db.Relations.CountAsync());
        Assert.Equal(("Fruitimport Zuid B.V.", "NL812345678B01", "Postbus 12, 5900 AA Venlo, NL", "Loswal 3, Venlo, NL", "Anja Peeters", "anja@fruitzuid.example", "06-11111111"),
            (customer.Name, customer.VATNumber, customer.BillingAddress, customer.ShippingAddress, customer.ContactPerson, customer.Email, customer.Phone));
        Assert.Equal(("Fruitimport Zuid B.V.", "NL812345678B01", "Postbus 12, 5900 AA Venlo, NL", "info@fruitzuid.example", "EUR", 45), (vendor.Name, vendor.VATNumber, vendor.BillingAddress, vendor.Email, vendor.DefaultCurrency, vendor.PaymentTermsDays));
    }

    [Fact]
    public async Task Search_Codes_Are_Unique_Regardless_Of_Case_With_An_Understandable_Error_Also_Against_A_Concurrent_Writer()
    {
        var first = await CreateAsync(Full());
        Assert.Equal(ServiceResultStatus.Created, first.Status);
        var request = Minimal("Andere onderneming");
        request.SearchCode = "fruitz";
        var clash = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, clash.Status);
        Assert.Contains($"relation #{first.Value.Id}", clash.Errors["searchCode"][0]);
        Assert.Contains("regardless of case", clash.Errors["searchCode"][0]);
        var badShape = Minimal("Nog een");
        badShape.SearchCode = "fruit z";
        Assert.Equal(ServiceResultStatus.Validation, (await CreateAsync(badShape)).Status);

        // Gelijktijdig: de controle vooraf ziet niets, de unieke index vangt de tweede schrijver; nette 409.
        var race = Minimal("Race");
        race.SearchCode = "RACE1";
        await using (var db = _factory.CreateContextBeforeGuard(async () =>
        {
            await using var other = _factory.CreateContext();
            other.Relations.Add(new Relation { Name = "Eerste", SearchCode = "race1", SearchCodeKey = "race1" });
            await other.SaveChangesAsync();
        }))
        {
            var result = await new RelationService(db).CreateAsync(race);
            Assert.Equal(ServiceResultStatus.Conflict, result.Status);
            Assert.Contains("RACE1", result.Message);
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.Relations.CountAsync(r => r.SearchCodeKey == "race1"));
    }

    [Fact]
    public async Task Names_Are_Not_Unique_But_Similar_Names_Need_An_Explicit_Acknowledgement_That_Points_At_The_Existing_Relation()
    {
        var existing = (await CreateAsync(Full())).Value;
        var similar = await CreateAsync(Minimal("fruitimport zuid"));
        Assert.Equal(ServiceResultStatus.Conflict, similar.Status);
        var body = similar.ConflictBody;
        Assert.NotNull(body);
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        Assert.Contains(RelationService.SimilarNamesCode, json);
        Assert.Contains($"\"Id\":{existing.Id}", json);
        var acknowledged = Minimal("fruitimport zuid");
        acknowledged.AcknowledgeSimilarNames = true;
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(acknowledged)).Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(2, await db.Relations.CountAsync());
    }

    [Fact]
    public async Task Identical_Replay_Returns_The_Same_Relation_And_A_Different_Payload_With_The_Same_RequestId_Is_Conflict()
    {
        var request = Full();
        var first = await CreateAsync(request);
        var replay = await CreateAsync(request);
        Assert.Equal((ServiceResultStatus.Created, ServiceResultStatus.Ok), (first.Status, replay.Status));
        Assert.Equal(first.Value.Id, replay.Value.Id);
        request.Phone = "+31 77 000 0000";
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(request)).Status);
        await using var db = _factory.CreateContext();
        Assert.Equal(1, await db.Relations.CountAsync());
    }

    [Fact]
    public async Task Update_Needs_The_Expected_Version_And_A_Stale_Version_Is_Rejected_Without_Change()
    {
        var created = (await CreateAsync(Full())).Value;
        var a = From(created); a.TradeName = "FZ"; a.Addresses = null; a.Contacts = null;
        var b = From(created); b.TradeName = "ZF"; b.Addresses = null; b.Contacts = null;
        var okA = await UpdateAsync(created.Id, a);
        Assert.Equal(ServiceResultStatus.Ok, okA.Status);
        Assert.Equal(1, okA.Value.Version);
        var stale = await UpdateAsync(created.Id, b);
        Assert.Equal(ServiceResultStatus.Conflict, stale.Status);
        Assert.Contains("changed by someone else", stale.Message);
        Assert.Equal(ServiceResultStatus.Validation, (await UpdateAsync(created.Id, new SaveRelationDto { Name = "x" })).Status);
        var current = (await GetAsync(created.Id)).Value;
        Assert.Equal(("FZ", 1, 4, 2), (current.TradeName, current.Version, current.Addresses.Count, current.Contacts.Count));
        Assert.Equal(new[] { "4049929000001", "4049929000002" }, current.Vendor!.Ggns.Select(g => g.Ggn));
    }

    [Fact]
    public async Task A_Second_Role_Is_Added_To_The_Existing_Relation_Keeping_Ids_And_Never_Creating_A_Second_Company()
    {
        // Snelle klantaanmaak vanuit Verkoop (bestaande route): relatie + klantrol.
        int customerId;
        await using (var db = _factory.CreateContext())
        {
            var created = await new CustomerService(db).CreateAsync(new CreateOperationalCustomerDto { RequestId = Guid.NewGuid(), Name = "Groenten Noord", ContactPerson = "Piet", Phone = "06-2", ShippingAddress = "Kade 1, Harlingen" });
            Assert.Equal(ServiceResultStatus.Created, created.Status);
            customerId = created.Value.Id;
        }
        int relationId;
        await using (var db = _factory.CreateContext())
        {
            var customer = await db.Customers.Include(c => c.Relation).SingleAsync(c => c.Id == customerId);
            relationId = customer.RelationId;
            Assert.Equal("Groenten Noord", customer.Relation.Name);
            Assert.Equal("Piet", customer.ContactPerson);
        }
        await using (var db = _factory.CreateContext())
        {
            var service = new RelationService(db);
            var added = await service.AddRoleAsync(relationId, "vendor");
            Assert.Equal(ServiceResultStatus.Created, added.Status);
            Assert.Equal(customerId, added.Value.Customer!.CustomerId);
            Assert.NotNull(added.Value.Vendor);
            var again = await service.AddRoleAsync(relationId, "vendor");
            Assert.Equal(ServiceResultStatus.Ok, again.Status);
            Assert.Equal(added.Value.Vendor!.VendorId, again.Value.Vendor!.VendorId);
            Assert.Equal(ServiceResultStatus.Validation, (await service.AddRoleAsync(relationId, "partner")).Status);
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.Relations.CountAsync());
        var vendor = await verify.Vendors.SingleAsync();
        Assert.Equal((relationId, "Groenten Noord", "EUR"), (vendor.RelationId, vendor.Name, vendor.DefaultCurrency));
        Assert.Equal(1, (await ListAsync("vendor")).Total);
        Assert.Equal(1, (await ListAsync("customer")).Total);
    }

    [Fact]
    public async Task Legacy_Routes_Write_Through_The_Relation_And_Direct_Mirror_Edits_Are_Refused()
    {
        int customerId;
        await using (var db = _factory.CreateContext())
        {
            var customer = await new RelationService(db).CreateLegacyCustomerAsync(new CreateCustomerDto { Name = "Oude route", VATNumber = "NL 1", BillingAddress = "Straat 1, Stad", ContactPerson = "Kees", Email = "kees@x.nl", Phone = "1", PaymentTermsDays = 14 });
            customerId = customer.Id;
        }
        await using (var db = _factory.CreateContext())
        {
            var updated = await new RelationService(db).UpdateLegacyCustomerAsync(customerId, new CreateCustomerDto { Name = "Oude route hernoemd", VATNumber = "NL 1", BillingAddress = "Straat 2, Stad", ContactPerson = "Kees", Email = "kees@x.nl", Phone = "2", PaymentTermsDays = 21 });
            Assert.Equal(("Oude route hernoemd", "Straat 2, Stad", "2", 21), (updated.Name, updated.BillingAddress, updated.Phone, updated.PaymentTermsDays));
        }
        await using (var db = _factory.CreateContext())
        {
            var relation = await db.Relations.Include(r => r.Addresses).Include(r => r.Contacts).ThenInclude(c => c.Channels).SingleAsync();
            Assert.Equal("Oude route hernoemd", relation.Name);
            Assert.Equal("Straat 2, Stad", Assert.Single(relation.Addresses, a => a.Purpose == "Billing").Line1);
            Assert.Equal("2", Assert.Single(relation.Contacts).Channels.Single(c => c.Kind == "Phone").Value);
            // Een schrijver die de spiegelkolom rechtstreeks aanpast omzeilt de bron: geweigerd, ook met andere wijzigingen in dezelfde save.
            var customer = await db.Customers.SingleAsync();
            customer.Name = "Rechtstreeks";
            customer.PaymentTermsDays = 30;
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
            Assert.Contains("only change through the relation", exception.Message);
        }
        await using (var db = _factory.CreateContext())
        {
            // Rolspecifieke velden blijven vrij.
            (await db.Customers.SingleAsync()).PaymentTermsDays = 30;
            await db.SaveChangesAsync();
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal(("Oude route hernoemd", 30), ((await verify.Customers.SingleAsync()).Name, (await verify.Customers.SingleAsync()).PaymentTermsDays));
    }

    [Fact]
    public async Task A_Role_Added_Without_A_Relation_Gets_Its_Own_Relation_From_Its_Flat_Fields_Never_Merged()
    {
        await using (var db = _factory.CreateContext())
        {
            db.Customers.Add(new Customer { Name = "Seed klant", ContactPerson = "Jan", Email = "jan@x.nl", BillingAddress = "Markt 1, Amsterdam" });
            db.Vendors.Add(new Vendor { Name = "Seed klant", Email = "sales@x.nl", VATNumber = "NL2" });
            await db.SaveChangesAsync();
        }
        await using var verify = _factory.CreateContext();
        Assert.Equal(2, await verify.Relations.CountAsync());
        var customerRelation = await verify.Relations.Include(r => r.Customer).Include(r => r.Contacts).ThenInclude(c => c.Channels).Include(r => r.Addresses).SingleAsync(r => r.Customer != null);
        Assert.Equal(("Jan", "jan@x.nl", "Markt 1, Amsterdam"), (Assert.Single(customerRelation.Contacts).Name, Assert.Single(customerRelation.Contacts).Channels.Single().Value, Assert.Single(customerRelation.Addresses).Line1));
        var vendorRelation = await verify.Relations.Include(r => r.Vendor).SingleAsync(r => r.Vendor != null);
        Assert.Equal(("Seed klant", "sales@x.nl", "NL2"), (vendorRelation.Name, vendorRelation.Email, vendorRelation.VatNumber));
    }

    [Fact]
    public async Task An_Inactive_Relation_Refuses_New_Documents_And_Keeps_Existing_Ones_Readable_And_Searchable()
    {
        var created = (await CreateAsync(Full())).Value;
        int variantId;
        await using (var db = _factory.CreateContext())
        {
            var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.5m };
            db.Add(variant);
            await db.SaveChangesAsync();
            variantId = variant.Id;
        }
        int orderId;
        await using (var db = _factory.CreateContext())
        {
            var order = await new SalesOrderService(db).CreateAsync(new CreateSalesOrderDto { RequestId = Guid.NewGuid(), CustomerId = created.Customer!.CustomerId, RequestedDeliveryDate = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(2)), ActorReference = "office-1", Lines = new() { new() { ProductVariantId = variantId, Quantity = 10m, Unit = "box" } } });
            Assert.Equal(ServiceResultStatus.Created, order.Status);
            orderId = order.Value.Id;
        }
        var deactivate = From(created);
        deactivate.IsActive = false;
        var inactive = await UpdateAsync(created.Id, deactivate);
        Assert.Equal(ServiceResultStatus.Ok, inactive.Status);
        Assert.False(inactive.Value.IsActive);

        await using (var db = _factory.CreateContext())
        {
            var refusedOrder = await new SalesOrderService(db).CreateAsync(new CreateSalesOrderDto { RequestId = Guid.NewGuid(), CustomerId = created.Customer.CustomerId, RequestedDeliveryDate = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.FromHours(2)), ActorReference = "office-1", Lines = new() { new() { ProductVariantId = variantId, Quantity = 1m, Unit = "box" } } });
            Assert.Equal(ServiceResultStatus.Validation, refusedOrder.Status);
            Assert.Contains("inactive", refusedOrder.Errors["customerId"][0]);
            var refusedInvoice = await new PurchaseInvoiceService(db).CreateAsync(new CreatePurchaseInvoiceDto { RequestId = Guid.NewGuid(), VendorId = created.Vendor!.VendorId, InvoiceNumber = "X-1", InvoiceDate = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc), Lines = new() { new() { Description = "A", Quantity = 1m, UnitPrice = 1m } } });
            Assert.Equal(ServiceResultStatus.Validation, refusedInvoice.Status);
            Assert.Contains("inactive", refusedInvoice.Errors["vendorId"][0]);
            var refusedSupply = await new SupplyCommitmentService(db).CreateAsync(new CreateSupplyCommitmentDto { RequestId = Guid.NewGuid(), VendorId = created.Vendor.VendorId, Route = "Warehouse", Origin = "Peru", ExpectedAt = DateTimeOffset.UtcNow.AddDays(1), Lines = new() { new() { ProductVariantId = variantId, ExpectedQuantity = 1m, ExpectedUnit = "box" } } });
            Assert.NotEqual(ServiceResultStatus.Created, refusedSupply.Status);
            // Historie blijft leesbaar met de naam van de relatie.
            var existing = await new SalesOrderService(db).GetAsync(orderId);
            Assert.Equal(ServiceResultStatus.Ok, existing.Status);
            Assert.Equal("Fruitimport Zuid B.V.", existing.Value.CustomerName);
        }
        Assert.Equal(0, (await ListAsync()).Total);
        Assert.Equal(1, (await ListAsync(includeInactive: true)).Total);
        Assert.False((await ListAsync(includeInactive: true, query: "fruitz")).Items.Single().IsActive);
    }

    [Fact]
    public async Task Address_Rules_Country_For_New_Or_Changed_One_Default_Per_Purpose_Legacy_Text_May_Stay()
    {
        var request = Minimal();
        request.Addresses = new() { new() { Purpose = "Delivery", Line1 = "Zonder land 1", City = "Lille" } };
        var noCountry = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, noCountry.Status);
        Assert.Contains("addresses[0].countryCode", noCountry.Errors.Keys);
        request.Addresses = new() { new() { Purpose = "Delivery", Line1 = "A", CountryCode = "FR", IsDefault = true }, new() { Purpose = "Delivery", Line1 = "B", CountryCode = "FR", IsDefault = true } };
        var twoDefaults = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, twoDefaults.Status);
        Assert.Contains("addresses", twoDefaults.Errors.Keys);
        request.Addresses = new() { new() { Purpose = "Delivery", Line1 = "A", CountryCode = "FR" }, new() { Purpose = "Delivery", Line1 = "B", CountryCode = "fr" }, new() { Purpose = "Main", Line1 = "", City = "", PostalCode = "", CountryCode = "FR" } };
        var empty = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, empty.Status);
        Assert.Contains("addresses[2].line1", empty.Errors.Keys);
        request.Addresses.RemoveAt(2);
        var created = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(new[] { true, false }, created.Value.Addresses.Select(a => a.IsDefault));

        // Een uit vrije tekst overgenomen legacy-adres (land onbekend) mag onveranderd blijven staan, maar niet gewijzigd worden zonder land.
        int legacyId;
        await using (var db = _factory.CreateContext())
        {
            var relation = await db.Relations.Include(r => r.Addresses).SingleAsync();
            var legacy = new RelationAddress { Relation = relation, Purpose = "Billing", Line1 = "Marktweg 1, Venlo", IsDefault = true, SortOrder = 5 };
            relation.Addresses.Add(legacy);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;
        }
        var current = (await GetAsync(created.Value.Id)).Value;
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(current.Id, From(current))).Status);
        current = (await GetAsync(created.Value.Id)).Value;
        var legacyAddress = current.Addresses.Single(a => a.Id == legacyId);
        legacyAddress.City = "Venlo";
        var changed = await UpdateAsync(current.Id, From(current));
        Assert.Equal(ServiceResultStatus.Validation, changed.Status);
        Assert.Contains(changed.Errors.Keys, k => k.EndsWith(".countryCode"));
        legacyAddress.CountryCode = "NL";
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(current.Id, From(current))).Status);
    }

    [Fact]
    public async Task Contacts_Keep_Several_Phones_And_Emails_Reject_Bad_Emails_And_Allow_One_Primary()
    {
        var request = Minimal();
        request.Contacts = new() { new() { Name = "A", IsPrimary = true }, new() { Name = "B", IsPrimary = true } };
        var twoPrimary = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, twoPrimary.Status);
        Assert.Contains("contacts", twoPrimary.Errors.Keys);
        request.Contacts = new() { new() { Name = "A", Channels = new() { new() { Kind = "Email", Value = "geen-mail" }, new() { Kind = "Fax", Value = "1" }, new() { Kind = "Phone", Value = "" } } } };
        var bad = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, bad.Status);
        Assert.Equal(new[] { "contacts[0].channels[0].value", "contacts[0].channels[1].kind", "contacts[0].channels[2].value" }, bad.Errors.Keys.OrderBy(k => k));
        request.Contacts = new() { new() { Name = "A", Channels = new() { new() { Kind = "Phone", Label = "mobiel", Value = "06-1" }, new() { Kind = "Phone", Label = "kantoor", Value = "077-1" }, new() { Kind = "Email", Value = "a@x.nl" }, new() { Kind = "Email", Label = "privé", Value = "a@y.nl" } } } };
        var created = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        var contact = Assert.Single(created.Value.Contacts);
        Assert.Equal(new[] { "06-1", "077-1", "a@x.nl", "a@y.nl" }, contact.Channels.Select(c => c.Value));
        // Bewerken: een kanaal verwijderen en één toevoegen; de rest blijft op zijn Id.
        var current = created.Value;
        current.Contacts[0].Channels.RemoveAt(1);
        current.Contacts[0].Channels.Add(new ContactChannelDto { Kind = "Phone", Label = "thuis", Value = "010-1" });
        var updated = await UpdateAsync(current.Id, From(current));
        Assert.Equal(ServiceResultStatus.Ok, updated.Status);
        Assert.Equal(new[] { "06-1", "a@x.nl", "a@y.nl", "010-1" }, updated.Value.Contacts.Single().Channels.Select(c => c.Value));
        Assert.Equal(contact.Channels[0].Id, updated.Value.Contacts.Single().Channels[0].Id);
    }

    [Fact]
    public async Task Vendor_Ggns_Are_Thirteen_Digit_Text_Unique_Per_Supplier_And_Offered_To_Receipts_Only_When_Active()
    {
        var request = Full();
        request.Vendor!.Ggns = new() { new() { Ggn = "123" }, new() { Ggn = "4049929000001" }, new() { Ggn = "4049 929 000001" } };
        var bad = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, bad.Status);
        Assert.Equal(new[] { "vendor.ggns[0].ggn", "vendor.ggns[2].ggn" }, bad.Errors.Keys.OrderBy(k => k));
        Assert.Contains("CoC number is not a GGN", bad.Errors["vendor.ggns[0].ggn"][0]);
        var created = (await CreateAsync(Full())).Value;
        int variantId;
        await using (var db = _factory.CreateContext())
        {
            var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 12.5m };
            db.Add(variant);
            await db.SaveChangesAsync();
            variantId = variant.Id;
        }
        await using (var db = _factory.CreateContext())
        {
            var supply = await new SupplyCommitmentService(db).CreateAsync(new CreateSupplyCommitmentDto { RequestId = Guid.NewGuid(), VendorId = created.Vendor!.VendorId, Route = "Warehouse", Origin = "Peru", ExpectedAt = DateTimeOffset.UtcNow.AddDays(1), Lines = new() { new() { ProductVariantId = variantId, ExpectedQuantity = 1m, ExpectedUnit = "box" } } });
            Assert.Equal(ServiceResultStatus.Created, supply.Status);
            Assert.Equal(new[] { ("4049929000001", "Finca Uno"), ("4049929000002", (string)null) }, supply.Value.VendorGgns.Select(g => (g.Ggn, g.ProducerName)));
            Assert.DoesNotContain(typeof(SupplyCommitmentLineDto).GetProperties(), p => p.Name.Contains("Ggn"));
        }
        // Eén nummer inactief: alleen het actieve wordt nog aangeboden (precies één -> automatisch voorstel in de UI).
        var current = (await GetAsync(created.Id)).Value;
        current.Vendor!.Ggns[1].IsActive = false;
        var updated = await UpdateAsync(current.Id, From(current));
        Assert.Equal(ServiceResultStatus.Ok, updated.Status);
        await using (var db = _factory.CreateContext())
        {
            var list = await new SupplyCommitmentService(db).ListAsync(new SupplyCommitmentFilter { VendorId = created.Vendor.VendorId });
            Assert.Equal(new[] { "4049929000001" }, list.Value.Single().VendorGgns.Select(g => g.Ggn));
        }
    }

    [Fact]
    public async Task A_Changed_Vat_Number_Never_Inherits_The_Previous_Verification_And_Shape_Is_Checked_Not_Verified()
    {
        var created = (await CreateAsync(Full())).Value;
        await using (var db = _factory.CreateContext())
        {
            // Vervolgstap (VIES) zou dit zetten; hier alleen om het erven te toetsen.
            var relation = await db.Relations.SingleAsync();
            relation.VatVerificationStatus = VatVerification.Valid;
            relation.VatVerifiedNumber = relation.VatNumber;
            relation.VatVerifiedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var current = (await GetAsync(created.Id)).Value;
        Assert.Equal(VatVerification.Valid, current.VatVerificationStatus);
        var sameNumber = From(current);
        sameNumber.VatNumber = "nl 8123 45678 b01";
        var same = await UpdateAsync(current.Id, sameNumber);
        Assert.Equal(VatVerification.Valid, same.Value.VatVerificationStatus);
        var newNumber = From(same.Value);
        newNumber.VatNumber = "XI123456789";
        var changed = await UpdateAsync(current.Id, newNumber);
        Assert.Equal((VatVerification.NotChecked, (string)null, (DateTime?)null, "XI123456789"), (changed.Value.VatVerificationStatus, changed.Value.VatVerifiedNumber, changed.Value.VatVerifiedAt, changed.Value.VatNumber));
        var badNumber = From(changed.Value);
        badNumber.VatNumber = "12345";
        var bad = await UpdateAsync(current.Id, badNumber);
        Assert.Equal(ServiceResultStatus.Validation, bad.Status);
        Assert.Contains("not a VIES verification", bad.Errors["vatNumber"][0]);
    }

    [Fact]
    public async Task Vendor_Defaults_Are_Proposals_Existing_Purchase_Invoices_And_Replays_Keep_Their_Own_Values()
    {
        var created = (await CreateAsync(Full())).Value;
        var requestId = Guid.NewGuid();
        CreatePurchaseInvoiceDto Invoice() => new()
        {
            RequestId = requestId, VendorId = created.Vendor!.VendorId, InvoiceNumber = "GR-1", InvoiceDate = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc),
            PaymentTermsDays = 45, Currency = "EUR", Lines = new() { new() { Description = "Transport", Quantity = 1m, UnitPrice = 100m, VatPercentage = 21m } }
        };
        int invoiceId;
        await using (var db = _factory.CreateContext())
        {
            var invoice = await new PurchaseInvoiceService(db).CreateAsync(Invoice());
            Assert.Equal(ServiceResultStatus.Created, invoice.Status);
            Assert.Equal((45, "EUR"), (invoice.Value.PaymentTermsDays, invoice.Value.Currency));
            invoiceId = invoice.Value.Id;
        }
        // Default wijzigen: het bestaande document en zijn replay veranderen niet.
        var current = (await GetAsync(created.Id)).Value;
        var newDefaults = From(current);
        newDefaults.Vendor!.PaymentTermsDays = 60;
        newDefaults.Vendor.DefaultCurrency = "USD";
        var updated = await UpdateAsync(current.Id, newDefaults);
        Assert.Equal((60, "USD"), (updated.Value.Vendor!.PaymentTermsDays, updated.Value.Vendor.DefaultCurrency));
        await using (var db = _factory.CreateContext())
        {
            var service = new PurchaseInvoiceService(db);
            var replay = await service.CreateAsync(Invoice());
            Assert.Equal((ServiceResultStatus.Ok, invoiceId, 45, "EUR"), (replay.Status, replay.Value.Id, replay.Value.PaymentTermsDays, replay.Value.Currency));
            var vendors = await service.VendorsAsync();
            Assert.Equal((60, "USD", "FRUITZ", true), (vendors.Single().PaymentTermsDays, vendors.Single().DefaultCurrency, vendors.Single().SearchCode, vendors.Single().IsActive));
        }
        Assert.Equal(1, (await GetAsync(created.Id)).Value.Usage.PurchaseInvoices);
    }

    [Fact]
    public async Task List_Filters_By_Role_And_Searches_Code_Name_Trade_Name_City_And_Contact_With_Paging()
    {
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(Full())).Status);
        var customerOnly = Minimal("Alleen klant");
        customerOnly.Customer = new SaveCustomerRoleDto();
        customerOnly.SearchCode = "KLANT1";
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(customerOnly)).Status);
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(Minimal("Zonder rol"))).Status);
        Assert.Equal(3, (await ListAsync()).Total);
        Assert.Equal(2, (await ListAsync("customer")).Total);
        Assert.Equal(1, (await ListAsync("vendor")).Total);
        Assert.Equal("Fruitimport Zuid B.V.", (await ListAsync(query: "venlo")).Items.Single().Name);
        Assert.Equal("Fruitimport Zuid B.V.", (await ListAsync(query: "anja")).Items.Single().Name);
        Assert.Equal("Fruitimport Zuid B.V.", (await ListAsync(query: "fruitzuid")).Items.Single().Name);
        Assert.Equal("Alleen klant", (await ListAsync(query: "klant1")).Items.Single().Name);
        var item = (await ListAsync(query: "FRUITZ")).Items.Single();
        Assert.Equal(("Venlo", "NL", "Anja Peeters", true), (item.City, item.CountryCode, item.PrimaryContactName, item.CustomerId != null && item.VendorId != null));
        await using var db = _factory.CreateContext();
        var page = await new RelationService(db).ListAsync(null, null, false, 2, 2);
        Assert.Equal((3, 2, 1), (page.Total, page.Page, page.Items.Count));
    }

    // ---- Review #47 ----

    [Fact]
    public async Task Two_Concurrent_Additions_Of_The_Same_Role_End_With_One_Role_And_No_Unhandled_Exception()
    {
        var created = (await CreateAsync(Minimal("Groenten Noord"))).Value;
        int otherVendorId = 0;
        ServiceResult<RelationDto> result;
        await using (var db = _factory.CreateContextBeforeGuard(async () =>
        {
            // De andere gebruiker heeft ook "nog geen leveranciersrol" gelezen en slaat als eerste op.
            await using var other = _factory.CreateContext();
            var first = await new RelationService(other).AddRoleAsync(created.Id, "vendor");
            Assert.Equal(ServiceResultStatus.Created, first.Status);
            otherVendorId = first.Value.Vendor!.VendorId;
        }))
        {
            result = await new RelationService(db).AddRoleAsync(created.Id, "vendor");
        }
        Assert.Equal(ServiceResultStatus.Ok, result.Status);
        Assert.Equal(otherVendorId, result.Value.Vendor!.VendorId);
        await using var verify = _factory.CreateContext();
        Assert.Equal(1, await verify.Vendors.CountAsync(v => v.RelationId == created.Id));
        Assert.Equal(1, await verify.Relations.CountAsync());
    }

    [Fact]
    public async Task An_Exact_Create_Retry_Returns_The_Same_Relation_Even_When_Its_Search_Code_Was_Reused_Later()
    {
        var request = Minimal("Veritas Fruit");
        request.SearchCode = "VER";
        var created = (await CreateAsync(request)).Value;
        var rename = From((await GetAsync(created.Id)).Value);
        rename.SearchCode = "VERITA";
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(created.Id, rename)).Status);
        var other = Minimal("Verkoop Zuid");
        other.SearchCode = "ver";
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(other)).Status);

        var retry = await CreateAsync(request); // exact dezelfde opdracht, zelfde requestId
        Assert.Equal(ServiceResultStatus.Ok, retry.Status);
        Assert.Equal((created.Id, "VERITA"), (retry.Value.Id, retry.Value.SearchCode));
        request.Name = "Veritas Fruit B.V."; // afwijkende herhaling blijft een conflict
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateAsync(request)).Status);
        await using var verify = _factory.CreateContext();
        Assert.Equal(2, await verify.Relations.CountAsync());
    }

    [Fact]
    public async Task Search_Finds_A_Relation_By_Any_Contact_Name_Phone_Or_Email_Not_Only_The_Primary_Contact()
    {
        var request = Minimal("Fruithandel Oost");
        request.Contacts = new()
        {
            new() { Name = "Jan de Vries", IsPrimary = true, Channels = new() { new() { Kind = "Phone", Value = "06-11111111" } } },
            new() { Name = "Pieter Bakker", Channels = new() { new() { Kind = "Phone", Value = "077-2223344" }, new() { Kind = "Email", Value = "pieter@oost.example" } } },
        };
        Assert.Equal(ServiceResultStatus.Created, (await CreateAsync(request)).Status);
        foreach (var term in new[] { "pieter", "077-222", "pieter@oost", "06-1111" })
        {
            var item = Assert.Single((await ListAsync(query: term)).Items);
            Assert.Equal(("Fruithandel Oost", "Jan de Vries"), (item.Name, item.PrimaryContactName));
        }
    }

    // De werkplek leidt "intussen door iemand anders gewijzigd" af uit een versieverschil na rol toevoegen; dat werkt alleen als een
    // roltoevoeging zelf de relatieversie niet verhoogt. Een opslag op de oude basisversie na een wijziging van een collega is 409.
    [Fact]
    public async Task Adding_A_Role_Does_Not_Bump_The_Version_And_A_Save_On_The_Draft_Base_Version_After_A_Colleague_Change_Is_A_Conflict()
    {
        var created = (await CreateAsync(Minimal("Groenten Noord"))).Value;
        await using (var db = _factory.CreateContext())
        {
            var added = await new RelationService(db).AddRoleAsync(created.Id, "vendor");
            Assert.Equal(created.Version, added.Value.Version);
        }
        var colleague = From((await GetAsync(created.Id)).Value);
        colleague.TradeName = "Door collega";
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(created.Id, colleague)).Status);
        await using (var db = _factory.CreateContext())
        {
            var addedAgain = await new RelationService(db).AddRoleAsync(created.Id, "customer");
            Assert.Equal(created.Version + 1, addedAgain.Value.Version); // nieuwere kaart door de collega, niet door de rol
        }
        var mine = From(created); // concept op de oude basisversie
        mine.Name = "Mijn wijziging";
        var result = await UpdateAsync(created.Id, mine);
        Assert.Equal(ServiceResultStatus.Conflict, result.Status);
        var stored = (await GetAsync(created.Id)).Value;
        Assert.Equal(("Groenten Noord", "Door collega"), (stored.Name, stored.TradeName));
        Assert.NotNull(stored.Customer);
        Assert.NotNull(stored.Vendor);
    }
}
