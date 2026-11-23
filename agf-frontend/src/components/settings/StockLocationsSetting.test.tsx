import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, Posted, click, json, setSelectValue, setValue, stubFetch } from '../../test/reactHarness.ts';
import StockLocationsSetting from './StockLocationsSetting';

// Feedback 22 november: Instellingen › Voorraadlocaties. De eigen loods staat er altijd; een nieuwe locatie krijgt naam, soort en adres; een
// gebruikte locatie houdt haar soort.
const stored = [{ id: 4, name: 'Cross-dock Venlo', kind: 'Crossdock', address: 'Fictief Cross-dock\\nVenlo', countryCode: 'NL', isActive: true, inUse: true, version: 2 }];
let mounted: Mounted;
let posted: Posted[];
let puts: Posted[];

describe('StockLocationsSetting', () => {
    beforeEach(() => {
        puts = [];
        posted = stubFetch({
            onGet: (url) => url.includes('/countries') ? json(200, { countries: [{ code: 'GR', nameNl: 'Griekenland', nameEn: 'Greece' }, { code: 'NL', nameNl: 'Nederland', nameEn: 'Netherlands' }], aliases: [] }) : json(200, stored),
            nextPost: async () => json(201, { id: 5, name: 'Onderweg naar Griekenland', kind: 'InTransit', address: null, countryCode: null, isActive: true, inUse: false, version: 0 }),
        });
        const post = globalThis.fetch;
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            if (init?.method === 'PUT') { puts.push({ url: String(input), body: String(init.body) }); return json(200, { ...stored[0], address: 'Nieuw adres', version: 3 }); }
            return post(input, init);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('lists the own warehouse and the locations, adds one and keeps the kind of a used one', async () => {
        await mounted.render(<StockLocationsSetting />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="stock-location-4"]'), 'list');
        assert.ok(mounted.hasText('stock_location_own'));

        await click(mounted.container.querySelector<HTMLButtonElement>('[data-tour="stock-location-new"]')!);
        await act(async () => setValue(mounted.container.querySelector<HTMLInputElement>('[data-tour="stock-location-name"]')!, 'Onderweg naar Griekenland'));
        assert.equal(mounted.container.querySelector<HTMLSelectElement>('[data-tour="stock-location-kind"]')!.value, 'InTransit', 'in transit is the default for a new location');
        await act(async () => { mounted.container.querySelector<HTMLFormElement>('[data-testid="stock-location-form"]')!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="stock-location-5"]'), 'new location listed');
        assert.deepEqual(JSON.parse(posted[0].body), { name: 'Onderweg naar Griekenland', kind: 'InTransit', address: null, countryCode: null, isActive: true });

        await click(mounted.container.querySelector<HTMLButtonElement>('[data-tour="stock-location-edit"][data-tour-key="4"]')!);
        const kind = mounted.container.querySelector<HTMLSelectElement>('[data-tour="stock-location-kind"]')!;
        assert.equal(kind.disabled, true, 'a used location keeps its kind');
        await act(async () => setSelectValue(kind, 'Crossdock'));
        await act(async () => setValue(mounted.container.querySelector<HTMLTextAreaElement>('[data-tour="stock-location-address"]')!, 'Nieuw adres'));
        await act(async () => { mounted.container.querySelector<HTMLFormElement>('[data-testid="stock-location-form"]')!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => puts.length === 1, 'PUT');
        assert.ok(puts[0].url.endsWith('/stock-locations/4'));
        assert.equal(JSON.parse(puts[0].body).expectedVersion, 2);
    });
});
