import '../../../test/setupDom.ts';
import '../../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setSelectValue, setValue, stubFetch } from '../../../test/reactHarness.ts';
import { navigate } from '../../../test/nextNavigationStub.ts';
import type { PurchaseOrderDetail, PurchaseOrderListItem } from '../../../types/purchaseOrder';
import PurchaseOrdersPage from './page';

// Inkoopopdrachten: standaard open orders, zoeken op factuurnummer gaat naar de server en klikken opent het detail.

const item = (overrides: Partial<PurchaseOrderListItem> = {}): PurchaseOrderListItem => ({
    id: 7, orderNumber: 'IO-2026-0007', vendorId: 3, vendorName: "Nature's Pride", supplierReference: 'NP-88123', orderedOn: '2026-10-27',
    expectedAt: '2026-10-28T23:00:00Z', route: 'Warehouse', customerName: null, status: 'Received', storedStatus: 'Expected',
    expectedQuantity: 221, expectedNetWeightKg: 884, receivedQuantity: 221, rejectedQuantity: 0,
    invoiceNumbers: [{ kind: 'Invoice', invoiceNumber: 'F-4711' }], invoiceIndicator: 'Received', ...overrides,
});

const detail = (overrides: Partial<PurchaseOrderDetail> = {}): PurchaseOrderDetail => ({
    id: 7, orderNumber: 'IO-2026-0007', vendorId: 3, vendorName: "Nature's Pride", supplierReference: 'NP-88123', orderedOn: '2026-10-27',
    orderChannel: 'Phone', note: null, route: 'Warehouse', customerName: null, origin: 'Peru', destination: null, expectedAt: '2026-10-28T23:00:00Z',
    status: 'Received', storedStatus: 'Expected', invoiceIndicator: 'Received', expectedQuantity: 221, expectedNetWeightKg: 884,
    receivedQuantity: 221, receivedNetWeightKg: 884, rejectedQuantity: 0, rejectedNetWeightKg: 0,
    lines: [{ id: 70, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, expectedQuantity: 221, unit: 'box', expectedNetWeightKg: 884, receivedQuantity: 221, receivedNetWeightKg: 884, rejectedQuantity: 0, rejectedNetWeightKg: 0 }],
    receipts: [{ palletContentLineId: 40, palletId: 9, palletReference: 'P-000009', palletStatus: 'Received', supplyCommitmentLineId: 70, productName: 'Avocado', productVariantName: 'Hass',
        lotNumber: 'LOT-1', quantity: 221, unit: 'box', netWeightKg: 884, weightMethod: 'Weighed', netKgPerPackage: null, onHandQuantity: 221, onHandNetWeightKg: 884,
        reservedQuantity: 0, reservedNetWeightKg: 0, qualityStatus: 'Approved', qualityReason: null, origin: 'Receipt', receivedAt: '2026-10-27T10:00:00Z',
        rejectable: true, rejectableQuantity: 221, rejectableNetWeightKg: 884, version: 3 }],
    invoiceRefs: [{ id: 1, kind: 'Invoice', invoiceNumber: 'F-4711', invoiceDate: '2026-10-27', amountExclVat: 1234.5, incomingInvoiceId: null, actorReference: null, createdAt: '2026-10-27T11:00:00Z' }],
    rejections: [], history: [{ occurredAt: '2026-10-27T09:00:00Z', type: 'Ordered', actorReference: null, reference: 'NP-88123', channel: 'Phone', quantity: null, unit: null, fromStatus: null, toStatus: null, reason: null, decision: null }],
    ...overrides,
});

let mounted: Mounted;
let listUrls: string[];
let current: PurchaseOrderDetail;
let listItems: PurchaseOrderListItem[];

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const select = (id: string) => mounted.container.querySelector<HTMLSelectElement>(`[id="${id}"]`)!;

describe('Purchase orders page', () => {
    beforeEach(() => {
        window.localStorage.setItem('agf_actor_reference', 'Jan');
        listUrls = [];
        current = detail();
        listItems = [item()];
        stubFetch({
            onGet: (url) => {
                if (/\/incominginvoices\/vendors$/.test(url)) return json(200, [{ id: 3, name: "Nature's Pride" }]);
                if (/\/purchase-orders\?/.test(url)) { listUrls.push(url); return json(200, { items: listItems, total: listItems.length, page: 1, pageSize: 50 }); }
                if (/\/purchase-orders\/7$/.test(url)) return json(200, current);
                return json(404, { message: `unexpected GET ${url}` });
            },
            nextPost: async () => json(500, { message: 'no writes expected' }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('defaults to open orders and searches on the invoice number on the server', async () => {
        navigate('/purchase-orders');
        await mounted.render(<PurchaseOrdersPage />);
        await mounted.waitFor(() => mounted.hasText('IO-2026-0007'), 'list');
        assert.ok(listUrls[0].includes('status=open'));
        assert.ok(mounted.hasText('F-4711'));

        // De server zoekt (na een korte debounce); zoeken zet Open op Alle, leegmaken zet het terug.
        await act(async () => { setValue(input('po-search'), 'F-4711'); await new Promise((resolve) => setTimeout(resolve, 300)); });
        await mounted.waitFor(() => listUrls.some((url) => url.includes('query=F-4711') && url.includes('status=all')), 'search request');
        assert.equal(select('po-status').value, 'all');
        await act(async () => { setValue(input('po-search'), ''); await new Promise((resolve) => setTimeout(resolve, 300)); });
        assert.equal(select('po-status').value, 'open');
        await act(async () => { setSelectValue(select('po-status'), 'Rejected'); });
        await mounted.waitFor(() => listUrls.some((url) => url.includes('status=Rejected')), 'status filter');
    });

    test('opens the order detail with its own number, invoices and history', async () => {
        navigate('/purchase-orders');
        await mounted.render(<PurchaseOrdersPage />);
        const openButton = await mounted.waitFor(() => mounted.buttons().find((b) => b.getAttribute('aria-label') === 'po_open_order'), 'open button');
        await click(openButton);
        await mounted.waitFor(() => mounted.container.querySelector('#po-detail-title')?.textContent === 'IO-2026-0007', 'detail');
        assert.ok(mounted.hasText('NP-88123'));
        assert.ok(mounted.hasText('po_history_ordered'));
        assert.ok(mounted.hasText('F-4711'));
    });
});
