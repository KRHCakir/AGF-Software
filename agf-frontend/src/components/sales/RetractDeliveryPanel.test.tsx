import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setValue, stubFetch, storedCommands } from '../../test/reactHarness.ts';
import { SalesOrder, StockReservation } from '../../types/salesOrder';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesOrderDetail from './SalesOrderDetail';

// Intrekken door de echte componenten heen (SalesOrderDetail + beide lijsten + paneel + context). Effecten vooraf, geen POST zonder reden
// of verklaring, waarschuwing bij de checkbox, verloren antwoord en refresh: replay-only met identieke replay (200), 409 herladen, en na
// intrekken: historie met de ingetrokken bevestiging, knoppen voor opnieuw bevestigen en corrigeren terug, regel niet meer afgeleverd.

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
        id: 4, requestId: 'o-4', customerId: 6, customerName: 'SMOKE klant', status: 'Open', customerReference: null,
        requestedDeliveryDate: '2026-10-26T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version,
        cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: true, delivered: delivered === 20,
        lines: [{
            id: 7, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', quantity: 20, unit: 'box',
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
    lines: [{ id: 20, stockReservationId: 11, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: 'PAL-11', lotNumber: 'LOT-11', productName: 'Avocado', productVariantName: 'Hass', quantity: 10, unit: 'box', netWeightKg: 40, stockMovementId: 30 }],
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

const retractionResponse = (kind: 'Warehouse' | 'DirectTrade', requestId: string, confirmationId: number) => ({
    id: 70 + confirmationId, requestId, shipmentDeliveryConfirmationId: confirmationId, shipmentKind: kind, salesDispatchId: kind === 'Warehouse' ? 5 : null,
    directTradeShipmentId: kind === 'DirectTrade' ? 5 : null, salesOrderId: 4, reason: 'Verkeerde verzending aangeklikt', actorReference: 'office-1',
    recordedInErrorConfirmed: true, confirmationCode: 'DELIVERY_CONFIRMATION_RECORDED_IN_ERROR', confirmationStatement: 'Deze afleverbevestiging is ten onrechte vastgelegd.',
    recordedAt: '2026-10-20T11:00:00.000Z',
});
const retractedConfirmation = (kind: 'Warehouse' | 'DirectTrade', requestId = 'x') => ({ ...deliveryResponse(kind, `c-${kind}`), retraction: retractionResponse(kind, requestId, kind === 'Warehouse' ? 1 : 2) });
const historyDispatch = (history: unknown[]) => ({ ...dispatch(null), delivery: null, deliveryHistory: history });
const historyShipment = (history: unknown[]) => ({ ...shipment(null), deliveryRecorded: false, delivery: null, deliveryHistory: history });
const retractButton = (root: ParentNode | null) => Array.from(root?.querySelectorAll<HTMLButtonElement>('button') ?? []).find((b) => b.textContent === 'sales_retract_open') ?? null;
const dtRetractButton = () => Array.from(mounted.container.querySelectorAll<HTMLButtonElement>('button')).find((b) => b.textContent === 'sales_retract_open' && !warehouseSection()?.contains(b)) ?? null;

async function openWarehouseRetract() {
    await mounted.render(element(orderWith(8, true, true)));
    await click(await mounted.waitFor(() => retractButton(warehouseSection()), 'warehouse retract button'));
    return mounted.waitFor(() => panelOf('retract-w-5'), 'warehouse retract panel');
}

describe('RetractDeliveryPanel through SalesOrderDetail', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        reloads = 0;
        dispatches = [dispatch(deliveryResponse('Warehouse', 'c-Warehouse'))];
        shipments = [shipment(deliveryResponse('DirectTrade', 'c-DirectTrade'))];
        nextPost = async () => json(500, { message: 'no handler' });
        posted = stubFetch({
            onGet: (url) => (url.includes('/direct-trade-shipments') ? json(200, shipments) : url.includes('/dispatches') ? json(200, dispatches) : json(200, [])),
            nextPost: () => nextPost(),
        });
        mounted = new Mounted();
    });

    afterEach(async () => { await mounted.unmount(); });

    test('effects are shown up front; nothing is posted without a reason and the unchecked statement, whose warning sits right next to it', async () => {
        const panel = await openWarehouseRetract();
        const effects = panel.querySelector('[data-testid="retract-w-5-effects"]')!;
        for (const key of ['sales_retract_effect_kept', 'sales_retract_effect_status', 'sales_retract_effect_delivered', 'sales_retract_effect_no_stock', 'sales_retract_effect_next']) {
            assert.ok(effects.textContent?.includes(key), key);
        }
        const checkbox = panel.querySelector<HTMLInputElement>('#retract-w-5-confirm')!;
        assert.equal(checkbox.checked, false);
        const statement = panel.querySelector('[data-testid="retract-w-5-statement"]')!;
        assert.ok(statement.textContent?.includes('sales_retract_confirm_label') && statement.textContent?.includes('sales_retract_warning'));
        assert.equal(checkbox.getAttribute('aria-describedby'), 'retract-w-5-warning');
        assert.equal(panelOf('retract-dt-5'), null, 'the DirectTrade shipment with the same number is not touched');

        await submitPanel('retract-w-5');
        assert.equal(posted.length, 0);
        await act(async () => { setValue(panel.querySelector<HTMLTextAreaElement>('#retract-w-5-reason')!, 'Verkeerde verzending aangeklikt'); });
        await submitPanel('retract-w-5');
        assert.equal(posted.length, 0, 'still nothing without the statement');
        assert.ok(mounted.hasText('sales_retract_confirm_required'));
        assert.deepEqual(storedCommands(), {});
    });

    test('lost response: after refresh the confirmation is retracted, the panel is replay-only and the identical replay gets 200', async () => {
        await mounted.render(element(orderWith(8, true, true)));
        await click(await mounted.waitFor(() => dtRetractButton(), 'DirectTrade retract button'));
        const panel = await mounted.waitFor(() => panelOf('retract-dt-5'), 'DirectTrade retract panel');
        await act(async () => { setValue(panel.querySelector<HTMLTextAreaElement>('#retract-dt-5-reason')!, 'Verkeerde verzending aangeklikt'); });
        await click(panel.querySelector<HTMLInputElement>('#retract-dt-5-confirm')!);
        nextPost = async () => { throw new TypeError('network down'); };
        await submitPanel('retract-dt-5');
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after lost response');
        assert.ok(posted[0].url.endsWith('/sales-orders/4/direct-trade-shipments/5/delivery-confirmations/2/retractions'));
        const body = JSON.parse(posted[0].body);
        assert.deepEqual({ ...body, requestId: 'x' }, { requestId: 'x', expectedOrderVersion: 8, actorReference: 'office-1', reason: 'Verkeerde verzending aangeklikt', recordedInError: true });
        const frozen = Object.values(storedCommands())[0];
        assert.equal(frozen.key, 'retract-delivery:4:direct-trade:5:2');

        await mounted.unmount();
        shipments = [historyShipment([retractedConfirmation('DirectTrade', frozen.requestId)])];
        mounted = new Mounted();
        await mounted.render(element(orderWith(9, true, false)));
        await mounted.waitFor(() => panelOf('retract-dt-5'), 'panel reopened from the stored command');
        assert.ok(mounted.hasText('sales_retract_replay_only'));
        assert.equal(mounted.button('sales_retract_submit'), null);
        assert.equal(panelOf('retract-dt-5')!.querySelector<HTMLTextAreaElement>('#retract-dt-5-reason')!.disabled, true);

        nextPost = async () => json(200, retractionResponse('DirectTrade', frozen.requestId, 2));
        await click(mounted.button('supply_retry_same')!);
        assert.equal(posted.length, 2);
        assert.equal(posted[1].body, posted[0].body, 'identical requestId and body');
        assert.equal(posted[1].url, posted[0].url);
        await mounted.waitFor(() => (mounted.hasText('sales_notice_delivery_retracted') ? true : null), 'retract notice');
        assert.ok(mounted.hasText('supply_saved_before'));
        assert.deepEqual(storedCommands(), {});
    });

    test('a 409 keeps the panel, offers reload only and clears the command', async () => {
        const panel = await openWarehouseRetract();
        await act(async () => { setValue(panel.querySelector<HTMLTextAreaElement>('#retract-w-5-reason')!, 'x'); });
        await click(panel.querySelector<HTMLInputElement>('#retract-w-5-confirm')!);
        nextPost = async () => json(409, { message: 'Delivery confirmation 1 has already been retracted.' });
        await submitPanel('retract-w-5');
        await mounted.waitFor(() => (mounted.hasText('sales_conflict') ? true : null), 'conflict');
        assert.deepEqual(storedCommands(), {});
        await click(mounted.button('sales_reload_and_decide')!);
        assert.equal(reloads, 1);
    });

    test('after retraction: the history keeps the retracted confirmation, confirm and correct are offered again and nothing counts as delivered', async () => {
        dispatches = [historyDispatch([retractedConfirmation('Warehouse')])];
        shipments = [historyShipment([retractedConfirmation('DirectTrade')])];
        await mounted.render(element(orderWith(9)));
        await mounted.waitFor(() => confirmButtonIn(warehouseSection()), 'confirm button back');
        assert.ok(mounted.container.querySelectorAll('[data-testid="delivery-history"]').length === 2, 'both shipments show their retracted confirmation');
        assert.ok(mounted.hasText('sales_delivery_history_entry') && mounted.hasText('sales_delivery_history_retracted') && mounted.hasText('Verkeerde verzending aangeklikt'));
        assert.equal(retractButton(mounted.container), null, 'a retracted confirmation cannot be retracted again');
        assert.ok(mounted.button('sales_correct_dispatch'), 'warehouse correction is possible again');
        assert.ok(mounted.button('sales_dt_correct'), 'DirectTrade take-back is possible again');
        assert.equal(mounted.hasText('sales_delivered_badge'), false, 'no delivered badge from a historical confirmation');
        assert.ok(mounted.hasText('sales_line_shipped_not_confirmed'));
    });
});
