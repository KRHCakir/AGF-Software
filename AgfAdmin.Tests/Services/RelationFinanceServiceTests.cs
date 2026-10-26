using AgfAdmin.Data;
using AgfAdmin.DTOs;
using AgfAdmin.Models;
using AgfAdmin.Services;
using AgfAdmin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AgfAdmin.Tests.Services;

// Relaties fase 2 via de services: betalingscondities (stamgegevens, spiegel, resolver, regel 16), betaalwijze en rolafspraken,
// bankrekeningen (onbevestigd na wijziging, audit, nooit verwijderen, één standaard, bevestigen), strikte e-mail/btw alleen voor
// nieuwe of gewijzigde waarden, MX-waarschuwing via een nep-controleur, krediet en de kredietwaarschuwing bij een verkooporder.
public class RelationFinanceServiceTests : IDisposable
{
    private readonly SqliteAppDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private sealed class FakeEmailDomainChecker : IEmailDomainChecker
    {
        public Dictionary<string, EmailDomainStatus> Results { get; } = new(StringComparer.Ordinal);
        public List<string> Asked { get; } = new();
        public Task<EmailDomainStatus> CheckAsync(string domain, CancellationToken cancellationToken = default)
        {
            Asked.Add(domain);
            return Task.FromResult(Results.TryGetValue(domain, out var status) ? status : EmailDomainStatus.HasMailServer);
        }
    }

    private readonly FakeEmailDomainChecker _mx = new();

    private async Task<ServiceResult<RelationDto>> CreateAsync(SaveRelationDto request)
    {
        await using var db = _factory.CreateContext();
        return await new RelationService(db, _mx).CreateAsync(request);
    }

    private async Task<ServiceResult<RelationDto>> UpdateAsync(int id, SaveRelationDto request)
    {
        await using var db = _factory.CreateContext();
        return await new RelationService(db, _mx).UpdateAsync(id, request);
    }

    private async Task<ServiceResult<RelationDto>> ConfirmAsync(int id, int bankId, int? version, string actor)
    {
        await using var db = _factory.CreateContext();
        return await new RelationService(db, _mx).ConfirmBankAccountAsync(id, bankId, new ConfirmBankAccountDto { ExpectedVersion = version, ActorReference = actor });
    }

    private async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var db = _factory.CreateContext();
        return await action(db);
    }

    // PUT vervangt de kaart: vertrek altijd van de huidige kaart (inclusief rolafspraken en bankrekeningen).
    private static SaveRelationDto From(RelationDto c) => new()
    {
        ExpectedVersion = c.Version, SearchCode = c.SearchCode, Name = c.Name, Email = c.Email, Phone = c.Phone, VatNumber = c.VatNumber, IsActive = c.IsActive,
        Customer = c.Customer == null ? null : new SaveCustomerRoleDto
        {
            PaymentConditionId = c.Customer.PaymentConditionId, PaymentMethod = c.Customer.PaymentMethod, MandateReference = c.Customer.MandateReference, MandateSignedOn = c.Customer.MandateSignedOn,
            Currency = c.Customer.Currency, DeliveryTermKind = c.Customer.DeliveryTermKind, Incoterm = c.Customer.Incoterm, IncotermPlace = c.Customer.IncotermPlace, DeliveryTermText = c.Customer.DeliveryTermText,
            TradeConditions = c.Customer.TradeConditions, TradeConditionsText = c.Customer.TradeConditionsText, CreditLimit = c.Customer.CreditLimit, IsInsured = c.Customer.IsInsured,
            Insurer = c.Customer.Insurer, InsurerText = c.Customer.InsurerText, InsuredLimit = c.Customer.InsuredLimit, InsurerReference = c.Customer.InsurerReference, InsuredUntil = c.Customer.InsuredUntil
        },
        Vendor = c.Vendor == null ? null : new SaveVendorRoleDto
        {
            PaymentConditionId = c.Vendor.PaymentConditionId, DefaultCurrency = c.Vendor.DefaultCurrency, PaymentMethod = c.Vendor.PaymentMethod, MandateReference = c.Vendor.MandateReference,
            MandateSignedOn = c.Vendor.MandateSignedOn, DeliveryTermKind = c.Vendor.DeliveryTermKind, Incoterm = c.Vendor.Incoterm, IncotermPlace = c.Vendor.IncotermPlace,
            DeliveryTermText = c.Vendor.DeliveryTermText, TradeConditions = c.Vendor.TradeConditions, TradeConditionsText = c.Vendor.TradeConditionsText, Ggns = c.Vendor.Ggns
        },
        Addresses = c.Addresses, Contacts = c.Contacts,
        BankAccounts = c.BankAccounts.Select(a => new RelationBankAccountDto { Id = a.Id, Iban = a.Iban, OtherAccountNumber = a.OtherAccountNumber, Bic = a.Bic, AccountHolder = a.AccountHolder, Currency = a.Currency, IsDefault = a.IsDefault, IsActive = a.IsActive }).ToList()
    };

    private static SaveRelationDto New(string name = "Fruitimport Zuid") => new() { RequestId = Guid.NewGuid(), Name = name };

    private async Task<PaymentCondition> ConditionAsync(string code, int days, string basis = PaymentConditionBasis.InvoiceDate, bool active = true)
    {
        await using var db = _factory.CreateContext();
        var condition = new PaymentCondition { Code = code, CodeKey = code.ToLowerInvariant(), DescriptionNl = code, DescriptionEn = code, Days = days, Basis = basis, IsActive = active, SortOrder = days };
        db.PaymentConditions.Add(condition);
        await db.SaveChangesAsync();
        return condition;
    }

    // ---------------- Betalingscondities op de rollen ----------------

    [Fact]
    public async Task A_New_Role_Gets_The_Default_Condition_A_Chosen_Condition_Drives_The_Days_Mirror_And_Inactive_Conditions_Cannot_Be_Chosen()
    {
        var created = (await CreateAsync(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "Klant A", Customer = new SaveCustomerRoleDto(), Vendor = new SaveVendorRoleDto() })).Value;
        Assert.Equal(("30D", 30, PaymentConditionBasis.InvoiceDate), (created.Customer!.PaymentCondition.Code, created.Customer.PaymentTermsDays, created.Customer.PaymentCondition.Basis));
        Assert.Equal(created.Customer.PaymentConditionId, created.Vendor!.PaymentConditionId);
        Assert.Equal((PaymentMethods.BankTransfer, "EUR", "EUR"), (created.Customer.PaymentMethod, created.Customer.Currency, created.Vendor.DefaultCurrency));

        var eom = await ConditionAsync("EOM45", 45, PaymentConditionBasis.EndOfMonth);
        var request = From(created);
        request.Customer!.PaymentConditionId = eom.Id;
        var updated = (await UpdateAsync(created.Id, request)).Value;
        Assert.Equal((eom.Id, 45, PaymentConditionBasis.EndOfMonth), (updated.Customer!.PaymentConditionId, updated.Customer.PaymentTermsDays, updated.Customer.PaymentCondition.Basis));
        Assert.Equal(30, updated.Vendor!.PaymentTermsDays);

        // Een ongewijzigde legacy-termijn houdt de gekozen conditie (geen stille omzetting naar "45D").
        var legacy = From(updated);
        legacy.Customer!.PaymentConditionId = null;
        legacy.Customer.PaymentTermsDays = 45;
        Assert.Equal(eom.Id, (await UpdateAsync(created.Id, legacy)).Value.Customer!.PaymentConditionId);

        var inactive = await ConditionAsync("OLD60", 60, active: false);
        var current = (await WithDbAsync(db => new RelationService(db).GetAsync(created.Id))).Value;
        var pickInactive = From(current);
        pickInactive.Vendor!.PaymentConditionId = inactive.Id;
        var refused = await UpdateAsync(created.Id, pickInactive);
        Assert.Equal(ServiceResultStatus.Validation, refused.Status);
        Assert.Contains("vendor.paymentConditionId", refused.Errors.Keys);
        var unknown = From(current);
        unknown.Vendor!.PaymentConditionId = 999;
        Assert.Contains("vendor.paymentConditionId", (await UpdateAsync(created.Id, unknown)).Errors.Keys);
    }

    [Fact]
    public async Task Legacy_Days_Writers_Share_One_Resolver_That_Finds_Or_Creates_An_Invoice_Date_Condition()
    {
        await using (var db = _factory.CreateContext())
        {
            var service = new RelationService(db);
            await service.CreateLegacyCustomerAsync(new CreateCustomerDto { Name = "Oude route 1", PaymentTermsDays = 14 });
            await service.CreateLegacyCustomerAsync(new CreateCustomerDto { Name = "Oude route 2", PaymentTermsDays = 14 });
        }
        await using (var db = _factory.CreateContext())
        {
            // Rol zonder relatie en zonder conditie (seed/test): EnsureRelations + EnsurePaymentConditions in dezelfde save.
            db.Vendors.Add(new Vendor { Name = "Seed leverancier", PaymentTermsDays = 21 });
            db.Customers.Add(new Customer { Name = "Seed klant", PaymentTermsDays = 21 });
            await db.SaveChangesAsync();
        }
        await using var verify = _factory.CreateContext();
        var conditions = await verify.PaymentConditions.OrderBy(p => p.Days).ToListAsync();
        Assert.Equal(new[] { ("14D", 14, "14 dagen na factuurdatum", "14 days after invoice date"), ("21D", 21, "21 dagen na factuurdatum", "21 days after invoice date") },
            conditions.Select(p => (p.Code, p.Days, p.DescriptionNl, p.DescriptionEn)));
        Assert.All(conditions, p => Assert.Equal((PaymentConditionBasis.InvoiceDate, (decimal?)null, true), (p.Basis, p.DiscountPercent, p.IsActive)));
        Assert.All(await verify.Customers.Include(c => c.PaymentCondition).ToListAsync(), c => Assert.Equal(c.PaymentCondition.Days, c.PaymentTermsDays));
        Assert.Equal(conditions[1].Id, (await verify.Vendors.SingleAsync()).PaymentConditionId);

        // Een bezette code met een andere betekenis wordt niet hergebruikt.
        verify.PaymentConditions.Add(new PaymentCondition { Code = "7D", CodeKey = "7d", DescriptionNl = "7 dagen einde maand", DescriptionEn = "x", Days = 7, Basis = PaymentConditionBasis.EndOfMonth });
        await verify.SaveChangesAsync();
        var seven = await PaymentConditionResolver.ForDaysAsync(verify, 7);
        Assert.Equal(("7D-2", PaymentConditionBasis.InvoiceDate, 0), (seven.Code, seven.Basis, seven.Id));
    }

    [Fact]
    public async Task Guard_Rule_16_Refuses_A_Days_Mirror_That_Differs_From_The_Condition()
    {
        var created = (await CreateAsync(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "Klant B", Customer = new SaveCustomerRoleDto { PaymentTermsDays = 14 } })).Value;
        Assert.Equal(("14D", 14), (created.Customer!.PaymentCondition.Code, created.Customer.PaymentTermsDays));
        await using (var db = _factory.CreateContext())
        {
            (await db.Customers.SingleAsync()).PaymentTermsDays = 60;
            var exception = await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
            Assert.Contains("mirror the payment condition", exception.Message);
        }
        await using (var db = _factory.CreateContext())
        {
            // Een conditie met andere dagen zonder de rollen mee te nemen: geweigerd.
            (await db.PaymentConditions.SingleAsync(p => p.Code == "14D")).Days = 15;
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            var other = await ConditionAsync("60D", 60);
            var customer = await db.Customers.SingleAsync();
            customer.PaymentConditionId = other.Id;   // verwijzing zonder spiegel
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            await PaymentConditionResolver.AssignDaysAsync(db, await db.Customers.SingleAsync(), 60);
            await db.SaveChangesAsync();
        }
        Assert.Equal(60, await WithDbAsync(db => db.Customers.Select(c => c.PaymentTermsDays).SingleAsync()));
    }

    // ---------------- Stamgegevens betalingscondities ----------------

    private async Task<ServiceResult<PaymentConditionDto>> CreateConditionAsync(SavePaymentConditionDto request) =>
        await WithDbAsync(db => new PaymentConditionService(db).CreateAsync(request));

    [Fact]
    public async Task Payment_Condition_Codes_Are_Unique_Regardless_Of_Case_Create_Is_Idempotent_And_Days_Changes_Follow_The_Roles()
    {
        var request = new SavePaymentConditionDto { RequestId = Guid.NewGuid(), Code = "EOM30", DescriptionNl = "30 dagen einde maand", DescriptionEn = "30 days end of month", Days = 30, Basis = "EndOfMonth", DiscountPercent = 2m, DiscountDays = 8 };
        var created = await CreateConditionAsync(request);
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        Assert.Equal(("EOM30", 30, "EndOfMonth", 2m, 8, true, 0), (created.Value.Code, created.Value.Days, created.Value.Basis, created.Value.DiscountPercent!.Value, created.Value.DiscountDays!.Value, created.Value.IsActive, created.Value.Version));
        var replay = await CreateConditionAsync(request);
        Assert.Equal((ServiceResultStatus.Ok, created.Value.Id), (replay.Status, replay.Value.Id));
        request.Days = 31;
        Assert.Equal(ServiceResultStatus.Conflict, (await CreateConditionAsync(request)).Status);
        var duplicate = await CreateConditionAsync(new SavePaymentConditionDto { RequestId = Guid.NewGuid(), Code = "eom30", DescriptionNl = "x", DescriptionEn = "x", Days = 10, Basis = "InvoiceDate" });
        Assert.Equal(ServiceResultStatus.Validation, duplicate.Status);
        Assert.Contains("code", duplicate.Errors.Keys);

        // In gebruik: dagen wijzigen neemt de spiegel van alle rollen mee; verouderde versie = 409.
        var relation = (await CreateAsync(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "Klant C", Customer = new SaveCustomerRoleDto { PaymentConditionId = created.Value.Id }, Vendor = new SaveVendorRoleDto { PaymentConditionId = created.Value.Id } })).Value;
        Assert.Equal(30, relation.Customer!.PaymentTermsDays);
        var change = new SavePaymentConditionDto { ExpectedVersion = 0, Code = "EOM30", DescriptionNl = "45 dagen einde maand", DescriptionEn = "45 days end of month", Days = 45, Basis = "EndOfMonth" };
        var updated = await WithDbAsync(db => new PaymentConditionService(db).UpdateAsync(created.Value.Id, change));
        Assert.Equal(ServiceResultStatus.Ok, updated.Status);
        Assert.Equal((45, 1, (decimal?)null, 1, 1), (updated.Value.Days, updated.Value.Version, updated.Value.DiscountPercent, updated.Value.CustomerCount, updated.Value.VendorCount));
        Assert.Equal((45, 45), await WithDbAsync(async db => ((await db.Customers.SingleAsync()).PaymentTermsDays, (await db.Vendors.SingleAsync()).PaymentTermsDays)));
        Assert.Equal(ServiceResultStatus.Conflict, (await WithDbAsync(db => new PaymentConditionService(db).UpdateAsync(created.Value.Id, change))).Status);

        // Verwijderen: in gebruik -> 409 met code en advies; inactief zetten mag; ongebruikt -> verwijderd.
        var inUse = await WithDbAsync(db => new PaymentConditionService(db).DeleteAsync(created.Value.Id));
        Assert.Equal(ServiceResultStatus.Conflict, inUse.Status);
        Assert.Contains("inactief in plaats van verwijderen", inUse.Message);
        Assert.Contains(PaymentConditionService.InUseCode, System.Text.Json.JsonSerializer.Serialize(inUse.ConflictBody));
        change.ExpectedVersion = 1;
        change.IsActive = false;
        Assert.False((await WithDbAsync(db => new PaymentConditionService(db).UpdateAsync(created.Value.Id, change))).Value.IsActive);
        Assert.DoesNotContain(await WithDbAsync(db => new PaymentConditionService(db).ListAsync(false)), p => p.Id == created.Value.Id);
        Assert.Contains(await WithDbAsync(db => new PaymentConditionService(db).ListAsync(true)), p => p.Id == created.Value.Id);
        var card = (await WithDbAsync(db => new RelationService(db).GetAsync(relation.Id))).Value;
        Assert.Contains(card.Warnings, w => w.Code == RelationService.WarningPaymentConditionInactive);
        var unused = await CreateConditionAsync(new SavePaymentConditionDto { RequestId = Guid.NewGuid(), Code = "7D", DescriptionNl = "7", DescriptionEn = "7", Days = 7, Basis = "InvoiceDate" });
        Assert.Equal(ServiceResultStatus.Ok, (await WithDbAsync(db => new PaymentConditionService(db).DeleteAsync(unused.Value.Id))).Status);
        Assert.False(await WithDbAsync(db => db.PaymentConditions.AnyAsync(p => p.Id == unused.Value.Id)));
    }

    [Fact]
    public async Task Purchase_Invoice_Due_Date_Follows_The_Vendor_Condition_Basis_Only_When_The_Terms_Are_The_Conditions()
    {
        var eom = await ConditionAsync("EOM30", 30, PaymentConditionBasis.EndOfMonth);
        var relation = (await CreateAsync(new SaveRelationDto { RequestId = Guid.NewGuid(), Name = "Kweker", Vendor = new SaveVendorRoleDto { PaymentConditionId = eom.Id } })).Value;
        var vendorId = relation.Vendor!.VendorId;
        var vendors = await WithDbAsync(db => new PurchaseInvoiceService(db).VendorsAsync());
        Assert.Equal(("EOM30", PaymentConditionBasis.EndOfMonth, false, false), (vendors.Single().PaymentConditionCode, vendors.Single().PaymentConditionBasis, vendors.Single().HasDefaultBankAccount, vendors.Single().DefaultBankAccountUnconfirmed));
        CreatePurchaseInvoiceDto Invoice(int? terms) => new()
        {
            RequestId = Guid.NewGuid(), VendorId = vendorId, InvoiceNumber = $"F-{Guid.NewGuid():N}"[..12], InvoiceDate = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc), PaymentTermsDays = terms,
            Lines = new() { new() { Description = "Mango", Quantity = 1m, UnitPrice = 10m } }
        };
        var byCondition = await WithDbAsync(db => new PurchaseInvoiceService(db).CreateAsync(Invoice(null)));
        Assert.Equal((30, new DateTime(2026, 11, 30)), (byCondition.Value.PaymentTermsDays!.Value, byCondition.Value.DueDate!.Value));
        var sameTerms = await WithDbAsync(db => new PurchaseInvoiceService(db).CreateAsync(Invoice(30)));
        Assert.Equal(new DateTime(2026, 11, 30), sameTerms.Value.DueDate!.Value);
        var otherTerms = await WithDbAsync(db => new PurchaseInvoiceService(db).CreateAsync(Invoice(14)));
        Assert.Equal(new DateTime(2026, 10, 29), otherTerms.Value.DueDate!.Value);
    }

    // ---------------- Betaalwijze en rolafspraken ----------------

    [Fact]
    public async Task Direct_Debit_Requires_A_Mandate_And_Role_Terms_Round_Trip()
    {
        var request = new SaveRelationDto
        {
            RequestId = Guid.NewGuid(), Name = "Incasso klant",
            Customer = new SaveCustomerRoleDto { PaymentMethod = "DirectDebit", Currency = "gbp", DeliveryTermKind = "Incoterm", Incoterm = "dap", IncotermPlace = "Rotterdam", TradeConditions = "Other" }
        };
        var refused = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, refused.Status);
        Assert.Contains("customer.mandateReference", refused.Errors.Keys);
        Assert.Contains("customer.mandateSignedOn", refused.Errors.Keys);
        Assert.Contains("customer.tradeConditionsText", refused.Errors.Keys);
        request.Customer.MandateReference = "MND-2026-001";
        request.Customer.MandateSignedOn = new DateOnly(2026, 10, 1);
        request.Customer.TradeConditionsText = "GAFTA";
        request.Customer.CreditLimit = 10_000m;
        request.Customer.IsInsured = true;
        request.Customer.Insurer = "Other";
        request.Customer.InsurerText = "Credendo";
        request.Customer.InsuredLimit = 6_000m;
        request.Customer.InsuredUntil = new DateOnly(2026, 11, 1);
        var created = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Created, created.Status);
        var c = created.Value.Customer!;
        Assert.Equal(("DirectDebit", "MND-2026-001", "GBP", "DAP", "Rotterdam", "Other", "GAFTA"), (c.PaymentMethod, c.MandateReference, c.Currency, c.Incoterm, c.IncotermPlace, c.TradeConditions, c.TradeConditionsText));
        Assert.Equal((10_000m, true, "Other", "Credendo", 6_000m), (c.CreditLimit!.Value, c.IsInsured, c.Insurer, c.InsurerText, c.InsuredLimit!.Value));
        // Vandaag (vaste kalender) is 20-10-2026: verzekering verloopt binnen 30 dagen en dekt 4.000 minder dan de interne limiet.
        Assert.Contains(created.Value.Warnings, w => w.Code == RelationService.WarningInsuranceExpiresSoon);
        Assert.Contains(created.Value.Warnings, w => w.Code == RelationService.WarningInsuranceGap && w.Message.Contains("4.000,00"));
    }

    // ---------------- Bankrekeningen ----------------

    [Fact]
    public async Task Bank_Accounts_Start_Unconfirmed_With_Audit_Need_An_Actor_And_A_Change_Of_Iban_Or_Holder_Unconfirms_Again()
    {
        var request = New();
        request.BankAccounts = new() { new() { Iban = "nl91 abna 0417 1643 00", Bic = "abnanl2a", AccountHolder = "Fruitimport Zuid BV" } };
        var noActor = await CreateAsync(request);
        Assert.Equal(ServiceResultStatus.Validation, noActor.Status);
        Assert.Contains("actorReference", noActor.Errors.Keys);
        request.ActorReference = "Jan";
        var created = (await CreateAsync(request)).Value;
        var account = Assert.Single(created.BankAccounts);
        Assert.Equal(("NL91ABNA0417164300", "ABNANL2A", "EUR", true, BankAccountConfirmation.Unconfirmed), (account.Iban, account.Bic, account.Currency, account.IsDefault, account.ConfirmationStatus));
        var added = Assert.Single(created.BankAccountEvents);
        Assert.Equal((BankAccountEventKind.Added, "Jan", (string)null, "NL91ABNA0417164300", "Fruitimport Zuid BV"), (added.Kind, added.ActorReference, added.OldIban, added.NewIban, added.NewAccountHolder));
        Assert.Contains(created.Warnings, w => w.Code == RelationService.WarningDefaultBankAccountUnconfirmed && w.Field == "bankAccounts[0]");

        // Bevestigen: verwachte versie en wie verplicht; verouderde versie = 409; daarna geen waarschuwing meer en versie +1.
        Assert.Equal(ServiceResultStatus.Validation, (await ConfirmAsync(created.Id, account.Id!.Value, created.Version, " ")).Status);
        Assert.Equal(ServiceResultStatus.Conflict, (await ConfirmAsync(created.Id, account.Id!.Value, created.Version + 1, "Piet")).Status);
        var confirmed = (await ConfirmAsync(created.Id, account.Id!.Value, created.Version, "Piet")).Value;
        Assert.Equal((BankAccountConfirmation.Confirmed, "Piet", created.Version + 1), (confirmed.BankAccounts[0].ConfirmationStatus, confirmed.BankAccounts[0].ConfirmedBy, confirmed.Version));
        Assert.NotNull(confirmed.BankAccounts[0].ConfirmedAt);
        Assert.DoesNotContain(confirmed.Warnings, w => w.Code == RelationService.WarningDefaultBankAccountUnconfirmed);
        Assert.Equal(BankAccountEventKind.Confirmed, confirmed.BankAccountEvents[0].Kind);
        // Herhaald bevestigen: 200 zonder nieuwe auditregel of versie.
        var again = (await ConfirmAsync(created.Id, account.Id!.Value, confirmed.Version, "Piet")).Value;
        Assert.Equal((confirmed.Version, 2), (again.Version, again.BankAccountEvents.Count));

        // BIC of valuta wijzigen is geen identiteitswijziging: blijft bevestigd, geen audit, geen "wie" nodig.
        var bicOnly = From(again);
        bicOnly.BankAccounts[0].Bic = "ABNANL2AXXX";
        var bicSaved = (await UpdateAsync(created.Id, bicOnly)).Value;
        Assert.Equal((BankAccountConfirmation.Confirmed, 2), (bicSaved.BankAccounts[0].ConfirmationStatus, bicSaved.BankAccountEvents.Count));

        // IBAN wijzigen: weer onbevestigd, Changed-audit met oud en nieuw, "wie" verplicht.
        var changeIban = From(bicSaved);
        changeIban.BankAccounts[0].Iban = "DE89370400440532013000";
        Assert.Contains("actorReference", (await UpdateAsync(created.Id, changeIban)).Errors.Keys);
        changeIban.ActorReference = "Kees";
        var changed = (await UpdateAsync(created.Id, changeIban)).Value;
        Assert.Equal((BankAccountConfirmation.Unconfirmed, (string)null), (changed.BankAccounts[0].ConfirmationStatus, changed.BankAccounts[0].ConfirmedBy));
        Assert.Equal((BankAccountEventKind.Changed, "Kees", "NL91ABNA0417164300", "DE89370400440532013000"), (changed.BankAccountEvents[0].Kind, changed.BankAccountEvents[0].ActorReference, changed.BankAccountEvents[0].OldIban, changed.BankAccountEvents[0].NewIban));

        // Rekeninghouder wijzigen telt ook.
        var holder = From(changed);
        holder.ActorReference = "Kees";
        holder.BankAccounts[0].AccountHolder = "Fruitimport Zuid B.V.";
        var holderChanged = (await UpdateAsync(created.Id, holder)).Value;
        Assert.Equal((BankAccountEventKind.Changed, "Fruitimport Zuid BV", "Fruitimport Zuid B.V."), (holderChanged.BankAccountEvents[0].Kind, holderChanged.BankAccountEvents[0].OldAccountHolder, holderChanged.BankAccountEvents[0].NewAccountHolder));
    }

    [Fact]
    public async Task Bank_Accounts_Are_Never_Deleted_Only_Deactivated_Have_One_Active_Default_And_Validate_Iban_Bic_And_Other_Numbers()
    {
        var request = New();
        request.ActorReference = "Jan";
        request.BankAccounts = new()
        {
            new() { Iban = "NL91ABNA0417164300", AccountHolder = "Zuid" },
            new() { OtherAccountNumber = "US 021000021 / 123456789", AccountHolder = "Zuid USA", Currency = "usd" },
        };
        var created = (await CreateAsync(request)).Value;
        Assert.Equal(new[] { true, false }, created.BankAccounts.Select(a => a.IsDefault));
        Assert.Equal(("USD", (string)null), (created.BankAccounts[1].Currency, created.BankAccounts[1].Iban));

        // Weglaten = fout ("zet inactief in plaats van verwijderen").
        var omit = From(created);
        omit.BankAccounts.RemoveAt(1);
        var omitted = await UpdateAsync(created.Id, omit);
        Assert.Equal(ServiceResultStatus.Validation, omitted.Status);
        Assert.Contains("zet inactief in plaats van verwijderen", omitted.Errors["bankAccounts"][0]);

        // Twee standaarden, beide nummers, geen nummer, foute IBAN/BIC, dubbele IBAN, inactieve standaard: allemaal fouten.
        var bad = From(created);
        bad.ActorReference = "Jan";
        bad.BankAccounts[1].IsDefault = true;
        bad.BankAccounts.Add(new() { Iban = "NL91ABNA0417164301", OtherAccountNumber = "1", AccountHolder = "x" });
        bad.BankAccounts.Add(new() { AccountHolder = "x" });
        bad.BankAccounts.Add(new() { Iban = "NL91ABNA0417164300", Bic = "ABN", AccountHolder = " " });
        bad.BankAccounts.Add(new() { Iban = "BE68539007547034", AccountHolder = "y", IsDefault = true, IsActive = false });
        var errors = (await UpdateAsync(created.Id, bad)).Errors;
        Assert.Contains("bankAccounts", errors.Keys);
        Assert.Contains("bankAccounts[2].iban", errors.Keys);
        Assert.Contains("bankAccounts[2].otherAccountNumber", errors.Keys);
        Assert.Contains("bankAccounts[3].iban", errors.Keys);
        Assert.Contains("bankAccounts[4].bic", errors.Keys);
        Assert.Contains("bankAccounts[4].iban", errors.Keys);
        Assert.Contains("bankAccounts[4].accountHolder", errors.Keys);
        Assert.Contains("bankAccounts[5].isDefault", errors.Keys);

        // Standaard verhuizen naar een andere opgeslagen rekening (gefilterde unieke index) en de oude inactief zetten.
        var move = From(created);
        move.ActorReference = "Jan";
        move.BankAccounts[0].IsDefault = false;
        move.BankAccounts[0].IsActive = false;
        move.BankAccounts[1].IsDefault = true;
        var moved = await UpdateAsync(created.Id, move);
        Assert.Equal(ServiceResultStatus.Ok, moved.Status);
        Assert.Equal((false, false, true), (moved.Value.BankAccounts[0].IsActive, moved.Value.BankAccounts[0].IsDefault, moved.Value.BankAccounts[1].IsDefault));
        Assert.Equal(created.Version + 1, moved.Value.Version);
        Assert.Equal(BankAccountEventKind.Deactivated, moved.Value.BankAccountEvents[0].Kind);
        Assert.Equal(2, await WithDbAsync(db => db.RelationBankAccounts.CountAsync()));

        // En terug, zonder iets te deactiveren (de oude standaard wordt eerst uitgezet).
        var back = From(moved.Value);
        back.ActorReference = "Jan";
        back.BankAccounts[0].IsActive = true;
        back.BankAccounts[0].IsDefault = true;
        back.BankAccounts[1].IsDefault = false;
        var backResult = await UpdateAsync(created.Id, back);
        Assert.Equal(ServiceResultStatus.Ok, backResult.Status);
        Assert.Equal(new[] { true, false }, backResult.Value.BankAccounts.Select(a => a.IsDefault));

        // De database en de guard houden het ook tegen buiten de service om.
        await using (var db = _factory.CreateContext())
        {
            db.RelationBankAccounts.Remove(await db.RelationBankAccounts.FirstAsync());
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.RelationBankAccountEvents.FirstAsync()).ActorReference = "Iemand anders";
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            (await db.RelationBankAccounts.FirstAsync()).Iban = "BE68539007547034";   // zonder audit en zonder onbevestigen
            await Assert.ThrowsAsync<TraceabilityViolationException>(() => db.SaveChangesAsync());
        }
        await using (var db = _factory.CreateContext())
        {
            foreach (var a in await db.RelationBankAccounts.ToListAsync()) a.IsDefault = true;   // twee actieve standaarden
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Purchase_Vendor_List_Flags_An_Unconfirmed_Default_Bank_Account()
    {
        var request = New("Leverancier X");
        request.Vendor = new SaveVendorRoleDto();
        request.ActorReference = "Jan";
        request.BankAccounts = new() { new() { Iban = "NL91ABNA0417164300", AccountHolder = "X" } };
        var created = (await CreateAsync(request)).Value;
        var vendor = (await WithDbAsync(db => new PurchaseInvoiceService(db).VendorsAsync())).Single();
        Assert.Equal((true, true), (vendor.HasDefaultBankAccount, vendor.DefaultBankAccountUnconfirmed));
        await ConfirmAsync(created.Id, created.BankAccounts[0].Id!.Value, created.Version, "Piet");
        vendor = (await WithDbAsync(db => new PurchaseInvoiceService(db).VendorsAsync())).Single();
        Assert.Equal((true, false), (vendor.HasDefaultBankAccount, vendor.DefaultBankAccountUnconfirmed));
    }

    // ---------------- E-mail en btw: strikt alleen voor nieuwe of gewijzigde waarden ----------------

    [Fact]
    public async Task New_Or_Changed_Emails_Are_Strict_With_Typo_Hints_And_Mx_Warnings_That_Never_Block_Existing_Values_Stay_Saveable()
    {
        // Bestaande, oude data met een adres dat niet strikt is (oude lichte controle): blijft ongewijzigd opslaanbaar.
        int relationId;
        await using (var db = _factory.CreateContext())
        {
            var customer = await new RelationService(db).CreateLegacyCustomerAsync(new CreateCustomerDto { Name = "Oud", Email = "info@oud", ContactPerson = "Jan", Phone = "1", PaymentTermsDays = 14 });
            relationId = customer.RelationId;
            (await db.Relations.SingleAsync(r => r.Id == relationId)).Email = "kantoor@oud";
            await db.SaveChangesAsync();
        }
        var current = (await WithDbAsync(db => new RelationService(db).GetAsync(relationId))).Value;
        var unchanged = await UpdateAsync(relationId, From(current));
        Assert.Equal(ServiceResultStatus.Ok, unchanged.Status);
        Assert.Empty(_mx.Asked);

        var changed = From(unchanged.Value);
        changed.Email = "kantoor@oud.";
        var refused = await UpdateAsync(relationId, changed);
        Assert.Equal(ServiceResultStatus.Validation, refused.Status);
        Assert.Contains("email", refused.Errors.Keys);
        changed.Contacts[0].Channels.First(ch => ch.Kind == "Email").Value = "jan@@oud.nl";
        Assert.Contains("contacts[0].channels[0].value", (await UpdateAsync(relationId, changed)).Errors.Keys);

        // Tikfout: waarschuwing met suggestie (geen fout); geen mailserver en DNS onbereikbaar: waarschuwingen, opslaan gaat door.
        _mx.Results["fruit-zonder-mx.nl"] = EmailDomainStatus.NoMailServer;
        _mx.Results["offline.nl"] = EmailDomainStatus.Unavailable;
        changed.Email = "inkoop@gmial.com";
        changed.Contacts[0].Channels.First(ch => ch.Kind == "Email").Value = "jan@fruit-zonder-mx.nl";
        changed.Contacts[0].Channels.Add(new ContactChannelDto { Kind = "Email", Value = "facturen@offline.nl" });
        var saved = await UpdateAsync(relationId, changed);
        Assert.Equal(ServiceResultStatus.Ok, saved.Status);
        var typo = Assert.Single(saved.Value.Warnings, w => w.Code == RelationService.WarningEmailTypo);
        Assert.Equal(("email", "inkoop@gmail.com"), (typo.Field, typo.Suggestion));
        var noMx = Assert.Single(saved.Value.Warnings, w => w.Code == RelationService.WarningEmailNoMailServer);
        Assert.Contains("geen mailserver", noMx.Message);
        Assert.Single(saved.Value.Warnings, w => w.Code == RelationService.WarningEmailDomainUnchecked);
        Assert.DoesNotContain("gmial.com", _mx.Asked);   // bij een tikfout geen DNS-vraag

        // Bij lezen zijn de eenmalige signalen weg.
        Assert.DoesNotContain((await WithDbAsync(db => new RelationService(db).GetAsync(relationId))).Value.Warnings, w => w.Code.StartsWith("email"));
    }

    [Fact]
    public async Task Vat_Shape_Per_Country_Only_Applies_To_New_Or_Changed_Numbers()
    {
        int relationId;
        await using (var db = _factory.CreateContext())
        {
            var customer = await new RelationService(db).CreateLegacyCustomerAsync(new CreateCustomerDto { Name = "Oud btw", VATNumber = "NL1", PaymentTermsDays = 14 });
            relationId = customer.RelationId;
        }
        var current = (await WithDbAsync(db => new RelationService(db).GetAsync(relationId))).Value;
        Assert.Equal("NL1", current.VatNumber);
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(relationId, From(current))).Status);
        var dutch = From((await WithDbAsync(db => new RelationService(db).GetAsync(relationId))).Value);
        dutch.VatNumber = "NL12345678B01";
        Assert.Contains("NL123456789B01", (await UpdateAsync(relationId, dutch)).Errors["vatNumber"][0]);
        dutch.VatNumber = "GR123456789";
        Assert.Contains("EL123456789", (await UpdateAsync(relationId, dutch)).Errors["vatNumber"][0]);
        dutch.VatNumber = "nl 1234.56.789 b01";
        var ok = await UpdateAsync(relationId, dutch);
        Assert.Equal(("NL123456789B01", VatVerification.NotChecked), (ok.Value.VatNumber, ok.Value.VatVerificationStatus));
    }

    // ---------------- Instellingen en GGN-controle ----------------

    [Fact]
    public async Task Portal_Url_Setting_Defaults_And_Only_Accepts_Absolute_Https_And_Ggn_Checks_Round_Trip()
    {
        Assert.Equal(RelationRules.DefaultGlobalGapPortalUrl, (await WithDbAsync(db => new RelationService(db).GetSettingsAsync())).GlobalGapPortalUrl);
        Assert.Equal(ServiceResultStatus.Validation, (await WithDbAsync(db => new RelationService(db).UpdateSettingsAsync(new RelationSettingsDto { GlobalGapPortalUrl = "http://portal.example" }))).Status);
        Assert.Equal(ServiceResultStatus.Validation, (await WithDbAsync(db => new RelationService(db).UpdateSettingsAsync(new RelationSettingsDto { GlobalGapPortalUrl = "" }))).Status);
        Assert.Equal("https://portal.example/x", (await WithDbAsync(db => new RelationService(db).UpdateSettingsAsync(new RelationSettingsDto { GlobalGapPortalUrl = " https://portal.example/x " }))).Value.GlobalGapPortalUrl);
        Assert.Equal("https://portal.example/x", (await WithDbAsync(db => new RelationService(db).ReferenceAsync())).GlobalGapPortalUrl);

        var request = New("Kweker GGN");
        request.Vendor = new SaveVendorRoleDto { Ggns = new() { new() { Ggn = "4049929000001", CheckedOn = new DateOnly(2026, 10, 21) } } };
        var errors = (await CreateAsync(request)).Errors;
        Assert.Contains("vendor.ggns[0].checkedOn", errors.Keys);   // toekomst (vandaag = 20-10-2026)
        Assert.Contains("vendor.ggns[0].checkedBy", errors.Keys);
        request.Vendor.Ggns[0].CheckedOn = new DateOnly(2026, 10, 19);
        request.Vendor.Ggns[0].CheckedBy = "Anja";
        request.Vendor.Ggns[0].CertificateValidUntil = new DateOnly(2027, 3, 31);
        request.Vendor.Ggns[0].HasGgnLabel = true;
        var ggn = (await CreateAsync(request)).Value.Vendor!.Ggns.Single();
        Assert.Equal((new DateOnly(2026, 10, 19), "Anja", new DateOnly(2027, 3, 31), true), (ggn.CheckedOn!.Value, ggn.CheckedBy, ggn.CertificateValidUntil!.Value, ggn.HasGgnLabel));
    }

    // ---------------- Krediet ----------------

    [Fact]
    public async Task Credit_Exposure_Counts_Open_Invoices_And_Uninvoiced_Legacy_Orders_And_Reports_Unpriced_Sales_Orders()
    {
        var created = (await CreateAsync(new SaveRelationDto
        {
            RequestId = Guid.NewGuid(), Name = "Kredietklant",
            Customer = new SaveCustomerRoleDto { CreditLimit = 1_000m, IsInsured = true, Insurer = "Atradius", InsuredLimit = 800m, InsuredUntil = new DateOnly(2026, 10, 1) }
        })).Value;
        var customerId = created.Customer!.CustomerId;
        int variantId;
        await using (var db = _factory.CreateContext())
        {
            var variant = new ProductVariant { Product = new Product { Name = "Avocado" }, Name = "Hass", Origin = "Peru", Packaging = "4kg Box", DefaultPrice = 99m };
            db.ProductVariants.Add(variant);
            await db.SaveChangesAsync();
            variantId = variant.Id;
            Order Legacy(string status, int quantity, decimal price) => new() { CustomerId = customerId, Status = status, Lines = new() { new OrderLine { ProductVariantId = variantId, Quantity = quantity, UnitPrice = price } } };
            var invoicedOrder = Legacy("Pending", 10, 10m);   // heeft een factuur: telt alleen via die factuur
            db.Orders.AddRange(Legacy("Pending", 10, 20m), Legacy("Approved", 5, 10m), Legacy("Shipped", 3, 100m), Legacy("Invoiced", 50, 50m), invoicedOrder);
            await db.SaveChangesAsync();
            db.Invoices.AddRange(
                new Invoice { CustomerId = customerId, OrderId = invoicedOrder.Id, InvoiceNumber = "INV-1", InvoiceDate = DateTime.UtcNow, TotalAmount = 121m, PaymentStatus = "Open" },
                new Invoice { CustomerId = customerId, InvoiceNumber = "INV-2", InvoiceDate = DateTime.UtcNow, TotalAmount = 400m, PaymentStatus = "Paid" },
                new Invoice { CustomerId = customerId, InvoiceNumber = "INV-3", InvoiceDate = DateTime.UtcNow, TotalAmount = 79m, PaymentStatus = "Overdue" });
            db.SalesOrders.AddRange(
                new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = customerId, Status = "Open", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "Jan" },
                new SalesOrder { RequestId = Guid.NewGuid(), CustomerId = customerId, Status = "Cancelled", RequestedDeliveryDate = DateTime.UtcNow, ActorReference = "Jan", CancelledAt = DateTime.UtcNow, CancelReason = "x" });
            await db.SaveChangesAsync();
        }
        var today = new DateOnly(2026, 10, 20);
        var exposure = await WithDbAsync(db => new CreditExposureService(db).GetExposureAsync(customerId, today));
        Assert.Equal((200m, 2), (exposure.OpenInvoicesAmount, exposure.OpenInvoicesCount));
        Assert.Equal((300m, 1), (exposure.DeliveredNotInvoicedAmount, exposure.DeliveredNotInvoicedCount));
        Assert.Equal((250m, 2), (exposure.OpenOrdersAmount, exposure.OpenOrdersCount));
        Assert.Equal(750m, exposure.TotalOutstanding);
        Assert.Equal((1, false, "1 order zonder prijs, niet meegeteld"), (exposure.UnpricedSalesOrdersCount, exposure.IsComplete, exposure.UnpricedNote));
        Assert.Equal((1_000m, 0m, false), (exposure.CreditLimit!.Value, exposure.OverLimitAmount, exposure.IsOverLimit));
        Assert.Equal((200m, true, false), (exposure.InsuranceGap!.Value, exposure.InsuranceExpired, exposure.InsuranceExpiresWithin30Days));
        Assert.Null(await WithDbAsync(db => new CreditExposureService(db).GetExposureAsync(999, today)));

        // Limiet omlaag: een nieuwe verkooporder krijgt een waarschuwing in het antwoord, maar wordt gewoon aangemaakt.
        var lower = From(created);
        lower.Customer!.CreditLimit = 500m;
        Assert.Equal(ServiceResultStatus.Ok, (await UpdateAsync(created.Id, lower)).Status);
        var order = await WithDbAsync(db => new SalesOrderService(db).CreateAsync(new CreateSalesOrderDto
        {
            RequestId = Guid.NewGuid(), CustomerId = customerId, RequestedDeliveryDate = new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero), ActorReference = "Jan",
            Lines = new() { new() { ProductVariantId = variantId, Quantity = 10m, Unit = "box" } }
        }));
        Assert.Equal(ServiceResultStatus.Created, order.Status);
        var warning = order.Value.CreditWarning;
        Assert.NotNull(warning);
        Assert.Equal((500m, 750m, 250m, false, 2), (warning.CreditLimit, warning.Outstanding, warning.Difference, warning.IsComplete, warning.UnpricedSalesOrdersCount));
        Assert.Contains("2 orders zonder prijs", warning.Message);

        // Zonder limiet geen waarschuwing.
        var noLimit = From((await WithDbAsync(db => new RelationService(db).GetAsync(created.Id))).Value);
        noLimit.Customer!.CreditLimit = null;
        noLimit.Customer.IsInsured = false;
        await UpdateAsync(created.Id, noLimit);
        var second = await WithDbAsync(db => new SalesOrderService(db).CreateAsync(new CreateSalesOrderDto
        {
            RequestId = Guid.NewGuid(), CustomerId = customerId, RequestedDeliveryDate = new DateTimeOffset(2026, 10, 25, 8, 0, 0, TimeSpan.Zero), ActorReference = "Jan",
            Lines = new() { new() { ProductVariantId = variantId, Quantity = 10m, Unit = "box" } }
        }));
        Assert.Null(second.Value.CreditWarning);
        var card = (await WithDbAsync(db => new RelationService(db).GetAsync(created.Id))).Value;
        Assert.Equal((false, (string)null, (decimal?)null), (card.Customer!.IsInsured, card.Customer.Insurer, card.Customer.InsuredLimit));
    }

    [Fact]
    public async Task Expired_Insurance_Is_A_Standing_Warning_On_The_Card()
    {
        var created = (await CreateAsync(new SaveRelationDto
        {
            RequestId = Guid.NewGuid(), Name = "Verlopen verzekering",
            Customer = new SaveCustomerRoleDto { CreditLimit = 1_000m, IsInsured = true, Insurer = "Coface", InsuredLimit = 1_000m, InsuredUntil = new DateOnly(2026, 10, 19) }
        })).Value;
        Assert.Contains(created.Warnings, w => w.Code == RelationService.WarningInsuranceExpired);
        Assert.DoesNotContain(created.Warnings, w => w.Code == RelationService.WarningInsuranceGap);
        var exposure = await WithDbAsync(db => new CreditExposureService(db).GetExposureAsync(created.Customer!.CustomerId, new DateOnly(2026, 10, 20)));
        Assert.Equal((true, false, (decimal?)null, true), (exposure.InsuranceExpired, exposure.InsuranceExpiresWithin30Days, exposure.InsuranceGap, exposure.IsComplete));
    }
}
