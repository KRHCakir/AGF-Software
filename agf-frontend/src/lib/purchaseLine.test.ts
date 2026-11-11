import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { lineTotal, packagesPerPallet, pallets, sumLines } from './purchaseLine.ts';

describe('purchase order line math (mirror of PurchaseLineMath)', () => {
    test('pallets per line are colli over colli per pallet of the article; the total is their sum on two decimals', () => {
        assert.equal(Math.round(pallets(100, 252)! * 100) / 100, 0.4);
        assert.equal(Math.round(pallets(50, 264)! * 100) / 100, 0.19);
        assert.equal(pallets(10, null), null);
        assert.equal(packagesPerPallet(120, 252), 120, 'the own pallet input of an older line wins');
        assert.equal(packagesPerPallet(null, 252), 252);
        assert.equal(packagesPerPallet(null, 0), null);
        const totals = sumLines([
            { quantity: 100, netKg: 400, grossKg: 440, total: 1250, pallets: pallets(100, 252) },
            { quantity: 50, netKg: 200, grossKg: null, total: null, pallets: pallets(50, 264) },
            { quantity: 250, netKg: null, grossKg: null, total: 10, pallets: pallets(250, 120) },
        ]);
        assert.equal(totals.pallets, 2.67, '0,397 + 0,189 + 2,083: summed, then two decimals, never a whole pallet');
        assert.deepEqual({ ...totals, pallets: 0 }, { quantity: 400, netKg: 600, grossKg: 440, amount: 1260, pallets: 0, linesWithoutNet: 1, linesWithoutGross: 2, linesWithoutPrice: 1, linesWithoutPallets: 0 });
        assert.equal(sumLines([{ quantity: 250, netKg: null, grossKg: null, total: null, pallets: pallets(250, 120) }]).pallets, 2.08);
    });

    test('line total is quantity × price on cents, none without price', () => {
        assert.equal(lineTotal(3, 13.345), 40.04);
        assert.equal(lineTotal(100, 12.5), 1250);
        assert.equal(lineTotal(3, null), null);
        assert.equal(lineTotal(null, 2), null);
    });
});
