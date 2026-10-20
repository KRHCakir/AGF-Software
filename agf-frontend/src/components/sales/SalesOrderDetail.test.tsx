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

const reservation = (id: number, status: 'Active' | 'Released' | 'Consumed' | 'Corrected'): StockReservation => ({
    id, requestId: `r-${id}`, salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: `PAL-${id}`,
    warehouseLocation: null, lotNumber: `LOT-${id}`, quantity: 5, unit: 'box', netWeightKg: 20, source: 'Physical', status,
    createdAt: '2026-10-19T10:00:00.000Z', releasedAt: status === 'Released' ? '2026-10-19T10:30:00.000Z' : null,
    releaseReason: status === 'Released' ? 'eerder' : null, version: status === 'Active' ? 0 : status === 'Corrected' ? 2 : 1, contentQualityStatus: 'Approved', onHold: false,
    consumedAt: status === 'Consumed' || status === 'Corrected' ? '2026-10-19T13:00:00.000Z' : null,
    dispatchId: status === 'Consumed' || status === 'Corrected' ? 9 : null, dispatchReference: status === 'Consumed' || status === 'Corrected' ? 'VRB-9' : null,
    correctedAt: status === 'Corrected' ? '2026-10-19T14:00:00.000Z' : null, correctionId: status === 'Corrected' ? 3 : null, supplyCommitmentLineId: 1, supplyCommitmentId: 1, vendorName: null, origin: null, destination: null, expectedAt: null, directTradeShipmentId: null, shippedOn: null, shippedNetWeightKg: null, shippedLotNumber: null, directTradeCorrectionId: null,
});

// Verzending 9 met de opgegeven reserveringen; `corrected` vult de correctie zodat de historie beide toont.
const dispatchNine = (reservationIds: number[], corrected: boolean) => ({
    id: 9, requestId: 'd-9', salesOrderId: 4, reference: 'VRB-9', actorReference: 'office-1', dispatchedAt: '2026-10-19T13:00:00.000Z',
    correction: corrected ? {
        id: 3, requestId: 'c-3', salesDispatchId: 9, salesOrderId: 4, reason: 'Verkeerd gescand', actorReference: 'office-1', goodsNeverLeftWarehouseConfirmed: true, correctedAt: '2026-10-19T14:00:00.000Z',
        lines: reservationIds.map((rid, index) => ({ id: 50 + index, salesDispatchLineId: 20 + index, stockReservationId: rid, restoredPalletContentLineId: 70 + index, palletId: 2, palletReference: `PAL-${rid}`, lotNumber: `LOT-${rid}`, quantity: 5, unit: 'box', netWeightKg: 20, restoredQualityStatus: 'Quarantine', stockMovementId: 80 + index })),
    } : null,
    lines: reservationIds.map((rid, index) => ({ id: 20 + index, stockReservationId: rid, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: `PAL-${rid}`, lotNumber: `LOT-${rid}`, productName: 'Avocado', productVariantName: 'Green', quantity: 5, unit: 'box', netWeightKg: 20, stockMovementId: 30 + index })),
});

const orderWith = (status: 'Open' | 'Cancelled', version: number, reservations: StockReservation[]): SalesOrder => ({
    id: 4, requestId: 'o-4', customerId: 1, customerName: 'Local Market Stalls', status, customerReference: null,
    requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version,
    cancelledAt: status === 'Cancelled' ? '2026-10-19T12:00:00.000Z' : null,
    cancelReason: status === 'Cancelled' ? 'Klant heeft afgezegd' : null,
    cancelledBy: status === 'Cancelled' ? 'office-1' : null,
    fulfilled: false,
    lines: [{
        id: 7, productVariantId: 2, productName: 'Avocado', productVariantName: 'Green', quantity: 20, unit: 'box',
        reservedQuantity: reservations.filter((r) => r.status === 'Active').length * 5,
        reservedNetWeightKg: reservations.filter((r) => r.status === 'Active').length * 20, allocatedQuantity: 0, directTradeShippedQuantity: 0,
        dispatchedQuantity: reservations.filter((r) => r.status === 'Consumed').length * 5, dispatchedNetWeightKg: reservations.filter((r) => r.status === 'Consumed').length * 20,
        openQuantity: 20 - reservations.filter((r) => r.status === 'Active' || r.status === 'Consumed').length * 5, fulfilled: false, version: 3, reservations,
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

// De knop Vernieuwen draagt een pictogram vóór de tekst.
const refreshButton = (m: Mounted) => m.buttons().find((b) => (b.textContent ?? '').includes('sales_refresh')) ?? null;

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

    test('all consumed reservations are Corrected: cancellation is available again while dispatch and correction history stay visible', async () => {
        posted = stubFetch({ onGet: (url) => (url.includes('/dispatches') ? json(200, [dispatchNine([11, 12], true)]) : json(404, { message: `unexpected GET ${url}` })), nextPost: () => nextPost() });
        current = orderWith('Open', 3, [reservation(11, 'Corrected'), reservation(12, 'Corrected'), reservation(13, 'Released')]);
        await mounted.render(element(current));

        const cancel = await mounted.waitFor(() => mounted.button('sales_cancel_order'), 'cancel button');
        assert.equal(cancel.disabled, false, 'fully corrected consumption no longer blocks cancellation');
        assert.equal(cancel.title, '', 'no "after dispatch" explanation');
        assert.equal(mounted.hasText('sales_dispatches_title'), true, 'dispatch history section stays visible');
        await mounted.waitFor(() => mounted.hasText('sales_dispatch_corrected'), 'corrected badge in the dispatch list');
        assert.equal(mounted.hasText('sales_corrected_notice'), true);
        assert.equal(mounted.button('sales_correct_dispatch'), null, 'a corrected dispatch offers no second correction');
        await click(cancel);
        assert.ok(await mounted.waitFor(() => mounted.button('sales_cancel_confirm'), 'cancellation panel opens'));
    });

    test('at least one reservation remains Consumed: cancellation stays disabled although history is visible', async () => {
        posted = stubFetch({ onGet: (url) => (url.includes('/dispatches') ? json(200, [dispatchNine([11], true), { ...dispatchNine([12], false), id: 10, requestId: 'd-10', reference: 'VRB-10' }]) : json(404, { message: `unexpected GET ${url}` })), nextPost: () => nextPost() });
        current = orderWith('Open', 3, [reservation(11, 'Corrected'), reservation(12, 'Consumed')]);
        await mounted.render(element(current));

        const cancel = await mounted.waitFor(() => mounted.button('sales_cancel_order'), 'cancel button');
        assert.equal(cancel.disabled, true, 'uncorrected consumption blocks cancellation');
        assert.equal(cancel.title, 'sales_cancel_after_dispatch');
        assert.equal(mounted.hasText('sales_dispatches_title'), true);
        await mounted.waitFor(() => mounted.button('sales_correct_dispatch') !== null, 'the uncorrected dispatch can still be corrected');
        await click(cancel);
        assert.equal(mounted.button('sales_cancel_confirm'), null, 'no cancellation panel');
    });

    test('refresh is read-only: it keeps an open form with its input, keeps the frozen command, sends no write and reports failure without replacing the order', async () => {
        window.sessionStorage.setItem('agf_sales_pending_commands', JSON.stringify({ [frozenCancel.key]: frozenCancel }));
        current = orderWith('Open', 2, [reservation(11, 'Active')]);
        let refreshes = 0;
        let refreshOk = true;
        const onRefresh = async () => { refreshes += 1; return refreshOk; };
        await mounted.render(
            <PendingCommandsProvider>
                <HydratedGate>
                    <SalesOrderDetail order={current} actor="office-1" onOrderChanged={() => onOrderChanged()} onRefresh={onRefresh} />
                </HydratedGate>
            </PendingCommandsProvider>,
        );
        // Het bevroren annuleringscommando staat open met zijn herhaalknop.
        const retry = await mounted.waitFor(() => mounted.button('supply_retry_same'), 'frozen cancel command');
        const form = mounted.form()!;
        await click(refreshButton(mounted)!);
        await mounted.waitFor(() => refreshes === 1, 'refresh callback');
        await mounted.waitFor(() => refreshButton(mounted)?.disabled === false, 'refresh finished');
        assert.equal(posted.length, 0, 'refresh sends no write');
        assert.deepEqual(storedCommands(), { [frozenCancel.key]: frozenCancel }, 'frozen command untouched');
        assert.equal(mounted.form(), form, 'the pending panel stays mounted');
        assert.equal(mounted.button('supply_retry_same'), retry, 'same retry button instance');
        assert.equal(mounted.hasText('sales_refresh_failed'), false);
        // Mislukt vernieuwen: melding, maar niets vervangen of gewist.
        refreshOk = false;
        await click(refreshButton(mounted)!);
        await mounted.waitFor(() => mounted.hasText('sales_refresh_failed'), 'failure notice');
        assert.equal(mounted.form(), form);
        assert.deepEqual(storedCommands(), { [frozenCancel.key]: frozenCancel });
        assert.equal(refreshes, 2);
    });

    test('refresh keeps typed input in an open cancellation form', async () => {
        current = orderWith('Open', 2, [reservation(11, 'Active')]);
        await mounted.render(element(current));
        await click(await mounted.waitFor(() => mounted.button('sales_cancel_order'), 'cancel button'));
        const form = await mounted.waitFor(() => mounted.form(), 'cancel form');
        const reason = form.querySelector('textarea')!;
        await act(async () => { setValue(reason, 'Klant belt nog terug'); });
        await click(refreshButton(mounted)!);
        await mounted.waitFor(() => refreshButton(mounted)?.disabled === false, 'refresh finished');
        assert.equal(mounted.form(), form, 'same form instance');
        assert.equal(form.querySelector('textarea')!.value, 'Klant belt nog terug');
        assert.equal(posted.length, 0);
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
