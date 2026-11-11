import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import type { PurchaseOrderDetail as Order, PurchaseOrderLine } from '../../types/purchaseOrder';
import { PURCHASE_LINE_COLUMNS } from './PurchaseLineTable';
import PurchaseOrderDetail from './PurchaseOrderDetail';

// De regels op het orderdetail in de nieuwe indeling (feedback 11 november), alleen lezen: dezelfde kolommen als de invoer, een
// aangepaste bruto zichtbaar, en de totalen van de server onder dezelfde kolommen. Een oudere order zonder prijs blijft leesbaar.
const line = (over: Partial<PurchaseOrderLine>): PurchaseOrderLine => ({
    id: 31, productVariantId: 7, productName: 'Avocado', productVariantName: 'Hass', origin: 'Peru', size: '18', packaging: '4 kg doos', brand: 'Westfalia', lotNumber: null,
    expectedQuantity: 100, unit: 'colli', expectedNetWeightKg: 400, expectedGrossWeightKg: 440, weightSource: 'Calculated', unitPrice: 12.5, lineTotal: 1250, packagesPerPallet: 252, pallets: 0.4,
    receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0, ...over,
});
const order = (lines: PurchaseOrderLine[], totals?: Order['totals']): Order => ({
    id: 3, orderNumber: 'IO-2026-0003', vendorId: 1, vendorName: "Nature's Pride", supplierReference: null, orderedOn: '2026-11-02', orderChannel: null, note: null,
    route: 'Warehouse', customerName: null, origin: '', destination: null, expectedAt: null, status: 'Expected', storedStatus: 'Expected', invoiceIndicator: 'None',
    expectedQuantity: 150, expectedNetWeightKg: 600, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0,
    lines, receipts: [], invoiceRefs: [], rejections: [], history: [], salesOrders: [], deviations: [], totals,
});

let mounted: Mounted;
const cell = (row: number, column: string) => mounted.container.querySelectorAll('[data-tour="po-lines-table"] tbody tr')[row].querySelector(`[data-column="${column}"]`)!.textContent;
const total = (id: string) => mounted.container.querySelector(`[data-testid="line-totals-${id}"]`)?.textContent;

describe('PurchaseOrderDetail lines', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('same columns as the entry, adjusted gross marked, totals of the server under the same columns', async () => {
        const lines = [line({}), line({ id: 32, brand: null, expectedQuantity: 50, expectedNetWeightKg: 200, expectedGrossWeightKg: 230, weightSource: 'Entered', unitPrice: 9.75, lineTotal: 487.5, pallets: 0.19 })];
        await mounted.render(<PurchaseOrderDetail order={order(lines, { quantity: 150, netWeightKg: 600, grossWeightKg: 670, amount: 1737.5, pallets: 0.59, linesWithoutNet: 0, linesWithoutGross: 0, linesWithoutPrice: 0, linesWithoutPallets: 0 })} onChanged={() => {}} />);
        const head = Array.from(mounted.container.querySelectorAll('[data-tour="po-lines-table"] thead th')).map((th) => th.textContent);
        assert.deepEqual(head, PURCHASE_LINE_COLUMNS.map((column) => `po_line_col_${column}`));
        assert.deepEqual(['brand', 'country', 'size', 'gross', 'net', 'packaging', 'price', 'total'].map((column) => cell(0, column)), ['Westfalia', 'Peru', '18', '440 kg', '400 kg', '4 kg doos', '12.50', '1,250.00']);
        assert.ok(cell(1, 'gross')!.includes('po_line_gross_deviates'));
        assert.equal(cell(0, 'gross')!.includes('po_line_gross_deviates'), false);
        assert.equal(mounted.container.querySelectorAll('[data-tour="po-lines-table"] input').length, 0, 'read only');
        assert.deepEqual([total('quantity'), total('gross'), total('net'), total('amount'), total('pallets')], ['150', '670 kg', '600 kg', '1,737.50', 'po_line_pallets_total']);
    });

    test('an older order without price, gross or totals stays readable', async () => {
        await mounted.render(<PurchaseOrderDetail order={order([line({ brand: null, expectedGrossWeightKg: null, weightSource: 'Entered', unitPrice: null, lineTotal: null, pallets: null })])} onChanged={() => {}} />);
        assert.deepEqual(['brand', 'gross', 'price', 'total'].map((column) => cell(0, column)), ['–', '–', '–', '–']);
        assert.equal(mounted.container.querySelector('[data-tour="po-lines-table"] tfoot'), null);
    });
});
