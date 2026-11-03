import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import { matchesOrderSearch } from '../../lib/salesOrderRules';
import { SalesOrder, SalesOrderLine } from '../../types/salesOrder';
import LineSupplyInfo from './LineSupplyInfo';
import SalesOrderList from './SalesOrderList';

// Orderregel en orderlijst bij verkopen vóór aankomst: bron, stand van de variant, dekking (wacht op inkooporder IO-…, tekort) en de
// indicatoren Tekort en Wacht op voorraad; zoeken op VO-nummer, klantreferentie en partij.

const line = (overrides: Partial<SalesOrderLine> = {}): SalesOrderLine => ({
    id: 50, productVariantId: 7, productName: 'Tomaat', productVariantName: 'Ronde 67-82', quantity: 30, unit: 'colli', palletCount: null, packagesPerPallet: null,
    reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, directTradeShippedQuantity: 0,
    openQuantity: 30, fulfilled: false, deliveredQuantity: 0, delivered: false, version: 0, reservations: [], supplySource: 'Expected', awaitingStock: true,
    balance: { productVariantId: 7, unit: 'colli', stock: 40, expected: 20, expectedNotReceived: 20, expectedInInspection: 0, sold: 70, soldReserved: 0, free: -10, freeStock: 0, shortageQuantity: 10, shortage: true },
    coverage: { fromStock: 0, expected: [{ supplyCommitmentId: 2, supplyOrderNumber: 'IO-2026-0002', expectedAt: '2026-11-05T12:00:00', quantity: 20, inInspection: false }], shortage: 10 },
    ...overrides,
});
const order = (overrides: Partial<SalesOrder> = {}): SalesOrder => ({
    id: 5, orderNumber: 'VO-2026-0005', requestId: 'r', customerId: 1, customerName: 'Groothandel G', status: 'Open', customerReference: 'PO-778', internalNote: null,
    requestedDeliveryDate: '2026-11-06T06:00:00Z', actorReference: 'verkoop-1', createdAt: '2026-11-03T10:00:00Z', version: 0, cancelledAt: null, cancelReason: null, cancelledBy: null,
    fulfilled: false, delivered: false, lines: [line()], shortage: true, awaitingStock: true, ...overrides,
});

let mounted: Mounted;
describe('LineSupplyInfo and the order list', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('a line sold before arrival shows its source, the balance with Tekort, and waits for IO-2026-0002 (expected 05-11) with 10 short', async () => {
        await mounted.render(<LineSupplyInfo line={line()} />);
        const info = mounted.container.querySelector('[data-testid="line-supply-info"]')!;
        assert.equal(info.querySelector('[data-testid="line-source"]')?.textContent, 'sales_source_expected');
        assert.ok(info.querySelector('[data-testid="line-balance"]')?.textContent?.includes('sales_shortage'));
        const coverage = info.querySelector('[data-testid="line-coverage"]')!;
        assert.equal(coverage.querySelector('a')?.textContent, 'IO-2026-0002');
        assert.equal(coverage.querySelector('a')?.getAttribute('href'), '/purchase-orders?id=2');
        assert.ok(coverage.textContent?.includes('sales_cover_shortage'));
        assert.ok(mounted.hasText('sales_awaiting_stock'));
    });

    test('an older answer without these fields shows nothing extra', async () => {
        await mounted.render(<LineSupplyInfo line={line({ supplySource: undefined, balance: undefined, coverage: undefined, awaitingStock: undefined })} />);
        assert.equal(mounted.container.querySelector('[data-testid="line-supply-info"]'), null);
    });

    test('the list names the order by its VO number and shows Tekort and Wacht op voorraad', async () => {
        await mounted.render(<SalesOrderList orders={[order(), order({ id: 6, orderNumber: undefined, shortage: false, awaitingStock: false })]} selectedId={null} onSelect={() => {}} />);
        assert.ok(mounted.hasText('VO-2026-0005 · Groothandel G'));
        assert.ok(mounted.hasText('#6 · Groothandel G'), 'older answers keep #id');
        assert.equal(mounted.container.querySelectorAll('[data-testid="order-shortage"]').length, 1);
        assert.equal(mounted.container.querySelectorAll('[data-testid="order-awaiting-stock"]').length, 1);
    });

    test('search finds the VO number, the customer reference and a reserved lot', () => {
        const reserved = order({ lines: [line({ reservations: [{ lotNumber: 'LOT-A' } as unknown as SalesOrderLine['reservations'][number]] })] });
        assert.ok(matchesOrderSearch(reserved, 'vo-2026-0005'));
        assert.ok(matchesOrderSearch(reserved, 'PO-778'));
        assert.ok(matchesOrderSearch(reserved, 'lot-a'));
        assert.ok(!matchesOrderSearch(reserved, 'lot-b'));
    });
});
