import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, deferred, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import type { PaymentCondition } from '../../types/paymentCondition';
import PaymentConditionsAdmin from './PaymentConditionsAdmin';

// Beheer van betalingscondities door de echte componentlevenscyclus: lijst (inactieve op verzoek), toevoegen met inline validatie
// en requestId, bevroren opdracht bij een onbekende uitkomst, wijzigen op versie, inactief zetten, en verwijderen dat bij gebruik
// een 409 met aantallen geeft en dan inactief zetten voorstelt.

const condition = (over: Partial<PaymentCondition>): PaymentCondition => ({
    id: 1, code: '30D', descriptionNl: '30 dagen na factuurdatum', descriptionEn: '30 days after invoice date', days: 30, basis: 'InvoiceDate', discountPercent: null, discountDays: null,
    isActive: true, sortOrder: 30, version: 3, customerCount: 4, vendorCount: 2, ...over,
});

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let respond: (call: Call) => Response | Promise<Response>;

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="condition-${id}-input"]`)!;
const type = async (element: HTMLInputElement, value: string) => { await act(async () => { setValue(element, value); }); };
const click = async (element: HTMLElement) => { await act(async () => { element.click(); }); };
const text = () => mounted.container.textContent ?? '';
const writes = () => calls.filter((call) => call.method !== 'GET');
const row = (id: number) => mounted.container.querySelector(`[data-testid="payment-condition-row-${id}"]`)!;
const rowButton = (id: number, label: string) => Array.from(row(id).querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent === label)!;

describe('PaymentConditionsAdmin', () => {
    beforeEach(() => {
        calls = [];
        respond = (call) => (call.url.includes('includeInactive=true')
            ? json(200, [condition({}), condition({ id: 2, code: '60D', days: 60, isActive: false, customerCount: 0, vendorCount: 0 })])
            : json(200, [condition({}), condition({ id: 3, code: 'EOM30', basis: 'EndOfMonth', customerCount: 0, vendorCount: 0 })]));
        globalThis.fetch = (async (inputUrl: RequestInfo | URL, init?: RequestInit) => {
            const call: Call = { method: init?.method ?? 'GET', url: String(inputUrl), body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            return respond(call);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('lists active conditions with usage; inactive ones only on request', async () => {
        await mounted.render(<PaymentConditionsAdmin />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="payment-condition-row-3"]'), 'list');
        assert.ok(text().includes('30D') && text().includes('EOM30'));
        assert.ok(row(1).textContent!.includes('payment_conditions_usage'));
        // Op 390px moet de omschrijving een eigen regel krijgen en de knoppen eronder laten vallen, niet tot een smalle kolom krimpen.
        assert.ok(row(1).firstElementChild!.classList.contains('min-w-60'), 'description column keeps a minimum width so the buttons wrap on a phone');
        assert.equal(mounted.container.querySelector('[data-testid="payment-condition-row-2"]'), null);
        await click(mounted.container.querySelector<HTMLInputElement>('[data-testid="payment-conditions-include-inactive"]')!);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="payment-condition-row-2"]'), 'inactive shown');
        assert.ok(row(2).textContent!.includes('relations_inactive'));
        assert.ok(calls.some((call) => call.url.endsWith('/payment-conditions?includeInactive=true')));
    });

    test('adding validates inline, then posts once with a requestId; an unknown outcome freezes and retries the same command', async () => {
        await mounted.render(<PaymentConditionsAdmin />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="payment-condition-row-1"]'), 'list');
        await click(mounted.button('+ payment_conditions_new')!);
        await type(input('code'), 'EOM 60');
        await type(input('discountPercent'), '2');
        await click(mounted.button('payment_conditions_save')!);
        assert.equal(writes().length, 0);
        for (const key of ['payment_conditions_error_code', 'payment_conditions_error_required', 'payment_conditions_error_discount_pair']) assert.ok(text().includes(key), key);
        await type(input('code'), 'EOM60');
        await type(input('descriptionNl'), '60 dagen einde maand');
        await type(input('descriptionEn'), '60 days end of month');
        await type(input('days'), '60');
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('#condition-basis-input')!, 'EndOfMonth'); });
        await type(input('discountDays'), '10');
        assert.ok(mounted.container.querySelector('[data-testid="payment-condition-preview"]'), 'due date example');
        const first = deferred<Response>();
        respond = (call) => (call.method === 'POST' ? first.promise : json(200, [condition({})]));
        await click(mounted.button('payment_conditions_save')!);
        await mounted.waitFor(() => writes().length === 1, 'posted');
        await act(async () => { first.reject(new TypeError('Failed to fetch')); await new Promise((resolve) => setTimeout(resolve, 0)); });
        await mounted.waitFor(() => text().includes('payment_conditions_pending'), 'frozen');
        assert.ok(input('code').disabled, 'locked while frozen');
        respond = (call) => (call.method === 'POST' ? json(200, condition({ id: 9, code: 'EOM60' })) : json(200, [condition({})]));
        await click(mounted.button('supply_retry_same')!);
        await mounted.waitFor(() => writes().length === 2, 'retried');
        assert.deepEqual(writes()[1].body, writes()[0].body, 'same command');
        const body = writes()[0].body!;
        assert.equal(typeof body.requestId, 'string');
        assert.deepEqual([body.code, body.days, body.basis, body.discountPercent, body.discountDays, body.sortOrder, body.isActive], ['EOM60', 60, 'EndOfMonth', 2, 10, null, true]);
        await mounted.waitFor(() => text().includes('payment_conditions_saved_before'), 'replayed notice');
    });

    test('editing sends a PUT with the expected version; deactivating keeps the condition and sends isActive false', async () => {
        await mounted.render(<PaymentConditionsAdmin />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="payment-condition-row-1"]'), 'list');
        respond = (call) => (call.method === 'PUT' ? json(200, condition({ version: 4, days: 31 })) : json(200, [condition({ version: 4 })]));
        await click(rowButton(1, 'payment_conditions_edit_button'));
        assert.equal(input('code').value, '30D');
        await type(input('days'), '31');
        assert.ok(text().includes('payment_conditions_days_change_hint'));
        await click(mounted.button('payment_conditions_save')!);
        await mounted.waitFor(() => writes().length === 1, 'put');
        assert.ok(writes()[0].url.endsWith('/payment-conditions/1'));
        assert.deepEqual([writes()[0].method, writes()[0].body!.expectedVersion, writes()[0].body!.days], ['PUT', 3, 31]);
        await mounted.waitFor(() => text().includes('payment_conditions_saved'), 'saved');
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="payment-condition-row-1"]'), 'reloaded');
        respond = (call) => (call.method === 'PUT' ? json(200, condition({ version: 5, isActive: false })) : json(200, []));
        await click(rowButton(1, 'payment_conditions_deactivate'));
        await mounted.waitFor(() => writes().length === 2, 'deactivated');
        assert.deepEqual([writes()[1].body!.isActive, writes()[1].body!.expectedVersion, writes()[1].body!.code], [false, 4, '30D']);
    });

    test('deleting a used condition is refused with the counts and suggests deactivating; an unused one is deleted', async () => {
        await mounted.render(<PaymentConditionsAdmin />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="payment-condition-row-1"]'), 'list');
        respond = (call) => (call.method === 'DELETE'
            ? json(409, { message: 'Betalingsconditie 30D is in gebruik bij 4 klant(en) en 2 leverancier(s).', code: 'payment-condition-in-use', customerCount: 4, vendorCount: 2 })
            : call.method === 'PUT' ? json(200, condition({ isActive: false, version: 4 })) : json(200, [condition({}), condition({ id: 3, code: 'EOM30', customerCount: 0, vendorCount: 0 })]));
        await click(rowButton(1, 'payment_conditions_delete'));
        assert.equal(writes().length, 0, 'asks for confirmation first');
        await click(rowButton(1, 'payment_conditions_delete_confirm'));
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="payment-condition-in-use"]'), 'in use');
        assert.ok(text().includes('payment_conditions_in_use_counts'));
        await click(Array.from(mounted.container.querySelector('[data-testid="payment-condition-in-use"]')!.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent === 'payment_conditions_deactivate')!);
        await mounted.waitFor(() => writes().some((call) => call.method === 'PUT'), 'deactivate from the notice');
        assert.equal(writes().find((call) => call.method === 'PUT')!.body!.isActive, false);
        respond = (call) => (call.method === 'DELETE' ? new Response(null, { status: 204 }) : json(200, [condition({})]));
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="payment-condition-row-3"]'), 'list');
        await click(rowButton(3, 'payment_conditions_delete'));
        await click(rowButton(3, 'payment_conditions_delete_confirm'));
        await mounted.waitFor(() => text().includes('payment_conditions_deleted'), 'deleted');
        assert.ok(writes().some((call) => call.method === 'DELETE' && call.url.endsWith('/payment-conditions/3')));
    });
});
