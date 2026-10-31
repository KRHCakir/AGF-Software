import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { SupplyCommitment, SupplyCommitmentLine } from '../types/supply';
import { lineOrdinals, orderListGroups } from './receiptOrderList.ts';

// Orderlijst bij "Pallet ontvangen": zoeken op leverancier, ordernummer, referentie, partij en product; eerst vandaag of eerder verwacht.
const line = (id: number, product: string, variant: string, lot: string | null): SupplyCommitmentLine => ({
    id, productVariantId: id, productName: product, productVariantName: variant, lotNumber: lot, expectedQuantity: 10, expectedUnit: 'colli', expectedNetWeightKg: 40,
    weightMethod: 'Calculated', defaultNetKgPerPackage: 4, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null,
});
const order = (id: number, number: string, vendor: string, expectedAt: string | null, lines: SupplyCommitmentLine[], supplierReference: string | null = null): SupplyCommitment => ({
    id, requestId: `r-${id}`, vendorId: 1, vendorName: vendor, customerId: null, customerName: null, route: 'Warehouse', status: 'Expected', origin: '', destination: null,
    expectedAt, vendorGgns: [], lines, orderNumber: number, supplierReference, orderedOn: '2026-10-28',
});
const orders = [
    order(3, 'IO-2026-0003', 'Fruitimport Zuid B.V.', '2026-11-03T08:00:00Z', [line(31, 'Mango', 'Palmer', 'TEST-002')]),
    order(1, 'IO-2026-0001', "Nature's Pride", '2026-10-31T23:30:00Z', [line(11, 'Avocado', 'Hass', 'TEST-001'), line(12, 'Avocado', 'Hass', 'TEST-009'), line(13, 'Citroen', 'Primofiori', null)], 'NP-778'),
    order(2, 'IO-2026-0002', "Nature's Pride", '2026-10-29T07:00:00Z', [line(21, 'Avocado', 'Hass', 'LOT-7')]),
    order(4, 'IO-2026-0004', 'Zonder datum', null, [line(41, 'Kiwi', 'Hayward', null)]),
];

describe('receiptOrderList', () => {
    test('expected today or earlier first (Amsterdam calendar day), then the future, then without a date', () => {
        // 31 okt 23:30 UTC is 1 november in Amsterdam: vandaag.
        assert.deepEqual(orderListGroups(orders, '', '2026-11-01').map((g) => [g.commitment.orderNumber, g.due]), [
            ['IO-2026-0002', true], ['IO-2026-0001', true], ['IO-2026-0003', false], ['IO-2026-0004', false],
        ]);
    });

    test('every typed word must match somewhere: supplier, order number, supplier reference, lot or product', () => {
        const numbers = (query: string) => orderListGroups(orders, query, '2026-11-01').map((g) => [g.commitment.orderNumber, g.lines.map((l) => l.id)]);
        assert.deepEqual(numbers('nature hass'), [['IO-2026-0002', [21]], ['IO-2026-0001', [11, 12]]]);
        assert.deepEqual(numbers('0003'), [['IO-2026-0003', [31]]]);
        assert.deepEqual(numbers('np-778'), [['IO-2026-0001', [11, 12, 13]]]);
        assert.deepEqual(numbers('test-009'), [['IO-2026-0001', [12]]]);
        assert.deepEqual(numbers('fruitimport'), [['IO-2026-0003', [31]]]);
        assert.deepEqual(numbers('banaan'), []);
    });

    test('"line 1 of 2" only for several lines of the same product in one order', () => {
        const ordinals = lineOrdinals(orders[1].lines);
        assert.deepEqual([...ordinals.entries()], [[11, { index: 1, of: 2 }], [12, { index: 2, of: 2 }]]);
        assert.equal(lineOrdinals(orders[0].lines).size, 0);
    });
});
