import '../../../test/setupDom.ts';
import '../../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setSelectValue, setValue, stubFetch, type Posted } from '../../../test/reactHarness.ts';
import { navigate } from '../../../test/nextNavigationStub.ts';
import type { PurchaseOrderDetail, PurchaseOrderListItem } from '../../../types/purchaseOrder';
import PurchaseOrdersPage from './page';

// Inkoopopdrachten: zoeken op factuurnummer gaat naar de server, klikken opent het detail, en "Afkeuren" stuurt de
// gekozen hoeveelheid met reden, besluit en medewerker; daarna tonen detail en lijst de afgekeurde hoeveelheid.

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
let posted: Posted[];
let listUrls: string[];
let current: PurchaseOrderDetail;
let listItems: PurchaseOrderListItem[];

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const select = (id: string) => mounted.container.querySelector<HTMLSelectElement>(`[id="${id}"]`)!;

describe('Purchase orders page', () => {
    beforeEach(() => {
        window.localStorage.setItem('agf_actor_reference', 'Jan');
        window.sessionStorage.clear();
        listUrls = [];
        current = detail();
        listItems = [item()];
        posted = stubFetch({
            onGet: (url) => {
                if (/\/incominginvoices\/vendors$/.test(url)) return json(200, [{ id: 3, name: "Nature's Pride" }]);
                if (/\/purchase-orders\?/.test(url)) { listUrls.push(url); return json(200, { items: listItems, total: listItems.length, page: 1, pageSize: 50 }); }
                if (/\/purchase-orders\/7$/.test(url)) return json(200, current);
                return json(404, { message: `unexpected GET ${url}` });
            },
            nextPost: async () => {
                if (posted[posted.length - 1].url.endsWith('/invoice-refs')) {
                    const body = JSON.parse(posted[posted.length - 1].body);
                    current = detail({ invoiceRefs: [...current.invoiceRefs, { id: 2, kind: body.kind, invoiceNumber: body.invoiceNumber, invoiceDate: null, amountExclVat: null, incomingInvoiceId: null, actorReference: 'Jan', createdAt: '2026-10-27T12:00:00Z' }] });
                    listItems = [item({ invoiceNumbers: [...listItems[0].invoiceNumbers, { kind: body.kind, invoiceNumber: body.invoiceNumber }] })];
                    return json(201, current);
                }
                current = detail({
                    rejectedQuantity: 48, rejectedNetWeightKg: 192, invoiceIndicator: 'CreditNoteExpected',
                    rejections: [{ id: 1, reason: 'Quality', explanation: null, decision: 'ReturnToSupplier', actorReference: 'Jan', rejectedAt: '2026-10-27T12:00:00Z',
                        lines: [{ palletContentLineId: 40, palletReference: 'P-000009', lotNumber: 'LOT-1', quantity: 48, unit: 'box', netWeightKg: 192, splitOff: true }] }],
                });
                listItems = [item({ rejectedQuantity: 48, invoiceIndicator: 'CreditNoteExpected' })];
                return json(201, current);
            },
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

    test('opens the order and rejects part of a received line with reason, decision and actor', async () => {
        navigate('/purchase-orders');
        await mounted.render(<PurchaseOrdersPage />);
        const openButton = await mounted.waitFor(() => mounted.buttons().find((b) => b.getAttribute('aria-label') === 'po_open_order'), 'open button');
        await click(openButton);
        await mounted.waitFor(() => mounted.container.querySelector('#po-detail-title')?.textContent === 'IO-2026-0007', 'detail');

        await click(mounted.buttons().find((b) => b.getAttribute('aria-label') === 'po_reject_line')!);
        assert.equal(input('reject-40-quantity').value, '221');
        assert.equal(input('reject-40-kg').value, '884');
        await act(async () => { setValue(input('reject-40-quantity'), '48'); });
        await mounted.waitFor(() => input('reject-40-kg').value === '192', 'proportional kg');

        // Zonder reden wordt er niets verstuurd.
        await click(mounted.button('po_reject_save')!);
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('supply_error_required'));

        await act(async () => { setSelectValue(select('reject-40-reason'), 'Quality'); });
        await click(mounted.button('po_reject_save')!);
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.match(posted[0].url, /\/purchase-orders\/7\/rejections$/);
        const body = JSON.parse(posted[0].body);
        assert.equal(body.reason, 'Quality');
        assert.equal(body.decision, 'ReturnToSupplier');
        assert.equal(body.actorReference, 'Jan');
        assert.deepEqual(body.lines, [{ palletContentLineId: 40, expectedContentLineVersion: 3, quantity: 48, netWeightKg: 192 }]);

        await mounted.waitFor(() => mounted.hasText('po_rejected_of'), 'rejected summary');
        assert.ok(mounted.hasText('po_invoice_credit_note_expected'));
        assert.equal(mounted.container.querySelector('form[aria-label="po_reject_title"]'), null);
        await mounted.waitFor(() => listUrls.length >= 2, 'list reloaded');
    });
    test('"Factuur ontvangen" records the number on the order and the list shows it right away', async () => {
        navigate('/purchase-orders?id=7');
        await mounted.render(<PurchaseOrdersPage />);
        await mounted.waitFor(() => mounted.container.querySelector('#po-detail-title')?.textContent === 'IO-2026-0007', 'detail');
        await click(mounted.button('+ po_invoice_received_action')!);
        await act(async () => { setValue(input('invoiceRef-Invoice-number'), 'F-4712'); });
        await click(mounted.button('po_invoice_save')!);
        await mounted.waitFor(() => posted.length === 1, 'POST');
        assert.match(posted[0].url, /\/purchase-orders\/7\/invoice-refs$/);
        await mounted.waitFor(() => listUrls.length >= 2 && (mounted.container.textContent ?? '').split('F-4712').length > 2, 'number in detail and list');
    });
});
