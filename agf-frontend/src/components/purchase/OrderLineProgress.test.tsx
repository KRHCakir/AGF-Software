import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, stubFetch } from '../../test/reactHarness.ts';
import type { PurchaseOrderLine, PurchaseOrderListItem } from '../../types/purchaseOrder';
import PurchaseOrderList from './PurchaseOrderList';

// Feedback 22 november: in de lijst bij Deels ontvangen ook x van y regels en de open colli; uitklappen toont per regel besteld,
// ontvangen, open, partij en status (pas bij openen geladen).
const item = (over: Partial<PurchaseOrderListItem>): PurchaseOrderListItem => ({
    id: 5, orderNumber: 'IO-2026-0005', vendorId: 3, vendorName: 'Supplier B', supplierReference: null, orderedOn: '2026-11-10', expectedAt: null, route: 'Warehouse',
    customerName: null, status: 'PartiallyReceived', storedStatus: 'Expected', expectedQuantity: 100, expectedNetWeightKg: null, receivedQuantity: 40, rejectedQuantity: 0,
    invoiceNumbers: [], invoiceIndicator: 'None', documentStatus: 'Open', lineCount: 2, receivedLineCount: 1, openQuantity: 60, ...over,
});
const line = (over: Partial<PurchaseOrderLine>): PurchaseOrderLine => ({
    id: 1, productVariantId: 1, productName: 'Mango', productVariantName: 'Kent', origin: 'Brazilië', lotNumber: null, partijNumber: '2611001', expectedQuantity: 40, unit: 'colli',
    expectedNetWeightKg: 160, receivedQuantity: 40, receivedNetWeightKg: 160, rejectedQuantity: 0, rejectedNetWeightKg: 0, cancelledQuantity: 60, splitIntoLineIds: [2], ...over,
} as PurchaseOrderLine);

const detail = {
    id: 5, orderNumber: 'IO-2026-0005', vendorId: 3, vendorName: 'Supplier B', supplierReference: null, orderedOn: '2026-11-10', orderChannel: null, note: null,
    route: 'Warehouse', customerName: null, origin: '', destination: null, expectedAt: null, status: 'PartiallyReceived', storedStatus: 'Expected', invoiceIndicator: 'None',
    expectedQuantity: 100, expectedNetWeightKg: 400, receivedQuantity: 40, receivedNetWeightKg: 160, rejectedQuantity: 0, rejectedNetWeightKg: 0,
    receipts: [], invoiceRefs: [], rejections: [], history: [], salesOrders: [], deviations: [],
};
let mounted: Mounted;
let gets: string[];

describe('purchase order list: per line', () => {
    beforeEach(() => {
        gets = [];
        stubFetch({
            onGet: (url) => { gets.push(url); return json(200, { ...detail, lines: [line({}), line({ id: 2, origin: 'Peru', expectedQuantity: 60, receivedQuantity: 0, cancelledQuantity: null, splitFromLineId: 1, splitIntoLineIds: [] })] }); },
            nextPost: async () => json(500, {}),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('shows x of y lines and open packages, and expands to the lines without opening the order', async () => {
        let opened = 0;
        await mounted.render(<PurchaseOrderList items={[item({}), item({ id: 6, orderNumber: 'IO-2026-0006', status: 'Ordered' })]} selectedId={null} onOpen={() => { opened += 1; }}
            sort={null} onSort={() => {}} onSortChange={() => {}} />);
        // Alleen de deels ontvangen order (tabel en kaart): "1 van 2 regels · 60 colli open".
        assert.equal(mounted.container.querySelectorAll('[data-testid="po-list-partial"]').length, 2);
        assert.equal(gets.length, 0, 'nothing loaded before expanding');
        const expand = mounted.container.querySelector<HTMLButtonElement>('[data-tour="po-list-expand"][data-tour-key="IO-2026-0005"]')!;
        await click(expand);
        assert.equal(expand.getAttribute('aria-expanded'), 'true');
        assert.equal(opened, 0, 'expanding does not open the order');
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="order-line-progress-2"]'), 'lines shown');
        assert.ok(gets[0].endsWith('/purchase-orders/5'));
        const first = mounted.container.querySelector('[data-testid="order-line-progress-1"]')!.textContent ?? '';
        assert.ok(first.includes('po_line_status_closed') && first.includes('po_line_cancelled') && first.includes('2611001'), first);
        assert.ok((mounted.container.querySelector('[data-testid="order-line-progress-2"]')!.textContent ?? '').includes('po_line_split_from'));
        await click(expand);
        assert.equal(mounted.container.querySelector('[data-testid="order-line-progress-2"]'), null, 'collapsed again');
    });
});
