import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, deferred, json, setValue, stubFetch, storedCommands, submit } from '../../test/reactHarness.ts';
import { PendingCommand, correctDispatchKey } from '../../lib/pendingCommands';
import { SalesDispatch, SalesDispatchCorrection, SalesOrder, StockReservation } from '../../types/salesOrder';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesOrderDetail from './SalesOrderDetail';

// Verzendcorrectie door de echte componenten heen (SalesOrderDetail + DispatchList + CorrectDispatchPanel + context):
// zonder expliciete bevestiging gaat er niets naar de server; een onafgeronde correctie blijft zichtbaar en herhaalbaar
// nadat een verse respons de verzending al als gecorrigeerd toont (verwerkt, antwoord verloren), en de replay boekt niets dubbel.

const reservation = (id: number, status: 'Active' | 'Consumed' | 'Corrected', version = 1): StockReservation => ({
    id, requestId: `r-${id}`, salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: `PAL-${id}`,
    warehouseLocation: null, lotNumber: `LOT-${id}`, quantity: 5, unit: 'box', netWeightKg: 20, source: 'Physical', status,
    createdAt: '2026-10-19T10:00:00.000Z', releasedAt: null, releaseReason: null, version, contentQualityStatus: 'Approved', onHold: false,
    consumedAt: status === 'Active' ? null : '2026-10-19T13:00:00.000Z', dispatchId: status === 'Active' ? null : 9, dispatchReference: status === 'Active' ? null : 'VRB-9',
    correctedAt: status === 'Corrected' ? '2026-10-19T14:00:00.000Z' : null, correctionId: status === 'Corrected' ? 3 : null, supplyCommitmentLineId: 1, supplyCommitmentId: 1, vendorName: null, origin: null, destination: null, expectedAt: null, directTradeShipmentId: null, shippedOn: null, shippedNetWeightKg: null, shippedLotNumber: null, directTradeCorrectionId: null, deliveryConfirmationId: null, deliveredOn: null,
});

const orderWith = (version: number, reservations: StockReservation[]): SalesOrder => {
    const consumed = reservations.filter((r) => r.status === 'Consumed');
    const active = reservations.filter((r) => r.status === 'Active');
    return {
        id: 4, requestId: 'o-4', customerId: 1, customerName: 'Local Market Stalls', status: 'Open', customerReference: null,
        requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version,
        cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: consumed.length * 5 === 10, delivered: false,
        lines: [{
            id: 7, productVariantId: 2, productName: 'Avocado', productVariantName: 'Green', quantity: 10, unit: 'box',
            reservedQuantity: active.length * 5, reservedNetWeightKg: active.length * 20, allocatedQuantity: 0, dispatchedQuantity: consumed.length * 5, dispatchedNetWeightKg: consumed.length * 20, directTradeShippedQuantity: 0, deliveredQuantity: 0, delivered: false,
            openQuantity: 10 - active.length * 5 - consumed.length * 5, fulfilled: consumed.length * 5 === 10, version: 3, reservations,
        }],
    };
};

const dispatchWith = (correction: SalesDispatch['correction']): SalesDispatch => ({
    id: 9, requestId: 'd-9', salesOrderId: 4, reference: 'VRB-9', actorReference: 'office-1', dispatchedAt: '2026-10-19T13:00:00.000Z', delivery: null, correction,
    lines: [{ id: 1, stockReservationId: 11, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: 'PAL-11', lotNumber: 'LOT-11', productName: 'Avocado', productVariantName: 'Green', quantity: 5, unit: 'box', netWeightKg: 20, stockMovementId: 1 }],
});

const correctionResponse: SalesDispatchCorrection = {
    id: 3, requestId: 'c-lost', salesDispatchId: 9, salesOrderId: 4, reason: 'Verkeerd gescand', actorReference: 'office-1', goodsNeverLeftWarehouseConfirmed: true, correctedAt: '2026-10-19T14:00:00.000Z',
    lines: [{ id: 5, salesDispatchLineId: 1, stockReservationId: 11, restoredPalletContentLineId: 77, palletId: 2, palletReference: 'PAL-11', lotNumber: 'LOT-11', quantity: 5, unit: 'box', netWeightKg: 20, restoredQualityStatus: 'Quarantine', stockMovementId: 8 }],
};

const frozen: PendingCommand = {
    kind: 'correct-dispatch', key: correctDispatchKey(4, 9), orderId: 4, dispatchId: 9, requestId: 'c-lost', attempts: 1, lastError: 'network down', frozenAt: '2026-10-19T13:59:00.000Z',
    payload: { requestId: 'c-lost', expectedOrderVersion: 2, actorReference: 'office-1', reason: 'Verkeerd gescand', goodsNeverLeftWarehouse: true, lines: [{ stockReservationId: 11, expectedReservationVersion: 1 }] },
    display: { customerName: 'Local Market Stalls', lineCount: 1 },
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
let nextPost: () => Promise<Response>;
let dispatches: SalesDispatch[];
let reloads: number;

const element = (order: SalesOrder) => (
    <PendingCommandsProvider>
        <HydratedGate>
            <SalesOrderDetail order={order} actor="office-1" onOrderChanged={async () => { reloads += 1; return true; }} />
        </HydratedGate>
    </PendingCommandsProvider>
);

describe('CorrectDispatchPanel through the component lifecycle', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        reloads = 0;
        nextPost = async () => json(500, { message: 'no post handler' });
        dispatches = [dispatchWith(null)];
        posted = stubFetch({ onGet: (url) => (url.includes('/dispatches') ? json(200, dispatches) : json(404, { message: `unexpected GET ${url}` })), nextPost: () => nextPost() });
        mounted = new Mounted();
    });

    afterEach(async () => { await mounted.unmount(); });

    test('without the explicit confirmation nothing is sent; with it the frozen command carries goodsNeverLeftWarehouse and the versions', async () => {
        await mounted.render(element(orderWith(2, [reservation(11, 'Consumed')])));
        await click(await mounted.waitFor(() => mounted.button('sales_correct_dispatch'), 'correct button'));
        const form = await mounted.waitFor(() => mounted.form(), 'correction panel');
        const checkbox = form.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
        assert.equal(checkbox.checked, false, 'confirmation starts unchecked');
        assert.equal(mounted.hasText('sales_correct_not_a_return'), true);
        await act(async () => { setValue(form.querySelector('textarea')!, 'Verkeerd gescand'); });
        await submit(form);
        assert.equal(posted.length, 0, 'no request without confirmation');
        assert.equal(mounted.hasText('sales_correct_confirm_required'), true);
        assert.deepEqual(storedCommands(), {});

        await click(checkbox);
        const response = deferred<Response>();
        nextPost = () => response.promise;
        await submit(form);
        assert.equal(posted.length, 1);
        assert.equal(posted[0].url.endsWith('/sales-orders/4/dispatches/9/corrections'), true);
        const body = JSON.parse(posted[0].body);
        assert.equal(body.goodsNeverLeftWarehouse, true);
        assert.equal(body.expectedOrderVersion, 2);
        assert.deepEqual(body.lines, [{ stockReservationId: 11, expectedReservationVersion: 1 }]);
        assert.equal(mounted.form(), form);
        assert.equal(mounted.button('supply_retry_same'), null);
        dispatches = [dispatchWith(correctionResponse)];
        response.resolve(json(201, { ...correctionResponse, requestId: body.requestId }));
        await mounted.waitFor(() => mounted.hasText('sales_notice_corrected'), 'correction notice');
        assert.deepEqual(storedCommands(), {});
        assert.equal(reloads, 1);
    });

    test('committed but response lost: after refresh the dispatch is already corrected, the frozen correction stays visible, the identical replay gets 200 and clears storage', async () => {
        window.sessionStorage.setItem('agf_sales_pending_commands', JSON.stringify({ [frozen.key]: frozen }));
        dispatches = [dispatchWith(correctionResponse)];
        await mounted.render(element(orderWith(3, [reservation(11, 'Corrected', 2)])));

        const retry = await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry on the frozen correction');
        assert.equal(mounted.hasText('sales_correct_replay_only'), true);
        assert.equal(mounted.button('sales_correct_confirm_button'), null, 'no fresh correction');
        assert.equal(mounted.button('sales_correct_dispatch'), null, 'no second correction button for a corrected dispatch');
        assert.equal(mounted.form()!.querySelector<HTMLInputElement>('input[type="checkbox"]')!.checked, true);
        assert.equal(mounted.form()!.querySelector<HTMLInputElement>('input[type="checkbox"]')!.disabled, true);

        const response = deferred<Response>();
        nextPost = () => response.promise;
        await click(retry);
        assert.equal(posted.length, 1);
        assert.deepEqual(JSON.parse(posted[0].body), frozen.payload, 'identical frozen payload incl. the confirmation');
        response.resolve(json(200, correctionResponse));
        await mounted.waitFor(() => Object.keys(storedCommands()).length === 0, 'storage cleared after the 200 replay');
        await mounted.waitFor(() => mounted.hasText('sales_notice_corrected') && mounted.hasText('supply_saved_before'), 'replay notice');
        assert.equal(reloads, 1);
    });

    test('a 409 keeps the same panel and requires reload; after reload a fresh decision is possible', async () => {
        await mounted.render(element(orderWith(2, [reservation(11, 'Consumed')])));
        await click(await mounted.waitFor(() => mounted.button('sales_correct_dispatch'), 'correct button'));
        const form = await mounted.waitFor(() => mounted.form(), 'correction panel');
        await click(form.querySelector<HTMLInputElement>('input[type="checkbox"]')!);
        await act(async () => { setValue(form.querySelector('textarea')!, 'Verkeerd gescand'); });
        nextPost = async () => json(409, { message: 'Sales order 4 is at version 3, not 2; reload it and decide again.' });
        await submit(form);
        await mounted.waitFor(() => mounted.hasText('sales_conflict'), 'conflict');
        assert.equal(mounted.form(), form);
        assert.deepEqual(storedCommands(), {});
        await click(mounted.button('sales_reload_and_decide')!);
        await mounted.waitFor(() => mounted.form() !== null && mounted.form() !== form, 'fresh panel');
        assert.equal(mounted.hasText('sales_conflict'), false);
        assert.ok(mounted.button('sales_correct_confirm_button'));
    });
});
