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
        // Zonder ordernummer van de leverancier het eigen IO-nummer; met, dat nummer (het hoofdnummer).
        assert.equal(step(steps, 'order').links[0].label, 'IO-2026-0003');
        assert.equal(step(purchaseOrderChain(order({ supplierReference: '45-21' }), 'order'), 'order').links[0].label, '45-21');
        assert.equal(step(steps, 'receipt').state, 'open');
        assert.deepEqual(hrefs(step(steps, 'receipt')), ['/receipts?order=3']);
        assert.equal(step(steps, 'inspection').state, 'empty');
        assert.equal(step(steps, 'sales').state, 'empty');
        assert.deepEqual(step(steps, 'invoice'), { key: 'invoice', state: 'unavailable', parts: [{ key: 'chain_not_yet' }], links: [], current: false });
    });

    test('mixed units on one order: no unitless total, a "multiple units" part instead', () => {
        const base = order();
        const steps = purchaseOrderChain(order({ receivedQuantity: 15, lines: [base.lines[0], { ...base.lines[0], id: 32, unit: 'kg' }] }), 'order');
        assert.deepEqual(step(steps, 'receipt').parts, [{ key: 'chain_mixed_units' }]);
    });

    test('mixed units: receipt state is judged per unit, not on a raw sum', () => {
        const base = order().lines[0];
        const lines = [{ ...base, expectedQuantity: 10, receivedQuantity: 30 }, { ...base, id: 32, unit: 'kg', expectedQuantity: 20, receivedQuantity: 0 }];
        const steps = purchaseOrderChain(order({ expectedQuantity: 30, receivedQuantity: 30, lines }), 'order');
        assert.equal(step(steps, 'receipt').state, 'partial');
    });

    test('a cancelled order with nothing received: the receipt step is not "open" and has no receive action', () => {
        const steps = purchaseOrderChain(order({ status: 'Cancelled' }), 'order');
        assert.equal(step(steps, 'receipt').state, 'empty');
        assert.deepEqual(hrefs(step(steps, 'receipt')), []);
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

    test('delivery links: every shipped sales order counts, also after the first three sales orders', () => {
        const link = (id: number, shipped: number) => ({ salesOrderId: id, customerName: null, status: 'Open', unit: 'colli', reservedQuantity: shipped ? 0 : 2, shippedQuantity: shipped });
        const steps = purchaseOrderChain(order({ salesOrders: [link(1, 0), link(2, 0), link(3, 0), link(4, 5)] }), 'order');
        assert.deepEqual(hrefs(step(steps, 'sales')), ['/sales-orders?orderId=1', '/sales-orders?orderId=2', '/sales-orders?orderId=3']);
        assert.deepEqual(hrefs(step(steps, 'delivery')), ['/sales-orders?orderId=4']);
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

    test('a confirmed order says so in the sales step (the loading slip is in Documents); a cancelled one does not', () => {
        const confirmed = { ...salesOrder([salesLine({})]), confirmedAt: '2026-11-12T09:00:00Z' };
        assert.ok(step(salesOrderChain(confirmed), 'sales').parts.some((part) => part.key === 'chain_sales_confirmed'));
        assert.ok(!step(salesOrderChain(salesOrder([salesLine({})])), 'sales').parts.some((part) => part.key === 'chain_sales_confirmed'));
        assert.ok(!step(salesOrderChain({ ...confirmed, status: 'Cancelled' }), 'sales').parts.some((part) => part.key === 'chain_sales_confirmed'));
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

    test('customer-bound supply only (DirectTrade): stock does not apply, no link to Availability', () => {
        const allocated = salesLine({ reservedQuantity: 0, allocatedQuantity: 10, reservations: [reservation({ source: 'DirectTrade', palletId: null, palletContentLineId: null, palletReference: null, quantity: 10, contentQualityStatus: null })] });
        const steps = salesOrderChain(salesOrder([allocated]));
        assert.equal(step(steps, 'stock').state, 'unavailable');
        assert.deepEqual(step(steps, 'stock').parts, [{ key: 'chain_direct_trade' }]);
        assert.deepEqual(step(steps, 'stock').links, []);
        assert.deepEqual(step(steps, 'order').links.map((l) => l.label), ['IO-2026-0003'], 'the purchase order is still the origin');
    });

    test('mixed units: sales and delivery states are judged per unit, not on a raw sum', () => {
        const kg = salesLine({ id: 12, unit: 'box', quantity: 20, dispatchedQuantity: 0, deliveredQuantity: 0, reservations: [] });
        const colli = salesLine({ quantity: 10, dispatchedQuantity: 30, deliveredQuantity: 30 });
        const steps = salesOrderChain(salesOrder([colli, kg]));
        assert.equal(step(steps, 'sales').state, 'partial');
        assert.equal(step(steps, 'delivery').state, 'partial');
    });

    test('mixed: stock counts only the warehouse part', () => {
        const mixed = salesLine({ reservedQuantity: 4, allocatedQuantity: 6, reservations: [reservation({ quantity: 4 }), reservation({ id: 2, source: 'DirectTrade', palletId: null, palletReference: null, quantity: 6 })] });
        assert.deepEqual(step(salesOrderChain(salesOrder([mixed])), 'stock').parts, [{ key: 'chain_reserved', params: { quantity: 4, unit: 'colli' } }]);
    });

    test('not approved counts pallets: two reservations on one blocked pallet are one pallet', () => {
        const blocked = salesLine({ reservations: [reservation({ contentQualityStatus: 'Blocked' }), reservation({ id: 2, salesOrderLineId: 12, contentQualityStatus: 'Blocked' })] });
        assert.deepEqual(step(salesOrderChain(salesOrder([blocked])), 'inspection').parts, [{ key: 'chain_inspection_not_approved', params: { count: 1 } }]);
    });

    test('nothing reserved yet: no origin links; the sales order and stock link remain', () => {
        const steps = salesOrderChain(salesOrder([salesLine({ reservedQuantity: 0, reservations: [] })]));
        assert.equal(step(steps, 'order').state, 'empty');
        assert.deepEqual(step(steps, 'order').links, []);
        assert.deepEqual(hrefs(step(steps, 'sales')), ['/sales-orders?orderId=1']);
        assert.deepEqual(hrefs(step(steps, 'stock')), ['/availability?route=Warehouse&productVariantId=7&focus=available']);
    });
});

describe('sales order chain: sold before arrival', () => {
    test('the unreserved rest waits for IO-2026-0002 (expected 05-11) and the shortage is named on the sales step', () => {
        const line = salesLine({
            reservedQuantity: 0, reservations: [], quantity: 30, openQuantity: 30, unit: 'colli', supplySource: 'Expected',
            coverage: { fromStock: 0, shortage: 10, expected: [{ supplyCommitmentId: 2, supplyOrderNumber: 'IO-2026-0002', expectedAt: '2026-11-05T12:00:00', quantity: 20, inInspection: false }] },
        });
        const steps = salesOrderChain(salesOrder([line], { orderNumber: 'VO-2026-0005' }));
        const po = step(steps, 'order');
        assert.equal(po.state, 'open');
        assert.deepEqual(po.parts, [{ key: 'chain_waiting_for_po', params: { number: 'IO-2026-0002', date: '05-11' } }]);
        assert.deepEqual(hrefs(po), ['/purchase-orders?id=2']);
        const sales = step(steps, 'sales');
        assert.deepEqual(sales.parts[1], { key: 'chain_shortage', params: { quantity: 10, unit: 'colli' } });
        assert.equal(sales.links[0].label, 'VO-2026-0005');
    });

    test('goods already received but still in inspection keep the purchase order, without an expected date', () => {
        const line = salesLine({ reservations: [], coverage: { fromStock: 0, shortage: 0, expected: [{ supplyCommitmentId: 3, supplyOrderNumber: 'IO-2026-0003', expectedAt: '2026-11-01T12:00:00', quantity: 5, inInspection: true }] } });
        const po = step(salesOrderChain(salesOrder([line])), 'order');
        assert.deepEqual(po.parts, [{ key: 'chain_waiting_inspection', params: { number: 'IO-2026-0003' } }]);
        assert.deepEqual(hrefs(po), ['/purchase-orders?id=3']);
    });

    test('a cancelled order waits for nothing and has no shortage', () => {
        const line = salesLine({ reservations: [], coverage: { fromStock: 0, shortage: 5, expected: [{ supplyCommitmentId: 2, supplyOrderNumber: 'IO-2026-0002', expectedAt: null, quantity: 5, inInspection: false }] } });
        const steps = salesOrderChain(salesOrder([line], { status: 'Cancelled' }));
        assert.ok(!step(steps, 'order').parts.some((part) => part.key.startsWith('chain_waiting')));
        assert.equal(step(steps, 'sales').parts.length, 1);
    });
});

describe('chain: the lot links to its lot page', () => {
    test('from the sales order: the lot number comes from the lot itself and links to the lot detail', () => {
        const line = salesLine({ reservations: [reservation({ lotNumber: 'L1', partijId: 7, partijNumber: '2610251' })] });
        const steps = salesOrderChain(salesOrder([line]));
        assert.deepEqual(step(steps, 'order').parts, [{ key: 'chain_lots', params: { lots: '2610251' } }]);
        assert.deepEqual(step(steps, 'order').links.at(-1), { label: '2610251', key: 'chain_partij_link', params: { number: '2610251' }, href: '/partijen?id=7' });
    });

    test('the sales lot stands next to the order number on the sales step and links to the lot detail', () => {
        const steps = salesOrderChain(salesOrder([salesLine({})], { orderNumber: 'VO-2026-0001', salesPartijId: 40, salesPartijNumber: '261140' }));
        assert.deepEqual(step(steps, 'sales').links, [
            { label: 'VO-2026-0001', href: '/sales-orders?orderId=1' },
            { label: '261140', key: 'chain_sales_partij_link', params: { number: '261140' }, href: '/partijen?id=40' },
        ]);
        assert.deepEqual(step(salesOrderChain(salesOrder([salesLine({})])), 'sales').links, [{ label: '#1', href: '/sales-orders?orderId=1' }]);
    });

    test('from the purchase order: the lot of the order next to the order itself; no lot, no extra link', () => {
        assert.deepEqual(hrefs(step(purchaseOrderChain(order({ partijId: 7, partijNumber: '2610251' }), 'order'), 'order')), ['/purchase-orders?id=3', '/partijen?id=7']);
        assert.equal(step(purchaseOrderChain(order(), 'order'), 'order').links.length, 1);
    });
});
