import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, deferred, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import type { PaymentCondition } from '../../types/paymentCondition';
import type { CustomerRole, Relation, RelationBankAccount, RelationReference, RoleTerms, VendorRole } from '../../types/relation';
import RelationForm from './RelationForm';

// Relatiekaart door de echte componentlevenscyclus: aanmaken met alleen een naam (Ctrl+S), Enter slaat niets op, inline fouten met
// behoud van invoer, adressen/contacten/kanalen inline, gelijkende naam als definitieve 409 met "toch aanmaken" als nieuwe
// opdracht, bevroren opdracht met dezelfde requestId bij een onbekende uitkomst, tweede rol toevoegen zonder tweede onderneming.

const reference: RelationReference = {
    languages: ['nl', 'en'], addressPurposes: ['Main', 'Billing', 'Delivery'], departmentSuggestions: ['verkoop', 'inkoop'], registrationTypeSuggestions: ['KvK'], legalFormSuggestions: ['BV'],
    currencies: ['EUR', 'USD'], vatVerificationStatuses: ['NotChecked'], globalGapDatabaseUrl: 'https://database.globalgap.org/', legacyArticleGgns: [],
    globalGapPortalUrl: 'https://portal.example/certificates', paymentMethods: ['BankTransfer', 'DirectDebit', 'Cash', 'Offset'], paymentConditionBases: ['InvoiceDate', 'EndOfMonth'],
    deliveryTermKinds: ['Incoterm', 'Franco', 'AfKwekerij', 'Other'], incoterms: ['EXW', 'FCA', 'DAP'], tradeConditions: ['Own', 'Frugiventa', 'Cofreurop', 'Other'], creditInsurers: ['Atradius', 'AllianzTrade', 'Coface', 'Other'],
};
const terms = (over: Partial<RoleTerms> = {}): RoleTerms => ({
    paymentConditionId: 1, paymentCondition: null, paymentTermsDays: 30, paymentMethod: 'BankTransfer', mandateReference: null, mandateSignedOn: null, deliveryTermKind: null, incoterm: null,
    incotermPlace: null, deliveryTermText: null, tradeConditions: null, tradeConditionsText: null, ...over,
});
const customerRole = (over: Partial<CustomerRole> = {}): CustomerRole => ({
    ...terms(), customerId: 3, currency: 'EUR', creditLimit: null, isInsured: false, insurer: null, insurerText: null, insuredLimit: null, insurerReference: null, insuredUntil: null, ...over,
});
const vendorRole = (over: Partial<VendorRole> = {}): VendorRole => ({ ...terms(), vendorId: 9, defaultCurrency: 'EUR', ggns: [], ...over });
const saved = (over: Partial<Relation> = {}): Relation => ({
    id: 7, requestId: 'r-1', searchCode: 'FRUITZ', name: 'Fruitimport Zuid', tradeName: null, preferredLanguage: null, website: null, email: null, phone: null, vatNumber: null, vatVerificationStatus: 'NotApplicable',
    vatVerifiedNumber: null, vatVerifiedAt: null, registrationNumber: null, registrationCountry: null, registrationType: null, legalForm: null, internalNote: null, isActive: true,
    createdAt: '2026-10-22T08:00:00Z', updatedAt: '2026-10-22T08:00:00Z', version: 0, customer: customerRole(), vendor: null, addresses: [], contacts: [],
    usage: { salesOrders: 0, legacyOrders: 0, directTradeCommitments: 0, purchaseInvoices: 0, supplyCommitments: 0, defaultsInUse: [] }, bankAccounts: [], bankAccountEvents: [], warnings: [], ...over,
});

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let respond: (call: Call) => Response | Promise<Response>;

const condition = (over: Partial<PaymentCondition>): PaymentCondition => ({
    id: 1, code: '30D', descriptionNl: '30 dagen na factuurdatum', descriptionEn: '30 days after invoice date', days: 30, basis: 'InvoiceDate', discountPercent: null, discountDays: null, isActive: true,
    sortOrder: 30, version: 0, customerCount: 1, vendorCount: 0, ...over,
});
const activeConditions = [condition({}), condition({ id: 2, code: 'EOM30', descriptionNl: '30 dagen einde maand', descriptionEn: '30 days end of month', basis: 'EndOfMonth' })];
const bankAccount = (over: Partial<RelationBankAccount> = {}): RelationBankAccount => ({
    id: 41, iban: 'NL91ABNA0417164300', otherAccountNumber: null, bic: 'ABNANL2A', accountHolder: 'Fruitimport Zuid', currency: 'EUR', isDefault: true, isActive: true,
    confirmationStatus: 'Unconfirmed', confirmedAt: null, confirmedBy: null, ...over,
});
const credit = { customerId: 3, asOf: '2026-10-26', openInvoicesAmount: 1200, openInvoicesCount: 2, deliveredNotInvoicedAmount: 300, deliveredNotInvoicedCount: 1, openOrdersAmount: 0, openOrdersCount: 0,
    totalOutstanding: 1500, unpricedSalesOrdersCount: 3, unpricedNote: '3 orders zonder prijs, niet meegeteld', isComplete: false, creditLimit: 1000, overLimitAmount: 500, isOverLimit: true,
    isInsured: true, insurer: 'Atradius', insuredLimit: 750, insuredUntil: '2026-11-10', insuranceGap: 250, insuranceExpired: false, insuranceExpiresWithin30Days: true };

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const select = (id: string) => mounted.container.querySelector<HTMLSelectElement>(`[id="${id}-input"]`)!;
const choose = async (element: HTMLSelectElement, value: string) => { await act(async () => { setSelectValue(element, value); }); };
const openSection = async (name: string) => { if (!mounted.container.querySelector(`#relation-${name}`)) await click(mounted.container.querySelector<HTMLButtonElement>(`button[aria-controls="relation-${name}"]`)!); };
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
        respond = (call) => (call.method === 'PUT' ? json(200, saved({ version: 1, tradeName: 'FZ' })) : call.url.endsWith('/roles/vendor') ? json(201, saved({ version: 1, tradeName: 'FZ', vendor: vendorRole() })) : json(404, {}));
        await mounted.render(<RelationForm relation={saved()} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        assert.equal(input('searchCode').value, 'FRUITZ');
        assert.ok(input('customerRole').checked && input('customerRole').disabled, 'an existing role is never removed here');
        await type(input('tradeName'), 'FZ');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'updated');
        const put = calls.find((call) => call.method === 'PUT')!;
        assert.ok(put.url.endsWith('/relations/7'));
        const customer = put.body!.customer as { paymentConditionId: number; paymentMethod: string; currency: string; isInsured: boolean };
        assert.deepEqual([put.body!.expectedVersion, put.body!.tradeName, customer.paymentConditionId, customer.paymentMethod, customer.currency, customer.isInsured], [0, 'FZ', 1, 'BankTransfer', 'EUR', false]);
        await mounted.waitFor(() => text().includes('relations_saved'), 'saved');
        if (!mounted.container.querySelector('#relation-defaults')) await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="relation-defaults"]')!);
        await click(mounted.button('+ relations_add_role_vendor')!);
        await mounted.waitFor(() => calls.some((call) => call.url.endsWith('/roles/vendor')), 'role posted');
        await mounted.waitFor(() => input('vendorRole').checked, 'vendor role shown');
        assert.ok(text().includes('#3') && text().includes('#9'), 'both role numbers shown');
        await mounted.waitFor(() => text().includes('relations_role_added'), 'notice');
    });

    test('adding a second role keeps unsaved name, address and contact changes; Save then sends them on the returned version', async () => {
        const stored = saved({
            version: 3,
            addresses: [{ id: 11, purpose: 'Main', label: null, line1: 'Marktweg 1', line2: null, postalCode: null, city: 'Venlo', region: null, countryCode: 'NL', isDefault: true, isActive: true }],
            contacts: [{ id: 21, name: 'Jan', department: null, jobTitle: null, isPrimary: true, isActive: true, note: null, channels: [] }],
        });
        respond = (call) => (call.url.endsWith('/roles/vendor')
            ? json(201, { ...stored, vendor: vendorRole({ paymentConditionId: 4, paymentTermsDays: 45, defaultCurrency: 'USD' }) })
            : call.method === 'PUT' ? json(200, { ...stored, version: 4 }) : json(404, {}));
        await mounted.render(<RelationForm relation={stored} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('name'), 'Fruitimport Zuid (nieuw)');
        await type(input('addresses.' + mounted.container.querySelector('[data-testid^="address-"]')!.getAttribute('data-testid')!.replace('address-', '') + '.city'), 'Tegelen');
        const contactKey = mounted.container.querySelector('[data-testid^="contact-"]')!.getAttribute('data-testid')!.replace('contact-', '');
        await type(input(`contacts.${contactKey}.name`), 'Pieter');
        if (!mounted.container.querySelector('#relation-defaults')) await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="relation-defaults"]')!);
        await click(mounted.button('+ relations_add_role_vendor')!);
        await mounted.waitFor(() => input('vendorRole').checked, 'vendor role shown');
        assert.equal(input('name').value, 'Fruitimport Zuid (nieuw)', 'unsaved name kept');
        assert.equal(input(`contacts.${contactKey}.name`).value, 'Pieter', 'unsaved contact kept');
        assert.ok(mounted.container.querySelector<HTMLInputElement>('input[value="Tegelen"]'), 'unsaved address kept');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'saved');
        const body = calls.find((call) => call.method === 'PUT')!.body!;
        assert.equal(body.name, 'Fruitimport Zuid (nieuw)');
        assert.equal((body.addresses as { city: string }[])[0].city, 'Tegelen');
        assert.equal((body.contacts as { name: string }[])[0].name, 'Pieter');
        const vendor = body.vendor as { paymentConditionId: number; defaultCurrency: string; ggns: unknown[] };
        assert.deepEqual([vendor.paymentConditionId, vendor.defaultCurrency, vendor.ggns], [4, 'USD', []], 'role defaults from the server');
        assert.equal(body.expectedVersion, 3);
    });

    test('when a colleague changed the card before the role was added, Save keeps the base version of the draft and gets a conflict instead of overwriting', async () => {
        const stored = saved({ version: 3 });
        // Collega heeft intussen de handelsnaam gewijzigd (versie 4); daarna voegt deze gebruiker de leveranciersrol toe.
        const newest = saved({ version: 4, tradeName: 'Door collega', vendor: vendorRole() });
        respond = (call) => (call.url.endsWith('/roles/vendor') ? json(201, newest)
            : call.method === 'PUT' ? json(409, { message: 'Relation 7 was changed by someone else (version 4, expected 3); reload and apply your change again.' }) : json(404, {}));
        await mounted.render(<RelationForm relation={stored} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('name'), 'Mijn wijziging');
        if (!mounted.container.querySelector('#relation-defaults')) await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="relation-defaults"]')!);
        await click(mounted.button('+ relations_add_role_vendor')!);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="relation-stale"]'), 'stale warning shown');
        assert.equal(input('name').value, 'Mijn wijziging', 'draft kept');
        assert.equal(input('tradeName').value, '', 'colleague change not silently mixed into the draft');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'save attempted');
        assert.equal(calls.find((call) => call.method === 'PUT')!.body!.expectedVersion, 3, 'expected version is the one the draft is based on, not the newest card');
        await mounted.waitFor(() => text().includes('supply_error_http_409'), 'conflict reported');
        assert.equal(input('name').value, 'Mijn wijziging', 'draft still kept after the conflict');
        // Bewust herladen: nieuwste kaart, verder op versie 4.
        respond = (call) => (call.method === 'PUT' ? json(200, saved({ version: 5, tradeName: 'Door collega', vendor: newest.vendor })) : json(404, {}));
        await click(mounted.button('relations_stale_reload')!);
        assert.deepEqual([input('name').value, input('tradeName').value], ['Fruitimport Zuid', 'Door collega']);
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.filter((call) => call.method === 'PUT').length === 2, 'saved again');
        assert.equal(calls.filter((call) => call.method === 'PUT')[1].body!.expectedVersion, 4);
    });

    test('without a colleague change the role addition keeps the base version and Save succeeds without a warning', async () => {
        const stored = saved({ version: 3 });
        respond = (call) => (call.url.endsWith('/roles/vendor') ? json(201, saved({ version: 3, vendor: vendorRole() }))
            : call.method === 'PUT' ? json(200, saved({ version: 4 })) : json(404, {}));
        await mounted.render(<RelationForm relation={stored} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('name'), 'Mijn wijziging');
        if (!mounted.container.querySelector('#relation-defaults')) await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="relation-defaults"]')!);
        await click(mounted.button('+ relations_add_role_vendor')!);
        await mounted.waitFor(() => input('vendorRole').checked, 'role shown');
        assert.equal(mounted.container.querySelector('[data-testid="relation-stale"]'), null);
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'saved');
        assert.equal(calls.find((call) => call.method === 'PUT')!.body!.expectedVersion, 3);
    });

    test('payment conditions: only active ones are offered plus the inactive current one; direct debit needs a mandate before anything is sent', async () => {
        const inactive = { ...condition({ id: 7, code: '60D', descriptionNl: '60 dagen', descriptionEn: '60 days', days: 60, isActive: false }) };
        respond = (call) => (call.url.includes('/payment-conditions') ? json(200, activeConditions) : call.url.includes('/credit') ? json(200, credit)
            : call.method === 'PUT' ? json(200, saved({ version: 1 })) : json(404, {}));
        await mounted.render(<RelationForm relation={saved({ customer: customerRole({ paymentConditionId: 7, paymentCondition: inactive, paymentTermsDays: 60 }) })} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await mounted.waitFor(() => select('customer.paymentConditionId').options.length === 3, 'conditions loaded');
        const options = Array.from(select('customer.paymentConditionId').options).map((o) => o.value);
        assert.deepEqual(options, ['1', '2', '7'], 'active conditions plus the current inactive one, no default option');
        assert.equal(select('customer.paymentConditionId').value, '7');
        assert.ok(text().includes('relations_terms_condition_inactive'));
        await choose(select('customer.paymentConditionId'), '2');
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="customer-terms-example"]'), 'due date example');
        await choose(select('customer.paymentMethod'), 'DirectDebit');
        await press('s', { ctrlKey: true });
        assert.equal(calls.filter((call) => call.method === 'PUT').length, 0, 'nothing sent');
        assert.ok(text().includes('relations_error_mandate_required') && text().includes('relations_error_mandate_date_required'));
        await type(input('customer.mandateReference'), 'SEPA-77');
        await type(input('customer.mandateSignedOn'), '2026-01-15');
        await choose(select('customer.deliveryTermKind'), 'Incoterm');
        await choose(select('customer.incoterm'), 'FCA');
        await type(input('customer.incotermPlace'), 'Venlo');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'saved');
        const customer = calls.find((call) => call.method === 'PUT')!.body!.customer as Record<string, unknown>;
        assert.deepEqual([customer.paymentConditionId, customer.paymentMethod, customer.mandateReference, customer.mandateSignedOn, customer.deliveryTermKind, customer.incoterm, customer.incotermPlace, customer.deliveryTermText],
            [2, 'DirectDebit', 'SEPA-77', '2026-01-15', 'Incoterm', 'FCA', 'Venlo', null]);
    });

    test('credit: limit and insurance are edited inline; the exposure from the server shows outstanding, gap, expiry and the unpriced orders', async () => {
        respond = (call) => (call.url.includes('/customers/3/credit') ? json(200, credit) : call.url.includes('/payment-conditions') ? json(200, activeConditions)
            : call.method === 'PUT' ? json(200, saved({ version: 1 })) : json(404, {}));
        await mounted.render(<RelationForm relation={saved({ customer: customerRole({ creditLimit: 1000, isInsured: true, insurer: 'Atradius', insuredLimit: 750, insuredUntil: '2026-11-10' }) })} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="credit-outstanding"]'), 'exposure loaded');
        assert.ok(mounted.container.querySelector('[data-testid="credit-over-limit"]'));
        assert.ok(text().includes('relations_credit_unpriced'), 'unpriced orders counted, not estimated');
        assert.ok(mounted.container.querySelector('[data-testid="credit-insurance-gap"]'), 'live gap from the input');
        assert.equal(mounted.container.querySelector('label[for="customer.currency-input"]')?.textContent, 'relations_field_currency_sales', 'customer currency is not labelled as purchasing');
        await choose(select('customer.insurer'), 'Other');
        await type(input('customer.creditLimit'), '1.234,5');
        await press('s', { ctrlKey: true });
        assert.ok(text().includes('relations_error_amount') && text().includes('relations_error_insurer_text'));
        await type(input('customer.creditLimit'), '1500,50');
        await type(input('customer.insurerText'), 'Kredietpartner');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'saved');
        const customer = calls.find((call) => call.method === 'PUT')!.body!.customer as Record<string, unknown>;
        assert.deepEqual([customer.creditLimit, customer.isInsured, customer.insurer, customer.insurerText, customer.insuredLimit, customer.insuredUntil], [1500.5, true, 'Other', 'Kredietpartner', 750, '2026-11-10']);
    });

    test('bank accounts: IBAN checked inline, "Who" required for a new account, the full list is sent and saved accounts cannot be deleted', async () => {
        const stored = saved({ version: 2, bankAccounts: [bankAccount({ confirmationStatus: 'Confirmed', confirmedAt: '2026-10-20T08:00:00Z', confirmedBy: 'Anja' })] });
        respond = (call) => (call.method === 'PUT' ? json(200, saved({ version: 3, bankAccounts: [bankAccount({ confirmationStatus: 'Confirmed' }), bankAccount({ id: 42, iban: 'DE89370400440532013000', isDefault: false })] })) : json(404, {}));
        await mounted.render(<RelationForm relation={stored} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await openSection('bank');
        assert.ok(text().includes('relations_bank_status_Confirmed'));
        const savedRow = mounted.container.querySelector('[data-testid="bank-account-41"]')!;
        assert.equal(savedRow.querySelector('label[for$=".currency-input"]')?.textContent, 'relations_bank_currency', 'account currency is not labelled as purchasing');
        assert.equal(Array.from(savedRow.querySelectorAll('button')).some((b) => b.textContent === 'relations_remove'), false, 'no delete for a saved account');
        await click(mounted.button('+ relations_bank_add')!);
        const key = Array.from(mounted.container.querySelectorAll('[data-testid^="bank-account-"]')).map((el) => el.getAttribute('data-testid')!.replace('bank-account-', '')).find((k) => k !== '41')!;
        await type(input(`bankAccounts.${key}.iban`), 'DE89 3704 0044 0532 0130 01');
        await type(input(`bankAccounts.${key}.accountHolder`), 'Fruitimport Zuid');
        await press('s', { ctrlKey: true });
        assert.equal(calls.filter((call) => call.method === 'PUT').length, 0);
        assert.ok(text().includes('relations_error_iban_checksum') && text().includes('relations_error_actor_required'));
        await type(input(`bankAccounts.${key}.iban`), 'DE89 3704 0044 0532 0130 00');
        assert.ok(text().includes('relations_bank_iban_ok'));
        await type(input('actorReference'), 'Pieter');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'saved');
        const body = calls.find((call) => call.method === 'PUT')!.body!;
        assert.equal(body.actorReference, 'Pieter');
        assert.deepEqual((body.bankAccounts as { id: number | null; iban: string; isDefault: boolean }[]).map((a) => [a.id, a.iban, a.isDefault]), [[41, 'NL91ABNA0417164300', true], [null, 'DE89370400440532013000', false]]);
        await mounted.waitFor(() => input('actorReference').value === '', '"Who" is cleared after saving');
    });

    test('an unconfirmed default account is a prominent warning; confirming needs "Who" and sends the expected version', async () => {
        const stored = saved({ version: 4, bankAccounts: [bankAccount()], warnings: [{ code: 'default-bank-account-unconfirmed', field: 'bankAccounts[0]', message: 'x', suggestion: null }] });
        respond = (call) => (call.url.endsWith('/bank-accounts/41/confirm')
            ? json(200, saved({ version: 5, bankAccounts: [bankAccount({ confirmationStatus: 'Confirmed', confirmedAt: '2026-10-26T09:00:00Z', confirmedBy: 'Anja' })], bankAccountEvents: [{ id: 1, bankAccountId: 41, kind: 'Confirmed', actorReference: 'Anja', occurredAt: '2026-10-26T09:00:00Z', oldIban: null, newIban: null, oldAccountHolder: null, newAccountHolder: null, oldOtherAccountNumber: null, newOtherAccountNumber: null }] }))
            : call.method === 'PUT' ? json(200, saved({ version: 6 })) : json(404, {}));
        await mounted.render(<RelationForm relation={stored} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        assert.ok(mounted.container.querySelector('[data-testid="warning-default-bank-account-unconfirmed"]'), 'prominent warning');
        await openSection('bank');
        const confirmButton = mounted.container.querySelector<HTMLButtonElement>('[data-testid="bank-confirm-41"]')!;
        assert.ok(confirmButton.disabled, 'needs "Who" first');
        await type(input('actorReference'), 'Anja');
        await click(mounted.container.querySelector<HTMLButtonElement>('[data-testid="bank-confirm-41"]')!);
        await mounted.waitFor(() => calls.some((call) => call.url.endsWith('/confirm')), 'confirm posted');
        assert.deepEqual(calls.find((call) => call.url.endsWith('/confirm'))!.body, { expectedVersion: 4, actorReference: 'Anja' });
        await mounted.waitFor(() => text().includes('relations_bank_confirmed'), 'notice');
        assert.equal(mounted.container.querySelector('[data-testid="warning-default-bank-account-unconfirmed"]'), null, 'warning gone');
        assert.ok(mounted.container.querySelector('[data-testid="bank-account-events"]'), 'audit shown');
        await type(input('tradeName'), 'FZ');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'saved');
        assert.equal(calls.find((call) => call.method === 'PUT')!.body!.expectedVersion, 5, 'draft continues on the confirmed version');
    });

    test('an e-mail typo is a clickable suggestion, never an error; save warnings about the domain are shown by code', async () => {
        respond = () => json(201, saved({ customer: null, email: 'inkoop@fz.example', warnings: [{ code: 'email-no-mail-server', field: 'email', message: 'Het domein heeft geen mailserver', suggestion: null }] }));
        await mounted.render(<RelationForm relation={null} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('name'), 'Fruitimport Zuid');
        await type(input('email'), 'inkoop@gmial.com');
        const hint = mounted.container.querySelector('[data-testid="email-typo-hint"]')!;
        assert.ok(hint.textContent!.includes('relations_email_typo_hint'));
        await click(hint.querySelector('button')!);
        assert.equal(input('email').value, 'inkoop@gmail.com');
        assert.equal(mounted.container.querySelector('[data-testid="email-typo-hint"]'), null);
        await type(input('email'), 'inkoop@fz');
        await press('s', { ctrlKey: true });
        assert.equal(posts().length, 0, 'strict shape for a new address');
        assert.ok(text().includes('relations_error_invalid_email'));
        await type(input('email'), 'inkoop@fz.example');
        await press('s', { ctrlKey: true });
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="warning-email-no-mail-server"]'), 'save warning shown');
        assert.ok(text().includes('relations_warning_email_no_mail_server'));
    });

    test('VAT: a Greek number with GR is refused with the EL suggestion; the hint says shape only, no VIES', async () => {
        await mounted.render(<RelationForm relation={null} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('name'), 'Hellas Fruit');
        await openSection('company');
        assert.ok(text().includes('relations_hint_vat'));
        await type(input('vatNumber'), 'GR 123 456 789');
        await press('s', { ctrlKey: true });
        assert.equal(posts().length, 0);
        assert.ok(text().includes('relations_error_vat_greece'));
        await type(input('vatNumber'), 'NL123456789');
        await press('s', { ctrlKey: true });
        assert.ok(text().includes('relations_error_vat_country'));
    });

    test('GGN check: stores who and when, opens the portal (noopener) and copies the GGN, with a fallback when copying fails', async () => {
        const opened: unknown[][] = [];
        const originalOpen = window.open;
        window.open = ((...args: unknown[]) => { opened.push(args); return null; }) as typeof window.open;
        let copied = '';
        Object.defineProperty(window.navigator, 'clipboard', { configurable: true, value: { writeText: async (value: string) => { copied = value; } } });
        try {
            const vendor = vendorRole({ ggns: [{ id: 5, ggn: '4049929000001', producerName: 'Finca Uno', isActive: true, checkedOn: null, checkedBy: null, certificateValidUntil: null, hasGgnLabel: false }] });
            respond = (call) => (call.method === 'PUT' ? json(200, saved({ version: 1, vendor })) : json(404, {}));
            await mounted.render(<RelationForm relation={saved({ customer: null, vendor })} reference={reference} onSaved={() => {}} onClose={() => {}} />);
            assert.ok(text().includes('relations_ggn_never_checked'));
            await click(mounted.container.querySelector<HTMLButtonElement>('[data-testid="ggn-check-4049929000001"]')!);
            await mounted.waitFor(() => text().includes('relations_ggn_copied'), 'copied');
            assert.deepEqual(opened, [['https://portal.example/certificates', '_blank', 'noopener,noreferrer']]);
            assert.equal(copied, '4049929000001');
            Object.defineProperty(window.navigator, 'clipboard', { configurable: true, value: { writeText: async () => { throw new Error('denied'); } } });
            await click(mounted.container.querySelector<HTMLButtonElement>('[data-testid="ggn-check-4049929000001"]')!);
            await mounted.waitFor(() => text().includes('relations_ggn_copy_failed'), 'fallback');
            const key = mounted.container.querySelector('[data-testid^="ggn-k"]')!.getAttribute('data-testid')!.replace('ggn-', '');
            await type(input(`ggns.${key}.checkedOn`), '2026-10-26');
            await press('s', { ctrlKey: true });
            assert.ok(text().includes('relations_error_ggn_check_pair'), 'date without name');
            await type(input(`ggns.${key}.checkedBy`), 'Anja');
            await type(input(`ggns.${key}.certificateValidUntil`), '2027-03-31');
            await press('s', { ctrlKey: true });
            await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'saved');
            const ggns = (calls.find((call) => call.method === 'PUT')!.body!.vendor as { ggns: Record<string, unknown>[] }).ggns;
            assert.deepEqual([ggns[0].checkedOn, ggns[0].checkedBy, ggns[0].certificateValidUntil], ['2026-10-26', 'Anja', '2027-03-31']);
        } finally {
            window.open = originalOpen;
            Object.defineProperty(window.navigator, 'clipboard', { configurable: true, value: undefined });
        }
    });
});
