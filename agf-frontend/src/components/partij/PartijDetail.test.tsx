import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { PartijDetail as Detail } from '../../types/partij';
import PartijDetail from './PartijDetail';

// Partijdetail door de echte componentlevenscyclus: inkooporders, pallets en verkoop als links (geen dode links), het rustige blokje voor
// kosten en resultaat, "Nummer corrigeren" alleen met reden (dubbel nummer gemeld) en verwijderen alleen zonder koppelingen.

const detail = (over: Partial<Detail> = {}): Detail => ({
    id: 7, number: '2610251', status: 'Open', createdAt: '2026-10-27T09:00:00Z', vendors: ['Fruitimport Oost', "Nature's Pride"], products: ['Avocado Hass'],
    supplierLotReferences: ['NP-LOT-88'], unit: 'box', purchasedQuantity: 150, receivedQuantity: 70, onHandQuantity: 60, inInspectionQuantity: 10, openExpectedQuantity: 80,
    soldQuantity: 15, note: null, createdBy: 'Admin', linked: true, reservedQuantity: 15, shippedQuantity: 0,
    orders: [{ id: 46, orderNumber: 'IO-2026-0046', vendorName: "Nature's Pride", status: 'PartiallyReceived', orderedOn: '2026-10-27', expectedAt: null, supplierLotReference: 'NP-LOT-88', unit: 'box', expectedQuantity: 100, receivedQuantity: 60 }],
    receipts: [{ palletContentLineId: 900, palletId: 77, palletReference: 'P-000077', palletStatus: 'Received', supplyCommitmentId: 46, orderNumber: 'IO-2026-0046', productName: 'Avocado',
        productVariantName: 'Hass', lotNumber: '2610251', kind: 'Receipt', quantity: 60, unit: 'box', netWeightKg: 240, onHandQuantity: 60, qualityStatus: 'Approved', receivedAt: '2026-10-28T08:00:00Z' }],
    salesOrders: [{ salesOrderId: 12, orderNumber: 'VO-2026-0012', customerName: 'Groothandel Jansen', status: 'Open', unit: 'box', reservedQuantity: 15, shippedQuantity: 0 }],
    history: [{ occurredAt: '2026-10-27T09:00:00Z', type: 'Created', oldValue: null, newValue: '2610251', reason: null, supplyCommitmentId: null, actorReference: 'Admin' }],
    ...over,
});

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let correctAnswer: () => Response;

const tour = (name: string) => mounted.container.querySelector<HTMLElement>(`[data-tour="${name}"]`);
const hrefs = () => Array.from(mounted.container.querySelectorAll<HTMLAnchorElement>('a')).map((a) => a.getAttribute('href'));

describe('PartijDetail', () => {
    beforeEach(() => {
        calls = [];
        correctAnswer = () => json(200, { id: 7, number: '2610252', status: 'Open', note: null, createdAt: '2026-10-27T09:00:00Z', createdBy: 'Admin', linked: true });
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const call: Call = { method: init?.method ?? 'GET', url: String(input), body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.method === 'DELETE') return new Response(null, { status: 204 });
            return correctAnswer();
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('orders, pallets and sales are links; costs and result are a calm note, nothing else', async () => {
        await mounted.render(<PartijDetail partij={detail()} onChanged={() => {}} onDeleted={() => {}} />);
        assert.deepEqual(hrefs(), ['/purchase-orders?id=46', '/inspection?palletId=77', '/purchase-orders?id=46', '/sales-orders?orderId=12']);
        assert.ok(tour('partij-costs-later')!.textContent!.includes('partij_costs_later'));
        assert.ok(mounted.hasText('Groothandel Jansen') && mounted.hasText('NP-LOT-88'));
        // Gekoppeld: niet te verwijderen, het nummer ligt vast.
        assert.equal(tour('partij-delete'), null);
        assert.ok(mounted.hasText('partij_fixed_hint'));
    });

    test('correcting the number needs a reason; a duplicate number is named; then the correction is sent', async () => {
        let changed = 0;
        await mounted.render(<PartijDetail partij={detail()} onChanged={() => { changed++; }} onDeleted={() => {}} />);
        await act(async () => { tour('partij-correct-open')!.click(); });
        const number = mounted.container.querySelector<HTMLInputElement>('[id="partijCorrectNumber-input"]')!;
        const reason = mounted.container.querySelector<HTMLInputElement>('[id="partijCorrectReason-input"]')!;
        await act(async () => { setValue(number, '2610252'); });
        await act(async () => { tour('partij-correct-save')!.click(); });
        assert.ok(mounted.hasText('partij_link_reason_required'));
        assert.equal(calls.length, 0);

        correctAnswer = () => json(409, { message: 'exists', code: 'PARTIJ_EXISTS', partij: { id: 8, number: '2610252', status: 'Open', note: null, createdAt: '2026-10-27T09:00:00Z', createdBy: 'Admin', linked: false } });
        await act(async () => { setValue(reason, 'Typefout'); });
        await act(async () => { tour('partij-correct-save')!.click(); });
        await mounted.waitFor(() => mounted.hasText('partij_correct_exists'), 'duplicate named');
        correctAnswer = () => json(200, { id: 7, number: '2610253', status: 'Open', note: null, createdAt: '2026-10-27T09:00:00Z', createdBy: 'Admin', linked: true });
        await act(async () => { setValue(number, '2610253'); });
        await act(async () => { tour('partij-correct-save')!.click(); });
        await mounted.waitFor(() => changed === 1, 'changed');
        assert.deepEqual(calls.at(-1)!.body, { number: '2610253', reason: 'Typefout' });
    });

    test('an unlinked lot can be deleted after confirming', async () => {
        let deleted = false;
        await mounted.render(<PartijDetail partij={detail({ linked: false, orders: [], receipts: [], salesOrders: [] })} onChanged={() => {}} onDeleted={() => { deleted = true; }} />);
        await act(async () => { tour('partij-delete')!.click(); });
        await act(async () => { tour('partij-delete-confirm')!.click(); });
        await mounted.waitFor(() => deleted, 'deleted');
        assert.equal(calls[0].method, 'DELETE');
        assert.ok(calls[0].url.endsWith('/partijen/7'));
    });
});
