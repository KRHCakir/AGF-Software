import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { formatPercent, isVatRate, lineAmount, parseDiscount, summarize } from './orderLineMath.ts';
import { lineTotal } from './purchaseLine.ts';
import { type PurchaseArticle, emptyPurchaseLine, isBlankPurchaseLine, lineVatOf, purchaseLinePayloads, purchaseLineRow, purchaseLineTotals } from './purchaseOrderLines.ts';

// Feedback 22 november: dezelfde rekenregels als de server (OrderLineMath.cs): bedrag = aantal × prijs min korting op centen, btw per regel op
// centen en per tarief opgeteld, regels zonder prijs of btw apart geteld.
const avocado: PurchaseArticle = { packagingUnit: 'colli', defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: 252, vatPercentage: 9 };
const ginger: PurchaseArticle = { ...avocado, vatPercentage: 21 };
const line = (over: Partial<ReturnType<typeof emptyPurchaseLine>>) => ({ ...emptyPurchaseLine('k'), productVariantId: '1', ...over });

describe('order line math', () => {
    test('amount with and without discount, rounded to cents like the server', () => {
        assert.equal(lineAmount(100, 12.5, null), 1250);
        assert.equal(lineAmount(100, 12.5, 10), 1125);
        assert.equal(lineAmount(3, 13.345, null), 40.04);
        assert.equal(lineAmount(3, 13.345, 2.5), 39.03);
        assert.equal(lineAmount(7, 4.99, 100), 0);
        assert.equal(lineAmount(7, null, 10), null);
        assert.equal(lineTotal(3, 13.345), 40.04, 'the purchase line without discount is unchanged');
    });

    test('discount in nl notation: empty is none, 0 to 100 with at most 2 decimals', () => {
        assert.deepEqual(parseDiscount('', 'nl'), { ok: true, value: null });
        assert.deepEqual(parseDiscount('2,5', 'nl'), { ok: true, value: 2.5 });
        assert.deepEqual(parseDiscount('100', 'nl'), { ok: true, value: 100 });
        assert.deepEqual(parseDiscount('100,01', 'nl'), { ok: false, error: 'out_of_range' });
        assert.equal(parseDiscount('2,555', 'nl').ok, false);
        assert.equal(parseDiscount('-1', 'nl').ok, false);
        assert.equal(formatPercent(2.5, 'nl'), '2,5');
        assert.deepEqual([9, 21, 0, 6].map(isVatRate), [true, true, true, false]);
    });

    test('VAT per rate per line on cents, totals excl. and incl. VAT, lines without price or VAT counted apart (same numbers as the server test)', () => {
        const summary = summarize([
            { amount: 10.05, vatPercentage: 9 }, { amount: 10.05, vatPercentage: 9 }, { amount: 33.33, vatPercentage: 21 }, { amount: 5, vatPercentage: 0 },
            { amount: null, vatPercentage: 9 }, { amount: 12, vatPercentage: null },
        ]);
        assert.deepEqual(summary.vatRates, [{ percentage: 0, net: 5, vat: 0 }, { percentage: 9, net: 20.1, vat: 1.8 }, { percentage: 21, net: 33.33, vat: 7 }]);
        assert.deepEqual([summary.net, summary.vat, summary.gross, summary.linesWithoutPrice, summary.linesWithoutVat], [70.43, 8.8, 79.23, 1, 1]);
        assert.deepEqual(summarize([{ amount: null, vatPercentage: 9 }]), { net: null, vatRates: [], vat: null, gross: null, linesWithoutPrice: 1, linesWithoutVat: 0 });
    });

    test('the line: VAT proposed from the article until chosen, discount in the amount, both in the payload; a stored line without VAT keeps none', () => {
        const row = purchaseLineRow(line({ quantity: '30', unitPrice: '13,35', discount: '2,5' }), avocado);
        assert.deepEqual([row.discount, row.vat, row.total], [2.5, 9, 390.49]);
        assert.equal(purchaseLineRow(line({ quantity: '1', unitPrice: '1' }), ginger).vat, 21, 'proposal of another article');
        assert.equal(lineVatOf({ vat: '21', vatTouched: true }, avocado), 21, 'chosen wins over the article');
        assert.equal(lineVatOf({ vat: '', vatTouched: true }, avocado), null, 'a stored line without rate keeps none');
        assert.deepEqual(purchaseLineRow(line({ quantity: '1', discount: '101' }), avocado).errors, [{ field: 'discountPercentage', code: 'out_of_range' }]);
        const payload = purchaseLinePayloads([line({ quantity: '30', unitPrice: '13,35', discount: '2,5' })], () => avocado).lines[0].payload;
        assert.deepEqual([payload.discountPercentage, payload.vatPercentage], [2.5, 9]);
        assert.equal(isBlankPurchaseLine({ ...emptyPurchaseLine('x'), discount: '5' }), false, 'a typed discount is not an empty line');
        assert.equal(isBlankPurchaseLine({ ...emptyPurchaseLine('x'), vat: '21', vatTouched: true }), false, 'a chosen VAT is not an empty line');
        const totals = purchaseLineTotals([row, purchaseLineRow(line({ quantity: '20', unitPrice: '9,99', vat: '21', vatTouched: true }), avocado)], [{ amount: 15, vatPercentage: 9 }]);
        assert.deepEqual(totals.amounts?.vatRates, [{ percentage: 9, net: 405.49, vat: 36.49 }, { percentage: 21, net: 199.8, vat: 41.96 }]);
        assert.deepEqual([totals.amount, totals.amounts?.net, totals.amounts?.vat, totals.amounts?.gross], [590.29, 605.29, 78.45, 683.74]);
    });
});
