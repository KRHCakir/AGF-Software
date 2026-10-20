import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { activeConfirmation, hasConsistentDeliveryState, isActiveConfirmation } from './deliveryState.ts';
import { isDirectTradeShipment, isSalesDispatch } from '../types/salesOrder.ts';
import type { DeliveryConfirmation } from '../types/salesOrder';

// Eén definitie van actief (geen intrekking) en een strikte vormcontrole: een historische bevestiging kan nooit als actief binnenkomen.

const confirmation = (id: number, sequence: number, retracted: boolean): DeliveryConfirmation => ({
    id, requestId: `c-${id}`, shipmentKind: 'Warehouse', salesDispatchId: 5, directTradeShipmentId: null, salesOrderId: 4, deliveredOn: '2026-10-19',
    receiptReference: null, actorReference: 'office-1', deliveredInFullConfirmed: true, confirmationCode: 'DELIVERED_IN_FULL',
    confirmationStatement: 'Deze verzending is volledig en zonder afwijkingen afgeleverd.', recordedAt: '2026-10-19T10:00:00.000Z', sequence,
    retraction: retracted ? {
        id: 100 + id, requestId: `r-${id}`, shipmentDeliveryConfirmationId: id, shipmentKind: 'Warehouse', salesDispatchId: 5, directTradeShipmentId: null, salesOrderId: 4,
        reason: 'fout', actorReference: 'office-1', recordedInErrorConfirmed: true, confirmationCode: 'DELIVERY_CONFIRMATION_RECORDED_IN_ERROR',
        confirmationStatement: 'Deze afleverbevestiging is ten onrechte vastgelegd.', recordedAt: '2026-10-20T10:00:00.000Z',
    } : null,
});

describe('active delivery confirmation', () => {
    test('active means: no retraction; at most one is active', () => {
        assert.equal(isActiveConfirmation(confirmation(1, 1, false)), true);
        assert.equal(isActiveConfirmation(confirmation(1, 1, true)), false);
        assert.equal(activeConfirmation([confirmation(1, 1, true), confirmation(2, 2, false)])?.id, 2);
        assert.equal(activeConfirmation([confirmation(1, 1, true)]), null);
        assert.equal(activeConfirmation([]), null);
    });

    test('delivery must be exactly the single active confirmation of a gap-free history', () => {
        const retracted = confirmation(1, 1, true);
        const active = confirmation(2, 2, false);
        assert.equal(hasConsistentDeliveryState(active, [retracted, active]), true);
        assert.equal(hasConsistentDeliveryState(null, [retracted]), true);
        assert.equal(hasConsistentDeliveryState(null, []), true);
        assert.equal(hasConsistentDeliveryState(retracted, [retracted]), false, 'a retracted confirmation is never the delivery');
        assert.equal(hasConsistentDeliveryState(null, [retracted, active]), false, 'an active confirmation must be the delivery');
        assert.equal(hasConsistentDeliveryState(active, [confirmation(3, 1, false), active]), false, 'never two active');
        assert.equal(hasConsistentDeliveryState(active, [active]), false, 'numbers start at 1 without gaps');
        assert.equal(hasConsistentDeliveryState(confirmation(9, 2, false), [retracted, active]), false, 'delivery id must match');
    });

    test('dispatch and DirectTrade responses with an inconsistent delivery state are rejected', () => {
        const dispatch = { id: 5, requestId: 'd', salesOrderId: 4, reference: null, actorReference: 'o', dispatchedAt: '2026-10-19T10:00:00.000Z', correction: null, lines: [] };
        assert.equal(isSalesDispatch({ ...dispatch, delivery: null, deliveryHistory: [confirmation(1, 1, true)] }), true);
        assert.equal(isSalesDispatch({ ...dispatch, delivery: confirmation(1, 1, true), deliveryHistory: [confirmation(1, 1, true)] }), false);
        assert.equal(isSalesDispatch({ ...dispatch, delivery: null }), false, 'history is required');
        const shipment = { id: 5, requestId: 's', salesOrderId: 4, customerId: 6, customerName: 'K', shippedOn: '2026-10-19', reference: null, actorReference: 'o', recordedAt: '2026-10-19T10:00:00.000Z', correction: null, lines: [] };
        const active = { ...confirmation(2, 1, false), shipmentKind: 'DirectTrade' as const, salesDispatchId: null, directTradeShipmentId: 5 };
        assert.equal(isDirectTradeShipment({ ...shipment, deliveryRecorded: true, delivery: active, deliveryHistory: [active] }), true);
        assert.equal(isDirectTradeShipment({ ...shipment, deliveryRecorded: false, delivery: active, deliveryHistory: [active] }), false, 'deliveryRecorded follows the active confirmation');
    });
});
