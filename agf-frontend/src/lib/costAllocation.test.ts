import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { type CostTarget, checkAllocation, distribute } from './costAllocation.ts';

// Kosten verdelen: gelijk, naar kg en naar colli, in centen, met het restcent op de grootste partij; een partij zonder kg of colli doet
// niet mee. Controle: nooit meer dan het regelbedrag, een rest is algemene kosten.
const target = (partijId: number, quantity: number, netKg: number): CostTarget => ({
    partijId, number: String(partijId), kind: 'Sales', party: null, products: null, quantity, unit: 'colli', netKg,
});

describe('cost allocation', () => {
    test('equally: 100 over three lots is 33.34 + 33.33 + 33.33, the rest cent on the first of the largest', () => {
        assert.deepEqual(distribute(100, [target(1, 1, 1), target(2, 1, 1), target(3, 1, 1)], 'equal').amounts, [33.34, 33.33, 33.33]);
        assert.deepEqual(distribute(5000, [target(1, 1, 1), target(2, 1, 1)], 'equal').amounts, [2500, 2500]);
    });

    test('by colli and by kg: 20 + 120 + 360 = 500 colli; the rest cent goes to the largest lot', () => {
        const lots = [target(1, 20, 80), target(2, 120, 480), target(3, 360, 1440)];
        assert.deepEqual(distribute(1000, lots, 'colli').amounts, [40, 240, 720]);
        assert.deepEqual(distribute(100, [target(1, 1, 1), target(2, 2, 1)], 'colli').amounts, [33.33, 66.67]);
        assert.deepEqual(distribute(10, [target(1, 1, 3), target(2, 1, 3), target(3, 1, 4)], 'kg').amounts, [3, 3, 4]);
        assert.deepEqual(distribute(0.1, [target(1, 1, 1), target(2, 1, 1), target(3, 1, 1)], 'kg').amounts, [0.04, 0.03, 0.03]);
        const sum = distribute(1234.57, lots, 'kg').amounts.reduce<number>((total, value) => total + (value ?? 0), 0);
        assert.equal(Math.round(sum * 100), 123457);
    });

    test('a lot without kg or colli does not take part and is named; nobody with a basis gets nothing', () => {
        const result = distribute(600, [target(1, 10, 0), target(2, 0, 50), target(3, 20, 100)], 'kg');
        assert.deepEqual(result.amounts, [null, 200, 400]);
        assert.deepEqual(result.skipped, [1]);
        assert.deepEqual(distribute(600, [target(1, 10, 0), target(2, 0, 50), target(3, 20, 100)], 'colli').skipped, [2]);
        assert.deepEqual(distribute(600, [target(1, 0, 0)], 'kg'), { amounts: [null], skipped: [1] });
    });

    test('the check: 500 + 1000 out of 5000 leaves 3500 general costs; more than the line is refused; amounts with at most two decimals', () => {
        const ok = checkAllocation(5000, ['500', '1.000'], 'nl');
        assert.deepEqual([ok.ok, ok.allocated, ok.general, ok.over], [true, 1500, 3500, false]);
        const over = checkAllocation(5000, ['4.000', '1.000,01'], 'nl');
        assert.deepEqual([over.ok, over.over, over.general], [false, true, -0.01]);
        const bad = checkAllocation(5000, ['1,005', '', '0'], 'nl');
        assert.deepEqual(bad.amountErrors, ['too_many_decimals', 'empty', 'not_positive']);
        assert.equal(checkAllocation(5000, [], 'nl').general, 5000);
        assert.equal(checkAllocation(0.3, ['0.1', '0.2'], 'en').general, 0);
    });
});
