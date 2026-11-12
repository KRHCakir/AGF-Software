import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, before, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { SalesOrder } from '../../types/salesOrder';
import type { SalesOrderTransport as Transport } from '../../types/salesOrderTransport';
import SalesOrderTransport from './SalesOrderTransport';

// Documenten deel 2: het blok Transport op de verkooporder. Alleen invullen als er transport is; vervoerder kiezen of snel aanmaken, of eigen
// transport; datums met het gewone datumveld (tweecijferig jaar); na opslaan haalt de pagina de order en de documenten opnieuw op.

const order = (over: Partial<SalesOrder> = {}) => ({ id: 12, orderNumber: 'VO-2026-0012', status: 'Open', version: 3, lines: [{ id: 1 }], ...over }) as unknown as SalesOrder;
const transport = (over: Partial<Transport> = {}): Transport => ({
    salesOrderId: 12, exists: false, mode: null, carrierRelationId: null, carrierName: null, carrierAddress: null, loadingPlace: null, defaultLoadingPlace: 'Mijn AGF Bedrijf, Loodsweg 1',
    unloadingAddressId: null, unloadingPlace: null, defaultUnloadingPlace: 'Groothandel Noord, DC Noord, Havenweg 4', unloadingAddresses: [{ id: 3, purpose: 'Delivery', text: 'DC Noord, Havenweg 4', isDefault: true }],
    loadAt: null, unloadDate: null, temperatureC: null, loadingReference: null, unloadingReference: null, defaultUnloadingReference: 'PO-778', remarks: null, palletKind: null,
    defaultPalletKind: 'Europallet', version: 0, hasTransportOrder: false, ...over,
});
const carrierList = { total: 1, page: 1, pageSize: 200, items: [{ id: 7, searchCode: 'TRZEE', name: 'Transport Zeeland B.V.', tradeName: null, customerId: null, vendorId: null, isCarrier: true, isActive: true, city: 'Vlissingen', countryCode: 'NL', email: null, phone: null, vatNumber: null, version: 0, primaryContactName: null }] };

let mounted: Mounted;
let calls: { method: string; url: string; body: string | null }[];
let answer: (method: string, url: string, body: string | null) => Response;
const $ = <T extends HTMLElement = HTMLElement>(selector: string) => mounted.container.querySelector<T>(selector);
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });
const combobox = () => $<HTMLInputElement>('[data-tour="so-transport-carrier"] input[role="combobox"], input[role="combobox"]')!;
const key = async (name: string) => { await act(async () => { combobox().dispatchEvent(new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })); }); };
const type = async (element: HTMLInputElement | HTMLTextAreaElement, text: string) => { await act(async () => { setValue(element, text); }); };

describe('sales order transport block', () => {
    before(() => { process.env.TZ = 'Europe/Amsterdam'; });
    beforeEach(() => {
        calls = [];
        answer = (method, url) => (url.includes('/relations') ? json(200, carrierList) : json(200, transport()));
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const body = init?.body ? String(init.body) : null;
            calls.push({ method: init?.method ?? 'GET', url: String(input), body });
            return answer(init?.method ?? 'GET', String(input), body);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('no transport yet: fill it in with a carrier, dates with a two-digit year, and save; the page fetches the order again', async () => {
        let changed = 0;
        await mounted.render(<SalesOrderTransport order={order()} onChanged={() => { changed++; }} />);
        await settle();
        assert.ok(mounted.hasText('so_transport_none'));
        await act(async () => { $('[data-tour="so-transport-add"]')!.click(); });
        await settle();
        assert.ok(calls.some((call) => call.url.includes('/relations?') && call.url.includes('role=carrier')));
        // Zonder keuze opslaan: eerst kiezen.
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        assert.ok($('[data-testid="transport-problem-mode"]'));
        await act(async () => { $<HTMLInputElement>('[data-tour="so-transport-mode-carrier"]')!.click(); });
        await type(combobox(), 'zeel');
        await key('Enter');
        await type($<HTMLInputElement>('#transport-load-date-12')!, '13-11-26');
        await act(async () => { $<HTMLInputElement>('#transport-load-date-12')!.dispatchEvent(new window.FocusEvent('blur', { bubbles: true })); $<HTMLInputElement>('#transport-load-date-12')!.dispatchEvent(new window.FocusEvent('focusout', { bubbles: true })); });
        await type($<HTMLInputElement>('#transport-load-time-12')!, '6.30');
        await type($<HTMLInputElement>('#transport-temperature-12')!, '2');
        await act(async () => { const select = $<HTMLSelectElement>('#transport-unloading-12')!; select.value = '3'; select.dispatchEvent(new window.Event('change', { bubbles: true })); });
        answer = (method, url) => (url.includes('/relations') ? json(200, carrierList) : json(200, transport({ exists: true, mode: 'Carrier', carrierRelationId: 7, carrierName: 'Transport Zeeland B.V.', loadAt: '2026-11-13T05:30:00Z', temperatureC: 2, unloadingAddressId: 3, version: 1 })));
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => $('[data-testid="so-transport-summary"]'), 'summary shown');
        const put = JSON.parse(calls.find((call) => call.method === 'PUT')!.body!);
        assert.deepEqual([put.mode, put.carrierRelationId, put.loadAt, put.temperatureC, put.unloadingAddressId, put.expectedVersion, put.loadingPlace], ['Carrier', 7, '2026-11-13T05:30:00.000Z', 2, 3, null, null]);
        assert.ok(calls.find((call) => call.method === 'PUT')!.url.endsWith('/sales-orders/12/transport'));
        assert.equal(changed, 1);
        assert.ok(mounted.hasText('Transport Zeeland B.V.'));
    });

    test('a new carrier is created with only the name and the carrier role; a similar name offers the existing relation', async () => {
        await mounted.render(<SalesOrderTransport order={order()} />);
        await settle();
        await act(async () => { $('[data-tour="so-transport-add"]')!.click(); });
        await settle();
        await act(async () => { $<HTMLInputElement>('[data-tour="so-transport-mode-carrier"]')!.click(); });
        await type(combobox(), 'Snelvracht Oost');
        answer = (method, url) => (method === 'POST' && url.endsWith('/relations')
            ? json(409, { message: 'similar', code: 'similar-names', similarRelations: [{ id: 9, searchCode: null, name: 'Snelvracht Oost B.V.', customerId: null, vendorId: 4 }] })
            : url.includes('/roles/carrier') ? json(201, { id: 9, name: 'Snelvracht Oost B.V.' }) : json(200, carrierList));
        await act(async () => { $('[data-tour="so-transport-carrier-new"]')!.click(); });
        await mounted.waitFor(() => $('[data-testid="so-transport-carrier-similar"]'), 'similar names');
        const post = JSON.parse(calls.find((call) => call.method === 'POST')!.body!);
        assert.deepEqual([post.name, post.carrier, post.acknowledgeSimilarNames], ['Snelvracht Oost', true, false]);
        assert.ok(typeof post.requestId === 'string' && post.requestId.length > 0);
        assert.ok(mounted.hasText('so_transport_carrier_similar'));
    });

    test('own transport has no carrier field; an existing transport order cannot be removed; a cancelled order cannot be changed', async () => {
        answer = (method, url) => (url.includes('/relations') ? json(200, carrierList) : json(200, transport({ exists: true, mode: 'Own', version: 2, hasTransportOrder: true })));
        await mounted.render(<SalesOrderTransport order={order()} />);
        await settle();
        assert.ok(mounted.hasText('so_transport_mode_own'));
        await act(async () => { $('[data-tour="so-transport-edit"]')!.click(); });
        await settle();
        assert.equal($('[data-tour="so-transport-carrier"]'), null);
        assert.equal(mounted.hasText('so_transport_remove'), false);
        // Tab-volgorde: keuze, laaddatum, laadtijd, losdatum, temperatuur, laadplaats, losplaats (zoals op het scherm).
        const order_ = Array.from(mounted.container.querySelectorAll<HTMLElement>('[data-tour^="so-transport-"]')).map((element) => element.dataset.tour);
        assert.deepEqual(order_.filter((name) => !['so-transport-edit'].includes(name!)).slice(0, 8),
            ['so-transport-mode-carrier', 'so-transport-mode-own', 'so-transport-load-date', 'so-transport-load-time', 'so-transport-unload-date', 'so-transport-temperature', 'so-transport-loading-place', 'so-transport-unloading']);
        // Intussen elders gewijzigd (de order kreeg een nieuwe versie, het transport versie 3): opslaan stuurt de versie waarmee het formulier openging.
        answer = (method, url) => (url.includes('/relations') ? json(200, carrierList) : json(200, transport({ exists: true, mode: 'Own', version: 3, hasTransportOrder: true })));
        await mounted.render(<SalesOrderTransport order={order({ version: 5 })} />);
        await settle();
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await settle();
        assert.equal(JSON.parse(calls.filter((call) => call.method === 'PUT').slice(-1)[0].body!).expectedVersion, 2);
        await mounted.render(<SalesOrderTransport order={order({ status: 'Cancelled', version: 4 })} />);
        await settle();
        assert.equal($('[data-tour="so-transport-edit"]'), null);
    });
});
