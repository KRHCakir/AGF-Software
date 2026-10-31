import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { DeviationVariant } from '../types/receiptDeviation';
import { deviationConflict, deviationRows, shortNames, variantLabel } from './receiptDeviation.ts';

const hass = (overrides: Partial<DeviationVariant> = {}): DeviationVariant => ({
    productVariantId: 10, productId: 1, productName: 'Avocado', variantName: 'Hass', origin: 'Peru', size: '18', packaging: 'Doos',
    packagingUnit: 'colli', tradeClass: 'I', brand: 'Verita', ...overrides,
});

describe('receiptDeviation', () => {
    test('only the kinds the server names are red', () => {
        const rows = deviationRows(hass(), hass({ productVariantId: 12, origin: 'Zuid-Afrika', size: '8' }), ['Origin', 'Size']);
        assert.deepEqual(rows.filter((row) => row.differs).map((row) => [row.labelKey, row.expected, row.received]), [
            ['deviation_attr_origin', 'Peru', 'Zuid-Afrika'],
            ['deviation_attr_size', '18', '8'],
        ]);
        assert.equal(rows[0].expected, 'Avocado Hass');
    });

    test('identical attributes on another variant show nothing red (kind Other)', () => {
        assert.equal(deviationRows(hass(), hass({ productVariantId: 13 }), ['Other']).some((row) => row.differs), false);
    });

    test('labels and short names for the consequences', () => {
        assert.equal(variantLabel(hass({ brand: null, tradeClass: ' ' })), 'Avocado Hass · Peru · 18 · Doos (colli)');
        const without = (kind: string) => `zonder ${kind}`;
        assert.deepEqual(shortNames({ expected: hass(), received: hass({ origin: 'Zuid-Afrika' }), kinds: ['Origin'] }, without), { expected: 'Peru', received: 'Zuid-Afrika' });
        assert.deepEqual(shortNames({ expected: hass(), received: hass({ size: '16' }), kinds: ['Size'] }, without), { expected: '18', received: '16' });
        assert.deepEqual(shortNames({ expected: hass({ productVariantId: 13 }), received: hass(), kinds: ['Other'] }, without), { expected: 'Avocado Hass · Peru · 18 · Doos (colli) · I · Verita', received: 'Avocado Hass · Peru · 18 · Doos (colli) · I · Verita' });
    });

    test('an empty attribute is "none", never an empty text or the whole article again', () => {
        const without = (kind: string) => `zonder ${kind}`;
        // Besteld merk "test", ontvangen zonder merk, en andersom.
        assert.deepEqual(shortNames({ expected: hass({ brand: 'test' }), received: hass({ brand: null }), kinds: ['Brand'] }, without), { expected: 'test', received: 'zonder Brand' });
        assert.deepEqual(shortNames({ expected: hass({ brand: ' ' }), received: hass({ brand: 'test' }), kinds: ['Brand'] }, without), { expected: 'zonder Brand', received: 'test' });
        // In de tabel: leeg wordt een streepje, en rood volgt alleen de server (Brand).
        const rows = deviationRows(hass({ brand: 'test' }), hass({ brand: null }), ['Brand']);
        assert.deepEqual(rows.find((row) => row.labelKey === 'deviation_attr_brand'), { labelKey: 'deviation_attr_brand', expected: 'test', received: '–', differs: true });
        assert.equal(rows.filter((row) => row.differs).length, 1);
        const sizes = deviationRows(hass({ size: null }), hass({ size: '16' }), ['Size']);
        assert.deepEqual(sizes.find((row) => row.labelKey === 'deviation_attr_size'), { labelKey: 'deviation_attr_size', expected: '–', received: '16', differs: true });
    });

    test('a 409 is only a deviation with a known code', () => {
        assert.deepEqual(deviationConflict({ code: 'DEVIATION_CONFIRMATION_REQUIRED', deviation: { a: 1 } }), { code: 'DEVIATION_CONFIRMATION_REQUIRED', deviation: { a: 1 } });
        assert.equal(deviationConflict({ message: 'Pallet changed' }), null);
        assert.equal(deviationConflict({ code: 'SOMETHING_ELSE' }), null);
        assert.equal(deviationConflict(null), null);
    });

});
