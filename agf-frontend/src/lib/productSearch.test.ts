import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { describeVariant, findVariant, searchVariants, unambiguousMatch } from './productSearch.ts';
import type { SearchableProduct } from './productSearch.ts';

// Zoeken op product, variant, verpakking, herkomst, maatklasse en snelcode; een snelcode kiest alleen een variant.

const variant = (id: number, name: string, origin: string | null, packaging: string | null, sizeCategory: string | null, shortcutCode: string | null) =>
    ({ id, name, origin, packaging, sizeCategory, shortcutCode });

const products: SearchableProduct[] = [
    { id: 1, name: 'Avocado', variants: [
        variant(1, 'Hass', 'Peru', '4kg Box', '22', 'avohassper22'),
        variant(4, 'Hass', 'Peru', '4kg Box', '13', 'avohassper13'),
        variant(2, 'Green', 'Spain', '10kg Crate', null, null),
    ] },
    { id: 2, name: 'Mango', variants: [variant(3, 'Kent', 'Brazil', '4kg Box', null, null)] },
    { id: 3, name: 'Citroen', variants: [variant(5, 'Primofiori', 'Spanje', '10kg Doos', '4', null)] },
];

describe('searchVariants', () => {
    test('an archived article is never suggested, but an existing line still finds it', () => {
        const archived: SearchableProduct[] = [{ id: 1, name: 'Avocado', variants: [variant(1, 'Hass', 'Peru', '4kg Box', '22', 'avo22'), { ...variant(2, 'Hass', 'Peru', '4kg Box', '24', 'avo24'), isArchived: true }] }];
        assert.deepEqual(searchVariants(archived, 'hass').map((m) => m.variantId), [1]);
        assert.deepEqual(searchVariants(archived, 'avo24'), []);
        assert.equal(findVariant(archived, 2)?.variantId, 2);
    });

    test('the country code of the origin finds the variant too ("PE" -> Peru)', () => {
        const withCountry: SearchableProduct[] = [{ id: 1, name: 'Avocado', variants: [{ ...variant(1, 'Hass', 'Peru', '4 kg doos', '18', null), originCountryCode: 'PE' }, { ...variant(2, 'Hass', 'Chili', '4 kg doos', '18', null), originCountryCode: 'CL' }] }];
        assert.deepEqual(searchVariants(withCountry, 'hass pe').map((m) => m.variantId), [1]);
    });

    test('an empty query gives no suggestions', () => {
        assert.deepEqual(searchVariants(products, '   '), []);
    });

    test('matches on product, variant, packaging, origin and size, case and accent insensitive', () => {
        assert.deepEqual(searchVariants(products, 'avocado').map((m) => m.variantId), [2, 4, 1]);
        assert.deepEqual(searchVariants(products, 'CRATE').map((m) => m.variantId), [2]);
        assert.deepEqual(searchVariants(products, 'brazil').map((m) => m.variantId), [3]);
        assert.deepEqual(searchVariants(products, 'hass 13').map((m) => m.variantId), [4]);
        assert.deepEqual(searchVariants(products, 'citroén').map((m) => m.variantId), [5]);
    });

    test('an exact shortcut code ranks first and is flagged; a partial code only matches as text', () => {
        const exact = searchVariants(products, 'AvoHassPer13');
        assert.equal(exact[0].variantId, 4);
        assert.equal(exact[0].exactShortcut, true);
        const partial = searchVariants(products, 'avohassper');
        assert.deepEqual(partial.map((m) => m.variantId), [4, 1]);
        assert.ok(partial.every((m) => !m.exactShortcut));
    });

    test('describes a variant with its full description after selection', () => {
        assert.equal(describeVariant('Avocado', products[0].variants[1]), 'Avocado · Hass · 4kg Box · Peru · 13');
        assert.equal(describeVariant('Avocado', products[0].variants[2]), 'Avocado · Green · 10kg Crate · Spain');
    });
});

describe('unambiguousMatch', () => {
    test('a single hit or an exact shortcut is clear; several plain hits are ambiguous', () => {
        assert.equal(unambiguousMatch(searchVariants(products, 'kent'))?.variantId, 3);
        assert.equal(unambiguousMatch(searchVariants(products, 'avohassper13'))?.variantId, 4);
        assert.equal(unambiguousMatch(searchVariants(products, 'hass')), null);
        assert.equal(unambiguousMatch([]), null);
    });
});

describe('findVariant', () => {
    test('finds the description of a selected variant and null for unknown ids', () => {
        assert.equal(findVariant(products, 3)?.description, 'Mango · Kent · 4kg Box · Brazil');
        assert.equal(findVariant(products, 99), null);
    });
});
