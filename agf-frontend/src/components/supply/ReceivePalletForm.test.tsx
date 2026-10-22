import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { SupplyCommitment } from '../../types/supply';
import ReceivePalletForm, { ggnStateKey } from './ReceivePalletForm';

// Palletontvangst: de standaard-GGN van het artikel is een voorstel op de inhoudsregel. De gebruiker kan het per regel
// overnemen, aanpassen of bewust leegmaken; de daadwerkelijk gekozen waarde gaat in de payload van die regel (null = leeg).
// Een ongeldige GGN blokkeert het versturen inline; niets wordt verstuurd.

const commitment: SupplyCommitment = {
    id: 41, requestId: 'r-41', vendorId: 1, vendorName: 'Supplier B', customerId: null, customerName: null, route: 'Warehouse', status: 'Expected',
    origin: 'Peru', destination: null, expectedAt: '2026-10-23T06:00:00.000Z',
    lines: [
        { id: 501, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', lotNumber: 'LOT-A', expectedQuantity: 40, expectedUnit: 'box', expectedNetWeightKg: 160, weightMethod: 'Weighed', defaultNetKgPerPackage: null, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null, defaultGgn: '0049929123456' },
        { id: 502, productVariantId: 3, productName: 'Mango', productVariantName: 'Kent', lotNumber: null, expectedQuantity: 20, expectedUnit: 'box', expectedNetWeightKg: null, weightMethod: 'Weighed', defaultNetKgPerPackage: null, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null, defaultGgn: null },
    ],
};

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const type = async (element: HTMLInputElement, value: string) => { await act(async () => { setValue(element, value); }); };
const text = () => mounted.container.textContent ?? '';
const state = (id: number) => mounted.container.querySelector(`[data-testid="ggn-state-${id}"]`)!.textContent ?? '';
const submit = async () => { await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); }); };
const selectSource = async (lineId: number) => { await act(async () => { mounted.container.querySelector<HTMLInputElement>(`#source-${lineId}`)!.click(); }); };

describe('ReceivePalletForm GGN', () => {
    beforeEach(() => {
        calls = [];
        globalThis.fetch = (async (inputUrl: RequestInfo | URL, init?: RequestInit) => {
            const url = String(inputUrl);
            const call: Call = { method: init?.method ?? 'GET', url, body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.method === 'GET' && url.includes('/supply-commitments')) return json(200, [commitment]);
            if (call.method === 'GET') return json(500, {});
            if (url.endsWith('/pallets')) return json(201, { id: 77, reference: 'P-000077', logisticsStatus: 'Received', locationContext: 'Warehouse', warehouseLocation: null });
            if (url.endsWith('/content-lines')) {
                const body = call.body!;
                return json(201, { id: calls.length, palletId: 77, supplyCommitmentLineId: body.supplyCommitmentLineId, productVariantId: body.supplyCommitmentLineId === 501 ? 1 : 3, lotNumber: body.lotNumber, quantity: body.quantity, unit: 'box', netWeightKg: body.netWeightKg, weightMethod: 'Weighed', netKgPerPackage: null, ggn: body.ggn, qualityStatus: 'Quarantine' });
            }
            return json(404, {});
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('ggnStateKey names the relation to the article default', () => {
        assert.equal(ggnStateKey('0049929123456', '0049929123456'), 'supply_ggn_from_article');
        assert.equal(ggnStateKey(' 0049 929 123456 ', '0049929123456'), 'supply_ggn_from_article');
        assert.equal(ggnStateKey('0049929000002', '0049929123456'), 'supply_ggn_adjusted');
        assert.equal(ggnStateKey('', '0049929123456'), 'supply_ggn_cleared');
        assert.equal(ggnStateKey('', null), 'supply_ggn_none_in_article');
        assert.equal(ggnStateKey('0049929000002', null), null);
    });

    test('the article default is proposed per line, can be adjusted or deliberately cleared, and the chosen value is what is sent', async () => {
        await mounted.render(<ReceivePalletForm actor="wh-1" preselected={null} onPreselectedConsumed={() => {}} />);
        await mounted.waitFor(() => mounted.container.querySelector('#source-501') !== null, 'sources loaded');
        await selectSource(501);
        await selectSource(502);

        assert.equal(input('line.501.ggn').value, '0049929123456', 'proposal taken over from the article (leading zero kept)');
        assert.ok(state(501).includes('supply_ggn_from_article'));
        assert.ok(state(501).includes('supply_ggn_hint'), 'as given; certification not verified');
        assert.equal(input('line.502.ggn').value, '', 'no default on the article = empty');
        assert.ok(state(502).includes('supply_ggn_none_in_article'));

        await type(input('line.501.ggn'), '0049929000002');
        assert.ok(state(501).includes('supply_ggn_adjusted'), 'adjusted against the article default');
        await type(input('line.501.ggn'), '');
        assert.ok(state(501).includes('supply_ggn_cleared'), 'deliberately cleared');

        // Ongeldig blokkeert inline; niets verstuurd.
        await type(input('line.501.lotNumber'), 'LOT-A');
        await type(input('line.501.quantity'), '40');
        await type(input('line.501.netWeightKg'), '160');
        await type(input('line.502.lotNumber'), 'LOT-B');
        await type(input('line.502.quantity'), '20');
        await type(input('line.502.netWeightKg'), '80');
        await type(input('line.502.ggn'), '123');
        await submit();
        assert.equal(calls.filter((call) => call.method === 'POST').length, 0, 'nothing sent');
        assert.ok(text().includes('supply_error_ggn'));
        assert.equal(input('line.502.ggn').getAttribute('aria-invalid'), 'true');

        // Regel 501 bewust leeg, regel 502 handmatig een GGN: precies dat gaat mee.
        await type(input('line.502.ggn'), '4049929000003');
        await submit();
        await mounted.waitFor(() => calls.filter((call) => call.method === 'POST').length === 3, 'pallet + two lines posted');
        const lines = calls.filter((call) => call.url.endsWith('/content-lines')).map((call) => call.body!);
        assert.deepEqual(lines.map((line) => [line.supplyCommitmentLineId, line.ggn]), [[501, null], [502, '4049929000003']]);
        await mounted.waitFor(() => text().includes('supply_receive_success'), 'success panel');
        assert.ok(text().includes('4049929000003'), 'chosen GGN shown on the saved line');
    });
});
