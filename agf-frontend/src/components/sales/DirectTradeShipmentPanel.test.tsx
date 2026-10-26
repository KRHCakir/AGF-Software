import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setValue, stubFetch, storedCommands } from '../../test/reactHarness.ts';
import { SalesOrder, StockReservation } from '../../types/salesOrder';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesOrderDetail from './SalesOrderDetail';

// DirectTrade-verzending door de echte componenten heen (SalesOrderDetail + paneel + context): geen POST zonder kg of zonder toelichting
// bij een afwijkende partij; de bevroren payload draagt hele hoeveelheid, werkelijke kg, partij en kalenderdatum; een verloren antwoord
// blijft herhaalbaar ook als de toewijzing al als verzonden terugkomt; 409 vereist herladen; de badge zegt nooit "niet afgeleverd".

const allocation = (id: number, status: 'Active' | 'Consumed', lot: string | null = 'LOT-DT-SRC', version = 0): StockReservation => ({
    id, requestId: `a-${id}`, salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: null, palletId: null, palletReference: null,
    warehouseLocation: null, lotNumber: lot, quantity: 20, unit: 'box', netWeightKg: null, source: 'DirectTrade',
    supplyCommitmentLineId: 11, supplyCommitmentId: 10, vendorName: 'Example Supplier', origin: 'Peru', destination: 'Rotterdam', expectedAt: '2026-10-27T22:00:00.000Z',
    directTradeShipmentId: status === 'Consumed' ? 5 : null, shippedOn: status === 'Consumed' ? '2026-10-19' : null,
    shippedNetWeightKg: status === 'Consumed' ? 81.4 : null, shippedLotNumber: status === 'Consumed' ? 'LOT-DT-SRC' : null, directTradeCorrectionId: null, deliveryConfirmationId: null, deliveredOn: null,
    status, createdAt: '2026-10-20T08:00:00.000Z', releasedAt: null, releaseReason: null, version, contentQualityStatus: null, onHold: false,
    consumedAt: status === 'Consumed' ? '2026-10-20T09:00:00.000Z' : null, dispatchId: null, dispatchReference: null, correctedAt: null, correctionId: null,
});

const orderWith = (version: number, reservations: StockReservation[]): SalesOrder => {
    const active = reservations.filter((r) => r.status === 'Active').reduce((sum, r) => sum + r.quantity, 0);
    const shipped = reservations.filter((r) => r.status === 'Consumed').reduce((sum, r) => sum + r.quantity, 0);
    return {
        id: 4, requestId: 'o-4', customerId: 6, customerName: 'SMOKE DT klant', status: 'Open', customerReference: null, internalNote: null,
        requestedDeliveryDate: '2026-10-26T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version,
        cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false,
        lines: [{
            id: 7, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', quantity: 40, unit: 'box', palletCount: null, packagesPerPallet: null,
            reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: active, dispatchedQuantity: shipped, dispatchedNetWeightKg: shipped > 0 ? 81.4 : 0,
            directTradeShippedQuantity: shipped, deliveredQuantity: 0, delivered: false, openQuantity: 40 - active - shipped, fulfilled: false, version: 2, reservations,
        }],
    };
};

const shipmentResponse = (requestId: string) => ({
    id: 5, requestId, salesOrderId: 4, customerId: 6, customerName: 'SMOKE DT klant', shippedOn: '2026-10-19', reference: 'CMR-1', actorReference: 'office-1',
    recordedAt: '2026-10-20T09:00:00.000Z', deliveryRecorded: false, delivery: null, deliveryHistory: [], correction: null,
    lines: [{ id: 1, stockReservationId: 21, salesOrderLineId: 7, supplyCommitmentLineId: 11, supplyCommitmentId: 10, vendorId: 1, vendorName: 'Example Supplier',
        origin: 'Peru', destination: 'Rotterdam', productName: 'Avocado', productVariantName: 'Hass', quantity: 20, unit: 'box', netWeightKg: 81.4,
        lotNumber: 'LOT-DT-SRC', sourceLotNumber: 'LOT-DT-SRC', lotDeviationReason: null }],
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

const form = () => mounted.container.querySelector<HTMLFormElement>('form[aria-labelledby="dt-ship-4-title"]')!;
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`#${id}`)!;
const submitShipment = () => act(async () => { form().dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

async function openAndSelect(order: SalesOrder) {
    await mounted.render(element(order));
    await click(mounted.buttons().find((b) => b.textContent?.includes('sales_ship_order'))!);
    await mounted.waitFor(() => form(), 'shipment panel');
    await click(field('dt-ship-4-r-21'));
}

describe('DirectTradeShipmentPanel through SalesOrderDetail', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        reloads = 0;
        shipments = [];
        nextPost = async () => json(500, { message: 'no handler' });
        posted = stubFetch({
            onGet: (url) => (url.includes('/direct-trade-shipments') ? json(200, shipments) : url.includes('/dispatches') ? json(200, []) : json(200, [])),
            nextPost: () => nextPost(),
        });
        mounted = new Mounted();
    });

    afterEach(async () => { await mounted.unmount(); });

    test('the button is separate from the warehouse dispatch; kg starts empty and nothing is posted without it', async () => {
        await openAndSelect(orderWith(3, [allocation(21, 'Active')]));
        assert.ok(mounted.button('sales_dispatch_order')?.disabled, 'warehouse dispatch has nothing to dispatch');
        assert.ok(mounted.hasText('sales_ship_warning_title'));
        assert.equal(field('dt-ship-4-kg-21').value, '', 'kg is never prefilled or derived');
        assert.equal(field('dt-ship-4-lot-21').value, 'LOT-DT-SRC', 'lot defaults to the source lot');
        assert.equal(field('dt-ship-4-date').value.length, 10, 'a calendar date is prefilled with today');

        await submitShipment();

        assert.equal(posted.length, 0);
        assert.deepEqual(storedCommands(), {});
        assert.ok(mounted.hasText('supply_error_decimal_empty') || mounted.container.querySelector('#dt-ship-4-kg-21-error') !== null, 'kg field error shown');
    });

    test('a deviating lot asks for a reason before anything is sent; with the reason both values are frozen', async () => {
        await openAndSelect(orderWith(3, [allocation(21, 'Active')]));
        await act(async () => {
            setValue(field('dt-ship-4-kg-21'), '81,4');
            setValue(field('dt-ship-4-lot-21'), 'LOT-ANDERS');
            setValue(field('dt-ship-4-date'), '2026-10-19');
        });
        assert.ok(mounted.hasText('sales_ship_lot_deviates'));
        await submitShipment();
        assert.equal(posted.length, 0, 'no POST without a reason for the deviating lot');
        assert.ok(mounted.hasText('sales_ship_lot_reason_required'));

        await act(async () => { setValue(field('dt-ship-4-reason-21'), 'Leverancier herpakt'); });
        nextPost = async () => json(201, shipmentResponse(JSON.parse(posted[posted.length - 1]?.body ?? '{}').requestId ?? 'x'));
        await submitShipment();
        assert.equal(posted.length, 1);
        const body = JSON.parse(posted[0].body);
        assert.ok(posted[0].url.endsWith('/sales-orders/4/direct-trade-shipments'));
        assert.deepEqual(body.lines, [{ stockReservationId: 21, expectedReservationVersion: 0, quantity: 20, unit: 'box', netWeightKg: 81.4, lotNumber: 'LOT-ANDERS', lotDeviationReason: 'Leverancier herpakt' }]);
        assert.equal(body.shippedOn, '2026-10-19', 'a calendar date without time');
        assert.equal(body.expectedOrderVersion, 3);
    });

    test('lost response: after refresh the allocation is shipped, the frozen shipment stays replay-only and the identical replay gets 200', async () => {
        await openAndSelect(orderWith(3, [allocation(21, 'Active')]));
        await act(async () => {
            setValue(field('dt-ship-4-kg-21'), '81,4');
            setValue(field('dt-ship-4-date'), '2026-10-19');
        });
        nextPost = async () => { throw new TypeError('network down'); };
        await submitShipment();
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after lost response');
        const frozen = Object.values(storedCommands())[0];
        assert.equal(frozen.kind, 'ship-direct-trade');

        // Verversen: de server had het al verwerkt; de toewijzing komt terug als verzonden.
        await mounted.unmount();
        shipments = [shipmentResponse(frozen.requestId)];
        mounted = new Mounted();
        await mounted.render(element(orderWith(4, [allocation(21, 'Consumed', 'LOT-DT-SRC', 1)])));
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'replay-only panel after refresh');
        assert.ok(mounted.hasText('sales_ship_replay_only'));
        assert.ok(mounted.hasText('sales_ship_badge'));
        assert.equal(mounted.hasText('not delivered') || mounted.hasText('niet afgeleverd'), false);

        nextPost = async () => json(200, shipmentResponse(frozen.requestId));
        await click(mounted.button('supply_retry_same')!);
        assert.equal(posted.length, 2);
        assert.equal(posted[1].body, posted[0].body, 'identical requestId and body');
        await mounted.waitFor(() => (mounted.hasText('sales_notice_dt_shipped') ? true : null), 'shipped notice');
        assert.ok(mounted.hasText('supply_saved_before'));
        assert.ok(mounted.hasText('sales_notice_dt_shipped_next'));
        assert.deepEqual(storedCommands(), {});
    });

    test('a 409 keeps the panel, offers reload only and clears the command', async () => {
        await openAndSelect(orderWith(3, [allocation(21, 'Active')]));
        await act(async () => { setValue(field('dt-ship-4-kg-21'), '81,4'); setValue(field('dt-ship-4-date'), '2026-10-19'); });
        nextPost = async () => json(409, { message: 'Only whole allocations can be shipped.' });
        await submitShipment();
        await mounted.waitFor(() => (mounted.hasText('sales_conflict') ? true : null), 'conflict');
        assert.ok(mounted.button('sales_reload_and_decide'));
        assert.equal(mounted.button('supply_retry_same'), null);
        assert.deepEqual(storedCommands(), {});
        await click(mounted.button('sales_reload_and_decide')!);
        assert.equal(reloads, 1);
    });

    test('a shipped allocation shows "shipped, delivery not yet recorded" with actual date, kg and lot; the warehouse dispatch list stays hidden', async () => {
        shipments = [shipmentResponse('s-1')];
        await mounted.render(element(orderWith(4, [allocation(21, 'Consumed')])));
        await mounted.waitFor(() => (mounted.hasText('sales_ship_heading') ? true : null), 'DirectTrade shipment list');
        assert.ok(mounted.hasText('sales_ship_badge'));
        assert.ok(mounted.hasText('sales_ship_shipped_on'));
        assert.ok(mounted.hasText('sales_ship_kg_actual'));
        assert.ok(mounted.hasText('sales_line_dt_shipped'));
        assert.equal(mounted.hasText('sales_dispatches_title'), false, 'no warehouse dispatch section for a DirectTrade-only order');
        assert.ok(mounted.buttons().find((b) => b.textContent?.includes('sales_ship_order'))?.disabled, 'nothing left to ship');
        assert.ok(mounted.button('sales_cancel_order')?.disabled, 'cancel is disabled after a shipment');
    });

    test('a fully shipped line with a DirectTrade part says "fully shipped, delivery not yet confirmed", never "delivered" or "written off"', async () => {
        const fulfilled = orderWith(5, [allocation(21, 'Consumed')]);
        fulfilled.lines[0] = { ...fulfilled.lines[0], quantity: 20, openQuantity: 0, fulfilled: true };
        fulfilled.fulfilled = true;
        shipments = [shipmentResponse('s-1')];
        await mounted.render(element(fulfilled));
        await mounted.waitFor(() => (mounted.hasText('sales_ship_heading') ? true : null), 'DirectTrade shipment list');
        assert.ok(mounted.hasText('sales_line_shipped_not_confirmed'));
        assert.ok(mounted.hasText('sales_order_shipped_not_confirmed'));
        assert.equal(mounted.hasText('sales_line_delivered'), false);
        assert.equal(mounted.hasText('sales_order_delivered'), false);
        assert.ok(mounted.hasText('sales_ship_badge'), 'the shipment shows shipped, never delivered, until confirmed');
        assert.ok(mounted.hasText('sales_reservation_dt_shipped'));
        assert.equal(mounted.hasText('sales_reservation_consumed'), false, 'a DirectTrade row is never labelled as written off from stock');
    });
});

