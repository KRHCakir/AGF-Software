import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, deferred, json, setValue, stubFetch, storedCommands, submit } from '../../test/reactHarness.ts';
import { PendingCommand, dispatchKey } from '../../lib/pendingCommands';
import { SalesOrder, StockReservation } from '../../types/salesOrder';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesOrderDetail from './SalesOrderDetail';

// Verzendpaneel door de echte componenten heen (SalesOrderDetail + DispatchPanel + context): keuze van hele reserveringen
// (on hold niet kiesbaar), bevroren versies, en een onafgeronde verzending die zichtbaar en herhaalbaar blijft nadat een
// verse orderrespons de gekozen reserveringen al als Consumed toont (verwerkt, antwoord verloren).

const reservation = (id: number, status: 'Active' | 'Released' | 'Consumed', onHold = false, version = 0): StockReservation => ({
    id, requestId: `r-${id}`, salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: `PAL-${id}`,
    warehouseLocation: null, lotNumber: `LOT-${id}`, quantity: 5, unit: 'box', netWeightKg: 20, source: 'Physical', status,
    createdAt: '2026-10-19T10:00:00.000Z', releasedAt: status === 'Released' ? '2026-10-19T10:30:00.000Z' : null,
    releaseReason: status === 'Released' ? 'eerder' : null, version, contentQualityStatus: onHold ? 'Blocked' : 'Approved', onHold,
    consumedAt: status === 'Consumed' ? '2026-10-19T13:00:00.000Z' : null, dispatchId: status === 'Consumed' ? 9 : null, dispatchReference: status === 'Consumed' ? 'VRB-9' : null,
    correctedAt: null, correctionId: null, supplyCommitmentLineId: 1, supplyCommitmentId: 1, vendorName: null, origin: null, destination: null, expectedAt: null, directTradeShipmentId: null, shippedOn: null, shippedNetWeightKg: null, shippedLotNumber: null, directTradeCorrectionId: null, deliveryConfirmationId: null, deliveredOn: null,
});

const orderWith = (version: number, reservations: StockReservation[]): SalesOrder => {
    const active = reservations.filter((r) => r.status === 'Active');
    const consumed = reservations.filter((r) => r.status === 'Consumed');
    return {
        id: 4, requestId: 'o-4', customerId: 1, customerName: 'Local Market Stalls', status: 'Open', customerReference: null,
        requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version,
        cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: consumed.length * 5 === 20, delivered: false,
        lines: [{
            id: 7, productVariantId: 2, productName: 'Avocado', productVariantName: 'Green', quantity: 20, unit: 'box', palletCount: null, packagesPerPallet: null,
            reservedQuantity: active.length * 5, reservedNetWeightKg: active.length * 20, allocatedQuantity: 0, directTradeShippedQuantity: 0, deliveredQuantity: 0, delivered: false,
            dispatchedQuantity: consumed.length * 5, dispatchedNetWeightKg: consumed.length * 20,
            openQuantity: 20 - active.length * 5 - consumed.length * 5, fulfilled: consumed.length * 5 === 20, version: 3, reservations,
        }],
    };
};

const dispatchResponse = { id: 9, requestId: 'd-lost', salesOrderId: 4, reference: 'VRB-9', actorReference: 'office-1', dispatchedAt: '2026-10-19T13:00:00.000Z', delivery: null, deliveryHistory: [], correction: null,
    lines: [{ id: 1, stockReservationId: 11, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: 'PAL-11', lotNumber: 'LOT-11', productName: 'Avocado', productVariantName: 'Green', quantity: 5, unit: 'box', netWeightKg: 20, stockMovementId: 1 }] };

const frozen: PendingCommand = {
    kind: 'dispatch', key: dispatchKey(4), orderId: 4, requestId: 'd-lost', attempts: 1, lastError: 'network down', frozenAt: '2026-10-19T12:59:00.000Z',
    payload: { requestId: 'd-lost', expectedOrderVersion: 2, actorReference: 'office-1', reference: 'VRB-9', lines: [{ stockReservationId: 11, expectedReservationVersion: 0 }] },
    display: { customerName: 'Local Market Stalls', lineCount: 1 },
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
let nextPost: () => Promise<Response>;
let onGet: (url: string) => Response;
let current: SalesOrder;
let reloads: number;

const element = (order: SalesOrder) => (
    <PendingCommandsProvider>
        <HydratedGate>
            <SalesOrderDetail order={order} actor="office-1" onOrderChanged={async () => { reloads += 1; return true; }} />
        </HydratedGate>
    </PendingCommandsProvider>
);

describe('DispatchPanel through the component lifecycle', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        reloads = 0;
        nextPost = async () => json(500, { message: 'no post handler' });
        onGet = (url) => (url.includes('/dispatches') ? json(200, [dispatchResponse]) : json(404, { message: `unexpected GET ${url}` }));
        posted = stubFetch({ onGet: (url) => onGet(url), nextPost: () => nextPost() });
        mounted = new Mounted();
    });

    afterEach(async () => { await mounted.unmount(); });

    test('only active reservations that are not on hold can be chosen; the request freezes order and reservation versions', async () => {
        current = orderWith(2, [reservation(11, 'Active', false, 0), reservation(12, 'Active', true, 1), reservation(13, 'Released')]);
        await mounted.render(element(current));
        await click(await mounted.waitFor(() => mounted.button('sales_dispatch_order'), 'dispatch button'));
        const form = await mounted.waitFor(() => mounted.form(), 'dispatch panel');
        const boxes = Array.from(form.querySelectorAll<HTMLInputElement>('input[type="checkbox"]'));
        assert.equal(boxes.length, 2, 'released reservations are not offered; active and on-hold are listed');
        assert.equal(boxes[0].disabled, false);
        assert.equal(boxes[1].disabled, true, 'on hold cannot be dispatched');
        assert.equal((form.textContent ?? '').includes('PAL-13'), false);

        await click(boxes[0]);
        assert.equal(mounted.hasText('sales_dispatch_count'), true);
        await act(async () => { setValue(form.querySelector<HTMLInputElement>('input[maxlength="100"]')!, ' VRB-1 '); });
        const response = deferred<Response>();
        nextPost = () => response.promise;
        await submit(form);

        assert.equal(posted.length, 1);
        assert.equal(posted[0].url.endsWith('/sales-orders/4/dispatches'), true);
        const body = JSON.parse(posted[0].body);
        assert.deepEqual(body.lines, [{ stockReservationId: 11, expectedReservationVersion: 0 }]);
        assert.equal(body.expectedOrderVersion, 2);
        assert.equal(body.reference, 'VRB-1');
        assert.equal(mounted.form(), form, 'same instance while the request runs');
        assert.equal(mounted.button('supply_retry_same'), null);
        response.resolve(json(201, { ...dispatchResponse, reference: 'VRB-1', requestId: body.requestId }));
        await mounted.waitFor(() => mounted.hasText('sales_notice_dispatched'), 'dispatch notice');
        assert.deepEqual(storedCommands(), {});
        assert.equal(reloads, 1);
    });

    test('committed but response lost: after refresh the order shows the reservation Consumed, the frozen dispatch stays visible, the exact replay gets 200 and clears storage', async () => {
        window.sessionStorage.setItem('agf_sales_pending_commands', JSON.stringify({ [frozen.key]: frozen }));
        // Verse orderrespons na verversen: reservering 11 is al Consumed (versie 1), de order staat op versie 3.
        current = orderWith(3, [reservation(11, 'Consumed', false, 1), reservation(12, 'Active', false, 0)]);
        await mounted.render(element(current));

        const retry = await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry on the frozen dispatch');
        assert.equal(mounted.hasText('sales_dispatch_replay_only'), true);
        assert.equal(mounted.button('sales_dispatch_confirm'), null, 'no fresh dispatch while the command is unresolved');
        const checkbox = Array.from(mounted.form()!.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')).find((input) => input.checked);
        assert.ok(checkbox, 'the frozen selection is shown');
        assert.ok(Array.from(mounted.form()!.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')).every((input) => input.disabled));

        const response = deferred<Response>();
        nextPost = () => response.promise;
        await click(retry);
        assert.equal(posted.length, 1);
        assert.deepEqual(JSON.parse(posted[0].body), frozen.payload, 'identical frozen payload and requestId');
        response.resolve(json(200, dispatchResponse));
        await mounted.waitFor(() => Object.keys(storedCommands()).length === 0, 'storage cleared after the 200 replay');
        assert.equal(reloads, 1);
        await mounted.waitFor(() => mounted.hasText('sales_notice_dispatched') && mounted.hasText('supply_saved_before'), 'replay notice');
    });

    test('a fully dispatched line shows "fully shipped, delivery not yet confirmed", never delivered or fully reserved, and the dispatch button is disabled', async () => {
        current = orderWith(4, [reservation(11, 'Consumed'), reservation(12, 'Consumed'), reservation(13, 'Consumed'), reservation(14, 'Consumed')]);
        await mounted.render(element(current));
        await mounted.waitFor(() => mounted.hasText('sales_line_shipped_not_confirmed'), 'fulfilled line');
        assert.equal(mounted.hasText('sales_order_shipped_not_confirmed'), true);
        assert.equal(mounted.hasText('sales_line_delivered'), false, 'dispatching is never delivery');
        assert.equal(mounted.hasText('sales_line_complete'), false, 'never "fully reserved" with 0 reserved');
        assert.equal(mounted.button('sales_dispatch_order')?.disabled, true);
        assert.equal(mounted.button('sales_cancel_order')?.disabled, true, 'cancellation is refused once something is dispatched');
        await mounted.waitFor(() => mounted.hasText('sales_dispatch_heading'), 'dispatch list');
    });

    test('a 409 keeps the same panel and requires reload; a successful reload starts a fresh decision', async () => {
        current = orderWith(2, [reservation(11, 'Active')]);
        await mounted.render(element(current));
        await click(await mounted.waitFor(() => mounted.button('sales_dispatch_order'), 'dispatch button'));
        const form = await mounted.waitFor(() => mounted.form(), 'dispatch panel');
        await click(form.querySelector<HTMLInputElement>('input[type="checkbox"]')!);
        nextPost = async () => json(409, { message: 'Reservation 11 is at version 1, not 0; reload it and decide again.' });
        await submit(form);
        await mounted.waitFor(() => mounted.hasText('sales_conflict'), 'conflict');
        assert.equal(mounted.form(), form);
        assert.deepEqual(storedCommands(), {});
        await click(mounted.button('sales_reload_and_decide')!);
        await mounted.waitFor(() => mounted.form() !== null && mounted.form() !== form, 'fresh panel');
        assert.equal(mounted.hasText('sales_conflict'), false);
        assert.ok(mounted.button('sales_dispatch_confirm'));
    });
});
