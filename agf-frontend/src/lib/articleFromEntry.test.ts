import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { Article } from '../types/article';
import type { ProductOption, VariantOption } from '../types/referenceData';
import { enteredFrom, newArticleText, similarVariants, withArticle } from './articleFromEntry.ts';
import type { EnteredAttributes } from './receiptBlindEntry';

// "Bedoel je dit?": alleen een andere schrijfwijze of precies één ander kenmerk; leeg is "geen" en een echte waarde.
const variant = (id: number, extra: Partial<VariantOption> = {}): VariantOption => ({
    id, name: 'Hass', origin: 'Zuid-Afrika', originCountryCode: 'ZA', packaging: 'Test Doos', sizeCategory: '12', shortcutCode: null, defaultPackagesPerPallet: null,
    packagingUnit: 'colli', tradeClass: 'I', brand: 'Test', ...extra,
});
const names = new Map([['ZA', 'Zuid-Afrika'], ['PE', 'Peru']]);
const typed = (extra: Partial<EnteredAttributes> = {}): EnteredAttributes => ({ origin: 'ZA', size: '12', packaging: 'test  doos', tradeClass: 'I', brand: 'test', ...extra });
const t = (key: string) => ({ articles_value_none: 'geen', new_article_size: 'maat', new_article_packaging: 'verpakking', new_article_trade_class: 'klasse', new_article_brand: 'merk' }[key] ?? key);

describe('articleFromEntry', () => {
    test('only the spelling differs ("Test" and "test", double spaces): the same article, shown first', () => {
        assert.deepEqual(similarVariants([variant(2, { sizeCategory: '14' }), variant(1)], typed(), names).map((s) => [s.variant.id, s.differs]), [[1, null], [2, 'size']]);
    });

    test('exactly one other attribute is similar, two are not; archived variants never', () => {
        assert.deepEqual(similarVariants([variant(1, { originCountryCode: 'PE', origin: 'Peru' })], typed(), names).map((s) => s.differs), ['origin']);
        assert.deepEqual(similarVariants([variant(1, { brand: 'Other', tradeClass: 'II' })], typed(), names), []);
        assert.deepEqual(similarVariants([variant(1, { isArchived: true })], typed(), names), []);
    });

    test('empty against filled is one difference, never the same', () => {
        assert.deepEqual(similarVariants([variant(1)], typed({ brand: '' }), names).map((s) => s.differs), ['brand']);
        assert.deepEqual(similarVariants([variant(1, { brand: null })], typed(), names).map((s) => s.differs), ['brand']);
        assert.deepEqual(similarVariants([variant(1, { brand: null })], typed({ brand: ' ' }), names).map((s) => s.differs), [null]);
    });

    test('the text says exactly what is created, with "geen" for an empty attribute', () => {
        assert.equal(newArticleText(t, 'Avocado Hass', 'Zuid-Afrika', typed({ packaging: ' test  krat ', brand: '' })),
            'Avocado Hass · Zuid-Afrika · maat 12 · verpakking test krat · klasse I · merk geen');
        assert.deepEqual(enteredFrom(variant(1, { brand: null })), { origin: 'ZA', size: '12', packaging: 'Test Doos', tradeClass: 'I', brand: '' });
        assert.equal(enteredFrom(variant(1, { originCountryCode: null, origin: 'zuid-afrika' }), new Map([['ZA', 'Zuid-Afrika']])).origin, 'ZA');
    });

    test('the created article joins the lists of this screen (existing product kind, or a new one)', () => {
        const article = { id: 50, productId: 1, productName: 'Avocado', variantName: 'Hass', origin: 'Zuid-Afrika', originCountryCode: 'ZA', sizeCategory: '12', packaging: 'test',
            packagingUnit: 'colli', tradeClass: 'I', brand: 'test', weightMethod: 'Calculated', shortcutCode: null, defaultPackagesPerPallet: null, defaultNetKgPerPackage: 4,
            defaultGrossKgPerPackage: null } as unknown as Article;
        const products: ProductOption[] = [{ id: 1, name: 'Avocado', variants: [variant(1)] }];
        assert.deepEqual(withArticle(products, article)[0].variants.map((v) => v.id), [1, 50]);
        assert.deepEqual(withArticle(withArticle(products, article), article)[0].variants.map((v) => v.id), [1, 50]);
        assert.deepEqual(withArticle([], article).map((p) => [p.id, p.variants.length]), [[1, 1]]);
    });
});
