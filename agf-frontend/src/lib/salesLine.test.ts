import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { emptySalesLine, salesLinePayload } from './salesLine.ts';

// De verkooporderregel naar de payload (feedback 22 november), zoals de inkoopregel rekent maar met de veldnamen van de server.
const article = (extra: object = {}) => ({ packagingUnit: 'colli' as const, defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3, defaultPackagesPerPallet: 120, vatPercentage: 9, ...extra });
const line = (over: object) => ({ ...emptySalesLine('k'), productVariantId: '1', ...over });

describe('sales line payload', () => {
    test('unit, kg and VAT proposal from the article; price and discount as typed; the source only when chosen', () => {
        const built = salesLinePayload(line({ quantity: '30', unitPrice: '13,35', discount: '2,5', supplySource: 'Expected' }), 'lines[0]', article());
        assert.deepEqual(built.errors, []);
        assert.equal(built.unit, 'colli');
        assert.deepEqual(built.payload, { productVariantId: 1, quantity: 30, unit: 'colli', palletCount: null, packagesPerPallet: null, netWeightKg: 300, grossWeightKg: 309,
            weightSource: 'Calculated', supplySource: 'Expected', unitPrice: 13.35, discountPercentage: 2.5, vatPercentage: 9 });
    });

    test('an adjusted gross is Entered; a chosen VAT wins; without price, discount and kg the old payload shape stays', () => {
        assert.deepEqual(salesLinePayload(line({ quantity: '10', grossKg: '105', grossKgEntered: true, vat: '21', vatTouched: true }), 'lines[0]', article()).payload,
            { productVariantId: 1, quantity: 10, unit: 'colli', palletCount: null, packagesPerPallet: null, netWeightKg: 100, grossWeightKg: 105, weightSource: 'Entered', vatPercentage: 21 });
        assert.deepEqual(salesLinePayload(line({ quantity: '5', unit: 'box' }), 'lines[0]', undefined).payload,
            { productVariantId: 1, quantity: 5, unit: 'box', palletCount: null, packagesPerPallet: null });
    });

    test('errors carry the server keys (quantity, unit, grossWeightKg, discountPercentage)', () => {
        const fields = (over: object, extra?: object) => salesLinePayload(line(over), 'lines[2]', extra === undefined ? article() : article(extra)).errors;
        assert.deepEqual(fields({ quantity: '' }), [{ field: 'lines[2].quantity', code: 'required' }]);
        assert.deepEqual(fields({ quantity: '5' }, { packagingUnit: null }), [{ field: 'lines[2].unit', code: 'required' }]);
        assert.deepEqual(fields({ quantity: '10', grossKg: '99', grossKgEntered: true }), [{ field: 'lines[2].grossWeightKg', code: 'gross_below_net' }]);
        assert.deepEqual(fields({ quantity: '10', discount: '120' }), [{ field: 'lines[2].discountPercentage', code: 'out_of_range' }]);
    });
});
