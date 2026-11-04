import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, stubFetch } from '../../test/reactHarness.ts';
import { WarehouseRow } from '../../lib/availability';
import SalesSupplyTable from './SalesSupplyTable';

// Verkoopweergave: Voorraad, Verwacht, Verkocht en Vrij per variant + eenheid; negatief is rood met "Tekort"; Verkopen opent een nieuwe
// verkooporder met het vrije aantal (minimaal 1); uitklappen toont de stand per partij, inkooporder en verkooporder.

const q = (quantity: number, netWeightKg = 0) => ({ quantity, netWeightKg });
const breakdown = () => ({ approved: q(0), quarantine: q(0), blocked: q(0), rejected: q(0) });
const balance = (stock: number, expected: number, sold: number) => ({
    productVariantId: 7, unit: 'colli', stock, expected, expectedNotReceived: expected, expectedInInspection: 0, sold, soldReserved: 0,
    free: stock + expected - sold, freeStock: stock - sold, shortageQuantity: Math.max(0, sold - stock - expected), shortage: sold > stock + expected,
});
const row = (sales: ReturnType<typeof balance>): WarehouseRow => ({
    key: '7:colli',
    variant: { productVariantId: 7, productName: 'Tomaat', variantName: 'Ronde 67-82', warehouseSupply: [], directTradeSupply: [] },
    supply: {
        unit: 'colli', weightBasis: null, availableBeforeReservations: q(sales.stock), reserved: { ...q(0), applied: true }, availableAfterReservations: q(sales.stock), reservedOnHold: q(0),
        warehouse: breakdown(), receivedOutsideWarehouse: breakdown(), expectedOnPallets: breakdown(), inTransit: breakdown(),
        expectedUnregistered: { quantity: sales.expected, netWeightKg: null }, warnings: [], sales,
    },
} as unknown as WarehouseRow);

let mounted: Mounted;
let gets: string[];
describe('SalesSupplyTable', () => {
    beforeEach(() => {
        gets = [];
        stubFetch({
            onGet: (url) => {
                gets.push(url);
                return json(200, {
                    generatedAt: '2026-11-03T10:00:00Z', basis: { source: 'x', reservationsApplied: true, legacyInventoryIncluded: false, stockLedgerIncluded: false },
                    productVariantId: 7, productName: 'Tomaat', variantName: 'Ronde 67-82', sourceLines: [], contentLines: [],
                    sales: [], expectedSupplies: [{ supplyCommitmentId: 2, supplyCommitmentLineId: 20, supplyOrderNumber: 'IO-2026-0002', expectedAt: '2026-11-05T06:00:00Z', lotNumber: null, unit: 'colli', notReceived: 20, inInspection: 0 }],
                    salesLines: [{ salesOrderId: 5, orderNumber: 'VO-2026-0005', customerId: 1, customerName: 'Groothandel G', customerReference: null, requestedDeliveryDate: '2026-11-06T06:00:00Z',
                        salesOrderLineId: 50, unit: 'colli', supplySource: 'Expected', openQuantity: 30, reservedQuantity: 0,
                        coverage: { fromStock: 0, expected: [{ supplyCommitmentId: 2, supplyOrderNumber: 'IO-2026-0002', expectedAt: '2026-11-05T06:00:00Z', quantity: 20, inInspection: false }], shortage: 10 } }],
                });
            },
            nextPost: async () => json(500, {}),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    const cell = (col: string) => mounted.container.querySelector(`td[data-col="${col}"]`)?.textContent?.trim() ?? '';

    test('40 in stock, 20 expected, 0 sold: free 60 and Verkopen proposes 60', async () => {
        await mounted.render(<SalesSupplyTable rows={[row(balance(40, 20, 0))]} onOpen={() => {}} />);
        assert.deepEqual([cell('stock'), cell('expected'), cell('sold'), cell('free')], ['40', '20', '0', '60']);
        assert.equal(mounted.container.querySelector('[data-free-state]')?.getAttribute('data-free-state'), 'free');
        const sell = mounted.container.querySelector<HTMLAnchorElement>('a[data-tour="availability-sell"]');
        assert.equal(sell?.getAttribute('href'), '/sales-orders?new=1&variantId=7&unit=colli&quantity=60');
    });

    test('70 sold: free -10 in red with the word Tekort, and Verkopen still works with 1', async () => {
        await mounted.render(<SalesSupplyTable rows={[row(balance(40, 20, 70))]} onOpen={() => {}} />);
        assert.equal(mounted.container.querySelector('[data-free-state]')?.getAttribute('data-free-state'), 'shortage');
        assert.match(cell('free'), /-10\s*sales_shortage/);
        assert.equal(mounted.container.querySelector('a[data-tour="availability-sell"]')?.getAttribute('href'), '/sales-orders?new=1&variantId=7&unit=colli&quantity=1');
    });

    test('expanding a row loads the breakdown per lot, purchase order and sales order for exactly this variant and unit', async () => {
        await mounted.render(<SalesSupplyTable rows={[row(balance(40, 20, 70))]} onOpen={() => {}} />);
        const toggle = mounted.container.querySelector<HTMLButtonElement>('button[aria-expanded]')!;
        assert.equal(toggle.getAttribute('aria-expanded'), 'false');
        await click(toggle);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="sales-breakdown"]'), 'breakdown');
        await mounted.waitFor(() => mounted.hasText('VO-2026-0005'), 'sales order listed');
        assert.ok(gets.some((url) => url.includes('/availability/variants/7?') && url.includes('unit=colli')));
        assert.ok(mounted.hasText('IO-2026-0002'));
        assert.ok(mounted.hasText('sales_cover_shortage'));
        assert.equal(toggle.getAttribute('aria-expanded'), 'true');
    });
});
