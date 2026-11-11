import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// Veld Partij op de inkooporder door de echte componentlevenscyclus: voorstel, eigen nummer met dubbelcontrole (beide knoppen), 409
// PARTIJ_EXISTS bij opslaan, een bestaande partij kiezen, de zachte waarschuwing zonder partij en de Tab-volgorde.

const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true }],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: 'colli', defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null }] }],
};
const existing = { id: 7, number: '2610251', status: 'Open', note: null, createdAt: '2026-10-27T09:00:00Z', createdBy: 'Admin', linked: true };
const listed = { ...existing, vendors: ['Nature\'s Pride'], products: ['Mango Kent'], supplierLotReferences: [], unit: 'colli', purchasedQuantity: 100, receivedQuantity: 0, onHandQuantity: 0, inInspectionQuantity: 0, openExpectedQuantity: 100, soldQuantity: 0 };

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let suggestion: string | null;
let saveAnswer: () => Response;

const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const blur = (id: string) => act(async () => { field(id).dispatchEvent(new window.FocusEvent('blur', { bubbles: false })); field(id).dispatchEvent(new window.FocusEvent('focusout', { bubbles: true })); });
const enter = (id: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
const click = (element: HTMLElement) => act(async () => { element.click(); });
const tour = (name: string) => mounted.container.querySelector<HTMLElement>(`[data-tour="${name}"]`);
const posts = () => calls.filter((call) => call.method === 'POST');

async function fillOrder() {
    await type('vendorId-input', 'supb');
    await enter('vendorId-input');
    await type('expectedAt-input', '2026-10-30');
    await type('lines[0].productVariantId-input', 'hass');
    await enter('lines[0].productVariantId-input');
    await type('lines[0].expectedQuantity-input', '10');
}
const save = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

describe('PlanSupplyForm: lot on the purchase order', () => {
    beforeEach(() => {
        calls = [];
        suggestion = '2611001';
        saveAnswer = () => json(500, { message: 'stop' });
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const call: Call = { method: init?.method ?? 'GET', url: String(input), body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.url.includes('/partijen/next-number')) return json(200, { mode: suggestion === null ? 'Own' : 'Auto', format: '{JJ}{MM}{volgnr}', sequenceReset: 'Yearly', suggestion });
            if (call.url.includes('/partijen/by-number')) return new URL(call.url).searchParams.get('number')!.replace(/[\s-]/g, '') === '2610251' ? json(200, existing) : new Response(null, { status: 204 });
            if (call.url.includes('/partijen?')) return json(200, { items: [listed], total: 1, page: 1, pageSize: 10 });
            return saveAnswer();
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the suggested number is filled in and sent as a new lot without a number (the server assigns it)', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await mounted.waitFor(() => field('partij-input').value === '2611001', 'suggestion');
        await type('supplierLotReference-input', 'NP-LOT-88');
        await fillOrder();
        await save();
        await mounted.waitFor(() => posts().length > 0, 'POST');
        assert.deepEqual(posts()[0].body!.newPartij, { number: null });
        assert.equal(posts()[0].body!.supplierLotReference, 'NP-LOT-88');
        assert.equal(posts()[0].body!.partijId, undefined);
    });

    test('an existing own number asks: use the existing lot sends its id; another number focuses the field', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await mounted.waitFor(() => field('partij-input').value === '2611001', 'suggestion');
        await type('partij-input', '2610 251');
        await blur('partij-input');
        await mounted.waitFor(() => tour('supply-partij-exists'), 'exists question');
        assert.ok(mounted.hasText('partij_exists_question'));
        await click(tour('supply-partij-other-number')!);
        assert.equal(tour('supply-partij-exists'), null);
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 5)); });
        assert.equal(window.document.activeElement, field('partij-input'));

        await blur('partij-input');
        await mounted.waitFor(() => tour('supply-partij-exists'), 'exists question again');
        await click(tour('supply-partij-use-existing')!);
        await fillOrder();
        await save();
        await mounted.waitFor(() => posts().length > 0, 'POST');
        assert.equal(posts()[0].body!.partijId, 7);
        assert.equal(posts()[0].body!.newPartij, undefined);
    });

    test('a 409 PARTIJ_EXISTS on save asks the same question and saves nothing twice', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await mounted.waitFor(() => field('partij-input').value === '2611001', 'suggestion');
        await type('partij-input', 'PA-9');
        saveAnswer = () => json(409, { message: 'Lot PA-9 already exists.', code: 'PARTIJ_EXISTS', partij: { ...existing, id: 9, number: 'PA-9' } });
        await fillOrder();
        await save();
        await mounted.waitFor(() => tour('supply-partij-exists'), 'exists question after 409');
        assert.equal(posts().length, 1);
        assert.deepEqual(posts()[0].body!.newPartij, { number: 'PA-9' });
    });

    test('choosing an existing lot from the list; without a lot a soft warning stays next to save and the order is still sent', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await mounted.waitFor(() => field('partij-input').value === '2611001', 'suggestion');
        await click(tour('supply-partij-choose')!);
        await mounted.waitFor(() => tour('supply-partij-option'), 'options');
        assert.ok(mounted.hasText('Mango Kent') && mounted.hasText('Nature\'s Pride'));
        await click(tour('supply-partij-option')!);
        assert.equal(field('partij-input').value, '2610251');
        await click(tour('supply-partij-clear')!);
        assert.equal(field('partij-input').value, '2611001');

        await type('partij-input', '');
        assert.ok(tour('supply-partij-none'), 'soft warning');
        await fillOrder();
        await save();
        await mounted.waitFor(() => posts().length > 0, 'POST without lot');
        assert.equal(posts()[0].body!.newPartij, undefined);
        assert.equal(posts()[0].body!.partijId, undefined);
    });

    test('own-number mode leaves the field empty; the order header tabs Leverancier, Ordernummer leverancier, Besteldatum, then the partij', async () => {
        suggestion = null;
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await mounted.waitFor(() => calls.some((call) => call.url.includes('next-number')), 'numbering');
        assert.equal(field('partij-input').value, '');
        const focusable = Array.from(mounted.container.querySelectorAll<HTMLElement>('input, button, select')).filter((element) => element.tabIndex >= 0).map((element) => element.id || element.dataset.tour);
        assert.deepEqual(focusable.slice(0, 3), ['vendorId-input', 'supplierReference-input', 'orderedOn-input']);
        const at = (id: string) => focusable.indexOf(id);
        assert.ok(at('expectedAt-input') < at('origin-input') && at('origin-input') < at('partij-input') && at('partij-input') < at('supplierLotReference-input'), focusable.join(' '));
    });
});
