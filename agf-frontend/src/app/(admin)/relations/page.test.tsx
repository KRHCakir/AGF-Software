import '../../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../../test/reactHarness.ts';
import RelationsPage from './page';

// Werkplek Relaties door de hele pagina: de server bepaalt de zoekresultaten. Een relatie die alleen via een tweede contactpersoon of
// een telefoonnummer van een contact matcht (primair contact is iemand anders) blijft zichtbaar en opent met Enter.

const reference = { languages: ['nl'], addressPurposes: ['Main', 'Billing', 'Delivery'], departmentSuggestions: [], registrationTypeSuggestions: [], legalFormSuggestions: [], currencies: ['EUR'], vatVerificationStatuses: ['NotChecked'], globalGapDatabaseUrl: 'https://database.globalgap.org/', legacyArticleGgns: [],
    globalGapPortalUrl: 'https://portal.example/certificates', paymentMethods: ['BankTransfer'], paymentConditionBases: ['InvoiceDate'], defaultPaymentTermsDays: 30, deliveryTermKinds: [], incoterms: [], tradeConditions: [], creditInsurers: [] };
const oost = { id: 4, searchCode: 'OOST', name: 'Fruithandel Oost', tradeName: null, customerId: 2, vendorId: null, isActive: true, city: 'Venlo', countryCode: 'NL', email: null, phone: null, primaryContactName: 'Jan de Vries', vatNumber: null, version: 0 };
const other = { ...oost, id: 5, searchCode: 'WEST', name: 'Groenten West', primaryContactName: 'Kees' };
const card = { id: 4, requestId: null, searchCode: 'OOST', name: 'Fruithandel Oost', tradeName: null, preferredLanguage: null, website: null, email: null, phone: null, vatNumber: null, vatVerificationStatus: 'NotApplicable', vatVerifiedNumber: null, vatVerifiedAt: null,
    registrationNumber: null, registrationCountry: null, registrationType: null, legalForm: null, internalNote: null, isActive: true, createdAt: '2026-10-22T08:00:00Z', updatedAt: '2026-10-22T08:00:00Z', version: 0,
    customer: { customerId: 2, paymentConditionId: 1, paymentCondition: null, paymentTermsDays: 30, paymentTermsEndOfMonth: false, discountPercent: null, discountDays: null, paymentMethod: 'BankTransfer', mandateReference: null, mandateSignedOn: null, currency: 'EUR', deliveryTermKind: null,
        incoterm: null, incotermPlace: null, deliveryTermText: null, tradeConditions: null, tradeConditionsText: null, creditLimit: null, isInsured: false, insurer: null, insurerText: null, insuredLimit: null,
        insurerReference: null, insuredUntil: null },
    vendor: null, addresses: [], contacts: [], usage: { salesOrders: 0, legacyOrders: 0, directTradeCommitments: 0, purchaseInvoices: 0, supplyCommitments: 0, defaultsInUse: [] },
    bankAccounts: [], bankAccountEvents: [], warnings: [] };

let mounted: Mounted;
let urls: string[];

describe('RelationsPage search', () => {
    beforeEach(() => {
        urls = [];
        globalThis.fetch = (async (input: RequestInfo | URL) => {
            const url = String(input);
            urls.push(url);
            if (url.includes('/relations/reference')) return json(200, reference);
            if (/\/relations\/4$/.test(url)) return json(200, card);
            const query = new URL(url, 'http://x').searchParams.get('query') ?? '';
            // Server: zoekt in alle contacten en kanalen; "pieter" en "077-222" matchen alleen de tweede contactpersoon van Oost.
            const items = query === '' ? [oost, other] : ['pieter', '077-222'].includes(query.toLowerCase()) ? [oost] : [];
            return json(200, { items, total: items.length, page: 1, pageSize: 50 });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    for (const term of ['Pieter', '077-222']) {
        test(`a relation found by the server via "${term}" (not the primary contact) stays in the list and opens with Enter`, async () => {
            await mounted.render(<RelationsPage />);
            await mounted.waitFor(() => mounted.container.querySelector('[data-testid="relation-row-5"]'), 'initial list');
            const search = mounted.container.querySelector<HTMLInputElement>('#relation-search')!;
            await act(async () => { setValue(search, term); });
            await act(async () => { await new Promise((resolve) => setTimeout(resolve, 300)); }); // zoek-debounce (200 ms) verstrijkt
            await mounted.waitFor(() => urls.some((url) => url.includes(`query=${encodeURIComponent(term)}`)) && mounted.container.querySelector('[data-testid="relation-row-5"]') === null, 'server searched');
            await mounted.waitFor(() => mounted.container.querySelector('[data-testid="relation-row-4"]'), 'match shown');
            assert.ok(mounted.hasText('Jan de Vries'), 'row still shows the primary contact');
            await act(async () => { search.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
            await mounted.waitFor(() => mounted.container.querySelector<HTMLInputElement>('[id="searchCode-input"]')?.value === 'OOST', 'card opened');
        });
    }
});

// Fixes 5b: na opslaan van een nieuwe relatie verdwijnt het oude formulier; de lijst ververst en toont de nieuwe relatie kort
// gemarkeerd, en de melding blijft staan.
describe('RelationsPage after creating a relation', () => {
    let created = false;
    let listed = true;
    beforeEach(() => {
        listed = true;
        urls = [];
        created = false;
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = String(input);
            urls.push(url);
            if (url.includes('/relations/reference')) return json(200, reference);
            if (/\/relations\/4$/.test(url)) return json(200, card);
            if ((init?.method ?? 'GET') === 'POST' && /\/relations$/.test(url)) { created = true; return json(201, { ...card, id: 9, name: 'Nieuwe Kweker', searchCode: 'NIEUW', customer: null }); }
            const items = created && listed ? [oost, other, { ...oost, id: 9, name: 'Nieuwe Kweker', searchCode: 'NIEUW', customerId: null }] : [oost, other];
            return json(200, { items, total: items.length, page: 1, pageSize: 50 });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the form closes, the list reloads with the new relation marked, and the notice stays until something else is opened', async () => {
        window.history.replaceState(null, '', '/relations?new=1');
        await mounted.render(<RelationsPage />);
        await mounted.waitFor(() => mounted.container.querySelector('[id="name-input"]'), 'new form open');
        const listCalls = () => urls.filter((url) => /\/relations\?/.test(url)).length;
        const before = listCalls();
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[id="name-input"]')!, 'Nieuwe Kweker'); });
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(); });
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="created-notice"]'), 'notice');
        assert.equal(mounted.container.querySelector('[id="name-input"]'), null, 'the old form is gone');
        assert.ok(mounted.container.querySelector('[data-testid="created-notice"]')!.textContent!.includes('relations_created'));
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="relation-row-9"]'), 'list reloaded with the new relation');
        assert.equal(mounted.container.querySelector('tbody tr[data-testid^="relation-row-"]')!.getAttribute('data-testid'), 'relation-row-9', 'on top, right under the notice');
        assert.ok(listCalls() > before, 'list fetched again');
        assert.equal(mounted.container.querySelector('[data-testid="relation-row-9"]')!.getAttribute('data-fresh'), 'true', 'new row marked');
        await act(async () => { mounted.container.querySelector<HTMLElement>('[data-testid="relation-row-4"]')!.click(); });
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="created-notice"]') === null, 'notice cleared when opening another relation');
        window.history.replaceState(null, '', '/relations');
    });

    test('a new relation outside the loaded page is shown on top as well', async () => {
        listed = false;
        window.history.replaceState(null, '', '/relations?new=1');
        await mounted.render(<RelationsPage />);
        await mounted.waitFor(() => mounted.container.querySelector('[id="name-input"]'), 'new form open');
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[id="name-input"]')!, 'Nieuwe Kweker'); });
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('button[type="submit"]')!.click(); });
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="relation-row-9"]'), 'pinned row');
        const rows = Array.from(mounted.container.querySelectorAll('tbody tr[data-testid^="relation-row-"]')).map((row) => row.getAttribute('data-testid'));
        assert.deepEqual(rows, ['relation-row-9', 'relation-row-4', 'relation-row-5'], 'the new relation on top');
        window.history.replaceState(null, '', '/relations');
    });
});
