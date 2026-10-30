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
        assert.deepEqual(shortNames({ expected: hass(), received: hass({ origin: 'Zuid-Afrika' }), kinds: ['Origin'] }), { expected: 'Peru', received: 'Zuid-Afrika' });
        assert.deepEqual(shortNames({ expected: hass(), received: hass({ size: '16' }), kinds: ['Size'] }), { expected: '18', received: '16' });
    });

    test('a 409 is only a deviation with a known code', () => {
        assert.deepEqual(deviationConflict({ code: 'DEVIATION_CONFIRMATION_REQUIRED', deviation: { a: 1 } }), { code: 'DEVIATION_CONFIRMATION_REQUIRED', deviation: { a: 1 } });
        assert.equal(deviationConflict({ message: 'Pallet changed' }), null);
        assert.equal(deviationConflict({ code: 'SOMETHING_ELSE' }), null);
        assert.equal(deviationConflict(null), null);
    });

});
