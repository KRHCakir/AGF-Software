import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { coverageParts, dayMonth, freeState, palletEquivalent, parseSellPrefill, proposeSplit, sellHref, sellQuantity, singleSource } from './salesSupply.ts';

// Zelfde gevallen als SalesShortageTests.The_Proposal_Takes_What_Is_Free_In_Stock_And_The_Rest_From_Expected_Supply in de backend.
describe('proposeSplit', () => {
    for (const [quantity, freeStock, stock, expected] of [[50, 40, 40, 10], [30, 40, 30, 0], [5, 0, 0, 5], [5, -3, 0, 5], [0, 40, 0, 0]]) {
        test(`${quantity} with ${freeStock} free in stock: ${stock} from stock, ${expected} expected`, () => {
            assert.deepEqual(proposeSplit(quantity, freeStock), { stock, expected });
        });
    }

    test('one source when no split is needed, null when it is', () => {
        assert.equal(singleSource(30, 40), 'Stock');
        assert.equal(singleSource(30, 0), 'Expected');
        assert.equal(singleSource(50, 40), null);
    });

    test('decimals stay exact to three places', () => {
        assert.deepEqual(proposeSplit(10.5, 3.2), { stock: 3.2, expected: 7.3 });
    });
});

describe('the owner\'s practice: 40 in stock, 20 expected', () => {
    // Vrij = voorraad + verwacht − verkocht; negatief is een tekort, geen blokkade.
    const free = (stock: number, expected: number, sold: number) => stock + expected - sold;
    test('free 60, then 10, then -10 (shortage), back to 0 after a second purchase of 10', () => {
        assert.equal(freeState(free(40, 20, 0)), 'free');
        assert.equal(free(40, 20, 50), 10);
        assert.equal(freeState(free(40, 20, 70)), 'shortage');
        assert.equal(free(60, 0, 70), -10);
        assert.equal(freeState(free(60, 10, 70)), 'zero');
    });
});

describe('Verkopen from availability', () => {
    test('defaults to the free quantity, at least 1', () => {
        assert.equal(sellQuantity({ free: 60 }), 60);
        assert.equal(sellQuantity({ free: -10 }), 1);
        assert.equal(sellQuantity({ free: 0.5 }), 1);
        assert.equal(sellQuantity(null), 1);
    });

    test('the link round-trips into a prefill; anything incomplete or invalid is no prefill', () => {
        const href = sellHref(7, 'colli', 60);
        assert.equal(href, '/sales-orders?new=1&variantId=7&unit=colli&quantity=60');
        assert.deepEqual(parseSellPrefill(new URL(`http://x${href}`).searchParams), { productVariantId: 7, unit: 'colli', quantity: 60 });
        for (const query of ['variantId=7&unit=colli&quantity=60', 'new=1&variantId=0&unit=colli&quantity=6', 'new=1&variantId=7&unit=kist&quantity=6',
            'new=1&variantId=7&unit=colli&quantity=-1', 'new=1&variantId=7&unit=colli&quantity=1.2345', 'new=1&variantId=99999999999&unit=colli&quantity=1']) {
            assert.equal(parseSellPrefill(new URLSearchParams(query)), null, query);
        }
    });
});

describe('coverageParts', () => {
    test('now from stock, in inspection, expected per purchase order and the shortage, in that order', () => {
        const parts = coverageParts({
            fromStock: 40,
            expected: [
                { supplyCommitmentId: 1, supplyOrderNumber: 'IO-2026-0001', expectedAt: null, quantity: 4, inInspection: true },
                { supplyCommitmentId: 2, supplyOrderNumber: 'IO-2026-0002', expectedAt: '2026-11-05T06:00:00Z', quantity: 16, inInspection: false },
            ],
            shortage: 10,
        });
        assert.deepEqual(parts.map((p) => [p.kind, p.quantity]), [['stock', 40], ['inspection', 4], ['expected', 16], ['shortage', 10]]);
        assert.deepEqual(coverageParts(null), []);
        assert.deepEqual(coverageParts({ fromStock: 0, expected: [], shortage: 0 }), []);
    });
});

describe('small helpers', () => {
    test('pallets are information only, never rounded to whole pallets', () => {
        assert.equal(palletEquivalent(390, 122), 3.2);
        assert.equal(palletEquivalent(122, 122), 1);
        assert.equal(palletEquivalent(10, null), null);
    });

    test('day and month of an expected date in the own time zone', () => {
        assert.equal(dayMonth('2026-11-05T12:00:00'), '05-11');
        assert.equal(dayMonth('later'), 'later');
    });
});
