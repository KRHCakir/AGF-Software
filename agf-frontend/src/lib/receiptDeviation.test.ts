import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { DeviationCandidate, DeviationVariant } from '../types/receiptDeviation';
import { deviationConflict, deviationRows, searchCandidates, shortNames, variantLabel } from './receiptDeviation.ts';

const hass = (overrides: Partial<DeviationVariant> = {}): DeviationVariant => ({
    productVariantId: 10, productId: 1, productName: 'Avocado', variantName: 'Hass', origin: 'Peru', size: '18', packaging: 'Doos',
    packagingUnit: 'colli', tradeClass: 'I', brand: 'Verita', ...overrides,
});
const candidate = (variant: DeviationVariant, shortcutCode: string | null = null, otherArticle = false): DeviationCandidate =>
    ({ variant, shortcutCode, otherArticle, kinds: [] });

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

    test('search: exact shortcut first, then contains; never a near match by itself', () => {
        const za = candidate(hass({ productVariantId: 12, origin: 'ZA' }), 'AvoHassZA12');
        const za16 = candidate(hass({ productVariantId: 13, origin: 'ZA', size: '16' }), 'AvoHassZA16');
        const fuerte = candidate(hass({ productVariantId: 14, variantName: 'Fuerte' }), null, true);
        assert.deepEqual(searchCandidates([za16, za, fuerte], 'avohassza12').map((c) => c.variant.productVariantId), [12]);
        assert.deepEqual(searchCandidates([za16, za, fuerte], 'za').map((c) => c.variant.productVariantId), [13, 12]);
        assert.deepEqual(searchCandidates([za16, za, fuerte], '').map((c) => c.variant.productVariantId), [13, 12, 14]);
        assert.deepEqual(searchCandidates([za16, za, fuerte], 'mango'), []);
    });
});
