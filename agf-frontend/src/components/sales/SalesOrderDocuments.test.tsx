import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { SalesOrder } from '../../types/salesOrder';
import type { SalesOrderDocuments as Documents } from '../../types/salesOrderDocument';
import LoadingSlipSetting from '../settings/LoadingSlipSetting';
import SalesOrderDocuments, { inputFromLoadAt, loadAtFromInput } from './SalesOrderDocuments';

// Documenten deel 1 op de verkooporder: Bevestigen (met laden op en kenteken) maakt de laadbon; Bekijken, Downloaden en Afdrukken halen de
// PDF van die versie; een nieuwere versie geeft "Laadbon gewijzigd, opnieuw versturen" tot Doorgegeven. Een order zonder regels niet.

const order = (over: Partial<SalesOrder> = {}) => ({
    id: 12, orderNumber: 'VO-2026-0012', status: 'Open', version: 3, requestedDeliveryDate: '2026-11-13T00:00:00Z', customerName: 'Groothandel Noord',
    lines: [{ id: 1 }], confirmedAt: null, ...over,
}) as unknown as SalesOrder;

const slip = (version: number, over: Partial<Documents['documents'][number]> = {}) => ({
    id: 100 + version, kind: 'LoadingSlip', number: 'LB-2026-0012', version, reason: version === 1 ? 'Confirmed' : 'Changed', createdAt: '2026-11-12T09:00:00Z',
    createdBy: 'Jan', sentAt: null, sentBy: null, latest: false, ...over,
});
const docs = (over: Partial<Documents> = {}): Documents => ({
    salesOrderId: 12, confirmedAt: null, confirmedBy: null, loadAt: null, licensePlate: null, needsResend: false, documents: [], ...over,
});
const confirmed = (versions = 1, over: Partial<Documents> = {}) => docs({
    confirmedAt: '2026-11-12T09:00:00Z', confirmedBy: 'Jan', licensePlate: '12-ABC-3',
    documents: Array.from({ length: versions }, (_, i) => slip(versions - i, { latest: i === 0 })), ...over,
});

let mounted: Mounted;
let calls: { method: string; url: string; body: string | null }[];
let answer: (method: string, url: string) => Response;
const $ = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });

describe('sales order documents (loading slip)', () => {
    beforeEach(() => {
        calls = [];
        answer = () => json(200, docs());
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            calls.push({ method: init?.method ?? 'GET', url: String(input), body: init?.body ? String(init.body) : null });
            return answer(init?.method ?? 'GET', String(input));
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('Confirm asks for load time and plate on the page, then shows the loading slip; nothing is sent', async () => {
        let confirmedCalls = 0;
        await mounted.render(<SalesOrderDocuments order={order()} onConfirmed={() => { confirmedCalls++; }} />);
        await settle();
        assert.ok(calls[0].url.endsWith('/sales-orders/12/documents'));
        await act(async () => { $('[data-tour="so-confirm"]')!.click(); });
        assert.ok($('[data-testid="so-confirm-question"]'));
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[data-tour="so-license-plate"]')!, ' 12-ABC-3 '); });
        answer = () => json(200, confirmed());
        await act(async () => { $('[data-tour="so-confirm-yes"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="so-slip-latest"]') !== null, 'slip shown');
        const post = calls.find((call) => call.method === 'POST')!;
        assert.ok(post.url.endsWith('/sales-orders/12/confirm'));
        assert.deepEqual(JSON.parse(post.body!), { loadAt: null, licensePlate: '12-ABC-3' });
        assert.ok($('[data-testid="so-slip-latest"]')!.textContent!.includes('so_slip_name'));
        assert.equal($('[data-testid="so-slip-changed"]'), null);
        assert.equal(confirmedCalls, 1);
    });

    test('an order without lines cannot be confirmed; a cancelled order offers nothing', async () => {
        await mounted.render(<SalesOrderDocuments order={order({ lines: [] })} />);
        await settle();
        assert.ok($('[data-testid="so-confirm-no-lines"]'));
        assert.equal($('[data-tour="so-confirm"]'), null);
        await mounted.render(<SalesOrderDocuments order={order({ status: 'Cancelled', version: 4 })} />);
        await settle();
        assert.equal($('[data-tour="so-confirm"]'), null);
        assert.ok($('[data-testid="so-slip-cancelled"]'), 'cancelled, never confirmed: do not load');
    });

    test('a changed slip says so until passed on; older versions stay available', async () => {
        answer = () => json(200, confirmed(2, { needsResend: true }));
        await mounted.render(<SalesOrderDocuments order={order({ confirmedAt: '2026-11-12T09:00:00Z' })} />);
        await settle();
        assert.ok($('[data-testid="so-slip-changed"]')!.textContent!.includes('so_slip_changed'));
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[aria-expanded="false"]')!.click(); });
        assert.equal($('[data-testid="so-slip-older"]')!.querySelectorAll('li').length, 1);
        answer = () => json(200, confirmed(2, { needsResend: false }));
        await act(async () => { $('[data-tour="so-slip-sent"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="so-slip-changed"]') === null, 'notice gone');
        assert.ok(calls.some((call) => call.method === 'POST' && call.url.endsWith('/sales-orders/12/documents/102/sent')));
    });

    test('a blocked pop-up says so instead of silently doing nothing', async () => {
        answer = () => json(200, confirmed());
        const originalOpen = window.open;
        window.open = (() => null) as typeof window.open;
        try {
            await mounted.render(<SalesOrderDocuments order={order({ confirmedAt: '2026-11-12T09:00:00Z' })} />);
            await settle();
            await act(async () => { $('[data-tour="so-slip-view"]')!.click(); });
            await mounted.waitFor(() => $('[data-testid="so-slip-popup-blocked"]') !== null, 'blocked notice');
        } finally {
            window.open = originalOpen;
        }
    });

    test('View opens the PDF of that version, Download saves it, Print loads it in a frame', async () => {
        answer = (method, url) => (url.endsWith('/pdf') ? new Response(new Blob(['%PDF-1.7'], { type: 'application/pdf' }), { status: 200 }) : json(200, confirmed()));
        const opened: string[] = [];
        const created: string[] = [];
        const originalOpen = window.open;
        const originalCreate = URL.createObjectURL;
        // Het venster gaat meteen open (bij de klik); de PDF komt erin zodra die er is.
        const fakeWindow = { location: { href: '' }, close: () => undefined };
        window.open = (() => { opened.push('window'); return fakeWindow; }) as unknown as typeof window.open;
        URL.createObjectURL = (() => { created.push('blob:pdf'); return 'blob:pdf'; }) as typeof URL.createObjectURL;
        try {
            await mounted.render(<SalesOrderDocuments order={order({ confirmedAt: '2026-11-12T09:00:00Z' })} />);
            await settle();
            await act(async () => { $('[data-tour="so-slip-view"]')!.click(); });
            await mounted.waitFor(() => fakeWindow.location.href === 'blob:pdf', 'pdf in the window opened on click');
            assert.deepEqual(opened, ['window']);
            assert.ok(calls.some((call) => call.url.endsWith('/sales-orders/12/documents/101/pdf')));
            await act(async () => { $('[data-tour="so-slip-print"]')!.click(); });
            await mounted.waitFor(() => window.document.querySelector('iframe[src="blob:pdf"]') !== null, 'print frame');
        } finally {
            window.open = originalOpen;
            URL.createObjectURL = originalCreate;
            window.document.querySelectorAll('iframe').forEach((frame) => frame.remove());
        }
    });

    test('load time input goes to UTC and back; empty is the delivery date', () => {
        assert.equal(loadAtFromInput(''), null);
        const iso = loadAtFromInput('2026-11-13T06:30')!;
        assert.equal(inputFromLoadAt(iso), '2026-11-13T06:30');
    });

    test('the loading slip settings load and save language, customer and supplier column', async () => {
        answer = (method) => json(200, method === 'PUT' ? { language: 'nl', showCustomer: false, showSupplier: true } : { language: 'en', showCustomer: true, showSupplier: true });
        await mounted.render(<LoadingSlipSetting />);
        await settle();
        const select = mounted.container.querySelector<HTMLSelectElement>('#loading-slip-language')!;
        assert.equal(select.value, 'en');
        await act(async () => { select.value = 'nl'; select.dispatchEvent(new window.Event('change', { bubbles: true })); });
        await act(async () => { mounted.container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[0].click(); });
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => mounted.hasText('loading_slip_saved'), 'saved');
        assert.deepEqual(JSON.parse(calls.find((call) => call.method === 'PUT')!.body!), { language: 'nl', showCustomer: false, showSupplier: true });
    });
});
