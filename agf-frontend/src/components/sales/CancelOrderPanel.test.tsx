import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, deferred, json, setValue, stubFetch, storedCommands, submit } from '../../test/reactHarness.ts';
import { PendingCommand } from '../../lib/pendingCommands';
import { SalesOrder } from '../../types/salesOrder';
import CancelOrderPanel from './CancelOrderPanel';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';

// Annuleringspaneel door de echte componenten heen: de bevestiging toont precies de actieve reserveringen (ook on hold),
// dezelfde formulierinstantie blijft staan tijdens een traag verzoek, bij 409 en bij een onbekende uitkomst,
// en een onafgeronde regelactie blokkeert de annulering.

const reservation = (id: number, status: 'Active' | 'Released', onHold = false) => ({
    id, requestId: `r-${id}`, salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: `PAL-${id}`,
    warehouseLocation: null, lotNumber: `LOT-${id}`, quantity: 5, unit: 'box' as const, netWeightKg: 20, source: 'Physical', status,
    createdAt: '2026-10-19T10:00:00.000Z', releasedAt: status === 'Released' ? '2026-10-19T10:30:00.000Z' : null,
    releaseReason: status === 'Released' ? 'eerder' : null, version: 0, contentQualityStatus: onHold ? 'Blocked' as const : 'Approved' as const, onHold, consumedAt: null, dispatchId: null, dispatchReference: null, correctedAt: null, correctionId: null, supplyCommitmentLineId: 1, supplyCommitmentId: 1, vendorName: null, origin: null, destination: null, expectedAt: null,
});

const order: SalesOrder = {
    id: 4, requestId: 'o-4', customerId: 1, customerName: 'Local Market Stalls', status: 'Open', customerReference: null,
    requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version: 2,
    cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false,
    lines: [{
        id: 7, productVariantId: 2, productName: 'Avocado', productVariantName: 'Green', quantity: 20, unit: 'box',
        reservedQuantity: 10, reservedNetWeightKg: 40, allocatedQuantity: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, openQuantity: 10, fulfilled: false, version: 3,
        reservations: [reservation(11, 'Active', true), reservation(12, 'Released'), reservation(13, 'Active')],
    }],
};

const cancelledResponse = { ...order, status: 'Cancelled', version: 3, cancelledAt: '2026-10-19T12:00:00.000Z', cancelReason: 'Klant heeft afgezegd', cancelledBy: 'office-1' };

let mounted: Mounted;
let posted: { url: string; body: string }[];
let nextPost: () => Promise<Response>;
let cancelledCalls: number;
let reloads: number;

async function render(preloaded?: PendingCommand) {
    if (preloaded) window.sessionStorage.setItem('agf_sales_pending_commands', JSON.stringify({ [preloaded.key]: preloaded }));
    mounted = new Mounted();
    await mounted.render(
        <PendingCommandsProvider>
            <HydratedGate>
                <CancelOrderPanel order={order} actor="office-1" onCancelled={async () => { cancelledCalls += 1; }} onReload={() => { reloads += 1; }} onClose={() => undefined} />
            </HydratedGate>
        </PendingCommandsProvider>,
    );
    await mounted.waitFor(() => mounted.form(), 'cancel form');
}

describe('CancelOrderPanel through the component lifecycle', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        cancelledCalls = 0;
        reloads = 0;
        nextPost = async () => json(500, { message: 'no post handler' });
        posted = stubFetch({ onGet: () => json(404, { message: 'unexpected GET' }), nextPost: () => nextPost() });
    });

    afterEach(async () => { await mounted.unmount(); });

    test('the confirmation lists exactly the active reservations, flags on hold, and leaves released ones out', async () => {
        await render();
        assert.equal(mounted.hasText('sales_cancel_will_release'), true);
        assert.equal(mounted.hasText('PAL-11'), true);
        assert.equal(mounted.hasText('PAL-13'), true);
        assert.equal(mounted.hasText('PAL-12'), false, 'released reservations are not part of the release list');
        assert.equal(mounted.hasText('sales_on_hold'), true);
        assert.equal(mounted.hasText('sales_cancel_based_on_version'), true);
        assert.ok(mounted.button('sales_cancel_confirm'));
    });

    test('a delayed request keeps the same form busy without retry, then reports the cancellation with the frozen version', async () => {
        await render();
        const form = mounted.form()!;
        await act0(() => setValue(form.querySelector('textarea')!, '  Klant heeft afgezegd  '));
        const response = deferred<Response>();
        nextPost = () => response.promise;

        await submit(form);

        assert.equal(posted.length, 1);
        const body = JSON.parse(posted[0].body);
        assert.equal(body.expectedOrderVersion, 2);
        assert.equal(body.reason, 'Klant heeft afgezegd');
        assert.equal(posted[0].url.endsWith('/sales-orders/4/cancel'), true);
        const command = Object.values(storedCommands())[0];
        assert.equal(command?.kind, 'cancel-order');
        assert.equal(command.requestId, body.requestId);
        assert.equal(mounted.form(), form, 'same instance while the request runs');
        assert.equal(form.getAttribute('aria-busy'), 'true');
        assert.equal(mounted.button('supply_retry_same'), null);

        response.resolve(json(200, cancelledResponse));
        await mounted.waitFor(() => cancelledCalls === 1, 'onCancelled');
        assert.deepEqual(storedCommands(), {});
    });

    test('a 409 keeps the same form, shows the conflict, requires reload and clears the command', async () => {
        await render();
        const form = mounted.form()!;
        await act0(() => setValue(form.querySelector('textarea')!, 'Klant heeft afgezegd'));
        const response = deferred<Response>();
        nextPost = () => response.promise;
        await submit(form);

        response.resolve(json(409, { message: 'Sales order 4 is at version 3, not 2; reload it and decide again.' }));
        await mounted.waitFor(() => mounted.hasText('sales_conflict'), 'conflict notice');

        assert.equal(mounted.form(), form);
        assert.ok(mounted.button('sales_reload_and_decide'));
        assert.equal(mounted.button('supply_retry_same'), null);
        assert.equal(mounted.button('sales_cancel_confirm'), null);
        assert.equal(form.querySelector('textarea')!.disabled, true);
        assert.deepEqual(storedCommands(), {});
        assert.equal(cancelledCalls, 0);
        await click(mounted.button('sales_reload_and_decide')!);
        assert.equal(reloads, 1);
    });

    test('an unknown outcome keeps the frozen command; after a remount the retry resends the identical body', async () => {
        await render();
        const form = mounted.form()!;
        await act0(() => setValue(form.querySelector('textarea')!, 'Klant heeft afgezegd'));
        const first = deferred<Response>();
        nextPost = () => first.promise;
        await submit(form);
        first.reject(new TypeError('network down'));
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry button');
        assert.equal(mounted.form(), form);
        const kept = Object.values(storedCommands());
        assert.equal(kept.length, 1);
        assert.equal(kept[0].attempts, 1);

        // Navigeren en terugkomen: nieuwe instantie, zelfde commando uit de opslag, invoer vergrendeld.
        await mounted.unmount();
        mounted = new Mounted();
        await mounted.render(
            <PendingCommandsProvider>
                <HydratedGate>
                    <CancelOrderPanel order={order} actor="office-1" onCancelled={async () => { cancelledCalls += 1; }} onReload={() => { reloads += 1; }} onClose={() => undefined} />
                </HydratedGate>
            </PendingCommandsProvider>,
        );
        const retry = await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after remount');
        assert.equal(mounted.form()!.querySelector('textarea')!.value, 'Klant heeft afgezegd');
        assert.equal(mounted.form()!.querySelector('textarea')!.disabled, true);
        assert.equal(mounted.button('sales_cancel_confirm'), null);

        const second = deferred<Response>();
        nextPost = () => second.promise;
        await click(retry);
        assert.equal(posted.length, 2);
        assert.equal(posted[1].body, posted[0].body, 'identical payload and requestId');
        second.resolve(json(200, cancelledResponse));
        await mounted.waitFor(() => cancelledCalls === 1, 'onCancelled after retry');
        assert.deepEqual(storedCommands(), {});
    });

    test('an unresolved line command blocks cancellation of that order', async () => {
        await render({
            kind: 'reserve', key: 'reserve:4:7', orderId: 4, lineId: 7, requestId: 'r-x', attempts: 1, lastError: 'x', frozenAt: '2026-10-19T10:00:00.000Z',
            payload: { requestId: 'r-x', palletContentLineId: 2, quantity: 1, unit: 'box', netWeightKg: 4, actorReference: 'office-1', reason: null },
            display: { palletReference: 'P', lotNumber: 'L', productLabel: 'Avocado · Green' },
        });
        await mounted.waitFor(() => mounted.hasText('sales_cancel_blocked'), 'blocked notice');
        assert.equal(mounted.button('sales_cancel_confirm'), null);
        assert.equal(mounted.form()!.querySelector('textarea')!.disabled, true);
        await submit(mounted.form()!);
        assert.equal(posted.length, 0, 'no command is sent while a line command is unresolved');
    });
});

async function act0(action: () => void) {
    const { act } = await import('react');
    await act(async () => { action(); });
}
