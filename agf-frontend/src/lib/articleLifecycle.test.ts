import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { deleteBlockedReason, fullNameRows, usageList, visibleArticles } from './articleLifecycle.ts';

// Reden waarom een artikel niet verwijderd kan worden, de volledige naam in het bevestigingsvenster en de lijst met of zonder archief.
const t = (key: string, params?: Record<string, string>) => (params ? `${key}(${Object.values(params).join('|')})` : key);

describe('articleLifecycle', () => {
    test('the reason lists every kind of use with singular or plural and "and" before the last one', () => {
        assert.equal(usageList(t, [{ kind: 'receipts', count: 3 }]), '3 articles_usage_receipts_many');
        assert.equal(usageList(t, [{ kind: 'receipts', count: 1 }, { kind: 'purchaseOrderLines', count: 2 }, { kind: 'wasteLogs', count: 1 }]),
            '1 articles_usage_receipts_one, 2 articles_usage_purchaseOrderLines_many articles_usage_and 1 articles_usage_wasteLogs_one');
        assert.equal(deleteBlockedReason(t, { articleId: 1, canDelete: false, usages: [{ kind: 'receipts', count: 3 }], message: 'x' }),
            'articles_delete_blocked(3 articles_usage_receipts_many)');
    });

    test('nothing blocks deleting an unused article, and nothing is claimed while the usage is still loading', () => {
        assert.equal(deleteBlockedReason(t, { articleId: 1, canDelete: true, usages: [], message: null }), null);
        assert.equal(deleteBlockedReason(t, null), null);
    });

    test('the full name shows product, origin, size, packaging, class and brand; an empty attribute is null ("none"), never empty text', () => {
        const rows = fullNameRows({ productName: 'Avocado', variantName: 'Hass', origin: 'Zuid-Afrika', sizeCategory: '12', packaging: ' ', tradeClass: 'I', brand: null });
        assert.deepEqual(rows.map((row) => [row.labelKey, row.value]), [
            ['articles_name_product', 'Avocado Hass'], ['articles_name_origin', 'Zuid-Afrika'], ['articles_name_size', '12'],
            ['articles_name_packaging', null], ['articles_name_trade_class', 'I'], ['articles_name_brand', null],
        ]);
    });

    test('archived articles are only listed on request', () => {
        const articles = [{ id: 1, isArchived: false }, { id: 2, isArchived: true }, { id: 3 }];
        assert.deepEqual(visibleArticles(articles, false).map((a) => a.id), [1, 3]);
        assert.deepEqual(visibleArticles(articles, true).map((a) => a.id), [1, 2, 3]);
    });
});
