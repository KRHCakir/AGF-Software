import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { CustomerOption, ProductOption } from '../../types/referenceData';
import CreateSalesOrderForm from './CreateSalesOrderForm';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';

// Verkoop-partij in het orderformulier door de echte componentlevenscyclus: het voorstel van de server staat in het veld en gaat niet mee
// (de server kent het nummer toe), een zelf getypt nummer wel, een bestaand nummer wordt bij het verlaten van het veld en na 409 gemeld,
// en in modus Eigen is het veld verplicht.

const customers: CustomerOption[] = [{ id: 1, name: 'Local Market Stalls', searchCode: 'LMS', isActive: true }];
const products: ProductOption[] = [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null,
    defaultPackagesPerPallet: null, packagingUnit: 'colli', defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null }] }];

let mounted: Mounted;
let posts: Record<string, unknown>[];
let mode: 'Auto' | 'Own';
let existing: string[];
let postAnswer: () => Response;

const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const key = (id: string, name: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })); });
const fillOrder = async () => {
    await type('customerId-input', 'Lo');
    await key('customerId-input', 'Tab');
    await type('requestedDeliveryDate-input', '2026-11-07');
    await type('lines[0].productVariantId-input', 'hass');
    await key('lines[0].productVariantId-input', 'Enter');
    await type('lines[0].quantity-input', '500');
};
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
const partij = () => field('salesPartijNumber-input');

describe('CreateSalesOrderForm: sales lot', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        posts = [];
        mode = 'Auto';
        existing = [];
        postAnswer = () => json(500, { message: 'stop' });
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = String(input);
            if (init?.method === 'POST') { posts.push(JSON.parse(String(init.body)) as Record<string, unknown>); return postAnswer(); }
            if (url.includes('/partijen/next-number')) return json(200, { kind: 'Sales', mode, format: '{JJ}{MM}{volgnr}', sequenceReset: 'Yearly', sequenceDigits: 3, suggestion: mode === 'Auto' ? '2611004' : null });
            if (url.includes('/partijen/by-number')) {
                const number = new URL(url).searchParams.get('number')!.replace(/[\s-]/g, '');
                assert.equal(new URL(url).searchParams.get('kind'), 'Sales');
                return existing.includes(number) ? json(200, { id: 9, number, kind: 'Sales', salesOrderId: 3, status: 'Open', note: null, createdAt: '2026-11-05T09:00:00Z', createdBy: 'Admin', linked: true }) : new Response(null, { status: 204 });
            }
            return json(404, { message: 'unexpected GET' });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    const render = async () => {
        await mounted.render(<PendingCommandsProvider><HydratedGate>
            <CreateSalesOrderForm customers={customers} products={products} onSaved={() => {}} onOpen={() => {}} onCancel={() => {}} />
        </HydratedGate></PendingCommandsProvider>);
        await mounted.waitFor(() => mounted.form(), 'order form');
    };

    test('the suggestion stands in the field and is not sent; the server assigns the number', async () => {
        await render();
        await mounted.waitFor(() => partij().value === '2611004', 'suggestion');
        assert.ok(mounted.hasText('sales_partij_hint'));
        await fillOrder();
        await submit();
        await mounted.waitFor(() => posts.length === 1, 'POST');
        assert.equal('salesPartijNumber' in posts[0], false);
    });

    test('an own number is sent trimmed; a number that already exists is named after leaving the field and nothing is sent', async () => {
        existing = ['261140'];
        await render();
        await mounted.waitFor(() => partij().value === '2611004', 'suggestion');
        await fillOrder();
        await type('salesPartijNumber-input', '2611-40');
        await act(async () => { partij().dispatchEvent(new window.FocusEvent('blur', { bubbles: false })); partij().dispatchEvent(new window.FocusEvent('focusout', { bubbles: true })); });
        await mounted.waitFor(() => mounted.hasText('sales_partij_exists'), 'duplicate named');
        await submit();
        assert.equal(posts.length, 0);

        await type('salesPartijNumber-input', ' 261141 ');
        assert.equal(mounted.hasText('sales_partij_exists'), false);
        await submit();
        await mounted.waitFor(() => posts.length === 1, 'POST');
        assert.equal(posts[0].salesPartijNumber, '261141');
    });

    test('a 409 PARTIJ_EXISTS on save is shown at the field', async () => {
        postAnswer = () => json(409, { message: 'Lot 261141 already exists.', code: 'PARTIJ_EXISTS', partij: { id: 9, number: '261141', kind: 'Sales', salesOrderId: 3, status: 'Open', note: null, createdAt: '2026-11-05T09:00:00Z', createdBy: 'Admin', linked: true } });
        await render();
        await fillOrder();
        await type('salesPartijNumber-input', '261141');
        await submit();
        await mounted.waitFor(() => mounted.hasText('sales_partij_exists'), 'duplicate named after 409');
        assert.equal(mounted.hasText('Lot 261141 already exists.'), false);
    });

    test('own numbers only: the field is required', async () => {
        mode = 'Own';
        await render();
        await mounted.waitFor(() => mounted.hasText('sales_partij_hint_own'), 'own hint');
        assert.equal(partij().value, '');
        await fillOrder();
        await submit();
        assert.equal(posts.length, 0);
        assert.equal(partij().getAttribute('aria-invalid'), 'true');
        await type('salesPartijNumber-input', '261140');
        await submit();
        await mounted.waitFor(() => posts.length === 1, 'POST');
        assert.equal(posts[0].salesPartijNumber, '261140');
    });
});
