import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import type { PurchaseOrderDetail as Order } from '../../types/purchaseOrder';
import PurchaseOrderDetail from './PurchaseOrderDetail';

// Volgende stap op de inkooporder: "Ontvangen" opent Ontvangst met deze order gekozen, zolang er nog iets te ontvangen is in het eigen
// magazijn. Bovenaan staat de keten-strip met deze order als huidige stap.
const order = (over: Partial<Order> = {}): Order => ({
    id: 3, orderNumber: 'IO-2026-0003', vendorId: 1, vendorName: "Nature's Pride", supplierReference: null, orderedOn: '2026-11-02', orderChannel: null, note: null,
    route: 'Warehouse', customerName: null, origin: 'Peru', destination: null, expectedAt: null, status: 'Expected', storedStatus: 'Expected', invoiceIndicator: 'None',
    expectedQuantity: 20, expectedNetWeightKg: 80, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0,
    lines: [{ id: 31, productVariantId: 7, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, expectedQuantity: 20, unit: 'colli', expectedNetWeightKg: 80,
        receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0 }],
    receipts: [], invoiceRefs: [], rejections: [], history: [], salesOrders: [], deviations: [], ...over,
});

let mounted: Mounted;
const receive = () => mounted.container.querySelector<HTMLAnchorElement>('[data-tour="po-receive"]');

describe('PurchaseOrderDetail next step', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('"Ontvangen" opens Receipt with this order chosen while something is still to be received', async () => {
        await mounted.render(<PurchaseOrderDetail order={order({ receivedQuantity: 5 })} onChanged={() => {}} />);
        assert.equal(receive()?.getAttribute('href'), '/receipts?order=3');
        assert.equal(receive()?.textContent, 'po_receive');
        assert.equal(mounted.container.querySelector('[data-chain-step="order"]')?.getAttribute('aria-current'), 'step', 'the chain strip sits on top');
    });

    test('per order line: one line received too much and another too little still shows "Ontvangen"', async () => {
        const line = order().lines[0];
        const lines = [{ ...line, receivedQuantity: 25 }, { ...line, id: 32, receivedQuantity: 15 }];
        await mounted.render(<PurchaseOrderDetail order={order({ expectedQuantity: 40, receivedQuantity: 40, lines })} onChanged={() => {}} />);
        assert.equal(receive()?.getAttribute('href'), '/receipts?order=3', 'the order total matches, but line 32 still lacks 5');
    });

    test('no "Ontvangen" when everything is received, for customer-bound supply or a cancelled order', async () => {
        const fullyReceived = { receivedQuantity: 20, lines: order().lines.map((line) => ({ ...line, receivedQuantity: 20 })) };
        for (const over of [fullyReceived, { route: 'DirectTrade' }, { status: 'Cancelled', storedStatus: 'Cancelled' }]) {
            await mounted.render(<PurchaseOrderDetail order={order(over)} onChanged={() => {}} />);
            assert.equal(receive(), null, JSON.stringify(over));
        }
    });
});
