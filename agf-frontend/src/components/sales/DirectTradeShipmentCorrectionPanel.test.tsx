import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setValue, stubFetch, storedCommands } from '../../test/reactHarness.ts';
import { SalesOrder, StockReservation } from '../../types/salesOrder';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesOrderDetail from './SalesOrderDetail';

// Terugnemen van een DirectTrade-verzending door de echte componenten heen (SalesOrderDetail + lijst + paneel + context): alle regels
// zichtbaar, geen POST zonder reden of verklaring, bevroren payload met alle toewijzingen en versies, verloren antwoord → replay-only na
// verversen → identieke replay 200, 409 → herladen, en daarna historie met origineel en correctie, ordervraag open, annuleren mogelijk.

const allocation = (id: number, status: 'Consumed' | 'Corrected', version = 1): StockReservation => ({
    id, requestId: `a-${id}`, salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: null, palletId: null, palletReference: null,
    warehouseLocation: null, lotNumber: 'LOT-DT-SRC', quantity: 10, unit: 'box', netWeightKg: null, source: 'DirectTrade',
    supplyCommitmentLineId: 11, supplyCommitmentId: 10, vendorName: 'Example Supplier', origin: 'Peru', destination: 'Rotterdam', expectedAt: '2026-10-27T22:00:00.000Z',
    directTradeShipmentId: 5, shippedOn: '2026-10-19', shippedNetWeightKg: 40.5, shippedLotNumber: 'LOT-DT-SRC', directTradeCorrectionId: status === 'Corrected' ? 3 : null, deliveryConfirmationId: null, deliveredOn: null,
    status, createdAt: '2026-10-20T08:00:00.000Z', releasedAt: null, releaseReason: null, version, contentQualityStatus: null, onHold: false,
    consumedAt: '2026-10-20T09:00:00.000Z', dispatchId: null, dispatchReference: null, correctedAt: status === 'Corrected' ? '2026-10-20T10:00:00.000Z' : null, correctionId: null,
});

const orderWith = (version: number, reservations: StockReservation[]): SalesOrder => {
    const shipped = reservations.filter((r) => r.status === 'Consumed').reduce((sum, r) => sum + r.quantity, 0);
    return {
        id: 4, requestId: 'o-4', customerId: 6, customerName: 'SMOKE DT klant', status: 'Open', customerReference: null, internalNote: null,
        requestedDeliveryDate: '2026-10-26T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version,
        cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: shipped === 20, delivered: false,
        lines: [{
            id: 7, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', quantity: 20, unit: 'box', palletCount: null, packagesPerPallet: null,
            reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: 0, dispatchedQuantity: shipped, dispatchedNetWeightKg: shipped === 0 ? 0 : 81,
            directTradeShippedQuantity: shipped, deliveredQuantity: 0, delivered: false, openQuantity: 20 - shipped, fulfilled: shipped === 20, version: 3, reservations,
        }],
    };
};

const correctionResponse = (requestId: string) => ({
    id: 3, requestId, directTradeShipmentId: 5, salesOrderId: 4, reason: 'Vervoerder heeft niet geladen', actorReference: 'office-1',
    shipmentDidNotTakePlaceConfirmed: true, confirmationCode: 'SHIPMENT_DID_NOT_TAKE_PLACE', confirmationStatement: 'Deze geregistreerde verzending heeft niet plaatsgevonden.',
    correctedAt: '2026-10-20T10:00:00.000Z',
    lines: [21, 22].map((id, i) => ({ id: i + 1, directTradeShipmentLineId: i + 1, stockReservationId: id, supplyCommitmentLineId: 11, salesOrderLineId: 7, quantity: 10, unit: 'box', netWeightKg: 40.5 })),
});

const shipment = (correction: unknown = null) => ({
    id: 5, requestId: 's-5', salesOrderId: 4, customerId: 6, customerName: 'SMOKE DT klant', shippedOn: '2026-10-19', reference: 'CMR-1', actorReference: 'office-1',
    recordedAt: '2026-10-20T09:00:00.000Z', deliveryRecorded: false, delivery: null, deliveryHistory: [], correction,
    lines: [21, 22].map((id, i) => ({ id: i + 1, stockReservationId: id, salesOrderLineId: 7, supplyCommitmentLineId: 11, supplyCommitmentId: 10, vendorId: 1, vendorName: 'Example Supplier',
        origin: 'Peru', destination: 'Rotterdam', productName: 'Avocado', productVariantName: 'Hass', quantity: 10, unit: 'box', netWeightKg: 40.5,
        lotNumber: 'LOT-DT-SRC', sourceLotNumber: 'LOT-DT-SRC', lotDeviationReason: null })),
});

let mounted: Mounted;
let posted: { url: string; body: string }[];
let nextPost: () => Promise<Response>;
let reloads: number;
let shipments: unknown[];

const element = (order: SalesOrder) => (
    <PendingCommandsProvider>
        <HydratedGate>
            <SalesOrderDetail order={order} actor="office-1" onOrderChanged={async () => { reloads += 1; return true; }} />
        </HydratedGate>
    </PendingCommandsProvider>
);
const panel = () => mounted.container.querySelector<HTMLFormElement>('form[aria-labelledby="dt-correct-5-title"]');
const submitPanel = () => act(async () => { panel()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

async function openPanel(order: SalesOrder) {
    await mounted.render(element(order));
    const button = await mounted.waitFor(() => mounted.button('sales_dt_correct'), 'take-back button');
    await click(button);
    await mounted.waitFor(() => panel(), 'take-back panel');
}

describe('DirectTradeShipmentCorrectionPanel through SalesOrderDetail', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        reloads = 0;
        shipments = [shipment()];
        nextPost = async () => json(500, { message: 'no handler' });
        posted = stubFetch({ onGet: (url) => (url.includes('/direct-trade-shipments') ? json(200, shipments) : json(200, [])), nextPost: () => nextPost() });
        mounted = new Mounted();
    });

    afterEach(async () => { await mounted.unmount(); });

    test('all lines are listed; nothing is posted without a reason and the unchecked statement', async () => {
        await openPanel(orderWith(4, [allocation(21, 'Consumed'), allocation(22, 'Consumed')]));
        assert.equal(panel()!.querySelectorAll('li').length >= 2, true, 'both shipment lines are listed');
        const checkbox = mounted.container.querySelector<HTMLInputElement>('#dt-correct-5-confirm')!;
        assert.equal(checkbox.checked, false, 'the statement is never pre-checked');
        assert.ok(mounted.hasText('sales_dt_correct_confirm_label'));
        assert.ok(mounted.hasText('sales_dt_correct_effect_no_stock'));

        await submitPanel();
        assert.equal(posted.length, 0);
        await act(async () => { setValue(mounted.container.querySelector<HTMLTextAreaElement>('#dt-correct-5-reason')!, 'Vervoerder heeft niet geladen'); });
        await submitPanel();
        assert.equal(posted.length, 0, 'still nothing without the statement');
        assert.ok(mounted.hasText('sales_dt_correct_confirm_required'));
        assert.deepEqual(storedCommands(), {});
    });

    test('lost response: after refresh the shipment is taken back, the panel stays replay-only and the identical replay gets 200', async () => {
        await openPanel(orderWith(4, [allocation(21, 'Consumed'), allocation(22, 'Consumed')]));
        await act(async () => { setValue(mounted.container.querySelector<HTMLTextAreaElement>('#dt-correct-5-reason')!, 'Vervoerder heeft niet geladen'); });
        await click(mounted.container.querySelector<HTMLInputElement>('#dt-correct-5-confirm')!);
        nextPost = async () => { throw new TypeError('network down'); };
        await submitPanel();
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after lost response');
        assert.equal(posted.length, 1);
        const body = JSON.parse(posted[0].body);
        assert.ok(posted[0].url.endsWith('/sales-orders/4/direct-trade-shipments/5/corrections'));
        assert.deepEqual(body.lines, [{ stockReservationId: 21, expectedReservationVersion: 1 }, { stockReservationId: 22, expectedReservationVersion: 1 }]);
        assert.equal(body.shipmentDidNotTakePlace, true);
        assert.equal(body.expectedOrderVersion, 4);
        const frozen = Object.values(storedCommands())[0];
        assert.equal(frozen.kind, 'correct-dt-shipment');

        await mounted.unmount();
        shipments = [shipment(correctionResponse(frozen.requestId))];
        mounted = new Mounted();
        await mounted.render(element(orderWith(5, [allocation(21, 'Corrected', 2), allocation(22, 'Corrected', 2)])));
        await mounted.waitFor(() => panel(), 'panel reopened from the stored command');
        assert.ok(mounted.hasText('sales_dt_correct_replay_only'));
        assert.equal(mounted.button('sales_dt_correct_submit'), null);

        nextPost = async () => json(200, correctionResponse(frozen.requestId));
        await click(mounted.button('supply_retry_same')!);
        assert.equal(posted.length, 2);
        assert.equal(posted[1].body, posted[0].body, 'identical requestId and body');
        await mounted.waitFor(() => (mounted.hasText('sales_notice_dt_corrected') ? true : null), 'take-back notice');
        assert.ok(mounted.hasText('supply_saved_before'));
        assert.deepEqual(storedCommands(), {});
    });

    test('a 409 keeps the panel, offers reload only and clears the command', async () => {
        await openPanel(orderWith(4, [allocation(21, 'Consumed'), allocation(22, 'Consumed')]));
        await act(async () => { setValue(mounted.container.querySelector<HTMLTextAreaElement>('#dt-correct-5-reason')!, 'x'); });
        await click(mounted.container.querySelector<HTMLInputElement>('#dt-correct-5-confirm')!);
        nextPost = async () => json(409, { message: 'DirectTrade shipment 5 has already been taken back.' });
        await submitPanel();
        await mounted.waitFor(() => (mounted.hasText('sales_conflict') ? true : null), 'conflict');
        assert.ok(mounted.button('sales_reload_and_decide'));
        assert.deepEqual(storedCommands(), {});
        await click(mounted.button('sales_reload_and_decide')!);
        assert.equal(reloads, 1);
    });

    test('after a full take-back the history shows original and correction, demand is open and cancel is available', async () => {
        shipments = [shipment(correctionResponse('c-1'))];
        await mounted.render(element(orderWith(5, [allocation(21, 'Corrected', 2), allocation(22, 'Corrected', 2)])));
        await mounted.waitFor(() => (mounted.hasText('sales_dt_corrected_by') ? true : null), 'correction in history');
        assert.ok(mounted.hasText('sales_dt_corrected_badge'));
        assert.ok(mounted.hasText('Deze geregistreerde verzending heeft niet plaatsgevonden.'), 'the recorded statement is shown');
        assert.ok(mounted.hasText('Vervoerder heeft niet geladen'));
        assert.equal(mounted.button('sales_dt_correct'), null, 'a shipment can be taken back only once');
        assert.ok(mounted.hasText('sales_reservation_dt_corrected'));
        assert.ok(mounted.hasText('sales_dt_corrected_was_shipped'));
        assert.equal(mounted.hasText('sales_line_dt_shipped'), false, 'taken-back quantity no longer counts as shipped');
        assert.ok(mounted.hasText('sales_line_open'));
        assert.equal(mounted.button('sales_cancel_order')?.disabled, false, 'cancel is possible again');
        assert.equal(mounted.hasText('sales_dispatches_title'), false, 'no warehouse dispatch section');
    });
});
