import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json } from '../../test/reactHarness.ts';
import type { SalesOrder } from '../../types/salesOrder';
import OrderLinePanel from './OrderLinePanel';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import SalesDeliveries from './SalesDeliveries';

// Feedback 22 november: leveringen op de verkooporder. Deellevering maakt een tweede levering met een nieuw V-nummer; de gekozen pallets
// gaan mee. Een herhaling na een fout gebruikt dezelfde sleutel.

const reservation = (id: number, pallet: string, over: Record<string, unknown> = {}) => ({
    id, status: 'Active', palletReference: pallet, partijNumber: 'I-2611007', quantity: 20, unit: 'box', ...over,
});
const delivery = (id: number, sequence: number, partijNumber: string, reservationIds: number[], last: boolean) => ({
    id, sequence, partijId: 40 + id, partijNumber, last, loadAt: null, licensePlate: null, reservationIds, createdAt: '2026-11-23T09:00:00Z', createdBy: 'Jan',
});
const order = (over: Partial<SalesOrder> = {}) => ({
    id: 12, orderNumber: 'VO-2026-0012', status: 'Open', version: 3, requestedDeliveryDate: '2026-11-25T00:00:00Z', customerName: 'Groothandel Noord',
    lines: [{ id: 1, productName: 'Mango', productVariantName: 'Kent', reservations: [reservation(201, 'P-1'), reservation(202, 'P-2'), reservation(203, 'P-3', { status: 'Consumed' })] }],
    deliveries: [delivery(1, 1, 'V-2611001', [201, 202, 203], true)],
    ...over,
}) as unknown as SalesOrder;

// Het antwoord van de server: een geldige order (lege regels volstaan) met twee leveringen.
const saved = () => ({
    id: 12, requestId: 'o-12', customerId: 6, customerName: 'Groothandel Noord', status: 'Open', customerReference: null, requestedDeliveryDate: '2026-11-25T00:00:00Z',
    actorReference: 'Jan', createdAt: '2026-11-20T08:00:00Z', version: 4, cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false, lines: [],
    deliveries: [delivery(1, 1, 'V-2611001', [201, 203], false), delivery(2, 2, 'V-2611002', [202], true)],
});

let mounted: Mounted;
let calls: { method: string; url: string; body: string | null }[];
let answer: (method: string, url: string) => Response;
const one = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);
const all = (selector: string) => Array.from(mounted.container.querySelectorAll<HTMLElement>(selector));

describe('sales deliveries (partial delivery)', () => {
    beforeEach(() => {
        calls = [];
        answer = () => json(201, saved());
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            calls.push({ method: init?.method ?? 'GET', url: String(input), body: init?.body ? String(init.body) : null });
            return answer(init?.method ?? 'GET', String(input));
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('Partial delivery lists only active pallets and sends the chosen ones; a retry after an error uses the same key', async () => {
        let changed = 0;
        await mounted.render(<SalesDeliveries order={order()} readOnly={false} onChanged={() => { changed++; }} />);
        assert.equal(all('[data-testid="so-delivery"]').length, 1);
        await act(async () => { one('[data-tour="so-partial-delivery"]')!.click(); });
        const boxes = all('[data-tour="so-partial-delivery-reservation"]') as HTMLInputElement[];
        assert.equal(boxes.length, 2, 'a shipped pallet stays with its delivery and is not offered');
        await act(async () => { boxes[1].click(); });

        answer = () => json(503, { message: 'down' });
        await act(async () => { one('[data-tour="so-partial-delivery-create"]')!.click(); });
        await mounted.waitFor(() => {
            const button = one('[data-tour="so-partial-delivery-create"]') as HTMLButtonElement | null;
            return calls.filter((call) => call.method === 'POST').length >= 1 && button !== null && !button.disabled ? true : null;
        }, 'error kept the form');
        answer = () => json(201, saved());
        await act(async () => { one('[data-tour="so-partial-delivery-create"]')!.click(); });
        await mounted.waitFor(() => (changed === 1 ? true : null), 'order reloaded');

        const posts = calls.filter((call) => call.method === 'POST');
        assert.equal(posts.length, 2);
        assert.ok(posts[0].url.endsWith('/sales-orders/12/deliveries'));
        const [first, second] = posts.map((post) => JSON.parse(post.body!) as { requestId: string; reservationIds: number[] });
        assert.deepEqual(first.reservationIds, [202]);
        assert.equal(first.requestId, second.requestId, 'the retry is the same command');
        assert.equal(one('[data-testid="so-partial-delivery-form"]'), null);
    });

    test('after an uncertain error, Cancel and reopening keeps the same key and choice', async () => {
        let changed = 0;
        await mounted.render(<SalesDeliveries order={order()} readOnly={false} onChanged={() => { changed++; }} />);
        await act(async () => { one('[data-tour="so-partial-delivery"]')!.click(); });
        await act(async () => { (all('[data-tour="so-partial-delivery-reservation"]')[1] as HTMLInputElement).click(); });
        answer = () => json(503, { message: 'down' });
        await act(async () => { one('[data-tour="so-partial-delivery-create"]')!.click(); });
        await mounted.waitFor(() => {
            const button = one('[data-tour="so-partial-delivery-create"]') as HTMLButtonElement | null;
            return calls.filter((call) => call.method === 'POST').length >= 1 && button !== null && !button.disabled ? true : null;
        }, 'error kept the form');
        await act(async () => { (mounted.container.querySelector('[data-testid="so-partial-delivery-form"] button[type="button"]') as HTMLElement).click(); });
        assert.equal(one('[data-testid="so-partial-delivery-form"]'), null);

        answer = () => json(201, saved());
        await act(async () => { one('[data-tour="so-partial-delivery"]')!.click(); });
        await act(async () => { one('[data-tour="so-partial-delivery-create"]')!.click(); });
        await mounted.waitFor(() => (changed === 1 ? true : null), 'order reloaded');
        const [first, second] = calls.filter((call) => call.method === 'POST').map((post) => JSON.parse(post.body!) as { requestId: string; reservationIds: number[] });
        assert.equal(first.requestId, second.requestId);
        assert.deepEqual(second.reservationIds, [202]);
    });

    test('two deliveries are listed with their V-numbers; a closed order offers no partial delivery', async () => {
        const split = order({ status: 'Cancelled', deliveries: [delivery(1, 1, 'V-2611001', [201], false), delivery(2, 2, 'V-2611002', [202], true)] });
        await mounted.render(<SalesDeliveries order={split} readOnly onChanged={() => undefined} />);
        assert.deepEqual(all('[data-testid="so-delivery"]').map((li) => li.getAttribute('data-delivery-sequence')), ['1', '2']);
        assert.ok(mounted.hasText('V-2611002'));
        assert.equal(one('[data-tour="so-partial-delivery"]'), null);
    });

    test('with two deliveries every reserved pallet on the order line says which delivery it goes with', async () => {
        const physical = {
            id: 202, requestId: 'r-202', salesOrderId: 12, salesOrderLineId: 1, palletContentLineId: 2, palletId: 2, palletReference: 'P-2', warehouseLocation: 'A-01',
            lotNumber: 'I-2611007', quantity: 20, unit: 'box', netWeightKg: 80, source: 'Physical', supplyCommitmentLineId: 1, supplyCommitmentId: 9, vendorName: 'Example Supplier',
            origin: 'Peru', destination: null, expectedAt: null, directTradeShipmentId: null, shippedOn: null, shippedNetWeightKg: null, shippedLotNumber: null,
            directTradeCorrectionId: null, deliveryConfirmationId: null, deliveredOn: null, status: 'Active', createdAt: '2026-11-23T08:06:00.000Z', releasedAt: null,
            releaseReason: null, version: 0, contentQualityStatus: 'Approved', onHold: false, consumedAt: null, dispatchId: null, dispatchReference: null, correctedAt: null, correctionId: null,
            salesDeliveryId: 2,
        };
        const line = {
            id: 1, productVariantId: 1, productName: 'Mango', productVariantName: 'Kent', quantity: 40, unit: 'box', palletCount: null, packagesPerPallet: null, reservedQuantity: 20,
            reservedNetWeightKg: 80, allocatedQuantity: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, directTradeShippedQuantity: 0, deliveredQuantity: 0, delivered: false,
            openQuantity: 20, fulfilled: false, version: 0, reservations: [physical],
        };
        const split = order({ lines: [line], deliveries: [delivery(1, 1, 'V-2611001', [], false), delivery(2, 2, 'V-2611002', [202], true)] } as unknown as Partial<SalesOrder>);
        globalThis.fetch = (async () => json(200, [])) as typeof fetch;
        await mounted.render(
            <PendingCommandsProvider>
                <HydratedGate>
                    <OrderLinePanel order={split} line={split.lines[0]} readOnly={false} onOrderChanged={async () => true} />
                </HydratedGate>
            </PendingCommandsProvider>,
        );
        await mounted.waitFor(() => one('[data-testid="reservation-delivery"]'), 'delivery label');
        assert.ok(one('[data-testid="reservation-delivery"]')!.textContent!.includes('so_reservation_delivery'));
    });
});
