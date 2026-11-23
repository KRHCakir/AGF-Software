import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { setInputLocale } from './decimal.ts';
import { buildServiceLines, emptyServiceLine, serviceAmounts, serviceLineRow, serviceVat } from './serviceLines.ts';

// Dienstregels op een verkooporder (feedback 22 november), onafhankelijk uitgeschreven naar SalesOrderService.Services.
setInputLocale('nl');
const line = (over: object) => ({ ...emptyServiceLine('k'), costArticleId: '4', ...over });

describe('service lines', () => {
    test('VAT: chosen, else the article, else 21%; the amount is quantity times price minus discount', () => {
        assert.equal(serviceVat({ vat: '' }, null), 21);
        assert.equal(serviceVat({ vat: '' }, 9), 9);
        assert.equal(serviceVat({ vat: '0' }, 21), 0);
        assert.deepEqual(serviceLineRow(line({ quantity: '2', unitPrice: '12,50', discount: '10' }), 21), { quantity: 2, price: 12.5, discount: 10, vat: 21, amount: 22.5 });
        assert.deepEqual(serviceAmounts([line({ quantity: '1', unitPrice: '80' })], () => 9), [{ amount: 80, vatPercentage: 9 }]);
    });

    test('the payload carries the VAT always and price and discount only when given; errors use the server keys', () => {
        assert.deepEqual(buildServiceLines([line({ quantity: '1', unitPrice: '95' }), line({ quantity: '2' })], () => null),
            { ok: true, value: [{ costArticleId: 4, quantity: 1, unitPrice: 95, vatPercentage: 21 }, { costArticleId: 4, quantity: 2, vatPercentage: 21 }] });
        const bad = buildServiceLines([line({ costArticleId: '', quantity: '0', discount: '120' })], () => null);
        assert.deepEqual(bad.ok ? [] : bad.errors.map((error) => error.field), ['serviceLines[0].costArticleId', 'serviceLines[0].quantity', 'serviceLines[0].discountPercentage']);
    });
});
