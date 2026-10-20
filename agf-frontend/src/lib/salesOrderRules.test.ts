import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
    MAX_ORDER_LINES,
    buildOrderPayload,
    dispatchTotals,
    expectedAfterRequested,
    isShippableAllocation,
    isShippedAllocation,
    lotDeviates,
    validateShipmentLine,
    validateShippedOn,
    fullAllocationRemainder,
    validateAllocationInput,
    isDispatchable,
    normalizeDispatchReference,
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
        fulfilled: false,
        lines: [
            { id: 1, productName: 'Avocado', productVariantName: 'Hass', quantity: 30, reservedQuantity: 30, allocatedQuantity: 0, dispatchedQuantity: 0, openQuantity: 0, fulfilled: false, reservations: [reservation(false), reservation(true)] },
            { id: 2, productName: 'Mango', productVariantName: 'Kent', quantity: 5, reservedQuantity: 2, allocatedQuantity: 0, dispatchedQuantity: 0, openQuantity: 3, fulfilled: false, reservations: [reservation(false)] },
        ],
    };

    test('line progress comes straight from the server totals; on hold comes from the server flag only', () => {
        assert.deepEqual(lineProgress(order.lines[0]), { reserved: 30, allocated: 0, dispatched: 0, total: 30, open: 0, complete: true, onHold: true, ready: false, completeWithExpected: false, fulfilled: false, partiallyDispatched: false });
        assert.deepEqual(lineProgress(order.lines[1]), { reserved: 2, allocated: 0, dispatched: 0, total: 5, open: 3, complete: false, onHold: false, ready: false, completeWithExpected: false, fulfilled: false, partiallyDispatched: false });
    });

    test('a fully reserved line on blocked content is complete but never ready; the totals stay unchanged', () => {
        const blockedLine = { quantity: 30, reservedQuantity: 30, allocatedQuantity: 0, dispatchedQuantity: 0, openQuantity: 0, fulfilled: false, reservations: [reservation(true)] };
        assert.deepEqual(lineProgress(blockedLine), { reserved: 30, allocated: 0, dispatched: 0, total: 30, open: 0, complete: true, onHold: true, ready: false, completeWithExpected: false, fulfilled: false, partiallyDispatched: false });
        const approvedLine = { ...blockedLine, reservations: [reservation(false), reservation(false)] };
        assert.deepEqual(lineProgress(approvedLine), { reserved: 30, allocated: 0, dispatched: 0, total: 30, open: 0, complete: true, onHold: false, ready: true, completeWithExpected: false, fulfilled: false, partiallyDispatched: false });
        // Een vrijgegeven reservering op geblokkeerde inhoud is volgens de server niet on hold en blokkeert dus niet.
        const releasedOnBlocked = { ...blockedLine, reservations: [reservation(false)] };
        assert.equal(lineProgress(releasedOnBlocked).ready, true);
    });

    test('order progress counts complete lines and reservations on hold', () => {
        assert.deepEqual(orderProgress(order), { linesComplete: 1, linesFulfilled: 0, linesTotal: 2, onHoldCount: 1, allocatedLines: 0, complete: false, ready: false, fulfilled: false, partiallyDispatched: false });
        assert.deepEqual(orderProgress({ fulfilled: false, lines: [] }), { linesComplete: 0, linesFulfilled: 0, linesTotal: 0, onHoldCount: 0, allocatedLines: 0, complete: false, ready: false, fulfilled: false, partiallyDispatched: false });
    });

    test('a fully reserved order with a blocked line is complete but not ready; without on hold it is ready', () => {
        const fullyReservedBlocked = { fulfilled: false, lines: [
            { quantity: 30, reservedQuantity: 30, allocatedQuantity: 0, dispatchedQuantity: 0, openQuantity: 0, fulfilled: false, reservations: [reservation(true)] },
            { quantity: 5, reservedQuantity: 5, allocatedQuantity: 0, dispatchedQuantity: 0, openQuantity: 0, fulfilled: false, reservations: [reservation(false)] },
        ] };
        assert.deepEqual(orderProgress(fullyReservedBlocked), { linesComplete: 2, linesFulfilled: 0, linesTotal: 2, onHoldCount: 1, allocatedLines: 0, complete: true, ready: false, fulfilled: false, partiallyDispatched: false });
        const fullyReservedApproved = { fulfilled: false, lines: fullyReservedBlocked.lines.map((line) => ({ ...line, reservations: [reservation(false)] })) };
        assert.deepEqual(orderProgress(fullyReservedApproved), { linesComplete: 2, linesFulfilled: 0, linesTotal: 2, onHoldCount: 0, allocatedLines: 0, complete: true, ready: true, fulfilled: false, partiallyDispatched: false });
    });

    test('a partly dispatched line is complete-or-open by totals but never fulfilled; a fully dispatched line is fulfilled, not "fully reserved"', () => {
        // Deels verzonden: 10 verzonden, 15 gereserveerd, 5 open.
        const partly = { quantity: 30, reservedQuantity: 15, allocatedQuantity: 0, dispatchedQuantity: 10, openQuantity: 5, fulfilled: false, reservations: [reservation(false)] };
        assert.deepEqual(lineProgress(partly), { reserved: 15, allocated: 0, dispatched: 10, total: 30, open: 5, complete: false, onHold: false, ready: false, completeWithExpected: false, fulfilled: false, partiallyDispatched: true });
        // Volledig verzonden: 0 gereserveerd, open 0; dit is "geleverd", niet "volledig gereserveerd".
        const delivered = { quantity: 30, reservedQuantity: 0, allocatedQuantity: 0, dispatchedQuantity: 30, openQuantity: 0, fulfilled: true, reservations: [] };
        const progress = lineProgress(delivered);
        assert.equal(progress.fulfilled, true);
        assert.equal(progress.ready, true, 'totals are complete');
        assert.equal(progress.partiallyDispatched, false);
    });

    test('order progress: partly dispatched order versus fully fulfilled order', () => {
        const partlyOrder = { fulfilled: false, lines: [
            { quantity: 30, reservedQuantity: 0, allocatedQuantity: 0, dispatchedQuantity: 30, openQuantity: 0, fulfilled: true, reservations: [] },
            { quantity: 5, reservedQuantity: 5, allocatedQuantity: 0, dispatchedQuantity: 0, openQuantity: 0, fulfilled: false, reservations: [reservation(false)] },
        ] };
        assert.deepEqual(orderProgress(partlyOrder), { linesComplete: 2, linesFulfilled: 1, linesTotal: 2, onHoldCount: 0, allocatedLines: 0, complete: true, ready: true, fulfilled: false, partiallyDispatched: true });
        const fulfilledOrder = { fulfilled: true, lines: partlyOrder.lines.map((line) => ({ ...line, reservedQuantity: 0, allocatedQuantity: 0, dispatchedQuantity: line.quantity, fulfilled: true, reservations: [] })) };
        assert.deepEqual(orderProgress(fulfilledOrder), { linesComplete: 2, linesFulfilled: 2, linesTotal: 2, onHoldCount: 0, allocatedLines: 0, complete: true, ready: true, fulfilled: true, partiallyDispatched: false });
    });

    test('dispatch selection: only active reservations that are not on hold; totals per unit, never merged', () => {
        const active = { id: 1, status: 'Active', source: 'Physical', onHold: false, quantity: 5, unit: 'box', netWeightKg: 20 };
        const held = { id: 2, status: 'Active', source: 'Physical', onHold: true, quantity: 5, unit: 'box', netWeightKg: 20 };
        const released = { id: 3, status: 'Released', source: 'Physical', onHold: false, quantity: 5, unit: 'box', netWeightKg: 20 };
        const consumed = { id: 4, status: 'Consumed', source: 'Physical', onHold: false, quantity: 5, unit: 'box', netWeightKg: 20 };
        const colli = { id: 5, status: 'Active', source: 'Physical', onHold: false, quantity: 2.5, unit: 'colli', netWeightKg: 13.75 };
        // Een toewijzing van verwachte klantgebonden aanvoer is nooit vanuit het magazijn te verzenden, ook al is zij Active.
        const allocation = { id: 6, status: 'Active', source: 'DirectTrade', onHold: false, quantity: 5, unit: 'box', netWeightKg: null };
        assert.deepEqual([active, held, released, consumed, colli, allocation].map(isDispatchable), [true, false, false, false, true, false]);
        assert.deepEqual(dispatchTotals([allocation]), [{ unit: 'box', quantity: 5, netWeightKg: 0, count: 1 }], 'null kg never becomes a number by inference; it only sums as 0 for display');
        assert.deepEqual(dispatchTotals([active, colli, { ...active, id: 6, quantity: 1, netWeightKg: 4 }]), [
            { unit: 'box', quantity: 6, netWeightKg: 24, count: 2 },
            { unit: 'colli', quantity: 2.5, netWeightKg: 13.75, count: 1 },
        ]);
        assert.deepEqual(dispatchTotals([]), []);
        assert.deepEqual(normalizeDispatchReference('  VRB-1 '), { ok: true, value: 'VRB-1' });
        assert.deepEqual(normalizeDispatchReference('   '), { ok: true, value: null });
        assert.equal(normalizeDispatchReference('x'.repeat(101)).ok, false);
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

describe('direct trade allocation rules', () => {
    test('validateAllocationInput: within remaining and demand is valid; above remaining or demand is rejected with the field key; invalid numbers stop first', () => {
        assert.deepEqual(validateAllocationInput('10', 30, 40), { ok: true, value: { quantity: 10 } });
        assert.deepEqual(validateAllocationInput('12,5', 30, 40), { ok: true, value: { quantity: 12.5 } });
        assert.deepEqual(codes(validateAllocationInput('31', 30, 40)), { quantity: ['exceeds_remaining'] });
        assert.deepEqual(codes(validateAllocationInput('25', 30, 20)), { quantity: ['exceeds_demand'] });
        assert.deepEqual(codes(validateAllocationInput('35', 30, 20)), { quantity: ['exceeds_remaining', 'exceeds_demand'] });
        assert.deepEqual(codes(validateAllocationInput('0', 30, 20)), { quantity: ['not_positive'] });
        assert.deepEqual(codes(validateAllocationInput('', 30, 20)), { quantity: ['empty'] });
    });

    test('the full allocation remainder is the smaller of source remainder and open demand, never negative', () => {
        assert.equal(fullAllocationRemainder(30, 40), 30);
        assert.equal(fullAllocationRemainder(30, 20), 20);
        assert.equal(fullAllocationRemainder(0, 20), 0);
        assert.equal(fullAllocationRemainder(30, 0), 0);
    });

    test('expected-after-requested compares calendar days and never blocks on unknown dates', () => {
        assert.equal(expectedAfterRequested('2026-10-28T00:00:00.000Z', '2026-10-27T00:00:00.000Z'), true);
        assert.equal(expectedAfterRequested('2026-10-27T10:00:00.000Z', '2026-10-27T00:00:00.000Z'), false);
        assert.equal(expectedAfterRequested('2026-10-20T00:00:00.000Z', '2026-10-27T00:00:00.000Z'), false);
        assert.equal(expectedAfterRequested(null, '2026-10-27T00:00:00.000Z'), false);
        assert.equal(expectedAfterRequested('not a date', '2026-10-27T00:00:00.000Z'), false);
    });

    test('a line committed on expected supply is complete but never ready; dispatching the physical part never fulfils it', () => {
        // Rekenvoorbeeld: order 40, toegewezen 20, gereserveerd 10 -> open 10; nog 10 -> open 0 maar niet gereed.
        const partly = { quantity: 40, reservedQuantity: 10, allocatedQuantity: 20, dispatchedQuantity: 0, openQuantity: 10, fulfilled: false, reservations: [] };
        assert.deepEqual(lineProgress(partly), { reserved: 10, allocated: 20, dispatched: 0, total: 40, open: 10, complete: false, onHold: false, ready: false, completeWithExpected: false, fulfilled: false, partiallyDispatched: false });
        const committed = { ...partly, reservedQuantity: 20, openQuantity: 0 };
        assert.deepEqual(lineProgress(committed), { reserved: 20, allocated: 20, dispatched: 0, total: 40, open: 0, complete: true, onHold: false, ready: false, completeWithExpected: true, fulfilled: false, partiallyDispatched: false });
        // 20 fysiek verzonden: toegewezen 20, verzonden 20, open 0, niet vervuld.
        const dispatched = { ...committed, reservedQuantity: 0, dispatchedQuantity: 20 };
        assert.deepEqual(lineProgress(dispatched), { reserved: 0, allocated: 20, dispatched: 20, total: 40, open: 0, complete: true, onHold: false, ready: false, completeWithExpected: true, fulfilled: false, partiallyDispatched: true });
        // Zonder toewijzing is dezelfde regel wél gereed.
        assert.equal(lineProgress({ ...committed, allocatedQuantity: 0, reservedQuantity: 40 }).ready, true);
        assert.deepEqual(orderProgress({ fulfilled: false, lines: [committed] }), { linesComplete: 1, linesFulfilled: 0, linesTotal: 1, onHoldCount: 0, allocatedLines: 1, complete: true, ready: false, fulfilled: false, partiallyDispatched: false });
    });
});

describe('DirectTrade shipment rules', () => {
    test('only active DirectTrade allocations are shippable; a consumed one is shipped', () => {
        assert.equal(isShippableAllocation({ source: 'DirectTrade', status: 'Active' }), true);
        assert.equal(isShippableAllocation({ source: 'Physical', status: 'Active' }), false);
        assert.equal(isShippableAllocation({ source: 'DirectTrade', status: 'Released' }), false);
        assert.equal(isShippedAllocation({ source: 'DirectTrade', status: 'Consumed' }), true);
        assert.equal(isShippedAllocation({ source: 'Physical', status: 'Consumed' }), false);
    });

    test('kg is required and positive, lot is required, a deviating lot needs a reason; without source lot no reason is needed', () => {
        assert.deepEqual(validateShipmentLine(0, { netKg: '81,4', lotNumber: ' LOT-DT-SRC ', lotDeviationReason: 'ignored' }, 'LOT-DT-SRC'),
            { ok: true, value: { netWeightKg: 81.4, lotNumber: 'LOT-DT-SRC', lotDeviationReason: null } });
        assert.deepEqual(codes(validateShipmentLine(0, { netKg: '', lotNumber: 'L', lotDeviationReason: '' }, null)), { 'lines[0].netWeightKg': ['empty'] });
        assert.deepEqual(codes(validateShipmentLine(1, { netKg: '0', lotNumber: ' ', lotDeviationReason: '' }, null)), { 'lines[1].netWeightKg': ['not_positive'], 'lines[1].lotNumber': ['required'] });
        assert.deepEqual(codes(validateShipmentLine(0, { netKg: '10', lotNumber: 'LOT-ANDERS', lotDeviationReason: ' ' }, 'LOT-DT-SRC')), { 'lines[0].lotDeviationReason': ['required'] });
        assert.deepEqual(validateShipmentLine(0, { netKg: '10', lotNumber: 'LOT-ANDERS', lotDeviationReason: ' herpakt ' }, 'LOT-DT-SRC'),
            { ok: true, value: { netWeightKg: 10, lotNumber: 'LOT-ANDERS', lotDeviationReason: 'herpakt' } });
        assert.deepEqual(validateShipmentLine(0, { netKg: '10', lotNumber: 'LOT-VRIJ', lotDeviationReason: '' }, null),
            { ok: true, value: { netWeightKg: 10, lotNumber: 'LOT-VRIJ', lotDeviationReason: null } });
        assert.equal(lotDeviates('LOT-A', 'LOT-A'), false);
        assert.equal(lotDeviates('LOT-A', 'LOT-B'), true);
        assert.equal(lotDeviates(null, 'LOT-B'), false);
    });

    test('the shipping date is a required calendar date, never after today', () => {
        assert.deepEqual(validateShippedOn('2026-10-20', '2026-10-20'), { ok: true, value: '2026-10-20' });
        assert.deepEqual(codes(validateShippedOn('2026-10-21', '2026-10-20')), { shippedOn: ['future_date'] });
        assert.deepEqual(codes(validateShippedOn('', '2026-10-20')), { shippedOn: ['required'] });
        assert.deepEqual(codes(validateShippedOn('20-10-2026', '2026-10-20')), { shippedOn: ['invalid_date'] });
    });
});

