import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { activeLines, lineProgress, partialSummary } from './orderLineProgress.ts';
import type { PurchaseOrderLine } from '../types/purchaseOrder';

const line = (over: Partial<PurchaseOrderLine>): PurchaseOrderLine => ({
    id: 1, productVariantId: 1, productName: 'Mango', productVariantName: 'Kent', lotNumber: null, expectedQuantity: 100, unit: 'colli', expectedNetWeightKg: 400,
    receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0, ...over,
} as PurchaseOrderLine);

describe('order line progress', () => {
    test('a fully replaced line without receipt is not shown (as the server counts active lines)', () => {
        const lines = [line({ id: 1, expectedQuantity: 0, receivedQuantity: 0, cancelledQuantity: 100 }), line({ id: 2, expectedQuantity: 0, receivedQuantity: 5 }), line({ id: 3 })];
        assert.deepEqual(activeLines(lines).map((l) => l.id), [2, 3]);
    });

    test('ordered, received, open and status per line; a cancelled rest makes the line done and counts as ordered', () => {
        assert.deepEqual(lineProgress(line({})), { id: 1, ordered: 100, received: 0, open: 100, cancelled: 0, status: 'Ordered' });
        assert.equal(lineProgress(line({ receivedQuantity: 40 })).status, 'Partial');
        assert.equal(lineProgress(line({ receivedQuantity: 100 })).status, 'Received');
        assert.equal(lineProgress(line({ receivedQuantity: 120 })).open, 0, 'more than ordered: nothing open');
        assert.deepEqual(lineProgress(line({ expectedQuantity: 40, receivedQuantity: 40, cancelledQuantity: 60 })), { id: 1, ordered: 100, received: 40, open: 0, cancelled: 60, status: 'Closed' });
    });

    test('the list says x of y lines and the open packages only for a partly received order', () => {
        assert.deepEqual(partialSummary({ status: 'PartiallyReceived', lineCount: 30, receivedLineCount: 12, openQuantity: 840 }), { received: 12, lines: 30, open: 840 });
        assert.equal(partialSummary({ status: 'Ordered', lineCount: 3, receivedLineCount: 0, openQuantity: 10 }), null);
        assert.equal(partialSummary({ status: 'PartiallyReceived' }), null, 'an older server without the counts');
    });
});
