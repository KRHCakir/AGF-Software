import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { PurchaseOrderDetail, PurchaseOrderReceiptLine } from '../types/purchaseOrder';
import type { SalesOrder, SalesOrderLine, StockReservation } from '../types/salesOrder';
import { CHAIN_STEPS, ChainStep, purchaseOrderChain, salesOrderChain } from './chain.ts';

// De keten-strip rekent alleen met wat de API al geeft: status, aantallen en links per stap; de factuur is grijs zonder link.

const receipt = (over: Partial<PurchaseOrderReceiptLine>): PurchaseOrderReceiptLine => ({
    palletContentLineId: 1, palletId: 1, palletReference: 'P-000001', palletStatus: 'Received', supplyCommitmentLineId: 31, productName: 'Avocado', productVariantName: 'Hass',
    lotNumber: 'L1', quantity: 20, unit: 'colli', netWeightKg: 80, weightMethod: 'Calculated', netKgPerPackage: 4, onHandQuantity: 20, onHandNetWeightKg: 80,
    reservedQuantity: 0, reservedNetWeightKg: 0, qualityStatus: 'Quarantine', qualityReason: null, origin: 'Peru', receivedAt: '2026-11-02T10:00:00Z',
    rejectable: true, rejectableQuantity: 20, rejectableNetWeightKg: 80, version: 1, ...over,
});

const order = (over: Partial<PurchaseOrderDetail> = {}): PurchaseOrderDetail => ({
    id: 3, orderNumber: 'IO-2026-0003', vendorId: 1, vendorName: "Nature's Pride", supplierReference: null, orderedOn: '2026-11-02', orderChannel: null, note: null,
    route: 'Warehouse', customerName: null, origin: 'Peru', destination: null, expectedAt: null, status: 'Expected', storedStatus: 'Expected', invoiceIndicator: 'None',
    expectedQuantity: 20, expectedNetWeightKg: 80, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0,
    lines: [{ id: 31, productVariantId: 7, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, expectedQuantity: 20, unit: 'colli', expectedNetWeightKg: 80,
        receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0 }],
    receipts: [], invoiceRefs: [], rejections: [], history: [], salesOrders: [], ...over,
});

const step = (steps: ChainStep[], key: ChainStep['key']) => steps.find((s) => s.key === key)!;
const hrefs = (s: ChainStep) => s.links.map((link) => link.href);

describe('purchase order chain', () => {
    test('a new order: receiving is the next step (link to Ontvangst with the order chosen); nothing further yet; invoice greyed out', () => {
        const steps = purchaseOrderChain(order(), 'order');
        assert.deepEqual(steps.map((s) => s.key), CHAIN_STEPS);
        assert.equal(step(steps, 'order').current, true);
        assert.deepEqual(hrefs(step(steps, 'order')), ['/purchase-orders?id=3']);
        assert.equal(step(steps, 'receipt').state, 'open');
        assert.deepEqual(hrefs(step(steps, 'receipt')), ['/receipts?order=3']);
        assert.equal(step(steps, 'inspection').state, 'empty');
        assert.equal(step(steps, 'sales').state, 'empty');
        assert.deepEqual(step(steps, 'invoice'), { key: 'invoice', state: 'unavailable', parts: [{ key: 'chain_not_yet' }], links: [], current: false });
    });

    test('partly received and partly inspected: counts, pallet links, the open pallet in Keuring and stock for this article', () => {
        const steps = purchaseOrderChain(order({
            receivedQuantity: 15,
            receipts: [receipt({ palletId: 1, palletReference: 'P-1', qualityStatus: 'Approved', onHandQuantity: 10, reservedQuantity: 4 }),
                receipt({ palletContentLineId: 2, palletId: 2, palletReference: 'P-2', quantity: 5, onHandQuantity: 5, qualityStatus: 'Quarantine' })],
        }), 'receipt');
        assert.equal(step(steps, 'receipt').current, true);
        assert.equal(step(steps, 'receipt').state, 'partial');
        assert.deepEqual(step(steps, 'receipt').parts, [{ key: 'chain_received_of', params: { received: 15, expected: 20, unit: 'colli' } }, { key: 'chain_pallets', params: { count: 2 } }]);
        assert.deepEqual(hrefs(step(steps, 'receipt')), ['/receipts?order=3', '/inspection?palletId=1', '/inspection?palletId=2']);
        assert.equal(step(steps, 'inspection').state, 'partial');
        assert.deepEqual(hrefs(step(steps, 'inspection')), ['/inspection?palletId=2'], 'the pallet that still needs inspection');
        assert.deepEqual(step(steps, 'stock').parts, [{ key: 'chain_stock_available', params: { quantity: 6, unit: 'colli' } }], 'approved, on hand, minus reserved');
        assert.deepEqual(hrefs(step(steps, 'stock')), ['/availability?route=Warehouse&productVariantId=7&focus=available']);
    });

    test('sold from this order: the sales orders with reserved and shipped quantities, each a link', () => {
        const steps = purchaseOrderChain(order({
            receivedQuantity: 20, status: 'Received',
            receipts: [receipt({ qualityStatus: 'Approved', onHandQuantity: 16, reservedQuantity: 6 })],
            salesOrders: [
                { salesOrderId: 1, customerName: 'Lokaal Fruit B.V.', status: 'Open', unit: 'colli', reservedQuantity: 0, shippedQuantity: 4 },
                { salesOrderId: 5, customerName: 'Markt', status: 'Open', unit: 'colli', reservedQuantity: 6, shippedQuantity: 0 },
            ],
        }), 'order');
        assert.equal(step(steps, 'receipt').state, 'done');
        assert.deepEqual(hrefs(step(steps, 'receipt')), ['/inspection?palletId=1'], 'fully received: no "receive" link');
        assert.deepEqual(hrefs(step(steps, 'sales')), ['/sales-orders?orderId=1', '/sales-orders?orderId=5']);
        assert.deepEqual(step(steps, 'sales').parts, [{ key: 'chain_sales_orders', params: { count: 2 } }, { key: 'chain_reserved', params: { quantity: 6, unit: 'colli' } }]);
        assert.equal(step(steps, 'delivery').state, 'partial');
        assert.deepEqual(hrefs(step(steps, 'delivery')), ['/sales-orders?orderId=1'], 'only the order that shipped');
    });

    test('customer-bound supply (DirectTrade) never passes our warehouse: receipt, inspection and stock are not part of it', () => {
        const steps = purchaseOrderChain(order({ route: 'DirectTrade' }), 'order');
        for (const key of ['receipt', 'inspection', 'stock'] as const) {
            assert.equal(step(steps, key).state, 'unavailable', key);
            assert.deepEqual(step(steps, key).links, [], key);
        }
    });
});

const reservation = (over: Partial<StockReservation>): StockReservation => ({
    id: 1, requestId: null, salesOrderId: 1, salesOrderLineId: 11, palletContentLineId: 1, palletId: 1, palletReference: 'P-000001', warehouseLocation: null,
    lotNumber: 'L1', quantity: 4, unit: 'colli', netWeightKg: 16, source: 'Physical', supplyCommitmentLineId: 31, supplyCommitmentId: 3, supplyOrderNumber: 'IO-2026-0003',
    vendorName: null, origin: null, destination: null, expectedAt: null, directTradeShipmentId: null, shippedOn: null, shippedNetWeightKg: null, shippedLotNumber: null,
    directTradeCorrectionId: null, deliveryConfirmationId: null, deliveredOn: null, status: 'Active', createdAt: '2026-11-02T10:00:00Z', releasedAt: null, releaseReason: null,
    version: 1, contentQualityStatus: 'Approved', ...over,
} as StockReservation);

const salesLine = (over: Partial<SalesOrderLine>): SalesOrderLine => ({
    id: 11, productVariantId: 7, productName: 'Avocado', productVariantName: 'Hass', quantity: 10, unit: 'colli', palletCount: null, packagesPerPallet: null,
    reservedQuantity: 4, reservedNetWeightKg: 16, allocatedQuantity: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, directTradeShippedQuantity: 0, openQuantity: 6,
    fulfilled: false, deliveredQuantity: 0, delivered: false, version: 1, reservations: [reservation({})], ...over,
});

const salesOrder = (lines: SalesOrderLine[], over: Partial<SalesOrder> = {}): SalesOrder => ({
    id: 1, requestId: 'r', customerId: 1, customerName: 'Lokaal Fruit B.V.', status: 'Open', customerReference: null, internalNote: null,
    requestedDeliveryDate: '2026-11-05T00:00:00Z', actorReference: 'x', createdAt: '2026-11-02T10:00:00Z', version: 1, cancelledAt: null, cancelReason: null, cancelledBy: null,
    fulfilled: false, delivered: false, lines, ...over,
});

describe('sales order chain', () => {
    test('origin from the reservations: purchase order number and lot, the pallet, approved stock; the order itself is current', () => {
        const steps = salesOrderChain(salesOrder([salesLine({})]));
        assert.equal(step(steps, 'sales').current, true);
        assert.deepEqual(step(steps, 'order').links, [{ label: 'IO-2026-0003', href: '/purchase-orders?id=3' }]);
        assert.deepEqual(step(steps, 'order').parts, [{ key: 'chain_lots', params: { lots: 'L1' } }]);
        assert.deepEqual(hrefs(step(steps, 'receipt')), ['/inspection?palletId=1']);
        assert.equal(step(steps, 'inspection').state, 'done');
        assert.deepEqual(step(steps, 'stock').parts, [{ key: 'chain_reserved', params: { quantity: 4, unit: 'colli' } }]);
        assert.equal(step(steps, 'delivery').state, 'empty');
        assert.equal(step(steps, 'invoice').state, 'unavailable');
    });

    test('shipped and delivered: stock shows what came from stock; released reservations never count', () => {
        const shipped = salesLine({ reservedQuantity: 0, dispatchedQuantity: 10, deliveredQuantity: 10, openQuantity: 0, fulfilled: true, delivered: true,
            reservations: [reservation({ status: 'Consumed', quantity: 10 }), reservation({ id: 2, status: 'Released', supplyCommitmentId: 9, supplyOrderNumber: 'IO-2026-0009' })] });
        const steps = salesOrderChain(salesOrder([shipped]));
        assert.deepEqual(step(steps, 'order').links.map((l) => l.label), ['IO-2026-0003'], 'a released reservation is not origin');
        assert.deepEqual(step(steps, 'stock').parts, [{ key: 'chain_from_stock', params: { quantity: 10, unit: 'colli' } }]);
        assert.equal(step(steps, 'sales').state, 'done');
        assert.equal(step(steps, 'delivery').state, 'done');
    });

    test('nothing reserved yet: no origin links; the sales order and stock link remain', () => {
        const steps = salesOrderChain(salesOrder([salesLine({ reservedQuantity: 0, reservations: [] })]));
        assert.equal(step(steps, 'order').state, 'empty');
        assert.deepEqual(step(steps, 'order').links, []);
        assert.deepEqual(hrefs(step(steps, 'sales')), ['/sales-orders?orderId=1']);
        assert.deepEqual(hrefs(step(steps, 'stock')), ['/availability?route=Warehouse&productVariantId=7&focus=available']);
    });
});
