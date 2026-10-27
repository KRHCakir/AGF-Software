import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { SupplyCommitment } from '../../types/supply';
import ReceivePalletForm, { ggnCertificateWarning, ggnStateKey, proposedGgn } from './ReceivePalletForm';

// Palletontvangst: de standaard-GGN van het artikel is een voorstel op de inhoudsregel. De gebruiker kan het per regel
// overnemen, aanpassen of bewust leegmaken; de daadwerkelijk gekozen waarde gaat in de payload van die regel (null = leeg).
// Een ongeldige GGN blokkeert het versturen inline; niets wordt verstuurd.

const commitment: SupplyCommitment = {
    id: 41, requestId: 'r-41', vendorId: 1, vendorName: 'Supplier B', customerId: null, customerName: null, route: 'Warehouse', status: 'Expected',
    origin: 'Peru', destination: null, expectedAt: '2026-10-23T06:00:00.000Z', vendorGgns: [{ ggn: '0049929123456', producerName: 'Finca Uno', checkedOn: '2026-10-01', certificateValidUntil: '2099-12-31', hasGgnLabel: true }],
    lines: [
        { id: 501, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', lotNumber: 'LOT-A', expectedQuantity: 40, expectedUnit: 'box', expectedNetWeightKg: 160, weightMethod: 'Weighed', defaultNetKgPerPackage: null, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null },
        { id: 502, productVariantId: 3, productName: 'Mango', productVariantName: 'Kent', lotNumber: null, expectedQuantity: 20, expectedUnit: 'box', expectedNetWeightKg: null, weightMethod: 'Weighed', defaultNetKgPerPackage: null, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null },
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

    test('a supplier GGN is proposed only when exactly one applies; with several the user has to choose', () => {
        const check = { checkedOn: '2026-10-01', certificateValidUntil: '2027-03-31', hasGgnLabel: true };
        const one = [{ ggn: '0049929123456', producerName: null, ...check }];
        const two = [{ ggn: '0049929123456', producerName: 'A', ...check }, { ggn: '0049929000002', producerName: 'B', ...check }];
        assert.equal(proposedGgn(one), '0049929123456');
        assert.equal(proposedGgn(two), null);
        assert.equal(proposedGgn([]), null);
        assert.equal(ggnStateKey('0049929123456', one), 'supply_ggn_from_vendor');
        assert.equal(ggnStateKey(' 0049 929 123456 ', one), 'supply_ggn_from_vendor');
        assert.equal(ggnStateKey('0049929000009', one), 'supply_ggn_adjusted');
        assert.equal(ggnStateKey('', one), 'supply_ggn_cleared');
        assert.equal(ggnStateKey('', two), 'supply_ggn_choose');
        assert.equal(ggnStateKey('0049929000002', two), 'supply_ggn_chosen');
        assert.equal(ggnStateKey('0049929000009', two), 'supply_ggn_adjusted');
        assert.equal(ggnStateKey('', []), 'supply_ggn_none_in_vendor');
        assert.equal(ggnStateKey('0049929000002', []), null);
    });

    test('a chosen supplier GGN warns (never blocks) when its certificate expired or it was never checked', () => {
        const options = [
            { ggn: '0049929123456', producerName: null, checkedOn: '2026-10-01', certificateValidUntil: '2026-10-25', hasGgnLabel: true },
            { ggn: '0049929000002', producerName: null, checkedOn: null, certificateValidUntil: null, hasGgnLabel: false },
            { ggn: '0049929000003', producerName: null, checkedOn: '2026-10-01', certificateValidUntil: '2026-10-26', hasGgnLabel: true },
        ];
        assert.equal(ggnCertificateWarning('0049 929 123456', options, '2026-10-26'), 'supply_ggn_certificate_expired');
        assert.equal(ggnCertificateWarning('0049929000002', options, '2026-10-26'), 'supply_ggn_never_checked');
        assert.equal(ggnCertificateWarning('0049929000003', options, '2026-10-26'), null, 'valid through today');
        assert.equal(ggnCertificateWarning('0049929000009', options, '2026-10-26'), null, 'typed GGN outside the supplier list');
        assert.equal(ggnCertificateWarning('', options, '2026-10-26'), null);
    });

    test('the quantity shows the kg line from the article defaults; the source shows its pallets with loose colli', async () => {
        const original = commitment.lines[1];
        commitment.lines[1] = { ...original, expectedQuantity: 375, defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3, palletCount: 3, packagesPerPallet: 120, loosePackages: 15 };
        try {
            await mounted.render(<ReceivePalletForm actor="wh-1" preselected={null} onPreselectedConsumed={() => {}} />);
            await mounted.waitFor(() => mounted.container.querySelector('#source-502') !== null, 'sources loaded');
            assert.ok(text().includes('pallet_breakdown_loose'));
            await selectSource(502);
            await type(input('line.502.quantity'), '1.560');
            assert.ok(mounted.container.querySelector('[data-testid="line.502.quantity-kg"]'), 'kg line instead of the plain echo');
            assert.equal(mounted.container.querySelector('[data-testid="line.502.quantity-echo"]'), null);
        } finally {
            commitment.lines[1] = original;
        }
    });

    test('the single supplier GGN is proposed per line, can be adjusted or deliberately cleared, and the chosen value is what is sent', async () => {
        await mounted.render(<ReceivePalletForm actor="wh-1" preselected={null} onPreselectedConsumed={() => {}} />);
        await mounted.waitFor(() => mounted.container.querySelector('#source-501') !== null, 'sources loaded');
        await selectSource(501);
        await selectSource(502);

        assert.equal(input('line.501.ggn').value, '0049929123456', 'the single supplier GGN is proposed (leading zero kept)');
        assert.ok(state(501).includes('supply_ggn_from_vendor'));
        assert.ok(!state(501).includes('supply_ggn_hint'), 'no explanation of internal workings next to the GGN');
        assert.equal(input('line.502.ggn').value, '0049929123456', 'same supplier, same single proposal on every line');
        await type(input('line.502.ggn'), '');
        assert.ok(state(502).includes('supply_ggn_cleared'));

        await type(input('line.501.ggn'), '0049929000002');
        assert.ok(state(501).includes('supply_ggn_adjusted'), 'adjusted against the supplier GGN');
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

    test('with several supplier GGNs nothing is proposed and the user chooses from the list or types another', async () => {
        const two = { ...commitment, vendorGgns: [{ ggn: '0049929123456', producerName: 'Finca Uno', checkedOn: '2026-10-01', certificateValidUntil: '2099-12-31', hasGgnLabel: true }, { ggn: '0049929000002', producerName: 'Finca Dos', checkedOn: null, certificateValidUntil: null, hasGgnLabel: false }] };
        globalThis.fetch = (async (inputUrl: RequestInfo | URL) => (String(inputUrl).includes('/supply-commitments') ? json(200, [two]) : json(500, {}))) as typeof fetch;
        await mounted.render(<ReceivePalletForm actor="wh-1" preselected={null} onPreselectedConsumed={() => {}} />);
        await mounted.waitFor(() => mounted.container.querySelector('#source-501') !== null, 'sources loaded');
        await selectSource(501);
        assert.equal(input('line.501.ggn').value, '', 'nothing proposed with two candidates');
        assert.ok(state(501).includes('supply_ggn_choose'));
        const choice = mounted.container.querySelector<HTMLSelectElement>('[data-testid="ggn-choice-501"]')!;
        assert.equal(choice.options.length, 3);
        await act(async () => { const setter = Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype, 'value')!.set!; setter.call(choice, '0049929000002'); choice.dispatchEvent(new window.Event('change', { bubbles: true })); });
        assert.equal(input('line.501.ggn').value, '0049929000002');
        assert.ok(state(501).includes('supply_ggn_chosen'));
        await type(input('line.501.ggn'), '0049929000009');
        assert.ok(state(501).includes('supply_ggn_adjusted'));
    });
});
