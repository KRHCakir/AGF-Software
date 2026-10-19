import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, deferred, json, setValue, stubFetch, storedCommands, submit } from '../../test/reactHarness.ts';
import { PendingCommand, cancelKey } from '../../lib/pendingCommands';
import { SalesOrder, StockReservation } from '../../types/salesOrder';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesOrderDetail from './SalesOrderDetail';

// Annuleren op detailniveau: een onafgeronde annulering blijft zichtbaar als de order intussen Cancelled is
// (server heeft het commando verwerkt, antwoord verloren), en na een geslaagde herlaadactie na 409 begint
// een nieuwe beslissing, ook als de orderversie gelijk bleef maar de reserveringen veranderden.

const reservation = (id: number, status: 'Active' | 'Released'): StockReservation => ({
    id, requestId: `r-${id}`, salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: `PAL-${id}`,
    warehouseLocation: null, lotNumber: `LOT-${id}`, quantity: 5, unit: 'box', netWeightKg: 20, source: 'Physical', status,
    createdAt: '2026-10-19T10:00:00.000Z', releasedAt: status === 'Released' ? '2026-10-19T10:30:00.000Z' : null,
    releaseReason: status === 'Released' ? 'eerder' : null, version: status === 'Released' ? 1 : 0, contentQualityStatus: 'Approved', onHold: false,
});

const orderWith = (status: 'Open' | 'Cancelled', version: number, reservations: StockReservation[]): SalesOrder => ({
    id: 4, requestId: 'o-4', customerId: 1, customerName: 'Local Market Stalls', status, customerReference: null,
    requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version,
    cancelledAt: status === 'Cancelled' ? '2026-10-19T12:00:00.000Z' : null,
    cancelReason: status === 'Cancelled' ? 'Klant heeft afgezegd' : null,
    cancelledBy: status === 'Cancelled' ? 'office-1' : null,
    lines: [{
        id: 7, productVariantId: 2, productName: 'Avocado', productVariantName: 'Green', quantity: 20, unit: 'box',
        reservedQuantity: reservations.filter((r) => r.status === 'Active').length * 5,
        reservedNetWeightKg: reservations.filter((r) => r.status === 'Active').length * 20,
        openQuantity: 20 - reservations.filter((r) => r.status === 'Active').length * 5, version: 3, reservations,
    }],
});

const frozenCancel: PendingCommand = {
    kind: 'cancel-order', key: cancelKey(4), orderId: 4, requestId: 'k-lost', attempts: 1, lastError: 'network down', frozenAt: '2026-10-19T11:59:00.000Z',
    payload: { requestId: 'k-lost', expectedOrderVersion: 2, actorReference: 'office-1', reason: 'Klant heeft afgezegd' },
    display: { customerName: 'Local Market Stalls', activeReservationCount: 1 },
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
let nextPost: () => Promise<Response>;
let current: SalesOrder;
let onOrderChanged: () => Promise<boolean>;

const element = (order: SalesOrder) => (
    <PendingCommandsProvider>
        <HydratedGate>
            <SalesOrderDetail order={order} actor="office-1" onOrderChanged={() => onOrderChanged()} />
        </HydratedGate>
    </PendingCommandsProvider>
);

describe('SalesOrderDetail cancellation decisions', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        nextPost = async () => json(500, { message: 'no post handler' });
        posted = stubFetch({ onGet: () => json(404, { message: 'unexpected GET' }), nextPost: () => nextPost() });
        mounted = new Mounted();
    });

    afterEach(async () => { await mounted.unmount(); });

    test('a committed but unacknowledged cancellation stays visible on the Cancelled order; the identical replay resolves it', async () => {
        // De server heeft het commando verwerkt (order is Cancelled, versie 3), maar het antwoord ging verloren; het commando staat nog in de opslag.
        window.sessionStorage.setItem('agf_sales_pending_commands', JSON.stringify({ [frozenCancel.key]: frozenCancel }));
        current = orderWith('Cancelled', 3, [reservation(11, 'Released')]);
        let reloads = 0;
        onOrderChanged = async () => { reloads += 1; return true; };
        await mounted.render(element(current));

        const retry = await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry button on the cancelled order');
        assert.equal(mounted.hasText('sales_cancel_replay_only'), true);
        assert.equal(mounted.button('sales_cancel_confirm'), null, 'no fresh cancellation on a cancelled order');
        assert.equal(mounted.button('sales_cancel_order'), null);
        assert.equal(mounted.form()!.querySelector('textarea')!.value, 'Klant heeft afgezegd');
        assert.equal(mounted.form()!.querySelector('textarea')!.disabled, true);

        const response = deferred<Response>();
        nextPost = () => response.promise;
        await click(retry);
        assert.equal(posted.length, 1);
        assert.deepEqual(JSON.parse(posted[0].body), frozenCancel.payload, 'identical frozen payload and requestId');
        response.resolve(json(200, current));
        await mounted.waitFor(() => Object.keys(storedCommands()).length === 0, 'storage cleared after the 200 replay');
        assert.equal(reloads, 1);
        await mounted.waitFor(() => mounted.form() === null, 'panel gone once resolved');
    });

    test('after a 409 and a successful reload with the same order version but changed reservations, a fresh decision is possible', async () => {
        current = orderWith('Open', 2, [reservation(11, 'Active')]);
        onOrderChanged = async () => {
            // Herladen: dezelfde orderversie, maar reservering 11 is intussen vrijgegeven en 12 is erbij gekomen.
            current = orderWith('Open', 2, [reservation(11, 'Released'), reservation(12, 'Active')]);
            await act(async () => { mounted.root.render(element(current)); });
            return true;
        };
        await mounted.render(element(current));
        await click(await mounted.waitFor(() => mounted.button('sales_cancel_order'), 'cancel button'));
        const form = await mounted.waitFor(() => mounted.form(), 'cancel panel');
        assert.equal((form.textContent ?? '').includes('PAL-11'), true, 'the confirmation lists the active reservation before the conflict');
        await act(async () => { setValue(form.querySelector('textarea')!, 'Klant heeft afgezegd'); });
        nextPost = async () => json(409, { message: 'Sales order 4 or its reservations were changed concurrently; reload and decide again.' });
        await submit(form);
        await mounted.waitFor(() => mounted.hasText('sales_conflict'), 'conflict');
        assert.equal(form.querySelector('textarea')!.disabled, true);
        assert.deepEqual(storedCommands(), {});

        await click(mounted.button('sales_reload_and_decide')!);
        const fresh = await mounted.waitFor(() => (mounted.form() !== null && mounted.form() !== form ? mounted.form() : null), 'a fresh cancel panel after reload');

        assert.equal(mounted.hasText('sales_conflict'), false, 'conflict state is gone');
        assert.equal(fresh.querySelector('textarea')!.disabled, false, 'inputs are usable again');
        assert.equal(fresh.querySelector('textarea')!.value, '');
        assert.ok(mounted.button('sales_cancel_confirm'), 'a new decision can be made');
        // Alleen binnen het annuleringspaneel: de regeltabel eronder toont terecht ook vrijgegeven reserveringen.
        assert.equal((fresh.textContent ?? '').includes('PAL-12'), true, 'the confirmation lists the updated active reservation');
        assert.equal((fresh.textContent ?? '').includes('PAL-11'), false, 'the released one is no longer in the release list');
        assert.equal(mounted.hasText('sales_cancel_based_on_version'), true);
    });

    test('a reload that fails keeps the conflict state instead of pretending a fresh decision', async () => {
        current = orderWith('Open', 2, [reservation(11, 'Active')]);
        onOrderChanged = async () => false;
        await mounted.render(element(current));
        await click(await mounted.waitFor(() => mounted.button('sales_cancel_order'), 'cancel button'));
        const form = await mounted.waitFor(() => mounted.form(), 'cancel panel');
        await act(async () => { setValue(form.querySelector('textarea')!, 'Klant heeft afgezegd'); });
        nextPost = async () => json(409, { message: 'changed concurrently' });
        await submit(form);
        await mounted.waitFor(() => mounted.hasText('sales_conflict'), 'conflict');

        await click(mounted.button('sales_reload_and_decide')!);
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
        assert.equal(mounted.form(), form, 'the same panel stays while the reload did not succeed');
        assert.equal(mounted.hasText('sales_conflict'), true);
    });
});
