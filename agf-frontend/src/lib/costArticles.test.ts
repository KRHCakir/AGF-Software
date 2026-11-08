import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { costArticleGroup, costArticleNameKey, itemValue, pickedItem } from './costArticles.ts';

// Kostenartikelen in de artikelkeuze van een factuurregel: een eigen groep met negatieve sleutels (nooit te verwarren met een artikel),
// alleen actieve, en dezelfde naamsleutel als de server (CostArticleRules.NameKey).
describe('cost articles', () => {
    const article = (id: number, name: string, isActive = true) => ({ id, name, isStandard: true, isActive, sortOrder: id * 10, lineCount: 0 });

    test('one group with only the active cost articles, keyed by minus the id', () => {
        const group = costArticleGroup([article(1, 'Handling'), article(2, 'Afval', false), article(3, 'Zeevracht')], 'Kosten')!;
        assert.equal(group.name, 'Kosten');
        assert.deepEqual(group.variants.map((variant) => [variant.id, variant.name]), [[-1, 'Handling'], [-3, 'Zeevracht']]);
        assert.equal(costArticleGroup([article(2, 'Afval', false)], 'Kosten'), null);
    });

    test('a picked value is a cost article, an article or nothing; the line shows its cost article as the picked value', () => {
        assert.deepEqual(pickedItem('-3'), { kind: 'cost', costArticleId: '3' });
        assert.deepEqual(pickedItem('12'), { kind: 'article', productVariantId: '12' });
        assert.deepEqual(pickedItem(''), { kind: 'none' });
        assert.deepEqual(pickedItem('-0'), { kind: 'none' });
        assert.equal(itemValue({ productVariantId: '', costArticleId: '3' }), '-3');
        assert.equal(itemValue({ productVariantId: '12', costArticleId: '' }), '12');
        assert.equal(itemValue({ productVariantId: '12' }), '12');
    });

    test('the name key ignores case and extra spaces', () => {
        assert.equal(costArticleNameKey('  Auto   Huur '), 'auto huur');
        assert.equal(costArticleNameKey('   '), null);
    });
});
