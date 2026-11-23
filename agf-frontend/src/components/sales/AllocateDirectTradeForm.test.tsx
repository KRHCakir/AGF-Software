import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, stubFetch } from '../../test/reactHarness.ts';
import { SalesOrder, SalesOrderLine, StockReservation } from '../../types/salesOrder';
import OrderLinePanel from './OrderLinePanel';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesOrderDetail from './SalesOrderDetail';

// Klantgebonden DirectTrade-toewijzingen door de echte componenten heen (OrderLinePanel + context): een bestaande toewijzing toont nooit als
// gereed en is niet verzendbaar; een nieuwe starten kan sinds feedback 22 november niet meer.
// Zonder LanguageProvider geeft t() de sleutel terug; daar wordt op gezocht.

const line: SalesOrderLine = {
    id: 7, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', quantity: 40, unit: 'box', palletCount: null, packagesPerPallet: null,
    reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, directTradeShippedQuantity: 0, deliveredQuantity: 0, delivered: false, openQuantity: 40, fulfilled: false, version: 0, reservations: [],
};
const order: SalesOrder = {
    id: 4, requestId: 'o-4', customerId: 6, customerName: 'SMOKE DT klant', status: 'Open', customerReference: null, internalNote: null,
    requestedDeliveryDate: '2026-10-26T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-20T08:00:00.000Z', version: 0,
    cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false, lines: [line],
};
const allocationResponse = (requestId: string, quantity = 10): StockReservation => ({
    id: 21, requestId, salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: null, palletId: null, palletReference: null,
    warehouseLocation: null, lotNumber: 'LOT-DT-1', quantity, unit: 'box', netWeightKg: null, source: 'DirectTrade',
    supplyCommitmentLineId: 2, supplyCommitmentId: 10, vendorName: 'Example Supplier', origin: 'Peru', destination: 'Rotterdam', expectedAt: '2026-10-27T22:00:00.000Z', directTradeShipmentId: null, shippedOn: null, shippedNetWeightKg: null, shippedLotNumber: null, directTradeCorrectionId: null, deliveryConfirmationId: null, deliveredOn: null,
    status: 'Active', createdAt: '2026-10-20T08:05:00.000Z', releasedAt: null, releaseReason: null, version: 0, contentQualityStatus: null, onHold: false,
    consumedAt: null, dispatchId: null, dispatchReference: null, correctedAt: null, correctionId: null,
});
const physicalReservation: StockReservation = {
    id: 22, requestId: 'r-22', salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: 'PAL-2',
    warehouseLocation: 'A-01', lotNumber: 'LOT-A', quantity: 20, unit: 'box', netWeightKg: 80, source: 'Physical',
    supplyCommitmentLineId: 1, supplyCommitmentId: 9, vendorName: 'Example Supplier', origin: 'Peru', destination: null, expectedAt: '2026-10-21T22:00:00.000Z', directTradeShipmentId: null, shippedOn: null, shippedNetWeightKg: null, shippedLotNumber: null, directTradeCorrectionId: null, deliveryConfirmationId: null, deliveredOn: null,
    status: 'Active', createdAt: '2026-10-20T08:06:00.000Z', releasedAt: null, releaseReason: null, version: 0, contentQualityStatus: 'Approved', onHold: false,
    consumedAt: null, dispatchId: null, dispatchReference: null, correctedAt: null, correctionId: null,
};

let mounted: Mounted;
let orderChanged: number;

const panel = (current: SalesOrder, currentLine: SalesOrderLine) => (
    <PendingCommandsProvider>
        <HydratedGate>
            <OrderLinePanel order={current} line={currentLine} readOnly={false} onOrderChanged={async () => { orderChanged += 1; return true; }} />
        </HydratedGate>
    </PendingCommandsProvider>
);

// Feedback 22 november: een nieuwe klantgebonden toewijzing starten kan niet meer (directe handel is een gewone inkooporder met een locatie
// Onderweg); een bestaande toewijzing blijft in de order zichtbaar en wordt verwerkt zoals altijd.
describe('an allocation in the order', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        orderChanged = 0;
        stubFetch({ onGet: (url) => (url.includes('/direct-trade-supply') ? json(200, []) : json(404, { message: url })), nextPost: async () => json(500, {}) });
        mounted = new Mounted();
    });

    afterEach(async () => { await mounted.unmount(); });

    test('there is no button to start a new customer-bound allocation any more; FIFO and choosing stock stay', async () => {
        await mounted.render(panel(order, line));
        await mounted.waitFor(() => mounted.container.querySelector('[data-tour="sales-reserve-fifo"]'), 'line actions');
        assert.equal(mounted.buttons().filter((b) => b.textContent?.includes('sales_allocate_direct_trade')).length, 0);
        assert.ok(mounted.container.querySelector('[data-tour="sales-choose-stock"]'));
        assert.equal(orderChanged, 0);
    });

    test('a line fully committed with 20 expected and 20 physical is complete but not ready, shows no kg for the allocation, and the order cannot dispatch the allocation', async () => {
        const allocation = allocationResponse('a-1', 20);
        const committedLine: SalesOrderLine = { ...line, reservedQuantity: 20, reservedNetWeightKg: 80, allocatedQuantity: 20, openQuantity: 0, reservations: [allocation, physicalReservation] };
        const committedOrder: SalesOrder = { ...order, lines: [committedLine] };
        await mounted.render(
            <PendingCommandsProvider>
                <HydratedGate>
                    <SalesOrderDetail order={committedOrder} onOrderChanged={async () => true} />
                </HydratedGate>
            </PendingCommandsProvider>,
        );

        const status = Array.from(mounted.container.querySelectorAll('header span.font-medium')).map((span) => span.textContent ?? '');
        assert.ok(status.includes('sales_line_complete_expected'), `line status must say complete-with-expected, got ${status.join(' | ')}`);
        assert.ok(!status.includes('sales_line_complete'), 'never the green "fully reserved" state');
        assert.ok(mounted.hasText('sales_allocation_badge'));
        assert.ok(mounted.hasText('sales_allocation_no_kg'));
        assert.ok(mounted.hasText('sales_allocation_not_applicable'));
        assert.ok(mounted.hasText('sales_order_allocated_notice'));
        assert.ok(mounted.hasText('sales_order_complete_expected'));
        assert.ok(mounted.hasText('sales_line_allocated'));

        // Verzenden kan (er is een fysieke reservering), maar de toewijzing staat niet in de keuzelijst.
        const dispatchButton = mounted.button('sales_dispatch_order');
        assert.ok(dispatchButton && !dispatchButton.disabled, 'physical reservation is dispatchable');
        await click(dispatchButton!);
        await mounted.waitFor(() => (mounted.hasText('sales_dispatch_select') ? true : null), 'dispatch panel');
        assert.ok(mounted.hasText('sales_dispatch_allocations_excluded'));
        const checkboxes = Array.from(mounted.container.querySelectorAll<HTMLInputElement>('form input[type="checkbox"]'));
        assert.equal(checkboxes.length, 1, 'only the physical reservation is selectable');
        assert.ok(checkboxes[0].closest('li')?.textContent?.includes('PAL-2'), 'the selectable row is the physical reservation');
    });

    test('an order with only an allocation has nothing to dispatch', async () => {
        const allocation = allocationResponse('a-1', 20);
        const allocatedLine: SalesOrderLine = { ...line, allocatedQuantity: 20, openQuantity: 20, reservations: [allocation] };
        await mounted.render(
            <PendingCommandsProvider>
                <HydratedGate>
                    <SalesOrderDetail order={{ ...order, lines: [allocatedLine] }} onOrderChanged={async () => true} />
                </HydratedGate>
            </PendingCommandsProvider>,
        );
        const dispatchButton = mounted.button('sales_dispatch_order');
        assert.ok(dispatchButton?.disabled, 'an allocation of expected supply is never dispatchable from the warehouse');
        assert.ok(mounted.buttons().some((b) => b.getAttribute('aria-label') === 'sales_release_allocation_label'), 'the allocation can be released');
    });
});
