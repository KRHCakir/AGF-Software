import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { ProductOption, VariantOption } from '../types/referenceData';
import { articleVariants, emptyEntered, matchVariant, optionsFor, requiredFields, shownAttributes } from './receiptBlindEntry.ts';

const variant = (id: number, origin: string, code: string | null, size: string, packaging = '4 kg doos', extra: Partial<VariantOption> = {}): VariantOption => ({
    id, name: 'Hass', origin, originCountryCode: code, packaging, sizeCategory: size, shortcutCode: null, defaultPackagesPerPallet: null,
    packagingUnit: 'colli', tradeClass: 'I', brand: null, ...extra,
});
const hass = [variant(1, 'Peru', 'PE', '18'), variant(2, 'Peru', 'PE', '20'), variant(3, 'Chili', 'CL', '18'), variant(4, 'Zuid-Afrika', 'ZA', '18')];
const products: ProductOption[] = [
    { id: 1, name: 'Avocado', variants: [...hass, { ...variant(5, 'Peru', 'PE', '18'), name: 'Fuerte' }] },
    { id: 2, name: 'Mango', variants: [{ ...variant(6, 'Brazilië', 'BR', '8'), name: 'Palmer' }] },
];
const names = new Map([['PE', 'Peru'], ['CL', 'Chili'], ['ZA', 'Zuid-Afrika']]);
const shown = shownAttributes(hass);
const entered = (origin: string | null, size: string, packaging = '4 kg doos') => ({ ...emptyEntered(), origin, size, packaging });

describe('receiptBlindEntry', () => {
    test('only variants of the same article (product and variety) are candidates', () => {
        assert.deepEqual(articleVariants(products, 1).map((v) => v.id), [1, 2, 3, 4]);
        assert.deepEqual(articleVariants(products, 99), []);
    });

    test('class and brand are only asked when the variants differ in them; empty fields count as not filled', () => {
        assert.deepEqual(shown, ['size', 'packaging']);
        assert.deepEqual(shownAttributes([...hass, variant(7, 'Peru', 'PE', '18', '4 kg doos', { brand: 'Verita' })]), ['size', 'packaging', 'brand']);
        assert.deepEqual(requiredFields(hass, shown), { origin: true, attributes: ['size', 'packaging'] });
    });

    test('suggestions follow what is already filled: Peru gives sizes 18 and 20, size 20 only fits Peru', () => {
        assert.deepEqual(optionsFor(hass, entered('PE', ''), shown, 'size', names), ['18', '20']);
        assert.deepEqual(optionsFor(hass, entered(null, ''), shown, 'size', names), ['18', '20']);
        assert.deepEqual(optionsFor(hass, entered('CL', ''), shown, 'size', names), ['18']);
        assert.deepEqual(optionsFor(hass, entered(null, '20', ''), shown, 'packaging', names), ['4 kg doos']);
    });

    test('exactly the ordered variant, another existing variant, or a combination that does not exist', () => {
        assert.deepEqual(matchVariant(hass, entered('PE', '18'), shown, 1, names), { kind: 'match', variantId: 1 });
        assert.deepEqual(matchVariant(hass, entered('ZA', '18'), shown, 1, names), { kind: 'match', variantId: 4 });
        assert.deepEqual(matchVariant(hass, entered('PE', ' 20 '), shown, 1, names), { kind: 'match', variantId: 2 });
        assert.deepEqual(matchVariant(hass, entered('ZA', '20'), shown, 1, names), { kind: 'none' });
        assert.deepEqual(matchVariant(hass, entered('PE', '18', '10 kg krat'), shown, 1, names), { kind: 'none' });
    });

    test('never a guess: two variants with the same attributes match only if one of them is the ordered one', () => {
        const twins = [...hass, variant(8, 'Zuid-Afrika', 'ZA', '18')];
        assert.deepEqual(matchVariant(twins, entered('ZA', '18'), shown, 1, names), { kind: 'ambiguous' });
        assert.deepEqual(matchVariant(twins, entered('ZA', '18'), shown, 4, names), { kind: 'match', variantId: 4 });
    });

    test('an old variant without a country matches on its origin text', () => {
        const old = [variant(9, 'peru', null, '18')];
        assert.deepEqual(matchVariant(old, entered('PE', '18'), shownAttributes(old), 9, names), { kind: 'match', variantId: 9 });
    });
});
