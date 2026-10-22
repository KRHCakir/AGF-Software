import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setValue, stubFetch, storedCommands } from '../../test/reactHarness.ts';
import { todayInAmsterdam } from '../../lib/calendarDate';
import { SalesOrder, StockReservation } from '../../types/salesOrder';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesOrderDetail from './SalesOrderDetail';

// Afleverbevestiging door de echte componenten heen (SalesOrderDetail + beide lijsten + paneel + context). Magazijnverzending 5 en
// DirectTrade-verzending 5 hebben hetzelfde nummer: de knop bepaalt de soort en het endpoint. Geen POST zonder verklaring, waarschuwing
// direct bij de checkbox, verloren antwoord → replay-only na verversen → identieke replay 200, 409 → herladen, en een bevestigde
// aflevering toont "afgeleverd (bevestigd)" en verbergt de correctieknop.


const physical = (deliveryId: number | null = null): StockReservation => ({
    id: 11, requestId: 'r-11', salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: 'PAL-11',
    warehouseLocation: null, lotNumber: 'LOT-11', quantity: 10, unit: 'box', netWeightKg: 40, source: 'Physical', status: 'Consumed',
    createdAt: '2026-10-19T10:00:00.000Z', releasedAt: null, releaseReason: null, version: 1, contentQualityStatus: 'Approved', onHold: false,
    consumedAt: '2026-10-19T13:00:00.000Z', dispatchId: 5, dispatchReference: 'VRB-5', correctedAt: null, correctionId: null,
    supplyCommitmentLineId: 1, supplyCommitmentId: 1, vendorName: null, origin: null, destination: null, expectedAt: null, directTradeShipmentId: null,
    shippedOn: null, shippedNetWeightKg: null, shippedLotNumber: null, directTradeCorrectionId: null,
    deliveryConfirmationId: deliveryId, deliveredOn: deliveryId === null ? null : '2026-10-19',
});

const allocation = (deliveryId: number | null = null): StockReservation => ({
    id: 21, requestId: 'a-21', salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: null, palletId: null, palletReference: null,
    warehouseLocation: null, lotNumber: 'LOT-DT-SRC', quantity: 10, unit: 'box', netWeightKg: null, source: 'DirectTrade',
    supplyCommitmentLineId: 11, supplyCommitmentId: 10, vendorName: 'Example Supplier', origin: 'Peru', destination: 'Rotterdam', expectedAt: '2026-10-27T22:00:00.000Z',
    directTradeShipmentId: 5, shippedOn: '2026-10-19', shippedNetWeightKg: 40.5, shippedLotNumber: 'LOT-DT-SRC', directTradeCorrectionId: null,
    deliveryConfirmationId: deliveryId, deliveredOn: deliveryId === null ? null : '2026-10-19',
    status: 'Consumed', createdAt: '2026-10-20T08:00:00.000Z', releasedAt: null, releaseReason: null, version: 1, contentQualityStatus: null, onHold: false,
    consumedAt: '2026-10-20T09:00:00.000Z', dispatchId: null, dispatchReference: null, correctedAt: null, correctionId: null,
});

const orderWith = (version: number, warehouseDelivered = false, dtDelivered = false): SalesOrder => {
    const delivered = (warehouseDelivered ? 10 : 0) + (dtDelivered ? 10 : 0);
    return {
        id: 4, requestId: 'o-4', customerId: 6, customerName: 'SMOKE klant', status: 'Open', customerReference: null, internalNote: null,
        requestedDeliveryDate: '2026-10-26T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version,
        cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: true, delivered: delivered === 20,
        lines: [{
            id: 7, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', quantity: 20, unit: 'box', palletCount: null, packagesPerPallet: null,
            reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: 0, dispatchedQuantity: 20, dispatchedNetWeightKg: 80.5,
            directTradeShippedQuantity: 10, deliveredQuantity: delivered, delivered: delivered === 20, openQuantity: 0, fulfilled: true, version: 3,
            reservations: [physical(warehouseDelivered ? 1 : null), allocation(dtDelivered ? 2 : null)],
        }],
    };
};

const deliveryResponse = (kind: 'Warehouse' | 'DirectTrade', requestId: string, id = kind === 'Warehouse' ? 1 : 2) => ({
    id, requestId, shipmentKind: kind, salesDispatchId: kind === 'Warehouse' ? 5 : null, directTradeShipmentId: kind === 'DirectTrade' ? 5 : null, salesOrderId: 4,
    deliveredOn: '2026-10-19', receiptReference: 'CMR-OK-1', actorReference: 'office-1', deliveredInFullConfirmed: true,
    confirmationCode: 'DELIVERED_IN_FULL', confirmationStatement: 'Deze verzending is volledig en zonder afwijkingen afgeleverd.', recordedAt: '2026-10-20T10:00:00.000Z', sequence: 1, retraction: null,
});

const dispatch = (delivery: unknown = null) => ({
    id: 5, requestId: 'd-5', salesOrderId: 4, reference: 'VRB-5', actorReference: 'office-1', dispatchedAt: '2026-10-19T13:00:00.000Z', delivery, deliveryHistory: delivery === null ? [] : [delivery], correction: null,
    lines: [{ id: 20, stockReservationId: 11, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: 'PAL-11', lotNumber: 'LOT-11', ggn: null, productName: 'Avocado', productVariantName: 'Hass', quantity: 10, unit: 'box', netWeightKg: 40, stockMovementId: 30 }],
});

const shipment = (delivery: unknown = null) => ({
    id: 5, requestId: 's-5', salesOrderId: 4, customerId: 6, customerName: 'SMOKE klant', shippedOn: '2026-10-19', reference: 'CMR-1', actorReference: 'office-1',
    recordedAt: '2026-10-20T09:00:00.000Z', deliveryRecorded: delivery !== null, delivery, deliveryHistory: delivery === null ? [] : [delivery], correction: null,
    lines: [{ id: 1, stockReservationId: 21, salesOrderLineId: 7, supplyCommitmentLineId: 11, supplyCommitmentId: 10, vendorId: 1, vendorName: 'Example Supplier',
        origin: 'Peru', destination: 'Rotterdam', productName: 'Avocado', productVariantName: 'Hass', quantity: 10, unit: 'box', netWeightKg: 40.5,
        lotNumber: 'LOT-DT-SRC', sourceLotNumber: 'LOT-DT-SRC', lotDeviationReason: null }],
});

let mounted: Mounted;
let posted: { url: string; body: string }[];
let nextPost: () => Promise<Response>;
let reloads: number;
let dispatches: unknown[];
let shipments: unknown[];

const element = (order: SalesOrder) => (
    <PendingCommandsProvider>
        <HydratedGate>
            <SalesOrderDetail order={order} actor="office-1" onOrderChanged={async () => { reloads += 1; return true; }} />
        </HydratedGate>
    </PendingCommandsProvider>
);
const panelOf = (prefix: string) => mounted.container.querySelector<HTMLFormElement>(`form[aria-labelledby="${prefix}-title"]`);
const submitPanel = (prefix: string) => act(async () => { panelOf(prefix)!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
const warehouseSection = () => mounted.container.querySelector<HTMLElement>('section[aria-labelledby="dispatches-4"]');
const confirmButtonIn = (root: ParentNode | null) => Array.from(root?.querySelectorAll<HTMLButtonElement>('button') ?? []).find((b) => b.textContent === 'sales_delivery_confirm') ?? null;
const directTradeConfirmButton = () => Array.from(mounted.container.querySelectorAll<HTMLButtonElement>('button'))
    .find((b) => b.textContent === 'sales_delivery_confirm' && !warehouseSection()?.contains(b)) ?? null;

async function openWarehouse(order: SalesOrder) {
    await mounted.render(element(order));
    await click(await mounted.waitFor(() => confirmButtonIn(warehouseSection()), 'warehouse confirm button'));
    return mounted.waitFor(() => panelOf('delivery-w-5'), 'warehouse delivery panel');
}

async function fillAndCheck(prefix: string) {
    await act(async () => {
        setValue(mounted.container.querySelector<HTMLInputElement>(`#${prefix}-date`)!, '2026-10-19');
        setValue(mounted.container.querySelector<HTMLInputElement>(`#${prefix}-reference`)!, 'CMR-OK-1');
    });
    await click(mounted.container.querySelector<HTMLInputElement>(`#${prefix}-confirm`)!);
}

describe('DeliveryConfirmationPanel through SalesOrderDetail', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        reloads = 0;
        dispatches = [dispatch()];
        shipments = [shipment()];
        nextPost = async () => json(500, { message: 'no handler' });
        posted = stubFetch({
            onGet: (url) => (url.includes('/direct-trade-shipments') ? json(200, shipments) : url.includes('/dispatches') ? json(200, dispatches) : json(200, [])),
            nextPost: () => nextPost(),
        });
        mounted = new Mounted();
    });

    afterEach(async () => { await mounted.unmount(); });

    test('shipped is not delivered: both shipments show "not yet confirmed" and the order says fully shipped, delivery not yet confirmed', async () => {
        await mounted.render(element(orderWith(6)));
        await mounted.waitFor(() => confirmButtonIn(warehouseSection()), 'warehouse list');
        await mounted.waitFor(() => directTradeConfirmButton(), 'DirectTrade list');
        assert.ok(mounted.hasText('sales_shipped_not_confirmed_badge'));
        assert.ok(mounted.hasText('sales_ship_badge'));
        assert.ok(mounted.hasText('sales_line_shipped_not_confirmed'));
        assert.ok(mounted.hasText('sales_order_shipped_not_confirmed'));
        assert.equal(mounted.hasText('sales_line_delivered'), false);
        assert.equal(mounted.hasText('sales_delivered_badge'), false);
    });

    test('the panel lists all lines, defaults to today in Amsterdam, bounds the date and posts nothing without the unchecked statement', async () => {
        const panel = await openWarehouse(orderWith(6));
        assert.ok(panel.querySelectorAll('li').length >= 1);
        const date = panel.querySelector<HTMLInputElement>('#delivery-w-5-date')!;
        assert.equal(date.value, todayInAmsterdam());
        assert.equal(date.min, '2026-10-19', 'the Amsterdam calendar date of the dispatch is the lower bound');
        assert.equal(date.max, todayInAmsterdam());
        const checkbox = panel.querySelector<HTMLInputElement>('#delivery-w-5-confirm')!;
        assert.equal(checkbox.checked, false, 'the statement is never pre-checked');
        const statement = panel.querySelector('[data-testid="delivery-w-5-statement"]')!;
        assert.ok(statement.textContent?.includes('sales_delivery_confirm_label'));
        assert.ok(statement.textContent?.includes('sales_delivery_warning'), 'the shortage/damage/partial warning sits right at the checkbox');
        assert.equal(checkbox.getAttribute('aria-describedby'), 'delivery-w-5-warning');

        await submitPanel('delivery-w-5');
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('sales_delivery_confirm_required'));
        await act(async () => { setValue(date, '2026-10-18'); });
        await click(checkbox);
        await submitPanel('delivery-w-5');
        assert.equal(posted.length, 0, 'a date before the shipping date is refused locally');
        assert.ok(mounted.hasText('sales_delivery_date_before_shipping'));
        assert.deepEqual(storedCommands(), {});
    });

    test('confirming warehouse dispatch 5 posts to the warehouse endpoint only, then shows the notice and reloads', async () => {
        await openWarehouse(orderWith(6));
        await fillAndCheck('delivery-w-5');
        nextPost = async () => json(201, deliveryResponse('Warehouse', JSON.parse(posted[0].body).requestId));
        await submitPanel('delivery-w-5');
        await mounted.waitFor(() => (mounted.hasText('sales_notice_delivery_confirmed') ? true : null), 'notice');
        assert.equal(posted.length, 1);
        assert.ok(posted[0].url.endsWith('/sales-orders/4/dispatches/5/delivery-confirmations'));
        const body = JSON.parse(posted[0].body);
        assert.deepEqual({ ...body, requestId: 'x' }, { requestId: 'x', expectedOrderVersion: 6, actorReference: 'office-1', deliveredOn: '2026-10-19', receiptReference: 'CMR-OK-1', deliveredInFull: true });
        assert.equal(reloads, 1);
        assert.deepEqual(storedCommands(), {});
    });

    test('equal numbers: DirectTrade shipment 5 posts to its own endpoint; after a lost response and refresh the panel is replay-only and the identical replay gets 200', async () => {
        await mounted.render(element(orderWith(6)));
        await click(await mounted.waitFor(() => directTradeConfirmButton(), 'DirectTrade confirm button'));
        await mounted.waitFor(() => panelOf('delivery-dt-5'), 'DirectTrade delivery panel');
        assert.equal(panelOf('delivery-w-5'), null, 'the warehouse dispatch with the same number is not touched');
        await fillAndCheck('delivery-dt-5');
        nextPost = async () => { throw new TypeError('network down'); };
        await submitPanel('delivery-dt-5');
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after lost response');
        assert.ok(posted[0].url.endsWith('/sales-orders/4/direct-trade-shipments/5/delivery-confirmations'));
        const frozen = Object.values(storedCommands())[0];
        assert.equal(frozen.key, 'confirm-delivery:4:direct-trade:5');

        await mounted.unmount();
        shipments = [shipment(deliveryResponse('DirectTrade', frozen.requestId))];
        mounted = new Mounted();
        await mounted.render(element(orderWith(7, false, true)));
        await mounted.waitFor(() => panelOf('delivery-dt-5'), 'panel reopened from the stored command');
        assert.ok(mounted.hasText('sales_delivery_replay_only'));
        assert.equal(mounted.button('sales_delivery_submit'), null);
        assert.equal(panelOf('delivery-dt-5')!.querySelector<HTMLInputElement>('#delivery-dt-5-confirm')!.disabled, true);

        nextPost = async () => json(200, deliveryResponse('DirectTrade', frozen.requestId));
        await click(mounted.button('supply_retry_same')!);
        assert.equal(posted.length, 2);
        assert.equal(posted[1].body, posted[0].body, 'identical requestId and body');
        assert.equal(posted[1].url, posted[0].url);
        await mounted.waitFor(() => (mounted.hasText('sales_notice_delivery_confirmed') ? true : null), 'notice');
        assert.ok(mounted.hasText('supply_saved_before'));
        assert.deepEqual(storedCommands(), {});
    });

    test('a 409 keeps the panel, offers reload only and clears the command', async () => {
        await openWarehouse(orderWith(6));
        await fillAndCheck('delivery-w-5');
        nextPost = async () => json(409, { message: 'Dispatch 5 is already confirmed as delivered.' });
        await submitPanel('delivery-w-5');
        await mounted.waitFor(() => (mounted.hasText('sales_conflict') ? true : null), 'conflict');
        assert.deepEqual(storedCommands(), {});
        await click(mounted.button('sales_reload_and_decide')!);
        assert.equal(reloads, 1);
    });

    test('a confirmed delivery shows "delivered (confirmed)" with date and reference and hides the correction and confirm buttons', async () => {
        dispatches = [dispatch(deliveryResponse('Warehouse', 'd-1'))];
        shipments = [shipment(deliveryResponse('DirectTrade', 'd-2'))];
        await mounted.render(element(orderWith(8, true, true)));
        await mounted.waitFor(() => (mounted.hasText('sales_delivery_recorded') ? true : null), 'delivery shown');
        assert.ok(mounted.hasText('sales_delivered_badge'));
        assert.ok(mounted.hasText('CMR-OK-1'));
        assert.ok(mounted.hasText('sales_delivery_correction_blocked'));
        assert.equal(confirmButtonIn(mounted.container), null);
        assert.equal(mounted.button('sales_correct_dispatch'), null, 'warehouse correction is blocked after delivery');
        assert.equal(mounted.button('sales_dt_correct'), null, 'DirectTrade take-back is blocked after delivery');
        assert.ok(mounted.hasText('sales_line_delivered'));
        assert.ok(mounted.hasText('sales_order_delivered'));
    });
});
