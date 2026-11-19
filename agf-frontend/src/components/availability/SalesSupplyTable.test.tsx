import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, stubFetch } from '../../test/reactHarness.ts';
import { WarehouseRow } from '../../lib/availability';
import SalesSupplyTable from './SalesSupplyTable';

// Verkoopweergave: Besteld, Voorraad, Verkocht en Vrij per variant + eenheid (fixes 5b); negatief is rood met "Tekort"; Verkopen opent een nieuwe
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

    test('fixes 5b: the columns and the blocks are Besteld, Voorraad, Verkocht, Vrij te verkopen', async () => {
        await mounted.render(<SalesSupplyTable rows={[row(balance(40, 20, 0))]} onOpen={() => {}} />);
        const head = Array.from(mounted.container.querySelectorAll('thead th')).map((th) => th.firstChild?.textContent ?? '');
        assert.deepEqual(head.slice(2, 6), ['sales_col_expected', 'sales_col_stock', 'sales_col_sold', 'sales_col_free']);
        assert.deepEqual(Array.from(mounted.container.querySelectorAll('tbody td[data-col]')).map((td) => td.getAttribute('data-col')), ['expected', 'stock', 'sold', 'free']);
    });

    test('fixes 5b: the breakdown follows the same order and says where the goods are: pallet and bin, not received yet, not assigned yet', async () => {
        stubFetch({
            onGet: () => json(200, {
                generatedAt: '2026-11-03T10:00:00Z', basis: { source: 'x', reservationsApplied: true, legacyInventoryIncluded: false, stockLedgerIncluded: false },
                productVariantId: 7, productName: 'Tomaat', variantName: 'Ronde 67-82', sourceLines: [], sales: [],
                contentLines: [
                    { palletContentLineId: 1, palletId: 11, palletReference: 'P-000011', category: 'warehouse', logisticsStatus: 'Received', locationContext: 'Warehouse', warehouseLocation: 'Cel 3',
                        route: 'Warehouse', customerId: null, customerName: null, supplyCommitmentLineId: 19, lotNumber: 'L1', unit: 'colli', weightMethod: 'Weighed', netKgPerPackage: null, ggn: null, quantity: 40, netWeightKg: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, onHandNetWeightKg: 0, depleted: false, origin: 'Receipt', reservedNetWeightKg: 0, onHold: false, availableQuantity: 40, availableNetWeightKg: 0, sellableNow: true, closed: false, qualityStatus: 'Approved', onHandQuantity: 40, reservedQuantity: 10 },
                    { palletContentLineId: 2, palletId: 12, palletReference: 'P-000012', category: 'warehouse', logisticsStatus: 'Received', locationContext: 'Warehouse', warehouseLocation: null,
                        route: 'Warehouse', customerId: null, customerName: null, supplyCommitmentLineId: 20, lotNumber: 'L2', unit: 'colli', weightMethod: 'Weighed', netKgPerPackage: null, ggn: null, quantity: 5, netWeightKg: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, onHandNetWeightKg: 0, depleted: false, origin: 'Receipt', reservedNetWeightKg: 0, onHold: false, availableQuantity: 5, availableNetWeightKg: 0, sellableNow: true, closed: false, qualityStatus: 'Quarantine', onHandQuantity: 5, reservedQuantity: 0 },
                ],
                expectedSupplies: [{ supplyCommitmentId: 2, supplyCommitmentLineId: 20, supplyOrderNumber: 'IO-2026-0002', expectedAt: null, lotNumber: null, unit: 'colli', notReceived: 15, inInspection: 5 }],
                salesLines: [
                    { salesOrderId: 5, orderNumber: 'VO-2026-0005', customerId: 1, customerName: 'Groothandel G', customerReference: null, requestedDeliveryDate: '2026-11-06T06:00:00Z',
                        salesOrderLineId: 50, unit: 'colli', supplySource: 'Stock', openQuantity: 30, reservedQuantity: 10, coverage: { fromStock: 30, expected: [], shortage: 0 },
                        locations: [{ palletId: 11, palletReference: 'P-000011', locationContext: 'Warehouse', warehouseLocation: 'Cel 3', quantity: 10 }] },
                    { salesOrderId: 6, orderNumber: 'VO-2026-0006', customerId: 1, customerName: 'Groothandel G', customerReference: null, requestedDeliveryDate: '2026-11-07T06:00:00Z',
                        salesOrderLineId: 60, unit: 'colli', supplySource: 'Expected', openQuantity: 8, reservedQuantity: 0, coverage: { fromStock: 0, expected: [], shortage: 0 } },
                ],
            }),
            nextPost: async () => json(500, {}),
        });
        await mounted.render(<SalesSupplyTable rows={[row(balance(40, 20, 38))]} onOpen={() => {}} />);
        await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-expanded]')!);
        await mounted.waitFor(() => mounted.hasText('VO-2026-0006'), 'breakdown loaded');
        const headings = Array.from(mounted.container.querySelectorAll('[data-testid="sales-breakdown"] h3')).map((h) => h.textContent);
        assert.deepEqual(headings, ['sales_breakdown_purchases', 'sales_breakdown_lots', 'sales_breakdown_sales']);
        const purchase = mounted.container.querySelector('[data-testid="purchase-locations"]')!.textContent ?? '';
        assert.ok(purchase.includes('15 sales_location_not_in_yet'), purchase);
        assert.ok(purchase.includes('sales_location_on_pallet') && purchase.includes('availability_location_warehouse · sales_location_no_bin'), purchase);
        const lots = Array.from(mounted.container.querySelectorAll('[data-testid="lot-location"]')).map((span) => span.textContent);
        assert.deepEqual(lots, ['availability_location_warehouse · Cel 3', 'availability_location_warehouse · sales_location_no_bin']);
        const sold = Array.from(mounted.container.querySelectorAll('[data-testid="sale-locations"]')).map((list) => list.textContent ?? '');
        assert.ok(sold[0].includes('availability_location_warehouse · Cel 3') && sold[0].includes('sales_location_unassigned'), sold[0]);
        assert.ok(sold[1].includes('sales_location_unassigned_expected'), sold[1]);
    });
});
