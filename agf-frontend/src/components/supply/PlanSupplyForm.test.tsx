import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { todayInAmsterdam } from '../../lib/calendarDate.ts';
import { Mounted, json, setSelectValue, setValue, stubFetch } from '../../test/reactHarness.ts';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// Aanvoerregel met netto en bruto kg: berekend uit het artikel, bruto onder netto geweigerd vóór versturen, en de payload
// met expectedGrossWeightKg en de bron van de kg.

const reference: ReferenceData = {
    vendors: [
        { id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true },
        { id: 4, name: 'Nature\'s Pride', searchCode: 'NP', vatNumber: 'NL001', isActive: true },
        { id: 5, name: 'Supplier Old', searchCode: null, vatNumber: null, isActive: false },
    ],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: 'colli', defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 }] }],
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
let answer: () => Promise<Response>;

const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const enter = (id: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
// Leverancier typen in plaats van uit een lijst kiezen: Enter neemt de bovenste suggestie over.
const chooseVendor = async (text: string) => { await type('vendorId-input', text); await enter('vendorId-input'); };
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

describe('PlanSupplyForm: net and gross kg on the supply line', () => {
    beforeEach(() => {
        answer = async () => json(500, { message: 'stop' });
        posted = stubFetch({ onGet: () => json(404, { message: 'unexpected GET' }), nextPost: () => answer() });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('calculated kg is sent with its source; gross below net is refused before sending', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await chooseVendor('supb');
        await type('origin-input', 'Peru');
        await type('expectedAt-input', '2026-10-30');
        await type('lines[0].productVariantId-input', 'hass');
        await act(async () => { field('lines[0].productVariantId-input').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="lines[0].expectedUnit-input"]')!, 'colli'); });
        await type('lines[0].expectedQuantity-input', '1.560');
        assert.equal(field('lines[0].expectedNetWeightKg-input').value, '15.600');
        assert.equal(field('lines[0].expectedGrossWeightKg-input').value, '16.068');

        await type('lines[0].expectedGrossWeightKg-input', '15.000');
        await submit();
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('line_kg_gross_below_net'));

        await type('lines[0].expectedGrossWeightKg-input', '16.100');
        await submit();
        await mounted.waitFor(() => posted.length > 0, 'POST');
        const line = JSON.parse(posted[0].body).lines[0];
        assert.deepEqual([line.expectedNetWeightKg, line.expectedGrossWeightKg, line.weightSource], [15600, 16100, 'Entered']);
    });

    test('supplier is typed with suggestions (name, search code, VAT number), not picked from a dropdown; inactive suppliers are not offered', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        assert.equal(mounted.container.querySelector('select[id="vendorId-input"]'), null);
        await type('vendorId-input', 'suppl');
        assert.ok(mounted.hasText('Supplier B'));
        assert.equal(mounted.hasText('Supplier Old'), false);
        await type('vendorId-input', 'nl001');
        await enter('vendorId-input');
        assert.ok(field('vendorId-input').value.includes("Nature's Pride"));
        await type('origin-input', 'Peru');
        await type('expectedAt-input', '2026-10-30');
        await type('lines[0].productVariantId-input', 'hass');
        await enter('lines[0].productVariantId-input');
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="lines[0].expectedUnit-input"]')!, 'colli'); });
        await type('lines[0].expectedQuantity-input', '10');
        await submit();
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.equal(JSON.parse(posted[0].body).vendorId, 4);
    });

    test('order: supplier reference and order date (today) are sent, no order channel; the own order number is shown large', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        assert.equal(field('orderedOn-input').value, todayInAmsterdam());
        await type('supplierReference-input', '  NP-88123 ');
        assert.equal(mounted.button('po_channel_phone'), null);
        await chooseVendor('supb');
        await type('origin-input', 'Peru');
        await type('expectedAt-input', '2026-10-30');
        await type('lines[0].productVariantId-input', 'hass');
        await act(async () => { field('lines[0].productVariantId-input').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="lines[0].expectedUnit-input"]')!, 'colli'); });
        await type('lines[0].expectedQuantity-input', '221');

        answer = async () => {
            const sent = JSON.parse(posted[0].body);
            return json(201, { id: 45, requestId: sent.requestId, vendorId: 3, vendorName: 'Supplier B', customerId: null, customerName: null, route: 'Warehouse', status: 'Expected',
                origin: 'Peru', destination: null, expectedAt: sent.expectedAt, orderNumber: 'IO-2026-0045', supplierReference: 'NP-88123', orderedOn: sent.orderedOn, orderChannel: null,
                vendorGgns: [], lines: [] });
        };
        await submit();
        await mounted.waitFor(() => posted.length > 0, 'POST');
        const body = JSON.parse(posted[0].body);
        assert.equal(body.supplierReference, 'NP-88123');
        assert.equal(body.orderedOn, todayInAmsterdam());
        assert.equal('orderChannel' in body, false);
        const number = await mounted.waitFor(() => mounted.container.querySelector('[data-testid="supply-order-number"]'), 'order number');
        assert.equal(number.textContent, 'IO-2026-0045');
        assert.ok(number.className.includes('text-3xl'));
        assert.equal(mounted.container.querySelector('a[href="/purchase-orders?id=45"]')?.textContent, 'po_to_order');
    });

    test('without supplier reference the payload stays as before (fields omitted)', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await chooseVendor('supb');
        await type('origin-input', 'Peru');
        await type('expectedAt-input', '2026-10-30');
        await type('lines[0].productVariantId-input', 'hass');
        await act(async () => { field('lines[0].productVariantId-input').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="lines[0].expectedUnit-input"]')!, 'colli'); });
        await type('lines[0].expectedQuantity-input', '10');
        await submit();
        await mounted.waitFor(() => posted.length > 0, 'POST');
        const body = JSON.parse(posted[0].body);
        assert.equal('supplierReference' in body, false);
        assert.equal('orderChannel' in body, false);
    });
});
