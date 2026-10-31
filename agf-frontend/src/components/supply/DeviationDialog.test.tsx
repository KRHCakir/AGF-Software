import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import type { DeviationPreview, DeviationVariant } from '../../types/receiptDeviation';
import DeviationDialog from './DeviationDialog';

// Venster "Afwijking van de bestelling" bij alleen een ander gewicht: de vraag gaat over kg per colli, alleen de gewichtsrij is rood,
// Nee heeft de focus en Escape is Nee.

const hass: DeviationVariant = {
    productVariantId: 10, productId: 1, productName: 'Avocado', variantName: 'Hass', origin: 'Peru', size: '18', packaging: '4 kg doos',
    packagingUnit: 'colli', tradeClass: 'I', brand: null,
};
const weightOnly: DeviationPreview = {
    status: 'Deviation', supplyCommitmentId: 1, orderNumber: 'IO-2026-0001', originalLineId: 1, expected: hass, received: hass, kinds: ['Weight'],
    quantity: 10, unit: 'colli', originalExpectedQuantity: 10, originalReceivedQuantity: 0, reducedQuantity: 0, originalExpectedQuantityAfter: 10,
    excessQuantity: 0, assignedQuantity: null, expectedKgPerPackage: 4, receivedKgPerPackage: 6, expectedNetKg: 40, receivedNetKg: 60,
    originalExpectedNetKg: 40, originalExpectedNetKgAfter: 60,
};

let mounted: Mounted;
const dialog = () => window.document.querySelector<HTMLElement>('[data-tour="deviation-dialog"]')!;

describe('DeviationDialog weight', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('a weight-only deviation asks about kg per package; only the weight row is red; Escape is No', async () => {
        let no = 0;
        let yes = 0;
        await mounted.render(<DeviationDialog preview={weightOnly} onNo={() => { no++; }} onYes={() => { yes++; }} />);
        const text = dialog().textContent ?? '';
        assert.ok(text.includes('deviation_question_weight'));
        assert.ok(text.includes('deviation_consequence_weight'));
        const red = Array.from(dialog().querySelectorAll('[data-differs="true"]')).map((row) => row.textContent ?? '');
        assert.equal(red.length, 1);
        assert.ok(red[0].includes('deviation_attr_weight'));
        assert.equal(window.document.activeElement?.textContent, 'deviation_no');
        await act(async () => { dialog().dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })); });
        assert.deepEqual([no, yes], [1, 0]);
    });

    test('an empty brand against an ordered brand is a red row with a dash, never an empty cell', async () => {
        const brandOnly: DeviationPreview = { ...weightOnly, expected: { ...hass, brand: 'test' }, received: { ...hass, productVariantId: 11 }, kinds: ['Brand'] };
        await mounted.render(<DeviationDialog preview={brandOnly} onNo={() => {}} onYes={() => {}} />);
        const red = Array.from(dialog().querySelectorAll('[data-differs="true"]'));
        assert.equal(red.length, 1);
        const cells = Array.from(red[0].querySelectorAll('dd')).map((cell) => cell.lastChild?.textContent ?? '');
        assert.deepEqual([red[0].querySelector('dt')?.textContent, ...cells], ['deviation_attr_brand', 'test', '–']);
        assert.ok(red[0].querySelectorAll('dd')[1].className.includes('text-red-800'));
    });
});
