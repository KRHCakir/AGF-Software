import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { CustomerRole, Relation, RelationBankAccount, RelationListItem, RoleTerms, VendorRole } from '../types/relation';
import { bankAccountsNeedActor, buildRelationPayload, draftFromRelation, emailSuggestion, emptyAddressDraft, emptyBankAccountDraft, emptyContactDraft, emptyGgnDraft, emptyRelationDraft, groupRelationErrors, normalizeVat, relationRole } from './relationRules.ts';

// Relatieregels: alleen de naam verplicht, leeg = niet vastgelegd, zoekcode zonder spaties, land voor nieuwe/gewijzigde adressen,
// één standaard per doel en één primair contact, GGN's van 13 cijfers, btw alleen op schrijfwijze.

const today = '2026-10-26';
const terms = (over: Partial<RoleTerms> = {}): RoleTerms => ({
    paymentConditionId: 2, paymentCondition: null, paymentTermsDays: 30, paymentTermsEndOfMonth: false, discountPercent: null, discountDays: null, paymentMethod: 'BankTransfer', mandateReference: null, mandateSignedOn: null, deliveryTermKind: null, incoterm: null,
    incotermPlace: null, deliveryTermText: null, tradeConditions: null, tradeConditionsText: null, ...over,
});
const customerRole = (over: Partial<CustomerRole> = {}): CustomerRole => ({
    ...terms(), customerId: 9, currency: 'EUR', creditLimit: null, isInsured: false, insurer: null, insurerText: null, insuredLimit: null, insurerReference: null, insuredUntil: null, ...over,
});
const vendorRole = (over: Partial<VendorRole> = {}): VendorRole => ({ ...terms(), vendorId: 3, defaultCurrency: 'EUR', ggns: [], ...over });
const account = (over: Partial<RelationBankAccount> = {}): RelationBankAccount => ({
    id: 41, iban: 'NL91ABNA0417164300', otherAccountNumber: null, bic: 'ABNANL2A', accountHolder: 'Fruitimport Zuid B.V.', currency: 'EUR', isDefault: true, isActive: true,
    confirmationStatus: 'Confirmed', confirmedAt: '2026-10-20T08:00:00Z', confirmedBy: 'Anja', ...over,
});

const relation = (over: Partial<Relation> = {}): Relation => ({
    id: 5, requestId: null, searchCode: 'FRUITZ', name: 'Fruitimport Zuid B.V.', tradeName: 'FruitZuid', preferredLanguage: 'nl', website: null, email: 'info@fz.example', phone: null,
    vatNumber: 'NL812345678B01', vatVerificationStatus: 'NotChecked', vatVerifiedNumber: null, vatVerifiedAt: null, registrationNumber: '12345678', registrationCountry: 'NL', registrationType: 'KvK',
    legalForm: 'BV', internalNote: null, isActive: true, createdAt: '2026-10-22T08:00:00Z', updatedAt: '2026-10-22T08:00:00Z', version: 2,
    customer: customerRole({ paymentConditionId: 5, paymentTermsDays: 21 }),
    vendor: vendorRole({ paymentConditionId: 6, paymentTermsDays: 45, ggns: [{ id: 1, ggn: '4049929000001', producerName: 'Finca Uno', isActive: true, checkedOn: '2026-10-01', checkedBy: 'Anja', certificateValidUntil: '2027-03-31', hasGgnLabel: true }] }),
    addresses: [{ id: 11, purpose: 'Billing', label: null, line1: 'Marktweg 1, Venlo', line2: null, postalCode: null, city: null, region: null, countryCode: null, isDefault: true, isActive: true }],
    contacts: [{ id: 21, name: 'Anja', department: 'inkoop', jobTitle: null, isPrimary: true, isActive: true, note: null, channels: [{ id: 31, kind: 'Phone', label: 'mobiel', value: '06-1' }, { id: 32, kind: 'Email', label: null, value: 'anja@fz.example' }] }],
    usage: { salesOrders: 0, legacyOrders: 0, directTradeCommitments: 0, purchaseInvoices: 0, supplyCommitments: 0, defaultsInUse: [] },
    bankAccounts: [account()], bankAccountEvents: [], warnings: [],
    ...over,
});

describe('relation payload', () => {
    test('only the name is required; roles, addresses and contacts stay empty when not given', () => {
        const result = buildRelationPayload({ ...emptyRelationDraft(), name: ' Nieuwe relatie ' }, { requestId: 'r-1' });
        assert.equal(result.ok, true);
        if (!result.ok) return;
        assert.equal(result.value.name, 'Nieuwe relatie');
        assert.equal(result.value.requestId, 'r-1');
        assert.deepEqual([result.value.customer, result.value.vendor, result.value.addresses, result.value.contacts, result.value.searchCode, result.value.bankAccounts, result.value.actorReference], [null, null, [], [], null, [], null]);
        const missing = buildRelationPayload(emptyRelationDraft());
        assert.equal(missing.ok, false);
        if (!missing.ok) assert.deepEqual(groupRelationErrors(missing.errors), { name: ['required'] });
    });

    test('search code, vat shape, e-mail and registration country are checked without claiming verification', () => {
        const bad = buildRelationPayload({ ...emptyRelationDraft(), name: 'X', searchCode: 'fruit z', vatNumber: '12345', email: 'geen-mail', registrationNumber: '123' });
        assert.equal(bad.ok, false);
        if (!bad.ok) assert.deepEqual(groupRelationErrors(bad.errors), { searchCode: ['invalid'], email: ['invalid_email'], vatNumber: ['vat_shape'], registrationCountry: ['required'] });
        assert.equal(normalizeVat(' nl 8123.45.678-b01 '), 'NL812345678B01');
        const ok = buildRelationPayload({ ...emptyRelationDraft(), name: 'X', searchCode: 'FRUIT-Z.1', vatNumber: 'xi 123 456 789', registrationNumber: '123', registrationCountry: 'de' });
        assert.equal(ok.ok, true);
        if (ok.ok) assert.deepEqual([ok.value.searchCode, ok.value.vatNumber, ok.value.registrationCountry], ['FRUIT-Z.1', 'XI123456789', 'DE']);
    });

    test('addresses need a country when new or changed, at least one content field, and one default per purpose', () => {
        const draft = emptyRelationDraft();
        draft.name = 'X';
        const a = emptyAddressDraft('Delivery'); a.line1 = 'Kaai 1'; a.isDefault = true;
        const b = emptyAddressDraft('Delivery'); b.city = 'Antwerpen'; b.countryCode = 'be'; b.isDefault = true;
        const c = emptyAddressDraft('Main'); c.countryCode = 'NL';
        draft.addresses = [a, b, c];
        const bad = buildRelationPayload(draft);
        assert.equal(bad.ok, false);
        if (!bad.ok) assert.deepEqual(groupRelationErrors(bad.errors), { [`addresses.${a.key}.countryCode`]: ['country_required'], [`addresses.${c.key}.line1`]: ['address_empty'], addresses: ['two_defaults'] });
        a.countryCode = 'BE'; b.isDefault = false; draft.addresses = [a, b];
        const ok = buildRelationPayload(draft);
        assert.equal(ok.ok, true);
        if (ok.ok) assert.deepEqual(ok.value.addresses.map((x) => [x.countryCode, x.isDefault, x.line1 ?? x.city]), [['BE', true, 'Kaai 1'], ['BE', false, 'Antwerpen']]);
        // Een uit vrije tekst overgenomen legacy-adres zonder land mag onveranderd blijven, maar niet gewijzigd worden zonder land.
        const legacy = draftFromRelation(relation());
        assert.equal(buildRelationPayload(legacy, { expectedVersion: 2 }).ok, true);
        legacy.addresses[0].city = 'Venlo';
        const changed = buildRelationPayload(legacy, { expectedVersion: 2 });
        assert.equal(changed.ok, false);
        if (!changed.ok) assert.deepEqual(changed.errors.map((e) => e.code), ['country_required']);
    });

    test('contacts need a name, blank channels are skipped, bad e-mails are refused and only one primary contact is allowed', () => {
        const draft = emptyRelationDraft();
        draft.name = 'X';
        const a = emptyContactDraft(); a.name = 'A'; a.isPrimary = true; a.channels[0].value = '06-1'; a.channels[1].value = 'nope';
        const b = emptyContactDraft(); b.isPrimary = true;
        draft.contacts = [a, b];
        const bad = buildRelationPayload(draft);
        assert.equal(bad.ok, false);
        if (!bad.ok) assert.deepEqual(groupRelationErrors(bad.errors), { [`contacts.${a.key}.channels.${a.channels[1].key}`]: ['invalid_email'], [`contacts.${b.key}.name`]: ['required'], contacts: ['two_primary'] });
        a.channels[1].value = ''; b.name = 'B'; b.isPrimary = false;
        const ok = buildRelationPayload(draft);
        assert.equal(ok.ok, true);
        if (ok.ok) assert.deepEqual(ok.value.contacts.map((c) => [c.name, c.isPrimary, c.channels.map((ch) => ch.value)]), [['A', true, ['06-1']], ['B', false, []]]);
    });

    test('vendor role carries its terms, currency and 13-digit GGNs without duplicates; customer role its terms, currency and credit', () => {
        const draft = emptyRelationDraft();
        draft.name = 'X'; draft.vendorRole = true; draft.vendorTerms = { ...draft.vendorTerms, paymentTermsDays: '45', paymentTermsEndOfMonth: true, discountPercent: '2', discountDays: '', currency: 'usd' }; draft.customerRole = true;
        draft.customerTerms = { ...draft.customerTerms, currency: 'EURO', paymentMethod: 'DirectDebit' };
        const g1 = emptyGgnDraft(); g1.ggn = '4049 929 000001'; g1.producerName = 'Finca Uno';
        const g2 = emptyGgnDraft(); g2.ggn = '4049929000001';
        const g3 = emptyGgnDraft(); g3.ggn = '123';
        draft.ggns = [g1, g2, g3];
        const bad = buildRelationPayload(draft, { today });
        assert.equal(bad.ok, false);
        if (!bad.ok) assert.deepEqual(groupRelationErrors(bad.errors), {
            'customer.mandateReference': ['mandate_required'], 'customer.mandateSignedOn': ['mandate_date_required'], 'customer.currency': ['currency'],
            'vendor.discountDays': ['discount_days_required'], [`ggns.${g2.key}.ggn`]: ['ggn_duplicate'], [`ggns.${g3.key}.ggn`]: ['ggn_13_digits'],
        });
        draft.customerTerms = { ...draft.customerTerms, currency: 'eur', paymentMethod: 'BankTransfer' }; draft.credit = { ...draft.credit, creditLimit: '25000' }; draft.ggns = [g1];
        draft.vendorTerms = { ...draft.vendorTerms, discountDays: '10' };
        const ok = buildRelationPayload(draft, { today });
        assert.equal(ok.ok, true);
        if (!ok.ok) return;
        // Lege termijn = null (server: standaard), geen korting; de leverancier 45 dagen na einde maand met 2% binnen 10 dagen.
        assert.deepEqual([ok.value.customer?.paymentTermsDays, ok.value.customer?.paymentTermsEndOfMonth, ok.value.customer?.discountPercent, ok.value.customer?.discountDays, ok.value.customer?.currency, ok.value.customer?.creditLimit, ok.value.customer?.isInsured],
            [null, false, null, null, 'EUR', 25000, false]);
        assert.deepEqual([ok.value.vendor?.paymentTermsDays, ok.value.vendor?.paymentTermsEndOfMonth, ok.value.vendor?.discountPercent, ok.value.vendor?.discountDays, ok.value.vendor?.defaultCurrency, ok.value.vendor?.paymentMethod],
            [45, true, 2, 10, 'USD', 'BankTransfer']);
        assert.equal('paymentConditionId' in ok.value.vendor!, false, 'the form no longer sends a condition id');
        assert.deepEqual(ok.value.vendor?.ggns, [{ id: null, ggn: '4049929000001', producerName: 'Finca Uno', isActive: true, checkedOn: null, checkedBy: null, certificateValidUntil: null, hasGgnLabel: false }]);
    });

    test('a GGN check has a date that is not in the future and a name, both or neither', () => {
        const draft = emptyRelationDraft();
        draft.name = 'X'; draft.vendorRole = true;
        const g = emptyGgnDraft(); g.ggn = '4049929000001'; g.checkedOn = '2026-10-27';
        draft.ggns = [g];
        const bad = buildRelationPayload(draft, { today });
        assert.equal(bad.ok, false);
        if (!bad.ok) assert.deepEqual(groupRelationErrors(bad.errors), { [`ggns.${g.key}.checkedOn`]: ['ggn_check_future'], [`ggns.${g.key}.checkedBy`]: ['ggn_check_pair'] });
        g.checkedOn = today; g.checkedBy = 'Anja'; g.certificateValidUntil = '2027-01-31'; g.hasGgnLabel = true;
        const ok = buildRelationPayload(draft, { today });
        assert.equal(ok.ok, true);
        if (ok.ok) assert.deepEqual(ok.value.vendor?.ggns[0], { id: null, ggn: '4049929000001', producerName: null, isActive: true, checkedOn: today, checkedBy: 'Anja', certificateValidUntil: '2027-01-31', hasGgnLabel: true });
    });

    test('VAT per country and strict e-mail apply only to new or changed values', () => {
        const stored = relation({ vatNumber: 'NL123', email: 'oud@fz' });
        const draft = draftFromRelation(stored);
        assert.equal(buildRelationPayload(draft, { expectedVersion: 2, stored, today }).ok, true, 'unchanged legacy values stay saveable');
        draft.vatNumber = 'nl 8123.45678'; draft.email = 'nieuw@fz';
        const bad = buildRelationPayload(draft, { expectedVersion: 2, stored, today });
        assert.equal(bad.ok, false);
        if (!bad.ok) {
            assert.deepEqual(groupRelationErrors(bad.errors), { email: ['invalid_email'], vatNumber: ['vat_country'] });
            assert.deepEqual(bad.errors.find((e) => e.code === 'vat_country')?.params, { country: 'NL', example: 'NL123456789B01' });
        }
        draft.vatNumber = 'GR 123456789'; draft.email = 'nieuw@fz.nl';
        const greek = buildRelationPayload(draft, { expectedVersion: 2, stored, today });
        assert.equal(greek.ok, false);
        if (!greek.ok) assert.deepEqual(greek.errors, [{ field: 'vatNumber', code: 'vat_greece', params: { suggestion: 'EL123456789' } }]);
        // Contactkanaal: alleen een nieuw of gewijzigd adres wordt strikt getoetst.
        const withChannel = relation({ contacts: [{ id: 21, name: 'Anja', department: null, jobTitle: null, isPrimary: true, isActive: true, note: null, channels: [{ id: 32, kind: 'Email', label: null, value: 'anja@fz' }] }] });
        const channelDraft = draftFromRelation(withChannel);
        assert.equal(buildRelationPayload(channelDraft, { stored: withChannel, today }).ok, true);
        channelDraft.contacts[0].channels[0].value = 'anja2@fz';
        assert.equal(buildRelationPayload(channelDraft, { stored: withChannel, today }).ok, false);
    });

    test('an e-mail typo is a suggestion for new or changed addresses only, never an error', () => {
        assert.equal(emailSuggestion('inkoop@gmial.com', null), 'inkoop@gmail.com');
        assert.equal(emailSuggestion('inkoop@gmial.com', 'inkoop@gmial.com'), null);
        const draft = { ...emptyRelationDraft(), name: 'X', email: 'inkoop@gmial.com' };
        assert.equal(buildRelationPayload(draft, { today }).ok, true);
    });

    test('bank accounts: IBAN checks, BIC shape, holder required, one of IBAN or other number, inactive is never default, one active default', () => {
        const draft = emptyRelationDraft();
        draft.name = 'X'; draft.actorReference = 'Anja';
        const a = emptyBankAccountDraft(); a.iban = 'nl91 abna 0417 1643 01'; a.bic = 'ABNA'; a.isDefault = true;
        const b = emptyBankAccountDraft(); b.iban = 'US12345678901234'; b.accountHolder = 'X';
        const c = emptyBankAccountDraft(); c.useOther = true; c.accountHolder = 'X'; c.isDefault = true; c.isActive = false;
        const d = emptyBankAccountDraft(); d.iban = 'NL91ABNA041716430'; d.accountHolder = 'X'; d.isDefault = true;
        draft.bankAccounts = [a, b, c, d];
        const bad = buildRelationPayload(draft, { today });
        assert.equal(bad.ok, false);
        if (!bad.ok) assert.deepEqual(groupRelationErrors(bad.errors), {
            [`bankAccounts.${a.key}.iban`]: ['iban_checksum'], [`bankAccounts.${a.key}.bic`]: ['bic'], [`bankAccounts.${a.key}.accountHolder`]: ['required'],
            [`bankAccounts.${b.key}.iban`]: ['iban_country'],
            [`bankAccounts.${c.key}.otherAccountNumber`]: ['account_one_of'], [`bankAccounts.${c.key}.isDefault`]: ['inactive_default'],
            [`bankAccounts.${d.key}.iban`]: ['iban_length'], bankAccounts: ['two_default_accounts'],
        });
        a.iban = 'NL91 ABNA 0417 1643 00'; a.bic = 'abnanl2a'; a.accountHolder = ' Fruit B.V. ';
        b.iban = 'NL91ABNA0417164300';
        draft.bankAccounts = [a, b];
        const dup = buildRelationPayload(draft, { today });
        assert.equal(dup.ok, false);
        if (!dup.ok) assert.deepEqual(groupRelationErrors(dup.errors), { [`bankAccounts.${b.key}.iban`]: ['account_duplicate'] });
        c.isActive = true; c.isDefault = false; c.otherAccountNumber = 'CH-ACC 1'; c.currency = 'chf';
        draft.bankAccounts = [a, c];
        const ok = buildRelationPayload(draft, { today });
        assert.equal(ok.ok, true);
        if (ok.ok) assert.deepEqual(ok.value.bankAccounts, [
            { id: null, iban: 'NL91ABNA0417164300', otherAccountNumber: null, bic: 'ABNANL2A', accountHolder: 'Fruit B.V.', currency: 'EUR', isDefault: true, isActive: true },
            { id: null, iban: null, otherAccountNumber: 'CH-ACC 1', bic: null, accountHolder: 'X', currency: 'CHF', isDefault: false, isActive: true },
        ]);
    });

    test('"Wie" is required when an account is added, its IBAN, holder or number changes or it is (de)activated; not for BIC, currency or default', () => {
        const stored = relation();
        const draft = draftFromRelation(stored);
        assert.equal(draft.actorReference, '', 'never carried over from a previous save');
        assert.equal(bankAccountsNeedActor(draft.bankAccounts), false);
        draft.bankAccounts[0].bic = 'ABNANL2AXXX'; draft.bankAccounts[0].currency = 'USD';
        assert.equal(bankAccountsNeedActor(draft.bankAccounts), false, 'BIC and currency need no actor');
        assert.equal(buildRelationPayload(draft, { expectedVersion: 2, stored, today }).ok, true);
        for (const change of [
            (d: typeof draft) => { d.bankAccounts[0].iban = 'DE89370400440532013000'; },
            (d: typeof draft) => { d.bankAccounts[0].accountHolder = 'Ander'; },
            (d: typeof draft) => { d.bankAccounts[0].isActive = false; d.bankAccounts[0].isDefault = false; },
            (d: typeof draft) => { const extra = emptyBankAccountDraft(); extra.iban = 'BE68539007547034'; extra.accountHolder = 'X'; d.bankAccounts.push(extra); },
        ]) {
            const changed = draftFromRelation(stored);
            change(changed);
            assert.equal(bankAccountsNeedActor(changed.bankAccounts), true);
            const result = buildRelationPayload(changed, { expectedVersion: 2, stored, today });
            assert.equal(result.ok, false);
            if (!result.ok) assert.deepEqual(groupRelationErrors(result.errors), { actorReference: ['actor_required'] });
            changed.actorReference = ' Anja ';
            const ok = buildRelationPayload(changed, { expectedVersion: 2, stored, today });
            assert.equal(ok.ok, true);
            if (ok.ok) assert.equal(ok.value.actorReference, 'Anja');
        }
        // Spaties in een opgeslagen IBAN tellen niet als wijziging.
        const spaced = draftFromRelation(stored);
        spaced.bankAccounts[0].iban = 'nl91 abna 0417 1643 00';
        assert.equal(bankAccountsNeedActor(spaced.bankAccounts), false);
    });

    test('a relation round-trips through a draft with ids kept and the expected version', () => {
        const draft = draftFromRelation(relation());
        const result = buildRelationPayload(draft, { expectedVersion: 2, stored: relation(), today });
        assert.equal(result.ok, true);
        if (!result.ok) return;
        assert.equal(result.value.expectedVersion, 2);
        assert.deepEqual([result.value.customer?.paymentTermsDays, result.value.vendor?.paymentTermsDays, result.value.vendor?.ggns[0].id, result.value.addresses[0].id, result.value.contacts[0].channels.map((c) => c.id)], [21, 45, 1, 11, [31, 32]]);
        assert.deepEqual(result.value.vendor?.ggns[0], { id: 1, ggn: '4049929000001', producerName: 'Finca Uno', isActive: true, checkedOn: '2026-10-01', checkedBy: 'Anja', certificateValidUntil: '2027-03-31', hasGgnLabel: true });
        assert.deepEqual([result.value.bankAccounts.map((a) => a.id), result.value.actorReference], [[41], null]);
    });
});

describe('relation list', () => {
    const item = (over: Partial<RelationListItem>): RelationListItem => ({ id: 1, searchCode: null, name: 'A', tradeName: null, customerId: null, vendorId: null, isActive: true, city: null, countryCode: null, email: null, phone: null, primaryContactName: null, vatNumber: null, version: 0, ...over });
    test('role is derived from the present roles', () => {
        assert.deepEqual([relationRole(item({})), relationRole(item({ customerId: 1 })), relationRole(item({ vendorId: 2 })), relationRole(item({ customerId: 1, vendorId: 2 }))], ['none', 'customer', 'vendor', 'both']);
    });
});
