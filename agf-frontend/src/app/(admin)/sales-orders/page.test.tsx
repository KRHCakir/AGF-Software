import '../../../test/setupDom.ts';
import '../../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, deferred, json, stubFetch } from '../../../test/reactHarness.ts';
import { navigate } from '../../../test/nextNavigationStub.ts';
import { SalesOrder } from '../../../types/salesOrder';
import SalesOrdersPage from './page';

// Verkoopwerkplek: een traag antwoord van een eerder gekozen order (vernieuwen, herladen na fout) vervangt nooit de order
// die intussen is gekozen; ook een verouderde mislukking toont geen melding bij de huidige order.

const order = (id: number, customerName: string): SalesOrder => ({
    id, requestId: `o-${id}`, customerId: id, customerName, status: 'Open', customerReference: null,
    requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', createdAt: '2026-10-19T09:00:00.000Z', version: 1,
    cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false,
    lines: [{ id: id * 10, productVariantId: 2, productName: 'Avocado', productVariantName: 'Green', quantity: 20, unit: 'box', reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: 0, directTradeShippedQuantity: 0, deliveredQuantity: 0, delivered: false,
        dispatchedQuantity: 0, dispatchedNetWeightKg: 0, openQuantity: 20, fulfilled: false, version: 1, reservations: [] }],
});
const A = order(13, 'Klant A');
const B = order(14, 'Klant B');

type Answer = () => Promise<Response>;
let answers: Record<string, Answer[]>;
const next = (key: string, fallback: Answer): Answer => () => (answers[key]?.shift() ?? fallback)();
const ok = (body: unknown): Answer => async () => json(200, body);

let mounted: Mounted;
const refreshButton = () => mounted.buttons().find((b) => (b.textContent ?? '').includes('sales_refresh') && !(b.getAttribute('aria-label') ?? '').includes('sales_refresh')) ?? null;
const heading = () => mounted.container.querySelector('h2.text-lg')?.textContent ?? '';
const openOrder = (id: number) => mounted.buttons().find((b) => (b.getAttribute('aria-label') ?? '') === 'sales_open_order' && b.textContent?.includes(`#${id}`))!;

describe('SalesWorkspace stale order responses', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        window.localStorage.setItem('agf_actor_reference', 'office-1');
        answers = {};
        stubFetch({
            onGet: (url) => {
                if (/\/customers$/.test(url)) return json(200, [{ id: 13, name: 'Klant A' }, { id: 14, name: 'Klant B' }]);
                if (/\/products\?/.test(url)) return json(200, []);
                if (/\/sales-orders(\?.*)?$/.test(url)) return json(200, [A, B]);
                if (/\/sales-orders\/13$/.test(url)) return next('A', ok(A))();
                if (/\/sales-orders\/14$/.test(url)) return next('B', ok(B))();
                if (/\/dispatches$/.test(url)) return json(200, []);
                return json(404, { message: `unexpected GET ${url}` });
            },
            nextPost: async () => json(500, { message: 'no writes expected' }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('a slow refresh of order A does not replace order B after switching; an obsolete failure shows no notice', async () => {
        navigate('/sales-orders?orderId=13');
        await mounted.render(<SalesOrdersPage />);
        await mounted.waitFor(() => heading().includes('Klant A'), 'order A');
        // Vernieuwen van A blijft hangen; intussen wordt B gekozen en geladen.
        const slow = deferred<Response>();
        answers.A = [() => slow.promise];
        await click(refreshButton()!);
        await click(openOrder(14));
        await mounted.waitFor(() => heading().includes('Klant B'), 'order B');
        // Het trage antwoord van A komt binnen: B blijft staan.
        slow.resolve(json(200, { ...A, customerName: 'Klant A (laat)' }));
        await mounted.waitFor(() => refreshButton()?.disabled === false, 'refresh settled');
        assert.ok(heading().includes('Klant B'), `expected B, got "${heading()}"`);
        assert.equal(mounted.hasText('Klant A (laat)'), false);
        assert.equal(mounted.hasText('sales_refresh_failed'), false);
        // Verouderde mislukking: vernieuwen van B blijft hangen, terug naar A, dan faalt B: geen melding bij A.
        const failing = deferred<Response>();
        answers.B = [() => failing.promise];
        await click(refreshButton()!);
        await click(openOrder(13));
        await mounted.waitFor(() => heading().includes('Klant A'), 'order A again');
        failing.reject(new TypeError('network down'));
        await mounted.waitFor(() => refreshButton()?.disabled === false, 'refresh settled');
        assert.ok(heading().includes('Klant A'));
        assert.equal(mounted.hasText('sales_refresh_failed'), false, 'obsolete failure is ignored');
        // Een mislukking van de huidige order toont de melding wél.
        answers.A = [async () => json(500, { message: 'boom' })];
        await click(refreshButton()!);
        await mounted.waitFor(() => mounted.hasText('sales_refresh_failed'), 'failure notice for the current order');
        assert.ok(heading().includes('Klant A'), 'previous data stays');
    });

    test('a slow reload after an error (retry) does not replace the order selected in the meantime', async () => {
        navigate('/sales-orders?orderId=13');
        answers.A = [async () => json(500, { message: 'down' })];
        await mounted.render(<SalesOrdersPage />);
        const retry = await mounted.waitFor(() => mounted.button('availability_retry'), 'retry button');
        const slow = deferred<Response>();
        answers.A = [() => slow.promise];
        await click(retry);
        await click(openOrder(14));
        await mounted.waitFor(() => heading().includes('Klant B'), 'order B');
        slow.resolve(json(200, A));
        for (let i = 0; i < 5; i++) await mounted.waitFor(() => true, 'flush');
        assert.ok(heading().includes('Klant B'), `expected B, got "${heading()}"`);
    });

    test('switching away during the initial load of A keeps B', async () => {
        navigate('/sales-orders?orderId=13');
        const slow = deferred<Response>();
        answers.A = [() => slow.promise];
        await mounted.render(<SalesOrdersPage />);
        await mounted.waitFor(() => openOrder(14), 'list');
        await click(openOrder(14));
        await mounted.waitFor(() => heading().includes('Klant B'), 'order B');
        slow.resolve(json(200, A));
        for (let i = 0; i < 5; i++) await mounted.waitFor(() => true, 'flush');
        assert.ok(heading().includes('Klant B'));
    });
});
