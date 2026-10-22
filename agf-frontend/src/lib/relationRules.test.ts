import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { Relation, RelationListItem } from '../types/relation';
import { buildRelationPayload, draftFromRelation, emptyAddressDraft, emptyContactDraft, emptyGgnDraft, emptyRelationDraft, groupRelationErrors, normalizeVat, relationRole } from './relationRules.ts';

// Relatieregels: alleen de naam verplicht, leeg = niet vastgelegd, zoekcode zonder spaties, land voor nieuwe/gewijzigde adressen,
// één standaard per doel en één primair contact, GGN's van 13 cijfers, btw alleen op schrijfwijze.

const relation = (over: Partial<Relation> = {}): Relation => ({
    id: 5, requestId: null, searchCode: 'FRUITZ', name: 'Fruitimport Zuid B.V.', tradeName: 'FruitZuid', preferredLanguage: 'nl', website: null, email: 'info@fz.example', phone: null,
    vatNumber: 'NL812345678B01', vatVerificationStatus: 'NotChecked', vatVerifiedNumber: null, vatVerifiedAt: null, registrationNumber: '12345678', registrationCountry: 'NL', registrationType: 'KvK',
    legalForm: 'BV', internalNote: null, isActive: true, createdAt: '2026-10-22T08:00:00Z', updatedAt: '2026-10-22T08:00:00Z', version: 2,
    customer: { customerId: 9, paymentTermsDays: 21 }, vendor: { vendorId: 3, paymentTermsDays: 45, defaultCurrency: 'EUR', ggns: [{ id: 1, ggn: '4049929000001', producerName: 'Finca Uno', isActive: true }] },
    addresses: [{ id: 11, purpose: 'Billing', label: null, line1: 'Marktweg 1, Venlo', line2: null, postalCode: null, city: null, region: null, countryCode: null, isDefault: true, isActive: true }],
    contacts: [{ id: 21, name: 'Anja', department: 'inkoop', jobTitle: null, isPrimary: true, isActive: true, note: null, channels: [{ id: 31, kind: 'Phone', label: 'mobiel', value: '06-1' }, { id: 32, kind: 'Email', label: null, value: 'anja@fz.example' }] }],
    usage: { salesOrders: 0, legacyOrders: 0, directTradeCommitments: 0, purchaseInvoices: 0, supplyCommitments: 0, defaultsInUse: [] },
    ...over,
});

describe('relation payload', () => {
    test('only the name is required; roles, addresses and contacts stay empty when not given', () => {
        const result = buildRelationPayload({ ...emptyRelationDraft(), name: ' Nieuwe relatie ' }, { requestId: 'r-1' });
        assert.equal(result.ok, true);
        if (!result.ok) return;
        assert.equal(result.value.name, 'Nieuwe relatie');
        assert.equal(result.value.requestId, 'r-1');
        assert.deepEqual([result.value.customer, result.value.vendor, result.value.addresses, result.value.contacts, result.value.searchCode], [null, null, [], [], null]);
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

    test('vendor role carries terms, currency and 13-digit GGNs without duplicates; customer role only its terms', () => {
        const draft = emptyRelationDraft();
        draft.name = 'X'; draft.vendorRole = true; draft.vendorPaymentTermsDays = '45'; draft.vendorCurrency = 'eur'; draft.customerRole = true; draft.customerPaymentTermsDays = '400';
        const g1 = emptyGgnDraft(); g1.ggn = '4049 929 000001'; g1.producerName = 'Finca Uno';
        const g2 = emptyGgnDraft(); g2.ggn = '4049929000001';
        const g3 = emptyGgnDraft(); g3.ggn = '123';
        draft.ggns = [g1, g2, g3];
        const bad = buildRelationPayload(draft);
        assert.equal(bad.ok, false);
        if (!bad.ok) assert.deepEqual(groupRelationErrors(bad.errors), { customerPaymentTermsDays: ['terms_range'], [`ggns.${g2.key}.ggn`]: ['ggn_duplicate'], [`ggns.${g3.key}.ggn`]: ['ggn_13_digits'] });
        draft.customerPaymentTermsDays = '21'; draft.ggns = [g1];
        const ok = buildRelationPayload(draft);
        assert.equal(ok.ok, true);
        if (ok.ok) assert.deepEqual([ok.value.customer, ok.value.vendor], [{ paymentTermsDays: 21 }, { paymentTermsDays: 45, defaultCurrency: 'EUR', ggns: [{ id: null, ggn: '4049929000001', producerName: 'Finca Uno', isActive: true }] }]);
    });

    test('a relation round-trips through a draft with ids kept and the expected version', () => {
        const draft = draftFromRelation(relation());
        const result = buildRelationPayload(draft, { expectedVersion: 2 });
        assert.equal(result.ok, true);
        if (!result.ok) return;
        assert.equal(result.value.expectedVersion, 2);
        assert.deepEqual([result.value.customer, result.value.vendor?.ggns[0].id, result.value.addresses[0].id, result.value.contacts[0].channels.map((c) => c.id)], [{ paymentTermsDays: 21 }, 1, 11, [31, 32]]);
    });
});

describe('relation list', () => {
    const item = (over: Partial<RelationListItem>): RelationListItem => ({ id: 1, searchCode: null, name: 'A', tradeName: null, customerId: null, vendorId: null, isActive: true, city: null, countryCode: null, email: null, phone: null, primaryContactName: null, vatNumber: null, version: 0, ...over });
    test('role is derived from the present roles', () => {
        assert.deepEqual([relationRole(item({})), relationRole(item({ customerId: 1 })), relationRole(item({ vendorId: 2 })), relationRole(item({ customerId: 1, vendorId: 2 }))], ['none', 'customer', 'vendor', 'both']);
    });
});
