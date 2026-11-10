import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { CostArticle } from '../types/costArticle';
import { buildPackagingLines, effectivePackagesPerPallet,packagingLineTotal, palletArticle, palletProposal, parsePackagingQuantity, parseUnitPrice } from './packaging.ts';
import { buildOrderPayload, groupErrors, type OrderDraft } from './salesOrderRules.ts';
import { setInputLocale } from './decimal.ts';

// Emballage op een verkooporder, onafhankelijk uitgeschreven naar SalesOrderService.Packaging en CostArticleRules.

const article = (id: number, name: string, extra: Partial<CostArticle> = {}): CostArticle =>
    ({ id, name, kind: 'Packaging', isStandard: true, isActive: true, sortOrder: id * 10, lineCount: 0, defaultUnitPrice: null, vatPercentage: 9, ...extra });
const goods = (productVariantId: string, quantity: string) =>
    ({ key: `g-${productVariantId}-${quantity}`, productVariantId, quantity, unit: (productVariantId === '' ? '' : 'colli') as 'colli' | '', mode: 'packages' as const, palletCount: '', packagesPerPallet: '' });
const pack = (costArticleId: string, quantity: string, unitPrice = '') => ({ key: `p-${costArticleId}-${quantity}`, costArticleId, quantity, unitPrice });
const draft = (extra: Partial<OrderDraft>): OrderDraft => ({ customerId: '7', customerReference: '', internalNote: '', requestedDeliveryDate: '2026-11-12', lines: [goods('', '')], ...extra });

describe('packaging rules', () => {
    test('a price per piece is empty (no price yet) or 0 or more with at most 2 decimals, in the notation of the screen', () => {
        assert.deepEqual(parseUnitPrice('', 'nl'), { ok: true, value: null });
        assert.deepEqual(parseUnitPrice('7,50', 'nl'), { ok: true, value: 7.5 });
        assert.deepEqual(parseUnitPrice('0,00', 'nl'), { ok: true, value: 0 }, 'a free exchange of a Euro pallet');
        assert.deepEqual(parseUnitPrice('1.250,00', 'nl'), { ok: true, value: 1250 });
        assert.deepEqual(parseUnitPrice('7.50', 'en'), { ok: true, value: 7.5 });
        assert.equal(parseUnitPrice('7,505', 'nl').ok, false);
        assert.equal(parseUnitPrice('-1', 'nl').ok, false);
        assert.equal(parseUnitPrice('7.50', 'nl').ok, false, 'a point is never a decimal sign in Dutch');
    });

    test('a quantity is a whole number from 1 to 99.999', () => {
        assert.equal(parsePackagingQuantity('1', 'nl'), 1);
        assert.equal(parsePackagingQuantity('1.000', 'nl'), 1000);
        for (const text of ['', '0', '1,5', '-2', '100.000', 'twee']) assert.equal(parsePackagingQuantity(text, 'nl'), null, text);
    });

    test('lines are checked all at once with the field keys of the backend; a valid price stays a number', () => {
        const wrong = buildPackagingLines([pack('', '1,5', 'x'), pack('3', '2', '0,00')], 'nl');
        assert.equal(wrong.ok, false);
        if (wrong.ok) return;
        assert.deepEqual(groupErrors(wrong.errors), {
            'packagingLines[0].costArticleId': ['required'], 'packagingLines[0].quantity': ['not_whole'], 'packagingLines[0].unitPrice': ['invalid'],
        });
        const right = buildPackagingLines([pack('3', '2', '7,50'), pack('2', '10')], 'nl');
        assert.deepEqual(right, { ok: true, value: [{ costArticleId: 3, quantity: 2, unitPrice: 7.5 }, { costArticleId: 2, quantity: 10, unitPrice: null }] });
    });

    test('the amount is quantity × price and stays empty without a price', () => {
        assert.equal(packagingLineTotal(pack('1', '3', '1,10'), 'nl'), 3.3);
        assert.equal(packagingLineTotal(pack('1', '3'), 'nl'), null);
        assert.equal(packagingLineTotal(pack('1', '', '1,10'), 'nl'), null);
    });

    test('the pallet proposal adds up the pallets of the goods lines and rounds up; 1 without pallet information', () => {
        assert.equal(palletProposal([{ quantity: 110, packagesPerPallet: 120 }]), 1, '110 colli kiwi = 0,9 pallet');
        assert.equal(palletProposal([{ quantity: 121, packagesPerPallet: 120 }]), 2);
        assert.equal(palletProposal([{ quantity: 110, packagesPerPallet: 120 }, { quantity: 130, packagesPerPallet: 120 }]), 2);
        assert.equal(palletProposal([{ quantity: 40, packagesPerPallet: null }, { quantity: null, packagesPerPallet: 120 }]), 1);
    });

    test('the pallet proposal follows the packages per pallet typed on the goods line, else the article default', () => {
        assert.equal(effectivePackagesPerPallet({ mode: 'pallets', packagesPerPallet: '60' }, 120), 60);
        assert.equal(effectivePackagesPerPallet({ mode: 'pallets', packagesPerPallet: '' }, 120), 120);
        assert.equal(effectivePackagesPerPallet({ mode: 'packages', packagesPerPallet: '60' }, 120), 120);
    });

    test('Add pallet takes Pallet wegwerp, otherwise an active article with pallet in its name, never a cost kind', () => {
        assert.equal(palletArticle([article(2, 'Euro-pallet'), article(1, 'Pallet wegwerp')])?.id, 1);
        assert.equal(palletArticle([article(1, 'Pallet wegwerp', { isActive: false }), article(2, 'Euro-pallet'), article(3, 'Krat')])?.id, 2);
        assert.equal(palletArticle([article(3, 'Krat'), article(9, 'Pallet handling', { kind: 'Cost' })]), null);
    });
});

describe('order payload with packaging', () => {
    test('without a locale argument the screen language counts, as set by the LanguageProvider (English: 7.50, not 7,50)', () => {
        setInputLocale('en');
        try {
            assert.deepEqual(parseUnitPrice('7.50'), { ok: true, value: 7.5 });
            assert.equal(parseUnitPrice('7,50').ok, false);
            const english = buildOrderPayload(draft({ packagingLines: [pack('2', '1,000', '7.50')] }));
            assert.deepEqual(english.ok && english.value.packagingLines, [{ costArticleId: 2, quantity: 1000, unitPrice: 7.5 }]);
        } finally {
            setInputLocale('nl');
        }
    });

    test('an order with only packaging leaves the empty goods line out and sends the packaging', () => {
        const result = buildOrderPayload(draft({ packagingLines: [pack('2', '10', '0,00')] }));
        assert.equal(result.ok, true);
        if (!result.ok) return;
        assert.deepEqual(result.value.lines, []);
        assert.deepEqual(result.value.packagingLines, [{ costArticleId: 2, quantity: 10, unitPrice: 0 }]);
    });

    test('without goods and without packaging a line is still required; without packaging the payload has no packaging field', () => {
        const blank = buildOrderPayload(draft({}));
        assert.ok(!blank.ok && 'lines[0].productVariantId' in groupErrors(blank.errors), 'without packaging an empty goods line is checked as before');
        const empty = buildOrderPayload(draft({ lines: [] }));
        assert.deepEqual(empty.ok ? {} : groupErrors(empty.errors), { lines: ['required'] });
        const plain = buildOrderPayload(draft({ lines: [goods('5', '110')] }));
        assert.equal(plain.ok && 'packagingLines' in plain.value, false, 'the same command as before this field');
    });

    test('goods and packaging together keep their own field keys, also after a skipped empty goods line', () => {
        const result = buildOrderPayload(draft({ lines: [goods('', ''), goods('5', 'abc')], packagingLines: [pack('1', '0')] }));
        assert.equal(result.ok, false);
        if (result.ok) return;
        assert.deepEqual(Object.keys(groupErrors(result.errors)).sort(), ['lines[1].quantity', 'packagingLines[0].quantity']);
    });
});
