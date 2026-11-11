import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { setInputLocale } from './decimal.ts';
import { emptyPurchaseLine, isBlankPurchaseLine, purchaseLinePayloads, purchaseLineRow, purchaseLineTotals, type PurchaseArticle } from './purchaseOrderLines.ts';

// De regel van Inkooporder aanmaken: alleen product, aantal en prijs (en eventueel de bruto) zijn invoer, de rest komt uit het artikel.
setInputLocale('nl');
const avocado: PurchaseArticle = { packagingUnit: 'colli', defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: 252 };
const noUnit: PurchaseArticle = { packagingUnit: null, defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null, defaultPackagesPerPallet: null };
const line = (over: object) => ({ ...emptyPurchaseLine('k'), productVariantId: '1', ...over });

describe('purchase order line drafts', () => {
    test('unit, net and gross come from the article; Bruto aanpassen makes only this line Entered; price and pallets follow', () => {
        const row = purchaseLineRow(line({ quantity: '100', unitPrice: '12,50' }), avocado);
        assert.deepEqual([row.unit, row.unitFromArticle, row.weight, row.price, row.total, row.perPallet, row.errors], ['colli', true, { netWeightKg: 400, grossWeightKg: 440, weightSource: 'Calculated' }, 12.5, 1250, 252, []]);
        assert.equal(Math.round(row.pallets! * 100) / 100, 0.4);
        const adjusted = purchaseLineRow(line({ quantity: '50', grossKg: '230', grossKgEntered: true }), avocado);
        assert.deepEqual(adjusted.weight, { netWeightKg: 200, grossWeightKg: 230, weightSource: 'Entered' });
        assert.deepEqual(purchaseLineRow(line({ quantity: '50', grossKg: '150', grossKgEntered: true }), avocado).errors, [{ field: 'expectedGrossWeightKg', code: 'gross_below_net' }]);
    });

    test('without unit on the article the line asks for one; a bad price is an error; no packages per pallet = no pallets', () => {
        const row = purchaseLineRow(line({ quantity: '5', unitPrice: '1,005' }), noUnit);
        assert.deepEqual(row.errors.map((error) => error.field), ['expectedUnit', 'unitPrice']);
        assert.equal(row.pallets, null);
        assert.equal(row.weight?.weightSource ?? null, null);
        assert.deepEqual(purchaseLineRow(line({ quantity: '5', unit: 'box', unitPrice: '0' }), noUnit).errors, [], '0,00 is a price');
    });

    test('an empty line is skipped when there is a filled one, keeping the form index for the error keys; totals ignore it', () => {
        assert.ok(isBlankPurchaseLine(emptyPurchaseLine('x')));
        assert.equal(isBlankPurchaseLine({ ...emptyPurchaseLine('x'), unitPrice: '1' }), false);
        const lines = [emptyPurchaseLine('a'), line({ quantity: '', unitPrice: '2' }), emptyPurchaseLine('c')];
        const built = purchaseLinePayloads(lines, () => avocado);
        assert.deepEqual(built.lines.map((entry) => entry.index), [1]);
        assert.deepEqual(built.errors, [{ index: 1, error: { field: 'expectedQuantity', code: 'required' } }]);
        const ok = purchaseLinePayloads([line({ quantity: '7', unitPrice: '12' }), emptyPurchaseLine('b')], () => avocado);
        assert.deepEqual(ok.lines.map((entry) => entry.payload), [{ productVariantId: 1, expectedQuantity: 7, expectedUnit: 'colli', expectedNetWeightKg: 28, lotNumber: null, palletCount: null, packagesPerPallet: null, expectedGrossWeightKg: 30.8, weightSource: 'Calculated', unitPrice: 12 }]);
        assert.deepEqual(purchaseLinePayloads([emptyPurchaseLine('a')], () => avocado).errors.map((entry) => entry.error.field), ['productVariantId', 'expectedQuantity'], 'only empty lines: the first one is checked');
        const totals = purchaseLineTotals([purchaseLineRow(line({ quantity: '100', unitPrice: '12,50' }), avocado), purchaseLineRow(line({ quantity: '30' }), noUnit)]);
        assert.deepEqual([totals.quantity, totals.netKg, totals.amount, totals.linesWithoutPrice, totals.linesWithoutPallets], [130, 400, 1250, 1, 1]);
    });
});
