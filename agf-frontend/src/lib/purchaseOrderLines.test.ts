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
        assert.deepEqual([row.unit, row.weight, row.price, row.total, row.perPallet, row.errors], ['colli', { netWeightKg: 400, grossWeightKg: 440, weightSource: 'Calculated' }, 12.5, 1250, 252, []]);
        assert.equal(Math.round(row.pallets! * 100) / 100, 0.4);
        const adjusted = purchaseLineRow(line({ quantity: '50', grossKg: '230', grossKgEntered: true }), avocado);
        assert.deepEqual(adjusted.weight, { netWeightKg: 200, grossWeightKg: 230, weightSource: 'Entered' });
        assert.deepEqual(purchaseLineRow(line({ quantity: '50', grossKg: '150', grossKgEntered: true }), avocado).errors, [{ field: 'expectedGrossWeightKg', code: 'gross_below_net' }]);
    });

    test('fixes 5b: no unit choice on the line; a new line takes the article unit, a stored line keeps its own unit', () => {
        assert.equal(purchaseLineRow(line({ quantity: '200' }), avocado).unit, 'colli');
        // Opgeslagen als doos terwijl het artikel nu colli zegt: de regel blijft zoals zij is opgeslagen.
        const stored = purchaseLineRow(line({ quantity: '200', unit: 'box' }), avocado);
        assert.deepEqual([stored.unit, stored.errors], ['box', []]);
        assert.equal(purchaseLinePayloads([line({ quantity: '200', unit: 'box' })], () => avocado).lines[0].payload.expectedUnit, 'box');
        assert.equal(purchaseLinePayloads([line({ quantity: '200' })], () => avocado).lines[0].payload.expectedUnit, 'colli');
    });

    test('without unit on the article the line has an error (set it on the article); a bad price is an error; no packages per pallet = no pallets', () => {
        const row = purchaseLineRow(line({ quantity: '5', unitPrice: '1,005' }), noUnit);
        assert.deepEqual(row.errors.map((error) => error.field), ['expectedUnit', 'unitPrice']);
        assert.equal(row.pallets, null);
        assert.equal(row.weight?.weightSource ?? null, null);
        assert.deepEqual(purchaseLineRow(line({ quantity: '5', unit: 'box', unitPrice: '0' }), noUnit).errors, [], '0,00 is a price');
    });

    test('an empty line is skipped when there is a filled one, keeping the form index for the error keys; totals ignore it', () => {
        assert.ok(isBlankPurchaseLine(emptyPurchaseLine('x')));
        assert.equal(isBlankPurchaseLine({ ...emptyPurchaseLine('x'), unitPrice: '1' }), false);
        assert.equal(isBlankPurchaseLine({ ...emptyPurchaseLine('x'), ggn: '4049928415684' }), false, 'a typed GGN is not an empty line');
        const lines = [emptyPurchaseLine('a'), line({ quantity: '', unitPrice: '2' }), emptyPurchaseLine('c')];
        const built = purchaseLinePayloads(lines, () => avocado);
        assert.deepEqual(built.lines.map((entry) => entry.index), [1]);
        assert.deepEqual(built.errors, [{ index: 1, error: { field: 'expectedQuantity', code: 'required' } }]);
        const ok = purchaseLinePayloads([line({ quantity: '7', unitPrice: '12' }), emptyPurchaseLine('b')], () => avocado);
        assert.deepEqual(ok.lines.map((entry) => entry.payload), [{ productVariantId: 1, expectedQuantity: 7, expectedUnit: 'colli', expectedNetWeightKg: 28, lotNumber: null, ggn: null, palletCount: null, packagesPerPallet: null, expectedGrossWeightKg: 30.8, weightSource: 'Calculated', unitPrice: 12, discountPercentage: null, vatPercentage: null }]);
        assert.deepEqual(purchaseLinePayloads([emptyPurchaseLine('a')], () => avocado).errors.map((entry) => entry.error.field), ['productVariantId', 'expectedQuantity'], 'only empty lines: the first one is checked');
        assert.deepEqual(purchaseLinePayloads([emptyPurchaseLine('a'), emptyPurchaseLine('b'), emptyPurchaseLine('c')], () => avocado).errors.map((entry) => entry.index), [0, 0], 'several empty lines: only the first gets the messages');
        const totals = purchaseLineTotals([purchaseLineRow(line({ quantity: '100', unitPrice: '12,50' }), avocado), purchaseLineRow(line({ quantity: '30' }), noUnit)]);
        assert.deepEqual([totals.quantity, totals.netKg, totals.amount, totals.linesWithoutPrice, totals.linesWithoutPallets], [130, 400, 1250, 1, 1]);
    });

    test('the GGN of the product goes along normalized, empty is none, and not 13 digits is an error on the line', () => {
        const built = purchaseLinePayloads([line({ quantity: '7', ggn: '404 9928 415684' }), line({ quantity: '3', ggn: '' })], () => avocado);
        assert.deepEqual(built.lines.map((entry) => entry.payload.ggn), ['4049928415684', null]);
        assert.deepEqual(purchaseLineRow(line({ quantity: '7', ggn: '1234' }), avocado).errors, [{ field: 'ggn', code: 'ggn_13_digits' }]);
        assert.deepEqual(purchaseLineRow(line({ quantity: '7', ggn: '4049928415685' }), avocado).errors, [], 'a wrong check digit is only a warning');
    });
});
