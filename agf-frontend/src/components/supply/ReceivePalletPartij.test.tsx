import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json } from '../../test/reactHarness.ts';
import type { SupplyCommitment } from '../../types/supply';
import ReceivePalletForm from './ReceivePalletForm';

// Ontvangst met partij: de partij (en de partij van de leverancier) van de orderregel staat er, er valt niets te typen en het lotnummer
// gaat leeg mee (de server neemt het partijnummer over). Zonder partij: de melding met "Partij kiezen of aanmaken", dat een partij
// aanmaakt en de order koppelt, en daarna de order opnieuw laadt.

const products = [{ id: 1, name: 'Avocado', variants: [{ id: 10, name: 'Hass', origin: 'Peru', originCountryCode: 'PE', packaging: '4 kg doos', sizeCategory: '18', shortcutCode: null, packagingUnit: 'colli', tradeClass: 'I', brand: null, defaultNetKgPerPackage: 4 }] }];
const countries = { countries: [{ code: 'PE', nameNl: 'Peru', nameEn: 'Peru' }], aliases: [] };
const order = (partij: boolean): SupplyCommitment => ({
    id: 46, requestId: 'r-46', vendorId: 1, vendorName: "Nature's Pride", customerId: null, customerName: null, route: 'Warehouse', status: 'Expected',
    origin: 'Peru', destination: null, expectedAt: null, vendorGgns: [], orderNumber: 'IO-2026-0046', supplierReference: 'NP-778', orderedOn: '2026-10-28',
    partijId: partij ? 7 : null, partijNumber: partij ? '2610251' : null, supplierLotReference: partij ? 'NP-LOT-88' : null,
    lines: [{ id: 501, productVariantId: 10, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, partijId: partij ? 7 : null, partijNumber: partij ? '2610251' : null,
        expectedQuantity: 10, expectedUnit: 'colli', expectedNetWeightKg: 40, weightMethod: 'Calculated', defaultNetKgPerPackage: 4, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null }],
});

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let linked: boolean;

const tour = (name: string) => mounted.container.querySelector<HTMLElement>(`[data-tour="${name}"]`);
const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`);

async function arrange() {
    await mounted.render(<ReceivePalletForm preselected={null} onPreselectedConsumed={() => {}} />);
    await mounted.waitFor(() => mounted.container.querySelector('#source-501') !== null, 'sources loaded');
    await act(async () => { mounted.container.querySelector<HTMLInputElement>('#source-501')!.click(); });
    await mounted.waitFor(() => input('line.501.quantity'), 'line fields');
}

describe('ReceivePalletForm and the lot of the order', () => {
    beforeEach(() => {
        calls = [];
        linked = false;
        globalThis.fetch = (async (inputUrl: RequestInfo | URL, init?: RequestInit) => {
            const url = String(inputUrl);
            const call: Call = { method: init?.method ?? 'GET', url, body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.method === 'GET' && url.includes('/supply-commitments')) return json(200, [order(linked || url.includes('linked'))]);
            if (call.method === 'GET' && url.includes('/products')) return json(200, products);
            if (call.method === 'GET' && url.includes('/countries')) return json(200, countries);
            if (call.method === 'GET' && url.includes('/partijen/next-number')) return json(200, { mode: 'Auto', format: '{JJ}{MM}{volgnr}', sequenceReset: 'Yearly', suggestion: '2611001' });
            if (url.endsWith('/partijen')) return json(201, { id: 8, number: '2611001', status: 'Open', note: null, createdAt: '2026-11-04T10:00:00Z', createdBy: 'Admin', linked: false });
            if (url.endsWith('/partijen/8/orders')) { linked = true; return json(200, { id: 8, number: '2611001', status: 'Open', note: null, createdAt: '2026-11-04T10:00:00Z', createdBy: 'Admin', linked: true }); }
            return json(500, {});
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('with a lot on the order line: the lot is shown and nothing is typed', async () => {
        linked = true;
        await arrange();
        assert.equal(tour('receive-partij')!.textContent, '2610251');
        assert.ok(mounted.hasText('NP-LOT-88'));
        assert.equal(input('line.501.lotNumber'), null);
        assert.equal(tour('partij-link'), null);
        assert.equal(tour('receive-line-partij')!.textContent, '2610251');
    });

    test('without a lot: choose or create one, which links the order and reloads it', async () => {
        await arrange();
        assert.ok(tour('partij-link'));
        assert.ok(mounted.hasText('partij_link_none'));
        assert.ok(input('line.501.lotNumber'), 'the old lot field stays as a fallback');
        await act(async () => { tour('partij-link-open')!.click(); });
        await mounted.waitFor(() => input('partij')?.value === '2611001', 'suggestion');
        await act(async () => { tour('partij-link-save')!.click(); });
        await mounted.waitFor(() => tour('receive-partij'), 'lot shown after linking');
        const writes = calls.filter((call) => call.method === 'POST');
        assert.deepEqual(writes.map((call) => new URL(call.url).pathname), ['/api/partijen', '/api/partijen/8/orders']);
        assert.equal(writes[0].body!.number, null);
        assert.deepEqual(writes[1].body, { supplyCommitmentId: 46, reason: null });
    });
});
