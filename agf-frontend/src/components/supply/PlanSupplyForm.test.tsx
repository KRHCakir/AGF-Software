import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setSelectValue, setValue, stubFetch } from '../../test/reactHarness.ts';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// Aanvoerregel met netto en bruto kg: berekend uit het artikel, bruto onder netto geweigerd vóór versturen, en de payload
// met expectedGrossWeightKg en de bron van de kg.

const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B' }],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: 'colli', defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 }] }],
};

let mounted: Mounted;
let posted: { url: string; body: string }[];

const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

describe('PlanSupplyForm: net and gross kg on the supply line', () => {
    beforeEach(() => {
        posted = stubFetch({ onGet: () => json(404, { message: 'unexpected GET' }), nextPost: async () => json(500, { message: 'stop' }) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('calculated kg is sent with its source; gross below net is refused before sending', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="vendorId-input"]')!, '3'); });
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
});
