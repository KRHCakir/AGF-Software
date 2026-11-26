import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { buildDirectLines, emptyService, formKeyOf, isShownFormKey, serviceDraftAmounts } from './directInvoice.ts';
import { emptySalesLine, type SalesLineDraft } from './salesLine.ts';
import type { PurchaseArticle } from './purchaseOrderLines.ts';

const article: PurchaseArticle = { packagingUnit: 'box', vatPercentage: 9, defaultPackagesPerPallet: null };
const product = (over: Partial<SalesLineDraft> = {}): SalesLineDraft => ({ ...emptySalesLine('a'), productVariantId: '2', quantity: '10', unitPrice: '12,00', ...over });

describe('direct invoice lines', () => {
    test('a product line needs its lot; a free line needs a description; blank lines are left out', () => {
        const result = buildDirectLines([product(), emptySalesLine('b')], [{ ...emptyService('s'), unitPrice: '35' }], () => article, 'nl');
        assert.equal(result.ok, false);
        assert.deepEqual(!result.ok && result.errors, { 'lines[0].partijId': 'si_direct_lot_required', 'services[0].description': 'si_direct_description_required' });
    });

    test('product lines first, then free lines, with lot, revenue type and VAT', () => {
        const result = buildDirectLines([product({ partijId: 7, partijNumber: 'I-2611001' })],
            [{ ...emptyService('s', '1'), description: 'Stroom koelcel', unitPrice: '120,50' }], () => article, 'nl');
        assert.deepEqual(result, { ok: true, value: [
            { lineKind: 'Product', productVariantId: 2, partijId: 7, quantity: 10, unit: 'box', unitPrice: 12, discountPercentage: null, vatPercentage: 9 },
            { lineKind: 'Service', costArticleId: null, description: 'Stroom koelcel', quantity: 1, unitPrice: 120.5, discountPercentage: null, vatPercentage: 21, revenueTypeId: 1 },
        ] });
        assert.deepEqual(serviceDraftAmounts([{ ...emptyService('s'), description: 'x', unitPrice: '120,50' }], 'nl'), [{ amount: 120.5, vatPercentage: 21 }]);
    });

    test('the VAT on screen is what is sent: the article proposal or the chosen rate; without any rate the line is refused', () => {
        const lot = { partijId: 7, partijNumber: 'I-2611001' };
        const chosen = buildDirectLines([product({ ...lot, vat: '0', vatTouched: true })], [], () => article, 'nl');
        assert.equal(chosen.ok && chosen.value[0].vatPercentage, 0);
        const none = buildDirectLines([product(lot)], [], () => ({ ...article, vatPercentage: null }), 'nl');
        assert.deepEqual(!none.ok && none.errors, { 'lines[0].vatPercentage': 'si_direct_vat_required' });
        assert.equal(isShownFormKey('lines[0].vatPercentage'), true);
    });

    test('an empty invoice is refused and server keys find their field again', () => {
        assert.deepEqual(buildDirectLines([emptySalesLine('a')], [emptyService('s')], () => article, 'nl'), { ok: false, errors: { lines: 'si_needs_lines' } });
        assert.equal(formKeyOf('lines[1].quantity', [0, 2], [0]), 'lines[2].quantity');
        assert.equal(formKeyOf('lines[2].revenueTypeId', [0, 2], [3]), 'services[3].revenueTypeId');
        assert.equal(formKeyOf('customerId', [0], []), 'customerId');
        assert.equal(formKeyOf('lines[2].vatPercentage', [0, 2], [3]), 'services[3].vat');
        assert.equal(isShownFormKey('services[3].vat'), true);
        assert.equal(isShownFormKey('lines[0].discountPercentage'), false);
    });
});
