import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, deferred, json, setValue } from '../../test/reactHarness.ts';
import type { Relation, RelationReference } from '../../types/relation';
import RelationForm from './RelationForm';

// Relatiekaart door de echte componentlevenscyclus: aanmaken met alleen een naam (Ctrl+S), Enter slaat niets op, inline fouten met
// behoud van invoer, adressen/contacten/kanalen inline, gelijkende naam als definitieve 409 met "toch aanmaken" als nieuwe
// opdracht, bevroren opdracht met dezelfde requestId bij een onbekende uitkomst, tweede rol toevoegen zonder tweede onderneming.

const reference: RelationReference = {
    languages: ['nl', 'en'], addressPurposes: ['Main', 'Billing', 'Delivery'], departmentSuggestions: ['verkoop', 'inkoop'], registrationTypeSuggestions: ['KvK'], legalFormSuggestions: ['BV'],
    currencies: ['EUR', 'USD'], vatVerificationStatuses: ['NotChecked'], globalGapDatabaseUrl: 'https://database.globalgap.org/', legacyArticleGgns: [],
};
const saved = (over: Partial<Relation> = {}): Relation => ({
    id: 7, requestId: 'r-1', searchCode: 'FRUITZ', name: 'Fruitimport Zuid', tradeName: null, preferredLanguage: null, website: null, email: null, phone: null, vatNumber: null, vatVerificationStatus: 'NotApplicable',
    vatVerifiedNumber: null, vatVerifiedAt: null, registrationNumber: null, registrationCountry: null, registrationType: null, legalForm: null, internalNote: null, isActive: true,
    createdAt: '2026-10-22T08:00:00Z', updatedAt: '2026-10-22T08:00:00Z', version: 0, customer: { customerId: 3, paymentTermsDays: 30 }, vendor: null, addresses: [], contacts: [],
    usage: { salesOrders: 0, legacyOrders: 0, directTradeCommitments: 0, purchaseInvoices: 0, supplyCommitments: 0, defaultsInUse: [] }, ...over,
});

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let respond: (call: Call) => Response | Promise<Response>;

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const type = async (element: HTMLInputElement | HTMLTextAreaElement, value: string) => { await act(async () => { setValue(element, value); }); };
const text = () => mounted.container.textContent ?? '';
const press = async (key: string, options: KeyboardEventInit = {}) => { await act(async () => { window.document.dispatchEvent(new window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...options })); }); };
const enterIn = async (element: HTMLInputElement) => {
    const event = new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    await act(async () => { element.dispatchEvent(event); });
    return event.defaultPrevented;
};
const click = async (button: HTMLElement) => { await act(async () => { button.click(); }); };
const posts = () => calls.filter((call) => call.method === 'POST');

describe('RelationForm', () => {
    beforeEach(() => {
        calls = [];
        respond = () => json(404, {});
        window.sessionStorage.clear();
        globalThis.fetch = (async (inputUrl: RequestInfo | URL, init?: RequestInit) => {
            const call: Call = { method: init?.method ?? 'GET', url: String(inputUrl), body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            return respond(call);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('a relation needs only a name; Ctrl+S saves, Enter in a field saves nothing, roles and lists are sent explicitly', async () => {
        respond = (call) => json(201, saved({ name: (call.body as { name: string }).name, searchCode: null, customer: null }));
        let created: Relation | null = null;
        await mounted.render(<RelationForm relation={null} reference={reference} onSaved={(relation) => { created = relation; }} onClose={() => {}} />);
        assert.equal(window.document.activeElement, input('searchCode'), 'search code gets focus first');
        await type(input('name'), 'Fruitimport Zuid');
        assert.equal(await enterIn(input('name')), true, 'Enter is swallowed');
        assert.equal(posts().length, 0, 'Enter did not submit');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => posts().length === 1, 'posted');
        const body = posts()[0].body!;
        assert.equal(typeof body.requestId, 'string');
        assert.deepEqual([body.name, body.searchCode, body.customer, body.vendor, body.addresses, body.contacts, body.isActive, body.acknowledgeSimilarNames], ['Fruitimport Zuid', null, null, null, [], [], true, false]);
        await mounted.waitFor(() => created !== null, 'onSaved');
        assert.ok(text().includes('relations_created'));
        assert.equal(window.sessionStorage.getItem('agf_relation_pending_create'), null, 'nothing frozen after success');
    });

    test('inline validation keeps the input, opens the right section and sends nothing', async () => {
        await mounted.render(<RelationForm relation={null} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('searchCode'), 'fruit z');
        await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="relation-addresses"]')!);
        await click(mounted.button('+ relations_address_Delivery')!);
        const key = mounted.container.querySelector('[data-testid^="address-"]')!.getAttribute('data-testid')!.replace('address-', '');
        await type(input(`addresses.${key}.line1`), 'Kaai 1');
        await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="relation-addresses"]')!); // dichtklappen
        assert.equal(mounted.container.querySelector('#relation-addresses'), null);
        await click(mounted.container.querySelector<HTMLButtonElement>('button[type="submit"]')!);
        assert.equal(posts().length, 0, 'nothing sent');
        assert.ok(text().includes('relations_error_required'), 'name required');
        assert.ok(text().includes('relations_error_invalid'), 'search code shape');
        assert.ok(text().includes('relations_error_country_required'), 'country for a new address');
        assert.ok(mounted.container.querySelector('#relation-addresses'), 'section with the error opened again');
        assert.equal(input('searchCode').value, 'fruit z', 'input kept');
        assert.equal(input(`addresses.${key}.line1`).value, 'Kaai 1', 'address row kept');
        await type(input(`addresses.${key}.countryCode`), 'be');
        await type(input('searchCode'), 'FRUITZ');
        await type(input('name'), 'X');
        respond = () => json(201, saved({ name: 'X', customer: null }));
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => posts().length === 1, 'posted after fixing');
        assert.deepEqual(posts()[0].body!.addresses, [{ id: null, purpose: 'Delivery', label: null, line1: 'Kaai 1', line2: null, postalCode: null, city: null, region: null, countryCode: 'BE', isDefault: true, isActive: true }]);
    });

    test('contacts with several phones and e-mails are edited inline and sent as channels; blank channel rows are dropped', async () => {
        respond = () => json(201, saved({ customer: null }));
        await mounted.render(<RelationForm relation={null} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('name'), 'Fruitimport Zuid');
        await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="relation-contacts"]')!);
        await click(mounted.button('+ relations_contact_add')!);
        const contact = mounted.container.querySelector('[data-testid^="contact-"]')!;
        const key = contact.getAttribute('data-testid')!.replace('contact-', '');
        await type(input(`contacts.${key}.name`), 'Anja');
        await type(input(`contacts.${key}.department`), 'inkoop');
        const channelInputs = () => Array.from(contact.querySelectorAll<HTMLInputElement>('input[id^="contacts."][id*=".channels."]'));
        assert.equal(channelInputs().length, 2, 'a phone and an e-mail row to start with');
        await type(channelInputs()[0], '06-1');
        await type(channelInputs()[1], 'anja@fz.example');
        await click(Array.from(contact.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent === '+ relations_channel_phone')!);
        await type(channelInputs()[2], '077-1');
        await click(Array.from(contact.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent === '+ relations_channel_email')!);
        assert.equal(channelInputs().length, 4);
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => posts().length === 1, 'posted');
        const contacts = posts()[0].body!.contacts as { name: string; department: string; isPrimary: boolean; channels: { kind: string; value: string }[] }[];
        assert.equal(contacts.length, 1);
        assert.deepEqual([contacts[0].name, contacts[0].department, contacts[0].isPrimary], ['Anja', 'inkoop', true]);
        assert.deepEqual(contacts[0].channels.map((c) => [c.kind, c.value]), [['Phone', '06-1'], ['Email', 'anja@fz.example'], ['Phone', '077-1']]);
    });

    test('a similar name is a final 409: nothing frozen, "create anyway" is a new command with a new requestId and the flag set', async () => {
        respond = (call) => ((call.body as { acknowledgeSimilarNames: boolean }).acknowledgeSimilarNames
            ? json(201, saved({ customer: null }))
            : new Response(JSON.stringify({ message: 'similar', code: 'similar-names', similarRelations: [{ id: 7, searchCode: 'FRUITZ', name: 'Fruitimport Zuid', customerId: 3, vendorId: null }] }), { status: 409, headers: { 'Content-Type': 'application/json' } }));
        await mounted.render(<RelationForm relation={null} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('name'), 'fruitimport zuid');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => text().includes('relations_similar_title'), 'similar shown');
        assert.ok(text().includes('Fruitimport Zuid') && text().includes('relations_role_customer'), 'existing relation with its role shown');
        assert.equal(window.sessionStorage.getItem('agf_relation_pending_create'), null, 'final rejection freezes nothing');
        await click(mounted.button('relations_create_anyway')!);
        await mounted.waitFor(() => posts().length === 2, 'second command');
        assert.notEqual(posts()[0].body!.requestId, posts()[1].body!.requestId, 'new requestId');
        assert.equal(posts()[1].body!.acknowledgeSimilarNames, true);
    });

    test('an unknown outcome freezes payload and requestId (also in sessionStorage) and the retry repeats exactly the same command', async () => {
        const first = deferred<Response>();
        respond = () => first.promise;
        await mounted.render(<RelationForm relation={null} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('name'), 'Fruitimport Zuid');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => posts().length === 1, 'posted');
        await act(async () => { first.reject(new TypeError('Failed to fetch')); await new Promise((resolve) => setTimeout(resolve, 0)); });
        await mounted.waitFor(() => text().includes('relations_pending_title'), 'pending notice');
        assert.ok(input('name').disabled, 'locked while frozen');
        const stored = JSON.parse(window.sessionStorage.getItem('agf_relation_pending_create')!) as { requestId: string; payload: { name: string } };
        assert.equal(stored.requestId, posts()[0].body!.requestId);
        respond = () => json(200, saved({ customer: null }));
        await click(mounted.button('supply_retry_same')!);
        await mounted.waitFor(() => posts().length === 2, 'retried');
        assert.deepEqual(posts()[1].body, posts()[0].body, 'byte-for-byte the same command');
        await mounted.waitFor(() => text().includes('relations_saved_before'), 'replayed notice');
        assert.equal(window.sessionStorage.getItem('agf_relation_pending_create'), null);
    });

    test('editing sends a PUT with the expected version; the second role is added with one click and keeps the customer id', async () => {
        respond = (call) => (call.method === 'PUT' ? json(200, saved({ version: 1, tradeName: 'FZ' })) : call.url.endsWith('/roles/vendor') ? json(201, saved({ version: 1, tradeName: 'FZ', vendor: { vendorId: 9, paymentTermsDays: 30, defaultCurrency: 'EUR', ggns: [] } })) : json(404, {}));
        await mounted.render(<RelationForm relation={saved()} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        assert.equal(input('searchCode').value, 'FRUITZ');
        assert.ok(input('customerRole').checked && input('customerRole').disabled, 'an existing role is never removed here');
        await type(input('tradeName'), 'FZ');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'updated');
        const put = calls.find((call) => call.method === 'PUT')!;
        assert.ok(put.url.endsWith('/relations/7'));
        assert.deepEqual([put.body!.expectedVersion, put.body!.tradeName, put.body!.customer], [0, 'FZ', { paymentTermsDays: 30 }]);
        await mounted.waitFor(() => text().includes('relations_saved'), 'saved');
        if (!mounted.container.querySelector('#relation-defaults')) await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="relation-defaults"]')!);
        await click(mounted.button('+ relations_add_role_vendor')!);
        await mounted.waitFor(() => calls.some((call) => call.url.endsWith('/roles/vendor')), 'role posted');
        await mounted.waitFor(() => input('vendorRole').checked, 'vendor role shown');
        assert.ok(text().includes('#3') && text().includes('#9'), 'both role numbers shown');
        await mounted.waitFor(() => text().includes('relations_role_added'), 'notice');
    });
});
