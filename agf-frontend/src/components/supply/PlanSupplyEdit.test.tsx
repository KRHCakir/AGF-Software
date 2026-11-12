import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { setInputLocale } from '../../lib/decimal.ts';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { PurchaseOrderDetail, PurchaseOrderLine } from '../../types/purchaseOrder';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// Inkooporder aanpassen (stap 4a): hetzelfde formulier als aanmaken, gevuld met de opgeslagen order. Een niet aangeraakte regel gaat
// exact terug zoals ze was, Tab op de laatste prijs maakt een nieuwe regel, na ontvangst staat bij elke grens de uitleg, en een
// verouderde versie zegt dat in gewone taal.

setInputLocale('nl');
const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true }, { id: 4, name: 'Nature\'s Pride', searchCode: 'NP', vatNumber: 'NL001', isActive: true }],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: 'hass', defaultPackagesPerPallet: null, packagingUnit: 'colli', defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 }] }],
};

const storedLine = (over: Partial<PurchaseOrderLine> = {}): PurchaseOrderLine => ({
    id: 7, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, expectedQuantity: 221, unit: 'colli', expectedNetWeightKg: 884,
    expectedGrossWeightKg: 950, weightSource: 'Calculated', unitPrice: 12.5, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0,
    palletInputCount: 1, palletInputPackagesPerPallet: 221, palletInputLoose: null, inUse: false, ...over,
});

const order = (over: Partial<PurchaseOrderDetail> = {}): PurchaseOrderDetail => ({
    id: 5, orderNumber: 'IO-2026-0005', vendorId: 3, vendorName: 'Supplier B', supplierReference: '45-21', orderedOn: '2026-11-10', orderChannel: null, note: 'Bel bij aankomst',
    route: 'Warehouse', customerName: null, origin: '', destination: null, expectedAt: '2026-11-13T23:00:00Z', status: 'Ordered', storedStatus: 'Expected', invoiceIndicator: 'None',
    expectedQuantity: 221, expectedNetWeightKg: 884, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0, lines: [storedLine()], receipts: [],
    invoiceRefs: [], rejections: [], history: [], documentStatus: 'Open', editVersion: 2, editable: true, vendorFixed: false, partijFixed: false, partijId: null, ...over,
});

let mounted: Mounted;
let puts: { url: string; body: string }[];
let answer: () => Response;
let saved: PurchaseOrderDetail[];

const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const testId = (id: string) => mounted.container.querySelector<HTMLElement>(`[data-testid="${id}"]`);
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
const render = (o: PurchaseOrderDetail) => mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} editing={{ order: o, onSaved: (x) => saved.push(x) }} />);

describe('PlanSupplyForm: editing a purchase order', () => {
    beforeEach(() => {
        puts = [];
        saved = [];
        answer = () => json(200, order({ editVersion: 3 }));
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            if (init?.method === 'PUT') { puts.push({ url: String(input), body: String(init.body) }); return answer(); }
            // De dubbelcontrole van het ordernummer vindt de order zelf: bij aanpassen is dat geen dubbel.
            if (String(input).includes('/by-supplier-reference')) return json(200, [{ id: 5, orderNumber: 'IO-2026-0005', supplierReference: '45-21', orderedOn: '2026-11-10' }]);
            return json(200, { suggestion: null, format: 'Own' });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('opens with the stored values; an untouched line goes back as stored; Tab on the last price adds a line', async () => {
        await render(order());
        assert.equal(field('supplierReference-input').value, '45-21');
        assert.equal(field('note-input').value, 'Bel bij aankomst');
        assert.equal(field('lines[0].expectedQuantity-input').value, '221');
        assert.equal(field('lines[0].unitPrice-input').value, '12,50');
        assert.ok(mounted.hasText('po_edit_save'));
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 400)); });
        assert.equal(testId('supplier-reference-duplicate'), null, 'its own number is no duplicate');

        await type('lines[0].unitPrice-input', '13');
        await act(async () => { field('lines[0].unitPrice-input').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })); });
        assert.ok(field('lines[1].productVariantId-input'), 'Tab on the last price makes a new line');
        await submit();
        await mounted.waitFor(() => puts.length === 1, 'PUT');
        assert.equal(puts[0].url.endsWith('/purchase-orders/5'), true);
        const body = JSON.parse(puts[0].body);
        assert.equal(body.expectedVersion, 2);
        assert.equal(body.note, 'Bel bij aankomst');
        assert.equal(body.lines.length, 1, 'the empty new line does not count');
        // Netto uit het artikel zou nu 2210 zijn; de regel is niet aangeraakt, dus de opgeslagen kg en palletinvoer gaan terug.
        assert.deepEqual([body.lines[0].id, body.lines[0].expectedNetWeightKg, body.lines[0].palletCount, body.lines[0].unitPrice], [7, 884, 1, 13]);
        await mounted.waitFor(() => saved.length === 1, 'onSaved');
    });

    test('after a receipt every limit is explained: article fixed, never below received, the line stays, the supplier is fixed', async () => {
        await render(order({ vendorFixed: true, partijFixed: true, lines: [storedLine({ inUse: true, receivedQuantity: 40 })] }));
        assert.equal(field('lines[0].productVariantId-input').disabled, true);
        assert.ok(testId('lines[0].article-fixed')!.textContent!.includes('po_edit_article_fixed'));
        assert.ok(testId('lines[0].minimum')!.textContent!.includes('po_edit_min_quantity'));
        const remove = mounted.container.querySelector<HTMLButtonElement>('tr[data-tour="supply-line"] td:last-child button');
        assert.ok(remove, 'remove button present');
        assert.equal(remove.disabled, true);
        assert.equal(remove.title, 'po_edit_not_removable');
        assert.equal(field('vendorId-input').disabled, true);
        assert.ok(testId('po-edit-vendor-fixed'));
        assert.ok(testId('po-edit-partij-fixed'));

        // Alt+A (Nieuw artikel) opent bij een vast artikel niets: ook via de sneltoets wijzigt het artikel niet.
        await act(async () => { field('lines[0].expectedQuantity-input').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'a', code: 'KeyA', altKey: true, bubbles: true, cancelable: true })); });
        assert.equal(mounted.container.querySelector('[data-tour="supply-new-article"]'), null, 'no new-article panel on a fixed article');

        await type('lines[0].expectedQuantity-input', '39');
        await submit();
        assert.equal(puts.length, 0);
        assert.ok(mounted.hasText('po_edit_min_quantity'));
    });

    test('a stale version (409) says so in plain words with a reload', async () => {
        answer = () => json(409, { message: 'changed', code: 'stale' });
        await render(order());
        await type('lines[0].unitPrice-input', '14');
        await submit();
        await mounted.waitFor(() => testId('po-edit-conflict') !== null, 'conflict notice');
        assert.ok(testId('po-edit-conflict')!.textContent!.includes('po_edit_stale'));
        assert.equal(saved.length, 0);
    });
});
