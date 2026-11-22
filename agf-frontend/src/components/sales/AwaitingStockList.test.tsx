import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, setSelectValue, stubFetch } from '../../test/reactHarness.ts';
import { act } from 'react';
import AwaitingStockList from './AwaitingStockList';

// Werklijst "Verkocht, wacht op voorraad": de regel die vóór aankomst verkocht is en nu gereserveerd kan worden; de lijst reserveert nooit zelf.

let mounted: Mounted;
describe('AwaitingStockList', () => {
    beforeEach(() => { window.localStorage.clear(); mounted = new Mounted(); });
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

    // Feedback 22 november: Sorteren op; review #320: een artikel zonder variant sorteert op de productnaam, nooit op "null".
    test('sorting on product: an article without a variant sorts on its product name', async () => {
        const line = (id: number, productName: string, variantName: string | null) => ({ salesOrderId: id, orderNumber: `VO-2026-000${id}`, customerId: 1, customerName: 'Groothandel G',
            customerReference: null, requestedDeliveryDate: `2026-11-0${id}T06:00:00Z`, salesOrderLineId: id * 10, productVariantId: id, productName, variantName, unit: 'colli',
            unreservedQuantity: 5, reservableNow: 5, coverage: { fromStock: 5, expected: [], shortage: 0 } });
        stubFetch({ onGet: () => json(200, [line(1, 'Gember', 'Bio'), line(2, 'Gember', null), line(3, 'Mango', 'Kent')]), nextPost: async () => { throw new Error('no posts'); } });
        await mounted.render(<AwaitingStockList token={0} onReserve={() => {}} />);
        await mounted.waitFor(() => mounted.container.querySelector('#awaiting-stock-sort'), 'select');
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('#awaiting-stock-sort')!, 'product:asc'); });
        const order = Array.from(mounted.container.querySelectorAll('[data-awaiting-line]')).map((li) => li.getAttribute('data-awaiting-line'));
        assert.deepEqual(order, ['20', '10', '30'], 'Gember before Gember Bio (not "Gember null" after it)');
    });
});
