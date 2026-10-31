import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { Article } from '../types/article';
import { buildArticlePayload, draftFromArticle, emptyArticleDraft, formatGoodsCode, groupArticleErrors, normalizeGoodsCode, searchArticles } from './articleRules.ts';

// Artikelregels: alleen productsoort verplicht, leeg = niet vastgelegd, goederencode als tekst met stelsel en voorloopnullen,
// bruto nooit kleiner dan netto, lijstfilter met exacte zoekcode bovenaan.

const article = (over: Partial<Article> = {}): Article => ({
    id: 1, productId: 1, productName: 'Avocado', variantName: 'Hass', description: 'Avocado · Hass · 4kg Box · Peru', shortcutCode: 'avohassper22', origin: 'Peru', originCountryCode: 'PE',
    brand: null, sizeCategory: null, packaging: '4kg Box', packagingUnit: 'box', season: null, notes: null, weightMethod: 'Calculated',
    defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: 120, goodsCode: '08044000', goodsCodeSystem: 'CN', tradeClass: 'I', defaultPrice: 12.5, ...over,
});

describe('article payload', () => {
    test('only the product kind and the country are required; everything else stays empty (null) when not recorded', () => {
        const result = buildArticlePayload({ ...emptyArticleDraft(), originCountryCode: 'PE', productName: ' Tomaat ' });
        assert.equal(result.ok, true);
        if (!result.ok) return;
        assert.equal(result.value.productName, 'Tomaat');
        assert.equal(result.value.variantName, null);
        assert.equal(result.value.shortcutCode, null);
        assert.equal(result.value.goodsCode, null);
        assert.equal(result.value.goodsCodeSystem, null);
        assert.equal(result.value.defaultGrossKgPerPackage, null);
        assert.equal(result.value.weightMethod, 'Weighed');
        const missing = buildArticlePayload(emptyArticleDraft());
        assert.equal(missing.ok, false);
        if (!missing.ok) assert.deepEqual(groupArticleErrors(missing.errors), { productName: ['required'], originCountryCode: ['required'] });
    });

    test('goods codes are text: leading zeros survive, spaces are allowed, and the length follows the code system', () => {
        assert.equal(normalizeGoodsCode('0702 00 00'), '07020000');
        assert.equal(normalizeGoodsCode(' 0702.00.00 '), '07020000');
        assert.equal(normalizeGoodsCode('0804ABC4000'), '0804ABC4000', 'letters are kept so validation can refuse them');
        for (const bad of ['0804ABC4000', '0804 40 0O', '0804-40-00', '0804/4000', '0804\t40\t00']) {
            const result = buildArticlePayload({ ...emptyArticleDraft(), originCountryCode: 'PE', productName: 'Avocado', goodsCode: bad, goodsCodeSystem: 'CN' });
            assert.equal(result.ok, false, `${JSON.stringify(bad)} must be refused`);
            if (!result.ok) assert.deepEqual(groupArticleErrors(result.errors), { goodsCode: ['invalid'] }, `${JSON.stringify(bad)} gives an inline goods code error`);
        }
        assert.equal(formatGoodsCode('07020000'), '0702 00 00');
        assert.equal(formatGoodsCode('070200'), '0702 00');
        assert.equal(formatGoodsCode('0702000010'), '0702 00 00 10');
        const cn = buildArticlePayload({ ...emptyArticleDraft(), originCountryCode: 'PE', productName: 'Tomaat', goodsCode: '0702 00 00', goodsCodeSystem: 'CN' });
        assert.equal(cn.ok, true);
        if (cn.ok) assert.deepEqual([cn.value.goodsCode, cn.value.goodsCodeSystem], ['07020000', 'CN']);
        const hs = buildArticlePayload({ ...emptyArticleDraft(), originCountryCode: 'PE', productName: 'Tomaat', goodsCode: '0702 00 00', goodsCodeSystem: 'HS' });
        assert.equal(hs.ok, false);
        if (!hs.ok) assert.deepEqual(groupArticleErrors(hs.errors), { goodsCode: ['wrong_length'] });
        const noSystem = buildArticlePayload({ ...emptyArticleDraft(), originCountryCode: 'PE', productName: 'Tomaat', goodsCode: '07020000' });
        assert.equal(noSystem.ok, false);
        if (!noSystem.ok) assert.deepEqual(groupArticleErrors(noSystem.errors), { goodsCodeSystem: ['system_required'] });
        const letters = buildArticlePayload({ ...emptyArticleDraft(), originCountryCode: 'PE', productName: 'Tomaat', goodsCode: '0702AB', goodsCodeSystem: 'HS' });
        assert.equal(letters.ok, false);
        if (!letters.ok) assert.deepEqual(groupArticleErrors(letters.errors), { goodsCode: ['invalid'] });
        const systemOnly = buildArticlePayload({ ...emptyArticleDraft(), originCountryCode: 'PE', productName: 'Tomaat', goodsCodeSystem: 'TARIC' });
        assert.equal(systemOnly.ok, true);
        if (systemOnly.ok) assert.equal(systemOnly.value.goodsCodeSystem, null, 'a system without a code is not recorded');
    });

    test('weight and pallet defaults are positive decimals; gross kg may not be below net kg; search codes have no spaces', () => {
        const ok = buildArticlePayload({ ...emptyArticleDraft(), originCountryCode: 'PE', productName: 'Avocado', variantName: 'Hass', weightMethod: 'Calculated', defaultNetKgPerPackage: '4', defaultGrossKgPerPackage: '4,4', defaultPackagesPerPallet: '120', packagingUnit: 'box', shortcutCode: 'avo-hass.22' });
        assert.equal(ok.ok, true);
        if (ok.ok) assert.deepEqual([ok.value.defaultNetKgPerPackage, ok.value.defaultGrossKgPerPackage, ok.value.defaultPackagesPerPallet, ok.value.packagingUnit], [4, 4.4, 120, 'box']);
        const bad = buildArticlePayload({ ...emptyArticleDraft(), originCountryCode: 'PE', productName: 'Avocado', defaultNetKgPerPackage: '4', defaultGrossKgPerPackage: '3,9', defaultPackagesPerPallet: '0', shortcutCode: 'avo hass' });
        assert.equal(bad.ok, false);
        if (!bad.ok) assert.deepEqual(groupArticleErrors(bad.errors), { shortcutCode: ['invalid'], defaultGrossKgPerPackage: ['gross_below_net'], defaultPackagesPerPallet: ['not_positive'] });
    });

    test('an article round-trips into a draft and back without losing optional values', () => {
        const draft = draftFromArticle(article());
        assert.equal(draft.goodsCode, '08044000');
        assert.equal(draft.defaultGrossKgPerPackage, '4,4', 'prefilled in screen notation, never as 4.4 (= 44 in nl)');
        const rebuilt = buildArticlePayload(draft);
        assert.equal(rebuilt.ok, true);
        if (rebuilt.ok) assert.deepEqual(rebuilt.value, {
            productName: 'Avocado', variantName: 'Hass', shortcutCode: 'avohassper22', originCountryCode: 'PE', brand: null, sizeCategory: null, packaging: '4kg Box', packagingUnit: 'box',
            season: null, notes: null, weightMethod: 'Calculated', defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: 120, goodsCode: '08044000', goodsCodeSystem: 'CN', tradeClass: 'I',
        });
    });
});

describe('article list search', () => {
    const articles = [article(), article({ id: 2, variantName: 'Green', description: 'Avocado · Green · 10kg Crate · Spain', shortcutCode: null, origin: 'Spain', goodsCode: null }), article({ id: 3, productName: 'Mango', variantName: 'Kent', description: 'Mango · Kent · 4kg Box · Brazil', shortcutCode: 'avo', origin: 'Brazil', brand: 'Sol', goodsCode: '08045000' })];

    test('every term must match; an exact search code comes first; goods code, brand and #id are searchable', () => {
        assert.deepEqual(searchArticles(articles, 'avo').map((a) => a.id), [3, 2, 1], 'exact code "avo" first, then the avocados by description');
        assert.deepEqual(searchArticles(articles, 'avocado spain').map((a) => a.id), [2]);
        assert.deepEqual(searchArticles(articles, '08044').map((a) => a.id), [1]);
        assert.deepEqual(searchArticles(articles, '0804').map((a) => a.id), [1, 3]);
        assert.deepEqual(searchArticles(articles, 'sol').map((a) => a.id), [3]);
        assert.deepEqual(searchArticles(articles, '#2').map((a) => a.id), [2]);
        assert.equal(searchArticles(articles, '').length, 3);
    });
});
