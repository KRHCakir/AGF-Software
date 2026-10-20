import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, deferred, json, setValue, stubFetch, storedCommands, submit } from '../../test/reactHarness.ts';
import { SalesOrder, SalesOrderLine, StockReservation } from '../../types/salesOrder';
import OrderLinePanel from './OrderLinePanel';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesOrderDetail from './SalesOrderDetail';

// Toewijzen van verwachte, klantgebonden DirectTrade-aanvoer door de echte componenten heen (OrderLinePanel + picker + formulier
// + context): zonder bevestiging gaat er niets naar de server; de bevroren payload draagt de bevestiging en geen kg; een verloren
// antwoord blijft herhaalbaar; een 409 vereist herladen; een toewijzing toont nooit als gereed en is niet verzendbaar.
// Zonder LanguageProvider geeft t() de sleutel terug; daar wordt op gezocht.

const line: SalesOrderLine = {
    id: 7, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', quantity: 40, unit: 'box',
    reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, directTradeShippedQuantity: 0, deliveredQuantity: 0, delivered: false, openQuantity: 40, fulfilled: false, version: 0, reservations: [],
};
const order: SalesOrder = {
    id: 4, requestId: 'o-4', customerId: 6, customerName: 'SMOKE DT klant', status: 'Open', customerReference: null,
    requestedDeliveryDate: '2026-10-26T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-20T08:00:00.000Z', version: 0,
    cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false, lines: [line],
};
const supplyRow = {
    supplyCommitmentLineId: 2, supplyCommitmentId: 10, customerId: 6, customerName: 'SMOKE DT klant', vendorName: 'Example Supplier', origin: 'Peru', destination: 'Rotterdam',
    commitmentStatus: 'Expected', expectedAt: '2026-10-27T22:00:00.000Z', lotNumber: 'LOT-DT-1', unit: 'box', expectedQuantity: 30, declaredExpectedNetWeightKg: 120,
    allocatedQuantity: 0, remainingQuantity: 30, allocatedToThisLineQuantity: 0, sourceLineVersion: 0,
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
let posted: { url: string; body: string }[];
let nextPost: () => Promise<Response>;
let orderChanged: number;

const panel = (current: SalesOrder, currentLine: SalesOrderLine) => (
    <PendingCommandsProvider>
        <HydratedGate>
            <OrderLinePanel order={current} line={currentLine} actor="office-1" readOnly={false} onOrderChanged={async () => { orderChanged += 1; return true; }} />
        </HydratedGate>
    </PendingCommandsProvider>
);

async function openAllocatorAndFill(quantity = '10'): Promise<{ form: HTMLFormElement; checkbox: HTMLInputElement }> {
    await click(await mounted.waitFor(() => mounted.button('🔒 sales_allocate_direct_trade') ?? mounted.buttons().find((b) => b.textContent?.includes('sales_allocate_direct_trade')), 'allocate button'));
    await click(await mounted.waitFor(() => mounted.button('supply_select'), 'supply row select button'));
    const form = await mounted.waitFor(() => mounted.form(), 'allocation form');
    const inputs = Array.from(form.querySelectorAll<HTMLInputElement>('input[inputmode="decimal"]'));
    assert.equal(inputs.length, 1, 'quantity only: no kg input exists for expected supply');
    await act(async () => { setValue(inputs[0], quantity); });
    const checkbox = form.querySelector<HTMLInputElement>('input[type="checkbox"]')!;
    assert.equal(checkbox.checked, false, 'the acknowledgement is never pre-checked');
    return { form, checkbox };
}

describe('AllocateDirectTradeForm through OrderLinePanel', () => {
    beforeEach(async () => {
        window.sessionStorage.clear();
        orderChanged = 0;
        nextPost = async () => json(500, { message: 'no post handler' });
        posted = stubFetch({
            onGet: (url) => {
                if (url.includes('/direct-trade-supply')) return json(200, [supplyRow]);
                if (url.includes('/reservable-stock')) return json(200, []);
                if (url.includes('/availability')) return json(503, { message: 'availability unavailable in test' });
                return json(404, { message: `unexpected GET ${url}` });
            },
            nextPost: () => nextPost(),
        });
        mounted = new Mounted();
        await mounted.render(panel(order, line));
    });

    afterEach(async () => { await mounted.unmount(); });

    test('the picker shows the warning with the expected date, declared kg is labelled as the whole line, and nothing is posted without the acknowledgement', async () => {
        const { form, checkbox } = await openAllocatorAndFill();
        assert.ok(mounted.hasText('sales_allocate_warning_title'));
        assert.ok(mounted.hasText('sales_allocate_warning'));
        assert.ok(mounted.hasText('sales_allocate_date_warning'), 'expected 2026-10-28 is after requested 2026-10-27');
        assert.ok(mounted.hasText('sales_declared_kg_whole_line'));
        assert.ok(mounted.hasText('sales_allocation_badge'));

        await submit(form);

        assert.equal(posted.length, 0, 'no POST without the acknowledgement');
        assert.ok(mounted.hasText('sales_allocate_acknowledge_required'));
        assert.deepEqual(storedCommands(), {}, 'nothing is frozen without the acknowledgement');
        assert.equal(checkbox.disabled, false);
    });

    test('with the acknowledgement the frozen payload carries acknowledgeExpectedSupply true and no kg; a 201 renders the allocation notice', async () => {
        const { form, checkbox } = await openAllocatorAndFill();
        await click(checkbox);
        assert.equal(checkbox.checked, true);
        const response = deferred<Response>();
        nextPost = () => response.promise;

        await submit(form);

        assert.equal(posted.length, 1);
        assert.ok(posted[0].url.endsWith('/sales-orders/4/lines/7/direct-trade-allocations'));
        const body = JSON.parse(posted[0].body);
        assert.equal(body.acknowledgeExpectedSupply, true);
        assert.equal(body.quantity, 10);
        assert.equal(body.unit, 'box');
        assert.equal(body.supplyCommitmentLineId, 2);
        assert.equal('netWeightKg' in body, false, 'an allocation never carries kg');
        const command = Object.values(storedCommands())[0];
        assert.equal(command?.kind, 'allocate-direct-trade');
        assert.equal(body.requestId, command.requestId);
        assert.equal(form.getAttribute('aria-busy'), 'true');

        response.resolve(json(201, allocationResponse(command.requestId)));
        await mounted.waitFor(() => (mounted.hasText('sales_notice_allocated') ? true : null), 'allocation notice');
        assert.deepEqual(storedCommands(), {});
        assert.equal(orderChanged, 1);
    });

    test('a lost response keeps the frozen command; retry resends the identical body and a 200 replay completes it', async () => {
        const { form, checkbox } = await openAllocatorAndFill();
        await click(checkbox);
        const first = deferred<Response>();
        nextPost = () => first.promise;
        await submit(form);
        first.reject(new TypeError('network down'));
        const retry = await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry button');
        const kept = Object.values(storedCommands());
        assert.equal(kept.length, 1);
        assert.equal(kept[0].kind, 'allocate-direct-trade');
        assert.equal(kept[0].attempts, 1);

        const second = deferred<Response>();
        nextPost = () => second.promise;
        await click(retry);
        assert.equal(posted.length, 2);
        assert.equal(posted[1].body, posted[0].body, 'retry sends exactly the frozen payload and requestId');

        second.resolve(json(200, allocationResponse(kept[0].requestId)));
        await mounted.waitFor(() => (mounted.hasText('sales_notice_allocated') ? true : null), 'notice after replay');
        assert.ok(mounted.hasText('supply_saved_before'));
        assert.deepEqual(storedCommands(), {});
    });

    test('a 409 locks the form, offers reload only and never a retry', async () => {
        const { form, checkbox } = await openAllocatorAndFill();
        await click(checkbox);
        nextPost = async () => json(409, { message: 'Only 5 box of this supply line is still unallocated.' });
        await submit(form);
        await mounted.waitFor(() => (mounted.hasText('sales_conflict') ? true : null), 'conflict notice');
        assert.ok(mounted.button('sales_reload_and_decide'));
        assert.equal(mounted.button('supply_retry_same'), null);
        assert.equal(mounted.button('sales_confirm_allocation'), null);
        assert.deepEqual(storedCommands(), {}, 'a definitive 409 resolves the command');
        await click(mounted.button('sales_reload_and_decide')!);
        assert.equal(orderChanged, 1);
    });

    test('a stored allocation command reopens the allocator in replay-only mode after a remount', async () => {
        const { form, checkbox } = await openAllocatorAndFill();
        await click(checkbox);
        const first = deferred<Response>();
        nextPost = () => first.promise;
        await submit(form);
        first.reject(new TypeError('network down'));
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry button');
        await mounted.unmount();

        mounted = new Mounted();
        await mounted.render(panel(order, line));
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry button after remount');
        assert.ok(mounted.hasText('sales_pending_blocked_line'));
        assert.equal(mounted.buttons().filter((b) => b.textContent?.includes('sales_allocate_direct_trade')).length, 0, 'no fresh allocation next to an unresolved one');
        const selects = mounted.buttons().filter((b) => b.textContent?.trim() === 'supply_select' || b.textContent?.trim() === 'sales_selected');
        assert.ok(selects.every((b) => b.disabled), 'no other source can be picked while the command is unresolved');
    });
});

describe('lost response on the last available quantity', () => {
    // Review PR #30: de server legt de laatste resterende hoeveelheid vast, het antwoord gaat verloren; na verversen staat de
    // bronregel niet meer in de picker (rest 0). De verplichte datum moet dan uit het bevroren commando komen, en de herhaling
    // moet exact dezelfde requestId en body versturen.
    let supplyRows: unknown[];

    beforeEach(async () => {
        window.sessionStorage.clear();
        orderChanged = 0;
        supplyRows = [{ ...supplyRow, remainingQuantity: 10, allocatedQuantity: 20 }];
        nextPost = async () => json(500, { message: 'no post handler' });
        posted = stubFetch({
            onGet: (url) => (url.includes('/direct-trade-supply') ? json(200, supplyRows) : url.includes('/reservable-stock') ? json(200, []) : json(503, { message: url })),
            nextPost: () => nextPost(),
        });
        mounted = new Mounted();
        await mounted.render(panel(order, line));
    });

    afterEach(async () => { await mounted.unmount(); });

    test('after refresh the warning still shows the submitted expected date and the replay is byte-identical', async () => {
        await click(await mounted.waitFor(() => mounted.buttons().find((b) => b.textContent?.includes('sales_allocate_direct_trade')), 'allocate button'));
        await click(await mounted.waitFor(() => mounted.button('supply_select'), 'supply row'));
        const form = await mounted.waitFor(() => mounted.form(), 'allocation form');
        await click(mounted.button('sales_allocate_full_remainder')!);
        assert.equal(form.querySelector<HTMLInputElement>('input[inputmode="decimal"]')!.value, '10', 'full remainder = the last 10 on the source');
        await click(form.querySelector<HTMLInputElement>('input[type="checkbox"]')!);

        // Verwerkt door de server, antwoord verloren.
        nextPost = async () => { throw new TypeError('network down'); };
        await submit(form);
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after lost response');
        const frozen = Object.values(storedCommands())[0];
        assert.equal(frozen.kind, 'allocate-direct-trade');
        assert.equal((frozen.display as { expectedAt: string | null }).expectedAt, supplyRow.expectedAt, 'the displayed date is frozen as metadata');
        assert.equal('expectedAt' in (JSON.parse(posted[0].body) as Record<string, unknown>), false, 'the date never enters the payload');

        // Verversen: de bron is nu uitgeput en staat niet meer in de picker.
        supplyRows = [];
        await mounted.unmount();
        mounted = new Mounted();
        await mounted.render(panel(order, line));
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after refresh');
        assert.ok(mounted.hasText('sales_allocate_empty'), 'the source row is gone from the picker');
        assert.ok(mounted.hasText('sales_pending_supply_missing'));
        const time = mounted.container.querySelector<HTMLTimeElement>('form time');
        assert.equal(time?.dateTime, supplyRow.expectedAt, 'the warning still shows the expected date');
        assert.equal(mounted.container.querySelector('[data-expected-source]')?.getAttribute('data-expected-source'), 'snapshot');
        assert.ok(mounted.hasText('sales_allocate_expected_date_snapshot'), 'the date is identified as the date at submission');
        assert.ok(mounted.hasText('sales_allocate_date_warning'), 'the late-date warning survives too (28 okt after requested 27 okt)');
        assert.equal(mounted.hasText('availability_unknown'), false, 'no "unknown" date');

        nextPost = async () => json(200, allocationResponse(frozen.requestId, 10));
        await click(mounted.button('supply_retry_same')!);
        assert.equal(posted.length, 2);
        assert.equal(posted[1].body, posted[0].body, 'identical requestId and body');
        assert.equal(JSON.parse(posted[1].body).requestId, frozen.requestId);
        await mounted.waitFor(() => (mounted.hasText('sales_notice_allocated') ? true : null), 'notice after replay');
        assert.ok(mounted.hasText('supply_saved_before'));
        assert.deepEqual(storedCommands(), {});
    });

    test('with a source row available the date comes live from the picker, not from a snapshot', async () => {
        await click(await mounted.waitFor(() => mounted.buttons().find((b) => b.textContent?.includes('sales_allocate_direct_trade')), 'allocate button'));
        await click(await mounted.waitFor(() => mounted.button('supply_select'), 'supply row'));
        await mounted.waitFor(() => mounted.form(), 'allocation form');
        assert.equal(mounted.container.querySelector('[data-expected-source]')?.getAttribute('data-expected-source'), 'live');
        assert.equal(mounted.hasText('sales_allocate_expected_date_snapshot'), false);
    });
});

describe('an allocation in the order', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        orderChanged = 0;
        posted = stubFetch({ onGet: (url) => (url.includes('/direct-trade-supply') ? json(200, []) : json(404, { message: url })), nextPost: async () => json(500, {}) });
        mounted = new Mounted();
    });

    afterEach(async () => { await mounted.unmount(); });

    test('a line fully committed with 20 expected and 20 physical is complete but not ready, shows no kg for the allocation, and the order cannot dispatch the allocation', async () => {
        const allocation = allocationResponse('a-1', 20);
        const committedLine: SalesOrderLine = { ...line, reservedQuantity: 20, reservedNetWeightKg: 80, allocatedQuantity: 20, openQuantity: 0, reservations: [allocation, physicalReservation] };
        const committedOrder: SalesOrder = { ...order, lines: [committedLine] };
        await mounted.render(
            <PendingCommandsProvider>
                <HydratedGate>
                    <SalesOrderDetail order={committedOrder} actor="office-1" onOrderChanged={async () => true} />
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
                    <SalesOrderDetail order={{ ...order, lines: [allocatedLine] }} actor="office-1" onOrderChanged={async () => true} />
                </HydratedGate>
            </PendingCommandsProvider>,
        );
        const dispatchButton = mounted.button('sales_dispatch_order');
        assert.ok(dispatchButton?.disabled, 'an allocation of expected supply is never dispatchable from the warehouse');
        assert.ok(mounted.buttons().some((b) => b.getAttribute('aria-label') === 'sales_release_allocation_label'), 'the allocation can be released');
    });
});
