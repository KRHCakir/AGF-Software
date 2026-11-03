import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { SalesOrderLine } from '../types/salesOrder';
import { salesNextStep } from './salesNextStep.ts';

const line = (over: Partial<SalesOrderLine>) => ({ openQuantity: 0, reservedQuantity: 0, allocatedQuantity: 0, ...over } as SalesOrderLine);
const order = (lines: SalesOrderLine[], over: { status?: string; delivered?: boolean } = {}) => ({ status: 'Open', delivered: false, lines, ...over });

describe('next step on a sales order', () => {
    test('reserve first, then dispatch, then confirm delivery; done when delivered; nothing on a cancelled order', () => {
        assert.equal(salesNextStep(order([line({ openQuantity: 10 })])), 'reserve');
        assert.equal(salesNextStep(order([line({ openQuantity: 4, reservedQuantity: 6 })])), 'reserve', 'something is still open');
        assert.equal(salesNextStep(order([line({ reservedQuantity: 10 })])), 'dispatch');
        assert.equal(salesNextStep(order([line({ allocatedQuantity: 10 })])), 'dispatch', 'customer-bound allocation is shipped too');
        assert.equal(salesNextStep(order([line({})])), 'confirm', 'all dispatched, delivery not confirmed yet');
        assert.equal(salesNextStep(order([line({})], { delivered: true })), 'done');
        assert.equal(salesNextStep(order([line({ openQuantity: 10 })], { status: 'Cancelled' })), 'cancelled');
    });
});
