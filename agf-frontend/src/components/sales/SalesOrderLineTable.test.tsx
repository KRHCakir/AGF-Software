import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import type { SalesOrder, SalesOrderLine } from '../../types/salesOrder';
import { PURCHASE_LINE_COLUMNS } from '../purchase/PurchaseLineTable';
import SalesOrderLineTable from './SalesOrderLineTable';

// De regels van een verkooporder op het orderdetail (feedback 22 november), alleen lezen: dezelfde kolommen als de inkooporder, prijs,
// korting, btw en bedrag per regel, de totalen eronder en de btw per tarief over de hele order. Een oudere order zonder prijs blijft leesbaar.
const line = (over: Partial<SalesOrderLine>): SalesOrderLine => ({
    id: 11, productVariantId: 7, productName: 'Avocado', productVariantName: 'Hass', quantity: 30, unit: 'colli', palletCount: null, packagesPerPallet: null,
    netWeightKg: 300, grossWeightKg: 309, weightSource: 'Calculated', reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0,
    directTradeShippedQuantity: 0, openQuantity: 30, fulfilled: false, deliveredQuantity: 0, delivered: false, version: 0, reservations: [], supplySource: 'Stock',
    unitPrice: 13.35, discountPercentage: 2.5, vatPercentage: 9, lineTotal: 390.49, brand: 'Westfalia', origin: 'PE', size: '18', packaging: '4 kg doos', pallets: 0.25, ...over,
});
const order = (lines: SalesOrderLine[], totals?: SalesOrder['totals']): SalesOrder => ({
    id: 4, orderNumber: 'VO-2026-0004', requestId: 'r', customerId: 1, customerName: 'Fictieve Groothandel', status: 'Open', customerReference: null, internalNote: null,
    requestedDeliveryDate: '2026-11-30T00:00:00Z', actorReference: 'test', createdAt: '2026-11-23T08:00:00Z', version: 1, cancelledAt: null, cancelReason: null, cancelledBy: null,
    fulfilled: false, delivered: false, lines, totals,
});

let mounted: Mounted;
const cell = (row: number, column: string) => mounted.container.querySelectorAll('[data-testid="sales-order-lines"] tbody tr')[row].querySelector(`[data-column="${column}"]`)!.textContent;

describe('SalesOrderLineTable', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('the purchase columns with price, discount, VAT and amount, the totals row and the VAT per rate of the server', async () => {
        const amounts = { net: 405.49, vatRates: [{ percentage: 9, net: 405.49, vat: 36.49 }], vat: 36.49, gross: 441.98, linesWithoutPrice: 0, linesWithoutVat: 0 };
        await mounted.render(<SalesOrderLineTable order={order([line({})], { quantity: 30, netWeightKg: 300, grossWeightKg: 309, amount: 390.49, pallets: 0.25, linesWithoutNet: 0,
            linesWithoutGross: 0, linesWithoutPrice: 0, linesWithoutPallets: 0, amounts })} />);
        const head = Array.from(mounted.container.querySelectorAll('[data-testid="sales-order-lines"] thead th')).map((th) => th.textContent);
        assert.deepEqual(head, PURCHASE_LINE_COLUMNS.map((column) => `po_line_col_${column}`));
        assert.deepEqual(['brand', 'country', 'price', 'discount', 'vat', 'total'].map((column) => cell(0, column)), ['Westfalia', 'PE', '13.35 EUR', '2.5%', '9%', '390.49 EUR']);
        assert.equal(mounted.container.querySelector('[data-testid="line-totals-amount"]')!.textContent, '390.49 EUR');
        assert.equal(mounted.container.querySelector('[data-testid="order-amounts-gross"]')!.textContent, '441.98 EUR', 'packaging included in the order total');
    });

    test('an older line without price, discount or VAT stays readable; an order without goods lines shows no table', async () => {
        await mounted.render(<SalesOrderLineTable order={order([line({ unitPrice: null, discountPercentage: null, vatPercentage: null, lineTotal: null })])} />);
        assert.deepEqual(['price', 'discount', 'vat', 'total'].map((column) => cell(0, column)), ['–', '–', '–', '–']);
        assert.equal(mounted.container.querySelector('[data-testid="order-amounts"]'), null);
        await mounted.render(<SalesOrderLineTable order={order([])} />);
        assert.equal(mounted.container.querySelector('[data-testid="sales-order-lines"]'), null);
    });
});
