import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { parseStoredCommands } from '../../lib/pendingCommands.ts';
import { SalesOrder, SalesOrderLine } from '../../types/salesOrder';
import OrderLinePanel from './OrderLinePanel';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';

// Levenscyclus van het reserveerformulier door de echte componenten heen (OrderLinePanel + ReserveForm + context):
// dezelfde formulierinstantie blijft staan tijdens een traag verzoek, bij 409 en bij een onbekende uitkomst.
// Zonder LanguageProvider geeft t() de sleutel terug; daar wordt op gezocht.

const STORAGE_KEY = 'agf_sales_pending_commands';

const line: SalesOrderLine = {
    id: 7, productVariantId: 2, productName: 'Avocado', productVariantName: 'Green', quantity: 10, unit: 'box',
    reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, directTradeShippedQuantity: 0, deliveredQuantity: 0, delivered: false, openQuantity: 10, fulfilled: false, version: 0, reservations: [],
};
const order: SalesOrder = {
    id: 4, requestId: 'o-4', customerId: 1, customerName: 'Local Market Stalls', status: 'Open', customerReference: null,
    requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T10:00:00.000Z', version: 0,
    cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false, lines: [line],
};
const stockRow = {
    palletContentLineId: 2, palletId: 2, palletReference: 'SMOKE-PAL-002', warehouseLocation: null, lotNumber: 'LOT-SALES-1', unit: 'box',
    quantity: 40, netWeightKg: 160, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, onHandQuantity: 40, onHandNetWeightKg: 160, reservedQuantity: 15, reservedNetWeightKg: 60, allocatedQuantity: 0, availableQuantity: 25, availableNetWeightKg: 100,
    qualityStatus: 'Approved', contentLineVersion: 6,
};
const reservationResponse = (requestId: string) => ({
    id: 9, requestId, salesOrderId: 4, salesOrderLineId: 7, palletContentLineId: 2, palletId: 2, palletReference: 'SMOKE-PAL-002',
    warehouseLocation: null, lotNumber: 'LOT-SALES-1', quantity: 5, unit: 'box', netWeightKg: 20, source: 'Physical', status: 'Active',
    createdAt: '2026-10-19T10:05:00.000Z', releasedAt: null, releaseReason: null, version: 0, contentQualityStatus: 'Approved', onHold: false,
    consumedAt: null, dispatchId: null, dispatchReference: null, correctedAt: null, correctionId: null, supplyCommitmentLineId: 1, supplyCommitmentId: 1, vendorName: null, origin: null, destination: null, expectedAt: null, directTradeShipmentId: null, shippedOn: null, shippedNetWeightKg: null, shippedLotNumber: null, directTradeCorrectionId: null, deliveryConfirmationId: null, deliveredOn: null,
});

const json = (status: number, body: unknown) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

function deferred<T>() {
    let resolve!: (value: T) => void;
    let reject!: (error: unknown) => void;
    const promise = new Promise<T>((res, rej) => { resolve = res; reject = rej; });
    return { promise, resolve, reject };
}

interface Posted { url: string; body: string }

let root: Root;
let container: HTMLElement;
let posted: Posted[];
let nextPost: () => Promise<Response>;
let orderChanged: number;

const flush = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });

async function waitFor<T>(find: () => T | null | undefined, what: string): Promise<T> {
    for (let i = 0; i < 50; i++) {
        const found = find();
        if (found) return found;
        await flush();
    }
    throw new Error(`Timed out waiting for ${what}`);
}

const buttons = () => Array.from(container.querySelectorAll('button'));
const button = (text: string) => buttons().find((b) => b.textContent?.trim() === text) ?? null;
const hasText = (text: string) => (container.textContent ?? '').includes(text);
const click = (element: HTMLElement) => act(async () => { element.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true })); });

function setValue(input: HTMLInputElement, value: string) {
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')!.set!;
    setter.call(input, value);
    input.dispatchEvent(new window.Event('input', { bubbles: true }));
}

async function openFormAndFill(): Promise<{ form: HTMLFormElement; quantity: HTMLInputElement }> {
    await click(await waitFor(() => button('sales_choose_stock'), 'choose-stock button'));
    await click(await waitFor(() => button('supply_select'), 'stock row select button'));
    const form = await waitFor(() => container.querySelector<HTMLFormElement>('form'), 'reserve form');
    const inputs = Array.from(form.querySelectorAll<HTMLInputElement>('input[inputmode="decimal"]'));
    assert.equal(inputs.length, 2);
    await act(async () => { setValue(inputs[0], '5'); setValue(inputs[1], '20'); });
    return { form, quantity: inputs[0] };
}

const submit = (form: HTMLFormElement) => act(async () => { form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

const storedCommands = () => parseStoredCommands(window.sessionStorage.getItem(STORAGE_KEY));

describe('ReserveForm identity through submission and outcome handling', () => {
    beforeEach(async () => {
        window.sessionStorage.clear();
        posted = [];
        orderChanged = 0;
        nextPost = async () => json(500, { message: 'no post handler' });
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const url = String(input);
            if (init?.method === 'POST') {
                posted.push({ url, body: String(init.body) });
                return nextPost();
            }
            if (url.includes('/reservable-stock')) return json(200, [stockRow]);
            if (url.includes('/availability')) return json(503, { message: 'availability unavailable in test' });
            return json(404, { message: `unexpected GET ${url}` });
        }) as typeof fetch;
        container = window.document.createElement('div');
        window.document.body.appendChild(container);
        root = createRoot(container);
        await act(async () => {
            root.render(
                <PendingCommandsProvider>
                    <HydratedGate>
                        <OrderLinePanel order={order} line={line} actor="office-1" readOnly={false} onOrderChanged={async () => { orderChanged += 1; return true; }} />
                    </HydratedGate>
                </PendingCommandsProvider>,
            );
        });
    });

    afterEach(async () => {
        await act(async () => root.unmount());
        container.remove();
    });

    test('a delayed request keeps the same form instance busy, offers no retry, and completes into the success notice', async () => {
        const { form, quantity } = await openFormAndFill();
        const response = deferred<Response>();
        nextPost = () => response.promise;

        await submit(form);

        assert.equal(posted.length, 1);
        const command = Object.values(storedCommands())[0];
        assert.equal(command?.kind, 'reserve');
        assert.equal(JSON.parse(posted[0].body).requestId, command.requestId);
        // Dezelfde instantie, nog bezig: geen nieuw formulier, geen "opnieuw proberen", invoer nog aanwezig.
        assert.equal(container.querySelector('form'), form);
        assert.equal(form.getAttribute('aria-busy'), 'true');
        assert.equal(quantity.isConnected, true);
        assert.equal(quantity.value, '5');
        assert.equal(button('supply_retry_same'), null);
        assert.equal(hasText('supply_saving'), true);

        response.resolve(json(201, reservationResponse(command.requestId)));
        await waitFor(() => (hasText('sales_notice_reserved') ? true : null), 'success notice');

        assert.equal(orderChanged, 1);
        assert.deepEqual(storedCommands(), {});
        assert.equal(posted.length, 1);
    });

    test('a 409 keeps the same form instance, shows the conflict, requires reload and never offers a retry', async () => {
        const { form } = await openFormAndFill();
        const response = deferred<Response>();
        nextPost = () => response.promise;
        await submit(form);
        assert.equal(container.querySelector('form'), form);

        response.resolve(json(409, { message: 'The stock or order line was changed concurrently; reload and try again with a new requestId.' }));
        await waitFor(() => (hasText('sales_conflict') ? true : null), 'conflict notice');

        assert.equal(container.querySelector('form'), form, 'the same form instance must show the conflict');
        assert.ok(button('sales_reload_and_decide'), 'reload is the only way forward');
        assert.equal(button('supply_retry_same'), null);
        assert.equal(button('sales_confirm_reservation'), null);
        assert.ok(Array.from(form.querySelectorAll<HTMLInputElement>('input')).every((input) => input.disabled), 'inputs stay locked after 409');
        assert.deepEqual(storedCommands(), {}, 'a definitive 409 resolves the command; nothing is kept for retry');
        assert.equal(orderChanged, 0);
        assert.equal(posted.length, 1);

        await click(button('sales_reload_and_decide')!);
        await flush();
        assert.equal(orderChanged, 1, 'reload reads the order again');
        assert.equal(form.isConnected, false, 'after the user chooses reload a fresh decision starts in a new form');
    });

    test('an unknown outcome keeps the same instance with the frozen command; retry resends the identical body', async () => {
        const { form } = await openFormAndFill();
        const first = deferred<Response>();
        nextPost = () => first.promise;
        await submit(form);

        first.reject(new TypeError('network down'));
        const retry = await waitFor(() => button('supply_retry_same'), 'retry button');

        assert.equal(container.querySelector('form'), form, 'the same form instance offers the retry');
        const kept = Object.values(storedCommands());
        assert.equal(kept.length, 1);
        assert.equal(kept[0].attempts, 1);
        assert.equal(kept[0].lastError, 'network down');
        assert.equal(button('sales_reload_and_decide'), null);

        const second = deferred<Response>();
        nextPost = () => second.promise;
        await click(retry);
        assert.equal(posted.length, 2);
        assert.equal(posted[1].body, posted[0].body, 'retry sends exactly the frozen payload and requestId');
        assert.equal(container.querySelector('form'), form);

        second.resolve(json(201, reservationResponse(kept[0].requestId)));
        await waitFor(() => (hasText('sales_notice_reserved') ? true : null), 'success notice after retry');
        assert.deepEqual(storedCommands(), {});
        assert.equal(orderChanged, 1);
    });
});
