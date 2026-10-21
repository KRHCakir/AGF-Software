import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import { SalesOrder } from '../../types/salesOrder';
import SalesOrderList from './SalesOrderList';

// Orderlijst als eigen scrollgebied: sticky tabelkop, duidelijke selectie, en de gekozen order wordt binnen de lijst in beeld
// gebracht (alleen de lijst scrolt). jsdom heeft geen layout; afmetingen worden per element gesimuleerd.

const order = (id: number): SalesOrder => ({
    id, requestId: `o-${id}`, customerId: id, customerName: `Klant ${id}`, status: 'Open', customerReference: null, internalNote: null,
    requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version: 1,
    cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false,
    lines: [{ id: id * 10, productVariantId: 2, productName: 'Avocado', productVariantName: 'Green', quantity: 20, unit: 'box', palletCount: null, packagesPerPallet: null, reservedQuantity: 0, reservedNetWeightKg: 0,
        allocatedQuantity: 0, directTradeShippedQuantity: 0, deliveredQuantity: 0, delivered: false, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, openQuantity: 20,
        fulfilled: false, version: 1, reservations: [] }],
});
const orders = [1, 2, 3, 4, 5].map(order);

let mounted: Mounted;
const scroller = () => mounted.container.querySelector<HTMLDivElement>('[data-testid="sales-order-list-scroll"]')!;
const box = (top: number, height: number) => ({ top, bottom: top + height, height, left: 0, right: 400, width: 400, x: 0, y: top, toJSON: () => ({}) }) as DOMRect;

// Lijst van 300 px hoog vanaf y=100 met een kop van 30 px; elke rij 100 px, verschoven met scrollTop.
function simulateLayout() {
    const container = scroller();
    let scrollTop = 0;
    Object.defineProperty(container, 'scrollTop', { configurable: true, get: () => scrollTop, set: (value: number) => { scrollTop = Math.max(0, value); } });
    container.getBoundingClientRect = () => box(100, 300);
    container.querySelector('thead')!.getBoundingClientRect = () => box(100, 30);
    container.querySelectorAll<HTMLElement>('tbody tr').forEach((row, index) => {
        row.getBoundingClientRect = () => box(130 + index * 100 - scrollTop, 100);
    });
    return () => scrollTop;
}

describe('SalesOrderList as its own scroll area', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('header cells are sticky and the selected row is clearly marked', async () => {
        await mounted.render(<SalesOrderList orders={orders} selectedId={3} onSelect={() => {}} />);
        const headers = Array.from(scroller().querySelectorAll('thead th'));
        assert.ok(headers.length === 3 && headers.every((th) => th.className.includes('sticky') && th.className.includes('top-0')));
        const selected = scroller().querySelector<HTMLElement>('tr[data-order-id="3"]')!;
        assert.equal(selected.getAttribute('aria-selected'), 'true');
        assert.ok(selected.className.includes('bg-blue-100') && selected.className.includes('inset_4px'), 'background plus a left marker');
        assert.equal(selected.querySelector('button')!.getAttribute('aria-current'), 'true');
        assert.equal(scroller().querySelector('tr[data-order-id="2"]')!.getAttribute('aria-selected'), 'false');
    });

    test('a selection below the visible part is scrolled into view within the list only; a visible one is left alone', async () => {
        await mounted.render(<SalesOrderList orders={orders} selectedId={null} onSelect={() => {}} />);
        const scrollTop = simulateLayout();
        await mounted.render(<SalesOrderList orders={orders} selectedId={2} onSelect={() => {}} />);
        assert.equal(scrollTop(), 0, 'row 2 (230–330) is already visible');
        await mounted.render(<SalesOrderList orders={orders} selectedId={5} onSelect={() => {}} />);
        assert.equal(scrollTop(), 630 - 400 + 8, 'row 5 (530–630) is scrolled into view, plus a small margin');
        await mounted.render(<SalesOrderList orders={orders} selectedId={1} onSelect={() => {}} />);
        assert.equal(scrollTop(), 0, 'row 1 is brought back below the sticky header');
    });
});
