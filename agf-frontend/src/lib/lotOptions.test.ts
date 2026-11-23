import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { LotOption } from '../types/salesOrder';
import { exactLot, lotKey, matchingLots } from './lotOptions.ts';

const lot = (partijId: number | null, number: string | null): LotOption => ({
    partijId, number, unit: 'colli', availableQuantity: 10, palletCount: 1, size: null, color: null, origin: null, locations: [null], bins: [], receivedAt: '2026-11-20T08:00:00Z', vendorName: null,
});

describe('lot choice', () => {
    test('the key ignores spaces, dashes and case, so a number without letter finds both kinds', () => {
        assert.equal(lotKey(' i-2611 40 '), 'I261140');
        const options = [lot(1, 'I-261140'), lot(2, 'I-261141'), lot(null, 'LOT-A')];
        assert.deepEqual(matchingLots(options, '261140').map((o) => o.partijId), [1]);
        assert.deepEqual(matchingLots(options, '').map((o) => o.partijId), [1, 2], 'stock without a lot is shown but never chosen');
    });

    test('an exact number picks that lot; part of a number does not', () => {
        const options = [lot(1, 'I-261140'), lot(2, 'I-2611401')];
        assert.equal(exactLot(options, 'i 261140')?.partijId, 1);
        assert.equal(exactLot(options, '26114'), null);
        assert.equal(exactLot(options, ''), null);
        assert.equal(exactLot(options, '261140')?.partijId, 1, 'without the letter, when only one lot has that number');
        assert.equal(exactLot([lot(1, 'I-261140'), lot(3, 'X-261140')], '261140'), null, 'two lots with that number: no exact choice');
    });
});
