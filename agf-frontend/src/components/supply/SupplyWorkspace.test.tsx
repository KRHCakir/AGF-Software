import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, stubFetch } from '../../test/reactHarness.ts';
import SupplyWorkspace from './SupplyWorkspace';

// De vroegere werkplek "Aanvoer" als drie schermen met elk één taak, een eigen titel en één zin over wat je hier doet en wat daarna komt.
let mounted: Mounted;
let gets: string[];
const reference = (url: string) => (url.includes('/vendors') || url.includes('/customers') || url.includes('/products') ? json(200, []) : null);

describe('SupplyWorkspace', () => {
    beforeEach(() => {
        gets = [];
        window.localStorage.setItem('agf_actor_reference', 'wh-1');
        stubFetch({ onGet: (url) => { gets.push(url); return reference(url) ?? json(200, []); }, nextPost: async () => json(500, {}) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('Inkooporder aanmaken: own title and sentence, the warehouse form and no destination or mode choice', async () => {
        await mounted.render(<SupplyWorkspace task="purchase-order" />);
        await mounted.waitFor(() => mounted.hasText('supply_save_plan_warehouse'), 'form');
        assert.equal(mounted.container.querySelector('h1')?.textContent, 'nav_purchase_order_new');
        assert.equal(mounted.container.querySelector('[data-testid="page-intro"]')?.textContent, 'intro_purchase_order_new');
        assert.equal(mounted.container.querySelector('input[name="supply-destination"], input[name="supply-mode"]'), null);
    });

    test('Klantgebonden aanvoer: the DirectTrade form', async () => {
        await mounted.render(<SupplyWorkspace task="direct-trade" />);
        await mounted.waitFor(() => mounted.hasText('supply_save_plan_direct'), 'direct form');
        assert.equal(mounted.container.querySelector('h1')?.textContent, 'nav_direct_trade');
    });

    test('Ontvangst with ?order= loads that purchase order to choose it right away', async () => {
        await mounted.render(<SupplyWorkspace task="receipt" orderId={3} />);
        await mounted.waitFor(() => gets.some((url) => url.endsWith('/supply-commitments/3')), 'order 3 loaded');
        assert.equal(mounted.container.querySelector('h1')?.textContent, 'nav_receipts');
        assert.equal(mounted.container.querySelector('[data-testid="page-intro"]')?.textContent, 'intro_receipts');
    });
});

describe('SupplyWorkspace: another order in the link', () => {
    beforeEach(() => {
        window.localStorage.setItem('agf_actor_reference', 'wh-1');
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('a failed load of the new order never keeps the previous one chosen', async () => {
        const order = (id: number) => ({
            id, requestId: `r-${id}`, vendorId: 1, vendorName: `Leverancier ${id}`, customerId: null, customerName: null, route: 'Warehouse', status: 'Expected',
            origin: 'Peru', destination: null, expectedAt: '2026-11-04T06:00:00.000Z', vendorGgns: [],
            lines: [{ id: id * 10, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, expectedQuantity: 10, expectedUnit: 'box',
                expectedNetWeightKg: 40, weightMethod: 'Weighed', defaultNetKgPerPackage: null, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null }],
        });
        stubFetch({
            // Beide orders staan open; order 2 laden mislukt.
            onGet: (url) => url.endsWith('/supply-commitments/1') ? json(200, order(1)) : url.endsWith('/supply-commitments/2') ? json(500, { message: 'kapot' })
                : url.includes('/supply-commitments') ? json(200, [order(1), order(2)]) : json(200, []),
            nextPost: async () => json(500, {}),
        });
        await mounted.render(<SupplyWorkspace task="receipt" orderId={1} />);
        const checked = () => Array.from(mounted.container.querySelectorAll<HTMLInputElement>('input[type="checkbox"]')).filter((box) => box.checked).length;
        await mounted.waitFor(() => checked() === 1, 'the line of order 1 chosen');
        await mounted.render(<SupplyWorkspace task="receipt" orderId={2} />);
        await mounted.waitFor(() => mounted.container.querySelector('[role="alert"]'), 'error for order 2');
        await mounted.waitFor(() => mounted.hasText('Leverancier 2'), 'open orders listed');
        assert.equal(checked(), 0, 'nothing of order 1 stays chosen');
    });
});
