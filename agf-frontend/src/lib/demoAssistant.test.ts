import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { DEMO_ROUTES, TOUR_STEPS, findSalesOrders, isAllowedDemoHref, orderHref, tourStepIndexFor } from './demoAssistant.ts';

// Demo-assistent: alleen vaste routes, vaste rondleiding en zoeken op nummer, klant of referentie (alleen-lezen).

const order = (id: number, customerName: string | null, customerReference: string | null) => ({ id, customerName, customerReference });
const orders = [
    order(13, 'DEMO Klant B (donderdag-demo)', 'DEMO-A'),
    order(14, 'DEMO Klant B (donderdag-demo)', 'DEMO-B'),
    order(113, 'Local Market Stalls', 'PO-SMOKE-1'),
    order(51, 'DEMO-REP Klant 8D74', 'REF-13'),
];

describe('tour', () => {
    test('visits Aanvoer, Keuring, Beschikbaarheid and Verkoop in that order, only on allowed routes', () => {
        assert.deepEqual(TOUR_STEPS.map((step) => step.href), ['/supply', '/inspection', '/availability', '/sales-orders']);
        for (const step of TOUR_STEPS) assert.ok(isAllowedDemoHref(step.href), step.href);
    });

    test('finds the step for the current page', () => {
        assert.equal(tourStepIndexFor('/inspection'), 1);
        assert.equal(tourStepIndexFor('/sales-orders'), 3);
        assert.equal(tourStepIndexFor('/dashboard'), -1);
    });
});

describe('allowed navigation', () => {
    test('accepts only the fixed routes and a numeric order link', () => {
        assert.deepEqual([...DEMO_ROUTES], ['/supply', '/inspection', '/availability', '/sales-orders']);
        assert.ok(isAllowedDemoHref('/sales-orders?orderId=13'));
        for (const href of ['https://evil.example', '//evil.example', 'javascript:alert(1)', '/orders', '/sales-orders?orderId=1;x', '/sales-orders?orderId=0', '/supply/../orders', '/sales-orders?orderId=13&x=1']) {
            assert.equal(isAllowedDemoHref(href), false, href);
        }
    });

    test('orderHref builds a link only for a positive integer id', () => {
        assert.equal(orderHref(13), '/sales-orders?orderId=13');
        assert.throws(() => orderHref(0));
        assert.throws(() => orderHref(1.5));
    });
});

describe('findSalesOrders', () => {
    test('an empty query finds nothing', () => {
        assert.deepEqual(findSalesOrders(orders, '   '), { kind: 'empty' });
    });

    test('a number or #number matches the exact order number first', () => {
        assert.deepEqual(findSalesOrders(orders, '#13'), { kind: 'matches', orders: [orders[0]] });
        assert.deepEqual(findSalesOrders(orders, ' 14 '), { kind: 'matches', orders: [orders[1]] });
    });

    test('an unknown number finds nothing and never matches parts of other numbers', () => {
        assert.deepEqual(findSalesOrders(orders, '#9999'), { kind: 'matches', orders: [] });
        assert.deepEqual(findSalesOrders(orders, '#1'), { kind: 'matches', orders: [] });
    });

    test('a bare number without an exact order falls back to reference text', () => {
        assert.deepEqual(findSalesOrders(orders, '9'), { kind: 'matches', orders: [] });
        assert.deepEqual(findSalesOrders(orders, 'ref-13').orders, [orders[3]]);
    });

    test('customer and reference search ignore case and accents and can give several results', () => {
        assert.deepEqual(findSalesOrders(orders, 'demo klant b').orders?.map((o) => o.id), [13, 14]);
        assert.deepEqual(findSalesOrders(orders, 'po-smoke').orders?.map((o) => o.id), [113]);
        assert.deepEqual(findSalesOrders([order(7, 'Groenteboer Één', null)], 'een').orders?.map((o) => o.id), [7]);
    });

    test('does not search on products or other fields', () => {
        assert.deepEqual(findSalesOrders(orders, 'avocado').orders, []);
    });
});
