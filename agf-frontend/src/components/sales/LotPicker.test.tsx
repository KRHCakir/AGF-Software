import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import { salesLinePayload } from '../../lib/salesLine.ts';
import { emptySalesLine } from '../../lib/salesLine.ts';
import type { SalesOrderLine, StockReservation } from '../../types/salesOrder';
import ChangeLotForm from './ChangeLotForm';
import FifoReserveForm from './FifoReserveForm';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';
import LotPicker, { type ChosenLot } from './LotPicker';

// Feedback 22 november: de partijkeuze. Per partij vrij aantal, maat, kleur, herkomst, locatie en ontvangstdatum; typen zonder letter,
// pijltjes en Enter kiezen; leeg = de oudste partij eerst. Partij wijzigen bij het picken stuurt één opdracht met een vaste sleutel.

const option = (partijId: number, number: string, over: Record<string, unknown> = {}) => ({
    partijId, number, unit: 'colli', availableQuantity: 30, palletCount: 1, size: '67-82', color: 'Groen', origin: 'ES', locations: [null, 'Koelhuis Noord'], bins: ['B-01'],
    receivedAt: '2026-11-02T08:00:00Z', vendorName: 'Kwekerij Zonnegloed', ...over,
});
const options = [option(1, 'I-2611001'), option(2, 'I-2611002', { availableQuantity: 35, color: null, receivedAt: '2026-11-20T08:00:00Z' }), option(0, 'LOT-A', { partijId: null })];

let mounted: Mounted;
let calls: { method: string; url: string; body: string | null }[];
let answer: (method: string, url: string) => Response;
const one = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);
const all = (selector: string) => Array.from(mounted.container.querySelectorAll<HTMLElement>(selector));
const key = async (target: HTMLElement, name: string) => act(async () => { target.dispatchEvent(new window.KeyboardEvent('keydown', { key: name, bubbles: true })); });

describe('lot picker', () => {
    beforeEach(() => {
        calls = [];
        answer = (_method, url) => (url.includes('/lot-options') ? json(200, options) : json(201, []));
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            calls.push({ method: init?.method ?? 'GET', url: String(input), body: init?.body ? String(init.body) : null });
            return answer(init?.method ?? 'GET', String(input));
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the list shows each lot with free quantity, size, color, origin, location and receipt; Enter picks the highlighted lot', async () => {
        let chosen: ChosenLot | null = null;
        await mounted.render(<LotPicker id="lot" productVariantId={7} unit="colli" value={null} onChange={(lot) => { chosen = lot; }} label="Partij" />);
        await mounted.waitFor(() => (calls.length > 0 ? true : null), 'options requested');
        assert.ok(calls[0].url.includes('/sales-orders/lot-options?productVariantId=7&unit=colli'));
        const input = one('#lot') as HTMLInputElement;
        await key(input, 'ArrowDown');
        await mounted.waitFor(() => one('[data-testid="lot-picker-list"]'), 'list open');
        const rows = all('[role="option"]');
        assert.equal(rows.length, 2, 'stock without a lot is never offered');
        for (const label of ['lot_picker_size', 'lot_picker_color', 'lot_picker_origin', 'lot_picker_location', 'lot_picker_received']) {
            assert.ok(rows[0].textContent!.includes(label), label);
        }
        assert.ok(rows[0].textContent!.includes('Koelhuis Noord'));
        await key(input, 'ArrowDown');
        await key(input, 'Enter');
        assert.deepEqual(chosen, { partijId: 2, number: 'I-2611002' });
    });

    test('typing the number without letter filters the list, and an exact number is taken on leaving the field', async () => {
        let chosen: ChosenLot | null = null;
        await mounted.render(<LotPicker id="lot" productVariantId={7} unit="colli" value={null} onChange={(lot) => { chosen = lot; }} label="Partij" />);
        await mounted.waitFor(() => (calls.length > 0 ? true : null), 'options requested');
        await new Promise((resolve) => setTimeout(resolve, 10));
        const input = one('#lot') as HTMLInputElement;
        await act(async () => { setValue(input, '2611 002'); });
        assert.deepEqual(all('[role="option"]').map((row) => row.getAttribute('data-tour-key')), ['I-2611002']);
        await act(async () => { input.dispatchEvent(new window.FocusEvent('focusout', { bubbles: true })); });
        assert.deepEqual(chosen, { partijId: 2, number: 'I-2611002' });
    });

    test('Enter on an exactly typed number takes that lot, also when the list is still loading', async () => {
        let release: () => void = () => undefined;
        const loaded = new Promise<void>((resolve) => { release = resolve; });
        answer = (_method, url) => (url.includes('/lot-options') ? json(200, options) : json(201, []));
        const plain = globalThis.fetch;
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => { await loaded; return plain(input, init); }) as typeof fetch;
        let chosen: ChosenLot | null = null;
        await mounted.render(<LotPicker id="lot" productVariantId={7} unit="colli" value={null} onChange={(lot) => { chosen = lot; }} label="Partij" />);
        const input = one('#lot') as HTMLInputElement;
        await act(async () => { setValue(input, '2611001'); });
        await key(input, 'Escape');
        await key(input, 'Enter');
        assert.equal(chosen, null, 'nothing yet while loading');
        await act(async () => { release(); });
        await mounted.waitFor(() => (chosen !== null ? true : null), 'taken after loading');
        assert.deepEqual(chosen, { partijId: 1, number: 'I-2611001' });
    });

    test('the chosen lot goes with the order line payload; an empty choice sends nothing', () => {
        const line = { ...emptySalesLine('k'), productVariantId: '7', quantity: '10', unit: 'colli' as const, partijId: 2, partijNumber: 'I-2611002' };
        assert.equal(salesLinePayload(line, 'lines[0]', undefined).payload.partijId, 2);
        assert.equal('partijId' in salesLinePayload({ ...line, partijId: null }, 'lines[0]', undefined).payload, false);
    });

    test('Change lot sends the reservation version and the new lot; a retry uses the same key', async () => {
        const line = { id: 1, productVariantId: 7, unit: 'colli' } as unknown as SalesOrderLine;
        const reservation = { id: 55, version: 3, partijId: 1, partijNumber: 'I-2611001', lotNumber: 'I-2611001' } as unknown as StockReservation;
        let changed = 0;
        await mounted.render(<ChangeLotForm line={line} reservation={reservation} onCancel={() => undefined} onReload={() => undefined} onChanged={() => { changed++; }} />);
        await mounted.waitFor(() => (calls.length > 0 ? true : null), 'options requested');
        const input = one('#change-lot-55') as HTMLInputElement;
        await key(input, 'ArrowDown');
        await mounted.waitFor(() => one('[data-testid="lot-picker-list"]'), 'list open');
        assert.deepEqual(all('[role="option"]').map((row) => row.getAttribute('data-tour-key')), ['I-2611002'], 'the current lot is not offered');
        await key(input, 'Enter');
        answer = (_method, url) => (url.includes('/lot-options') ? json(200, options) : json(503, { message: 'down' }));
        await act(async () => { one('[data-tour="sales-change-lot-save"]')!.click(); });
        await mounted.waitFor(() => (calls.filter((call) => call.method === 'POST').length === 1 && !(one('[data-tour="sales-change-lot-save"]') as HTMLButtonElement).disabled ? true : null), 'first try done');
        answer = (_method, url) => (url.includes('/lot-options') ? json(200, options) : json(201, []));
        await act(async () => { one('[data-tour="sales-change-lot-save"]')!.click(); });
        await mounted.waitFor(() => (changed === 1 ? true : null), 'changed');
        const posts = calls.filter((call) => call.method === 'POST');
        assert.ok(posts[0].url.endsWith('/stock-reservations/55/change-lot'));
        const [first, second] = posts.map((post) => JSON.parse(post.body!) as { requestId: string; expectedReservationVersion: number; partijId: number });
        assert.deepEqual({ v: first.expectedReservationVersion, p: first.partijId }, { v: 3, p: 2 });
        assert.equal(first.requestId, second.requestId);
    });

    test('a conflict on Change lot offers to reload the order and decide again', async () => {
        const line = { id: 1, productVariantId: 7, unit: 'colli' } as unknown as SalesOrderLine;
        const reservation = { id: 55, version: 3, partijId: 1, partijNumber: 'I-2611001', lotNumber: 'I-2611001' } as unknown as StockReservation;
        let reloaded = 0;
        await mounted.render(<ChangeLotForm line={line} reservation={reservation} onCancel={() => undefined} onReload={() => { reloaded++; }} onChanged={() => undefined} />);
        await mounted.waitFor(() => (calls.length > 0 ? true : null), 'options requested');
        const input = one('#change-lot-55') as HTMLInputElement;
        await key(input, 'ArrowDown');
        await mounted.waitFor(() => one('[data-testid="lot-picker-list"]'), 'list open');
        await key(input, 'Enter');
        answer = (_method, url) => (url.includes('/lot-options') ? json(200, options) : json(409, { message: 'Reservation 55 is at version 4, not 3' }));
        await act(async () => { one('[data-tour="sales-change-lot-save"]')!.click(); });
        await mounted.waitFor(() => one('[data-testid="change-lot-reload"]'), 'reload offered');
        await act(async () => { (one('[data-testid="change-lot-reload"]') as HTMLButtonElement).click(); });
        assert.equal(reloaded, 1);
    });

    test('reserving from a chosen lot asks the oldest-first proposal of only that lot', async () => {
        const line = { id: 4, productVariantId: 7, unit: 'colli', quantity: 30, openQuantity: 30, version: 0, reservations: [] } as unknown as SalesOrderLine;
        answer = () => json(200, { salesOrderId: 9, salesOrderLineId: 4, unit: 'colli', neededQuantity: 30, proposedQuantity: 0, candidates: [], items: [] });
        await mounted.render(
            <PendingCommandsProvider>
                <HydratedGate>
                    <FifoReserveForm orderId={9} line={line} partijId={2} onSucceeded={async () => undefined} onReload={() => undefined} onCancel={() => undefined} />
                </HydratedGate>
            </PendingCommandsProvider>,
        );
        await mounted.waitFor(() => (calls.length > 0 ? true : null), 'proposal requested');
        assert.ok(calls[0].url.endsWith('/sales-orders/9/lines/4/fifo-proposal?partijId=2'));
    });
});
