import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { ProductOption, VariantOption } from '../types/referenceData';
import { RECEIPT_ATTRIBUTES, articleVariants, emptyEntered, matchVariant, optionsFor } from './receiptBlindEntry.ts';
import type { EnteredAttributes, ReceiptAttribute } from './receiptBlindEntry.ts';

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
const entered = (origin: string | null, size: string, packaging = '4 kg doos'): EnteredAttributes => ({ ...emptyEntered(), origin, size, packaging, tradeClass: 'I' });

describe('receiptBlindEntry', () => {
    test('only variants of the same article (product and variety) are candidates', () => {
        assert.deepEqual(articleVariants(products, 1).map((v) => v.id), [1, 2, 3, 4]);
        assert.deepEqual(articleVariants(products, 99), []);
    });

    test('suggestions follow what is already filled: Peru gives sizes 18 and 20, size 20 only fits Peru', () => {
        assert.deepEqual(optionsFor(hass, entered('PE', ''), 'size', names), ['18', '20']);
        assert.deepEqual(optionsFor(hass, entered(null, ''), 'size', names), ['18', '20']);
        assert.deepEqual(optionsFor(hass, entered('CL', ''), 'size', names), ['18']);
        assert.deepEqual(optionsFor(hass, entered(null, '20', ''), 'packaging', names), ['4 kg doos']);
    });

    test('exactly the ordered variant, another existing variant, or a combination that does not exist', () => {
        assert.deepEqual(matchVariant(hass, entered('PE', '18'), 1, names), { kind: 'match', variantId: 1 });
        assert.deepEqual(matchVariant(hass, entered('ZA', '18'), 1, names), { kind: 'match', variantId: 4 });
        assert.deepEqual(matchVariant(hass, entered('PE', ' 20 '), 1, names), { kind: 'match', variantId: 2 });
        assert.deepEqual(matchVariant(hass, entered('ZA', '20'), 1, names), { kind: 'none' });
        assert.deepEqual(matchVariant(hass, entered('PE', '18', '10 kg krat'), 1, names), { kind: 'none' });
    });

    test('never a guess: two variants with the same attributes match only if one of them is the ordered one', () => {
        const twins = [...hass, variant(8, 'Zuid-Afrika', 'ZA', '18')];
        assert.deepEqual(matchVariant(twins, entered('ZA', '18'), 1, names), { kind: 'ambiguous' });
        assert.deepEqual(matchVariant(twins, entered('ZA', '18'), 4, names), { kind: 'match', variantId: 4 });
    });

    test('empty means "none" and is a real value: every filled/empty combination of the four attributes only matches its own variant', () => {
        const value: Record<ReceiptAttribute, string> = { size: '18', packaging: '4 kg doos', tradeClass: 'I', brand: 'Verita' };
        // Alle 16 combinaties van gevulde en lege kenmerken als aparte varianten (zelfde artikel en land).
        const combos = Array.from({ length: 16 }, (_, mask) => Object.fromEntries(RECEIPT_ATTRIBUTES.map((attribute, bit) => [attribute, mask & (1 << bit) ? value[attribute] : null])) as Record<ReceiptAttribute, string | null>);
        const all = combos.map((combo, mask) => variant(100 + mask, 'Peru', 'PE', combo.size ?? '', combo.packaging ?? '', { tradeClass: combo.tradeClass, brand: combo.brand }));
        combos.forEach((combo, mask) => {
            const typed: EnteredAttributes = { origin: 'PE', size: combo.size ?? '', packaging: combo.packaging ?? '', tradeClass: combo.tradeClass ?? '', brand: combo.brand ?? '' };
            assert.deepEqual(matchVariant(all, typed, 999, names), { kind: 'match', variantId: 100 + mask }, `combination ${mask}`);
        });
        // Leeg past nooit op een gevulde waarde, en andersom; null en lege tekst zijn hetzelfde.
        const branded = [variant(20, 'Peru', 'PE', '18', '4 kg doos', { brand: 'test' })];
        assert.deepEqual(matchVariant(branded, entered('PE', '18'), 20, names), { kind: 'none' });
        const unbranded = [variant(21, 'Peru', 'PE', '18', '4 kg doos', { brand: '' })];
        assert.deepEqual(matchVariant(unbranded, { ...entered('PE', '18'), brand: 'test' }, 21, names), { kind: 'none' });
        assert.deepEqual(matchVariant(unbranded, entered('PE', '18'), 21, names), { kind: 'match', variantId: 21 });
    });

    test('spelling: case and double spaces do not matter ("Test" and "test" are the same)', () => {
        const branded = [variant(22, 'Peru', 'PE', '18', '4 kg  Doos', { brand: 'Test' })];
        assert.deepEqual(matchVariant(branded, { ...entered('PE', '18', '4 kg doos'), brand: ' test ' }, 22, names), { kind: 'match', variantId: 22 });
    });

    test('an old variant without a country matches on its origin text', () => {
        const old = [variant(9, 'peru', null, '18')];
        assert.deepEqual(matchVariant(old, entered('PE', '18'), 9, names), { kind: 'match', variantId: 9 });
    });
});
