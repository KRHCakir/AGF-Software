import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, stubFetch } from '../../test/reactHarness.ts';
import AwaitingStockList from './AwaitingStockList';

// Werklijst "Verkocht, wacht op voorraad": de regel die vóór aankomst verkocht is en nu gereserveerd kan worden; de lijst reserveert nooit zelf.

let mounted: Mounted;
describe('AwaitingStockList', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('lists the line sold before arrival and only opens the proposal on a click', async () => {
        stubFetch({
            onGet: () => json(200, [{ salesOrderId: 5, orderNumber: 'VO-2026-0005', customerId: 1, customerName: 'Groothandel G', customerReference: 'PO-778', requestedDeliveryDate: '2026-11-06T06:00:00Z',
                salesOrderLineId: 50, productVariantId: 7, productName: 'Tomaat', variantName: 'Ronde 67-82', unit: 'colli', unreservedQuantity: 15, reservableNow: 15,
                coverage: { fromStock: 15, expected: [], shortage: 0 } }]),
            nextPost: async () => { throw new Error('the worklist never posts'); },
        });
        let opened: [number, number] | null = null;
        await mounted.render(<AwaitingStockList token={0} onReserve={(orderId, lineId) => { opened = [orderId, lineId]; }} />);
        await mounted.waitFor(() => mounted.hasText('VO-2026-0005 · Groothandel G'), 'worklist');
        assert.ok(mounted.hasText('sales_awaiting_line'));
        await click(mounted.container.querySelector<HTMLButtonElement>('[data-tour="sales-awaiting-reserve"]')!);
        assert.deepEqual(opened, [5, 50]);
    });
});
