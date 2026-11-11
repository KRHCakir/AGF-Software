import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import PlanSupplyForm, { type ReferenceData } from '../supply/PlanSupplyForm';

// Inkooporder aanmaken: heeft deze leverancier het ordernummer al, dan een waarschuwing met een link naar die order (geen blokkade).

const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true }],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: 'colli', defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 }] }],
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
let gets: string[];
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const enter = (id: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 350)); });
const main = (scope: Element) => scope.querySelector('[data-testid="order-number-main"]');
const internal = (scope: Element) => scope.querySelector('[data-testid="order-number-internal"]');

describe('supplier order number: duplicate warning', () => {
    beforeEach(() => {
        gets = [];
        posted = stubFetch({
            onGet: (url) => {
                gets.push(url);
                return url.includes('/purchase-orders/by-supplier-reference')
                    ? json(200, [{ id: 12, orderNumber: 'IO-2026-0012', supplierReference: '45-21', orderedOn: '2026-11-01' }])
                    : json(404, { message: 'unexpected GET' });
            },
            nextPost: async () => json(500, { message: 'stop' }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('same supplier and number: a warning with a link to that order, not a blocking error; no lookup without a supplier', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await type('supplierReference-input', '4521');
        await settle();
        assert.equal(gets.some((url) => url.includes('by-supplier-reference')), false, 'no supplier chosen yet');
        await type('vendorId-input', 'supb');
        await enter('vendorId-input');
        await settle();
        const notice = await mounted.waitFor(() => mounted.container.querySelector('[data-testid="supplier-reference-duplicate"]'), 'duplicate warning');
        const lookup = gets.find((url) => url.includes('by-supplier-reference'))!;
        assert.ok(lookup.includes('supplierReference=4521') && lookup.includes('vendorId=3'), lookup);
        assert.equal(notice.querySelector('a')!.getAttribute('href'), '/purchase-orders?id=12');
        assert.equal(main(notice)!.textContent, '45-21');
        assert.equal(internal(notice)!.textContent, 'po_internal_number IO-2026-0012');
        assert.equal(field('supplierReference-input').getAttribute('aria-invalid'), 'false');

        // Leeg nummer: geen waarschuwing.
        await type('supplierReference-input', ' ');
        await settle();
        assert.equal(mounted.container.querySelector('[data-testid="supplier-reference-duplicate"]'), null);

        // Geen blokkade: met de waarschuwing in beeld stuurt opslaan de order gewoon (met het nummer zoals getypt).
        await type('supplierReference-input', '45 21');
        await settle();
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="supplier-reference-duplicate"]'), 'duplicate warning again');
        await type('expectedAt-input', '2026-10-30');
        await type('lines[0].productVariantId-input', 'hass');
        await enter('lines[0].productVariantId-input');
        await type('lines[0].expectedQuantity-input', '10');
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.equal(JSON.parse(posted[0].body).supplierReference, '45 21');
    });
});
