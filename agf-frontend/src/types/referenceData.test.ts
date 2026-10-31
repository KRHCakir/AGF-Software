import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { toProductOption } from './referenceData.ts';

// Varianten uit api/products: het archiefkenmerk moet meekomen, anders blijft een gearchiveerd artikel in de keuzelijsten staan.
describe('toProductOption', () => {
    test('isArchived comes along; only true counts as archived (an older backend without the field means active)', () => {
        const product = toProductOption({ id: 1, name: 'Avocado', variants: [
            { id: 1, name: 'Hass', isArchived: true }, { id: 2, name: 'Hass', isArchived: false }, { id: 3, name: 'Hass' },
        ] });
        assert.deepEqual(product.variants.map((variant) => [variant.id, variant.isArchived]), [[1, true], [2, false], [3, false]]);
    });
});
