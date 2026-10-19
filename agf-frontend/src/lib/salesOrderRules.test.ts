import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
    MAX_ORDER_LINES,
    buildOrderPayload,
    fullRemainderFits,
    groupErrors,
    lineProgress,
    matchesOrderSearch,
    normalizeReason,
    orderProgress,
    remainingAfter,
    toLocalMidnightIso,
    validateReservationInput,
} from './salesOrderRules.ts';
import type { BuildResult, OrderDraft } from './salesOrderRules.ts';

// Onafhankelijk uitgeschreven naar de backendregels in SalesOrderService en StockReservationService.
// Draait met de ingebouwde testrunner van Node (type stripping): `npm test`.

const line = (productVariantId: string, quantity: string, unit: 'box' | 'colli' | '' = 'box') =>
    ({ key: `${productVariantId}-${unit}-${quantity}`, productVariantId, quantity, unit });

const validDraft = (): OrderDraft => ({
    customerId: '7',
    customerReference: '  PO-2026-1  ',
    requestedDeliveryDate: '2026-10-23',
    lines: [line('1', '30'), line('1', '5', 'colli'), line('3', '12,5')],
});

const codes = (result: BuildResult<unknown>) => (result.ok ? {} : groupErrors(result.errors));

describe('buildOrderPayload', () => {
    test('a valid draft becomes the exact payload: trimmed reference, parsed decimals, explicit units', () => {
        const result = buildOrderPayload(validDraft(), ' office-1 ');
        assert.equal(result.ok, true);
        if (!result.ok) return;
        assert.equal(result.value.customerId, 7);
        assert.equal(result.value.customerReference, 'PO-2026-1');
        assert.equal(result.value.actorReference, 'office-1');
        assert.equal(result.value.requestedDeliveryDate, new Date(2026, 9, 23).toISOString());
        assert.deepEqual(result.value.lines, [
            { productVariantId: 1, quantity: 30, unit: 'box' },
            { productVariantId: 1, quantity: 5, unit: 'colli' },
            { productVariantId: 3, quantity: 12.5, unit: 'box' },
        ]);
    });

    test('a blank reference becomes null', () => {
        const result = buildOrderPayload({ ...validDraft(), customerReference: '   ' }, 'office-1');
        assert.equal(result.ok && result.value.customerReference, null);
    });

    test('customer, date, actor and at least one line are required', () => {
        const result = buildOrderPayload({ customerId: '', customerReference: '', requestedDeliveryDate: '', lines: [] }, '  ');
        assert.deepEqual(codes(result), {
            actorReference: ['required'],
            customerId: ['required'],
            requestedDeliveryDate: ['required'],
            lines: ['required'],
        });
    });

    test('an impossible calendar date is rejected, a leap day is accepted', () => {
        assert.deepEqual(codes(buildOrderPayload({ ...validDraft(), requestedDeliveryDate: '2026-02-30' }, 'office-1')), { requestedDeliveryDate: ['invalid_date'] });
        assert.equal(buildOrderPayload({ ...validDraft(), requestedDeliveryDate: '2028-02-29' }, 'office-1').ok, true);
        assert.equal(toLocalMidnightIso('2026-13-01'), null);
        assert.equal(toLocalMidnightIso('23-10-2026'), null);
    });

    test('the same variant and unit twice is a duplicate on the second line only; another unit is allowed', () => {
        const draft = { ...validDraft(), lines: [line('1', '30'), line('1', '5', 'colli'), line('1', '2')] };
        assert.deepEqual(codes(buildOrderPayload(draft, 'office-1')), { 'lines[2]': ['duplicate_line'] });
    });

    test('line errors use the backend field keys and never mask a duplicate check on invalid lines', () => {
        const draft = { ...validDraft(), lines: [line('', '0'), line('2', 'abc', ''), line('2', '1,2345')] };
        assert.deepEqual(codes(buildOrderPayload(draft, 'office-1')), {
            'lines[0].productVariantId': ['required'],
            'lines[0].quantity': ['not_positive'],
            'lines[1].quantity': ['invalid'],
            'lines[1].unit': ['required'],
            'lines[2].quantity': ['too_many_decimals'],
        });
    });

    test('too long reference and actor, and more than the maximum number of lines', () => {
        const draft = { ...validDraft(), customerReference: 'x'.repeat(101), lines: Array.from({ length: MAX_ORDER_LINES + 1 }, (_, i) => line(String(i + 1), '1')) };
        assert.deepEqual(codes(buildOrderPayload(draft, 'a'.repeat(101))), {
            actorReference: ['too_long'],
            customerReference: ['too_long'],
            lines: ['too_many_lines'],
        });
        assert.equal(buildOrderPayload({ ...validDraft(), customerReference: 'x'.repeat(100) }, 'a'.repeat(100)).ok, true);
    });
});

describe('validateReservationInput', () => {
    const stock = { availableQuantity: 25, availableNetWeightKg: 100 };

    test('a partial reservation below both balances and within demand is valid; comma decimals accepted', () => {
        const result = validateReservationInput({ quantity: '10', netKg: '40,5' }, stock, 30);
        assert.deepEqual(result, { ok: true, value: { quantity: 10, netWeightKg: 40.5 } });
    });

    test('the full remainder requires both balances exactly', () => {
        assert.deepEqual(validateReservationInput({ quantity: '25', netKg: '100' }, stock, 30), { ok: true, value: { quantity: 25, netWeightKg: 100 } });
        assert.deepEqual(codes(validateReservationInput({ quantity: '25', netKg: '99' }, stock, 30)), { netWeightKg: ['remainder_mismatch'] });
        assert.deepEqual(codes(validateReservationInput({ quantity: '24', netKg: '100' }, stock, 30)), { netWeightKg: ['remainder_mismatch'] });
    });

    test('never above the content balances, in quantity or in kg', () => {
        assert.deepEqual(codes(validateReservationInput({ quantity: '26', netKg: '100' }, stock, 30)), { quantity: ['exceeds_available'] });
        assert.deepEqual(codes(validateReservationInput({ quantity: '10', netKg: '100,001' }, stock, 30)), { netWeightKg: ['exceeds_available'] });
    });

    test('never above the open demand of the order line, and kg is never derived from the quantity', () => {
        assert.deepEqual(codes(validateReservationInput({ quantity: '10', netKg: '40' }, stock, 8)), { quantity: ['exceeds_demand'] });
        assert.deepEqual(codes(validateReservationInput({ quantity: '10', netKg: '' }, stock, 30)), { netWeightKg: ['empty'] });
        assert.deepEqual(codes(validateReservationInput({ quantity: '', netKg: '' }, stock, 30)), { quantity: ['empty'], netWeightKg: ['empty'] });
    });

    test('invalid numbers stop before the balance checks', () => {
        assert.deepEqual(codes(validateReservationInput({ quantity: '0', netKg: 'x' }, stock, 30)), { quantity: ['not_positive'], netWeightKg: ['invalid'] });
    });
});

describe('fullRemainderFits and remainingAfter', () => {
    test('only when the whole remaining content fits the open demand', () => {
        assert.equal(fullRemainderFits({ availableQuantity: 25, availableNetWeightKg: 100 }, 25), true);
        assert.equal(fullRemainderFits({ availableQuantity: 25, availableNetWeightKg: 100 }, 30), true);
        assert.equal(fullRemainderFits({ availableQuantity: 25, availableNetWeightKg: 100 }, 24), false);
        assert.equal(fullRemainderFits({ availableQuantity: 0, availableNetWeightKg: 0 }, 30), false);
        assert.equal(fullRemainderFits({ availableQuantity: 25, availableNetWeightKg: 100 }, 0), false);
    });

    test('remaining demand after a reservation is never negative', () => {
        assert.equal(remainingAfter(30, 10), 20);
        assert.equal(remainingAfter(30, 30), 0);
        assert.equal(remainingAfter(5, 10), 0);
    });
});

describe('normalizeReason', () => {
    test('trims, treats blank as missing when required and as null when optional, limits to 500', () => {
        assert.deepEqual(normalizeReason('  Klant annuleert ', true), { ok: true, value: 'Klant annuleert' });
        assert.deepEqual(codes(normalizeReason('   ', true)), { reason: ['required'] });
        assert.deepEqual(normalizeReason('   ', false), { ok: true, value: null });
        assert.equal(normalizeReason('r'.repeat(500), true).ok, true);
        assert.deepEqual(codes(normalizeReason('r'.repeat(501), true)), { reason: ['too_long'] });
    });
});

describe('progress and search', () => {
    const reservation = (onHold: boolean) => ({ onHold });
    const order = {
        id: 12,
        customerName: 'Groente & Fruit Zuid',
        customerReference: 'PO-77',
        lines: [
            { id: 1, productName: 'Avocado', productVariantName: 'Hass', quantity: 30, reservedQuantity: 30, openQuantity: 0, reservations: [reservation(false), reservation(true)] },
            { id: 2, productName: 'Mango', productVariantName: 'Kent', quantity: 5, reservedQuantity: 2, openQuantity: 3, reservations: [reservation(false)] },
        ],
    };

    test('line progress comes straight from the server totals; on hold comes from the server flag only', () => {
        assert.deepEqual(lineProgress(order.lines[0]), { reserved: 30, total: 30, open: 0, complete: true, onHold: true });
        assert.deepEqual(lineProgress(order.lines[1]), { reserved: 2, total: 5, open: 3, complete: false, onHold: false });
    });

    test('order progress counts complete lines and reservations on hold', () => {
        assert.deepEqual(orderProgress(order), { linesComplete: 1, linesTotal: 2, onHoldCount: 1, complete: false });
        assert.deepEqual(orderProgress({ lines: [] }), { linesComplete: 0, linesTotal: 0, onHoldCount: 0, complete: false });
    });

    test('search matches order number, customer (accent-insensitive), reference and products', () => {
        assert.equal(matchesOrderSearch(order, ''), true);
        assert.equal(matchesOrderSearch(order, '#12'), true);
        assert.equal(matchesOrderSearch(order, 'zuid'), true);
        assert.equal(matchesOrderSearch(order, 'po-77'), true);
        assert.equal(matchesOrderSearch(order, 'kent'), true);
        assert.equal(matchesOrderSearch(order, 'banaan'), false);
    });
});
