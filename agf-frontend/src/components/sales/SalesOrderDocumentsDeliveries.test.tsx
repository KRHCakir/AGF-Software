import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json } from '../../test/reactHarness.ts';
import type { SalesOrder } from '../../types/salesOrder';
import type { SalesOrderDocuments as Documents } from '../../types/salesOrderDocument';
import SalesOrderDocuments, { deliveryGroups } from './SalesOrderDocuments';

// Feedback 22 november: laadbon en transportopdracht per levering. Met één levering blijft het scherm zoals het was (geen kop); met twee
// leveringen een blok per levering met haar V-nummer, eigen laden op en kenteken, en eigen versies.

const order = { id: 12, orderNumber: 'VO-2026-0012', status: 'Open', version: 3, requestedDeliveryDate: '2026-11-25T00:00:00Z', lines: [{ id: 1 }] } as unknown as SalesOrder;
const slip = (id: number, deliveryId: number, sequence: number, number: string, version = 1, latest = true) => ({
    id, kind: 'LoadingSlip', number, version, reason: version === 1 ? 'Confirmed' : 'Changed', createdAt: '2026-11-23T09:00:00Z', createdBy: 'Jan',
    sentAt: null, sentBy: null, latest, salesDeliveryId: deliveryId, deliverySequence: sequence,
});
const documents = (): Documents => ({
    salesOrderId: 12, confirmedAt: '2026-11-23T09:00:00Z', confirmedBy: 'Jan', loadAt: null, licensePlate: '12-ABC-3', needsResend: true, hasTransport: true,
    documents: [slip(300, 1, 1, 'LB-2026-0012', 1, false), slip(301, 1, 1, 'LB-2026-0012', 2), slip(302, 2, 2, 'LB-2026-0012-2')],
    deliveries: [
        { salesDeliveryId: 1, sequence: 1, partijNumber: 'V-2611001', loadAt: null, licensePlate: '12-ABC-3', needsResend: true, transportOrderNeedsResend: false },
        { salesDeliveryId: 2, sequence: 2, partijNumber: 'V-2611002', loadAt: null, licensePlate: null, needsResend: false, transportOrderNeedsResend: false },
    ],
});

let mounted: Mounted;
let calls: { method: string; url: string; body: string | null }[];
const all = (selector: string) => Array.from(mounted.container.querySelectorAll<HTMLElement>(selector));

describe('sales order documents per delivery', () => {
    beforeEach(() => {
        calls = [];
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            calls.push({ method: init?.method ?? 'GET', url: String(input), body: init?.body ? String(init.body) : null });
            return json(200, documents());
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('one delivery (or an older server without deliveries) is one group with all documents', () => {
        const single = { ...documents(), deliveries: undefined };
        const groups = deliveryGroups(single);
        assert.equal(groups.length, 1);
        assert.equal(groups[0].documents.length, 3);
        assert.equal(groups[0].licensePlate, '12-ABC-3');
    });

    test('two deliveries give a block each with its own versions; changing the loading time of delivery 2 sends its id', async () => {
        await mounted.render(<SalesOrderDocuments order={order} />);
        await mounted.waitFor(() => (all('[data-testid="so-delivery-documents"]').length === 2 ? true : null), 'two blocks');
        const blocks = all('[data-testid="so-delivery-documents"]');
        assert.ok(blocks[1].textContent!.includes('so_delivery_heading'));
        assert.equal(blocks[0].querySelectorAll('[data-testid="so-slip-changed"]').length, 1, 'only delivery 1 has a newer slip to pass on');
        assert.equal(blocks[1].querySelectorAll('[data-testid="so-slip-changed"]').length, 0);
        assert.equal(blocks[1].querySelectorAll('[data-testid="so-slip-latest"]').length, 1);
        await act(async () => { (blocks[1].querySelector('[data-tour="so-loading-edit"]') as HTMLElement).click(); });
        await act(async () => { (blocks[1].querySelector('[data-testid="so-loading-form"] button[type="submit"]') as HTMLElement).click(); });
        await mounted.waitFor(() => (calls.some((call) => call.method === 'PUT') ? true : null), 'loading saved');
        const put = calls.find((call) => call.method === 'PUT')!;
        assert.ok(put.url.endsWith('/sales-orders/12/loading'));
        assert.equal((JSON.parse(put.body!) as { salesDeliveryId: number }).salesDeliveryId, 2);
    });
});
