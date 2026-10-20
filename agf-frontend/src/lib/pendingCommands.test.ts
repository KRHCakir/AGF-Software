import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
    CREATE_ORDER_KEY,
    allocateKey,
    blockedBy,
    correctDtShipmentKey,
    confirmDeliveryKey,
    retractDeliveryKey,
    shipDirectTradeKey,
    CREATE_CUSTOMER_KEY,
    cancelKey,
    correctDispatchKey,
    dispatchKey,
    commandsForLine,
    commandsForOrder,
    isPendingCommand,
    parseStoredCommands,
    releaseKey,
    reserveKey,
    serializeCommands,
    sortedCommands,
    withAttempt,
} from './pendingCommands.ts';
import type { PendingCommand, PendingCommands } from './pendingCommands.ts';

const reserve = (orderId: number, lineId: number, requestId = 'r-1', frozenAt = '2026-10-19T10:00:00.000Z'): PendingCommand => ({
    kind: 'reserve', key: reserveKey(orderId, lineId), orderId, lineId, requestId, attempts: 1, lastError: 'Netwerkfout', frozenAt,
    payload: { requestId, palletContentLineId: 2, quantity: 5, unit: 'box', netWeightKg: 20, actorReference: 'office-1', reason: null },
    display: { palletReference: 'SMOKE-PAL-002', lotNumber: 'LOT-SALES-1', productLabel: 'Avocado · Green' },
});

const release = (orderId: number, lineId: number, reservationId: number, requestId = 'x-1'): PendingCommand => ({
    kind: 'release', key: releaseKey(orderId, lineId, reservationId), orderId, lineId, reservationId, requestId, attempts: 1, lastError: null, frozenAt: '2026-10-19T09:00:00.000Z',
    payload: { requestId, expectedReservationVersion: 0, actorReference: 'office-1', reason: 'Klant annuleert' },
    display: { palletReference: 'P', lotNumber: 'L', productLabel: 'Mango · Kent' },
});

const create = (requestId = 'c-1'): PendingCommand => ({
    kind: 'create-order', key: CREATE_ORDER_KEY, requestId, attempts: 2, lastError: 'HTTP 502', frozenAt: '2026-10-19T08:00:00.000Z',
    payload: { requestId, customerId: 1, customerReference: null, requestedDeliveryDate: '2026-10-29T22:00:00.000Z', actorReference: 'office-1', lines: [{ productVariantId: 2, quantity: 10, unit: 'box' }] },
    display: { customerName: 'Local Market Stalls' },
});

const cancel = (orderId: number, requestId = 'k-1'): PendingCommand => ({
    kind: 'cancel-order', key: cancelKey(orderId), orderId, requestId, attempts: 1, lastError: 'HTTP 504', frozenAt: '2026-10-19T11:00:00.000Z',
    payload: { requestId, expectedOrderVersion: 3, actorReference: 'office-1', reason: 'Klant heeft afgezegd' },
    display: { customerName: 'Local Market Stalls', activeReservationCount: 2 },
});

const dispatch = (orderId: number, requestId = 'd-1'): PendingCommand => ({
    kind: 'dispatch', key: dispatchKey(orderId), orderId, requestId, attempts: 1, lastError: 'HTTP 502', frozenAt: '2026-10-19T12:00:00.000Z',
    payload: { requestId, expectedOrderVersion: 1, actorReference: 'office-1', reference: 'VRB-1', lines: [{ stockReservationId: 11, expectedReservationVersion: 0 }, { stockReservationId: 12, expectedReservationVersion: 2 }] },
    display: { customerName: 'Local Market Stalls', lineCount: 2 },
});

const correct = (orderId: number, dispatchId: number, requestId = 'x-1'): PendingCommand => ({
    kind: 'correct-dispatch', key: correctDispatchKey(orderId, dispatchId), orderId, dispatchId, requestId, attempts: 1, lastError: 'HTTP 504', frozenAt: '2026-10-19T13:00:00.000Z',
    payload: { requestId, expectedOrderVersion: 4, actorReference: 'office-1', reason: 'Verkeerd gescand', goodsNeverLeftWarehouse: true, lines: [{ stockReservationId: 11, expectedReservationVersion: 1 }] },
    display: { customerName: 'Local Market Stalls', lineCount: 1 },
});

const customer = (requestId = 'c-1', acknowledge = false): PendingCommand => ({
    kind: 'create-customer', key: CREATE_CUSTOMER_KEY, requestId, attempts: 1, lastError: 'HTTP 503', frozenAt: '2026-10-19T14:00:00.000Z',
    payload: { requestId, name: 'Groentehal Jansen', contactPerson: 'Piet', email: null, phone: '06-1', shippingAddress: null, acknowledgeSimilarNames: acknowledge },
    display: { name: 'Groentehal Jansen' },
});

const toMap = (...commands: PendingCommand[]): PendingCommands => Object.fromEntries(commands.map((command) => [command.key, command]));

describe('unresolved commands', () => {
    const stored = (command: PendingCommand, mutate: (copy: Record<string, unknown>) => void): string => {
        const copy = JSON.parse(JSON.stringify(command)) as Record<string, unknown>;
        mutate(copy);
        return JSON.stringify({ [String(copy.key)]: copy });
    };
    const payloadOf = (copy: Record<string, unknown>) => copy.payload as Record<string, unknown>;

    test('a new command never replaces an unresolved one on the same order line, nor a new order an unresolved order', () => {
        const commands = toMap(reserve(4, 7), create());
        assert.equal(blockedBy(commands, { kind: 'line', orderId: 4, lineId: 7 })?.requestId, 'r-1');
        assert.equal(blockedBy(commands, { kind: 'line', orderId: 4, lineId: 8 }), null);
        assert.equal(blockedBy(commands, { kind: 'line', orderId: 5, lineId: 7 }), null);
        assert.equal(blockedBy(commands, { kind: 'create-order' })?.requestId, 'c-1');
        assert.equal(blockedBy({}, { kind: 'create-order' }), null);
    });

    test('a pending release on a line also blocks a fresh reserve on that line (and the other way round)', () => {
        const commands = toMap(release(4, 7, 11));
        assert.equal(blockedBy(commands, { kind: 'line', orderId: 4, lineId: 7 })?.kind, 'release');
        assert.deepEqual(commandsForLine(commands, 4, 7).map((command) => command.key), [releaseKey(4, 7, 11)]);
        assert.deepEqual(commandsForOrder(toMap(reserve(4, 7), release(4, 9, 12), reserve(5, 1)), 4).map((command) => command.key).sort(), [releaseKey(4, 9, 12), reserveKey(4, 7)]);
    });

    test('an unresolved cancellation blocks every line of that order, and any line command blocks cancelling that order', () => {
        const pendingCancel = toMap(cancel(4));
        assert.equal(blockedBy(pendingCancel, { kind: 'line', orderId: 4, lineId: 7 })?.kind, 'cancel-order');
        assert.equal(blockedBy(pendingCancel, { kind: 'line', orderId: 4, lineId: 99 })?.kind, 'cancel-order');
        assert.equal(blockedBy(pendingCancel, { kind: 'line', orderId: 5, lineId: 7 }), null);
        assert.equal(blockedBy(pendingCancel, { kind: 'cancel-order', orderId: 4 })?.kind, 'cancel-order');
        assert.equal(blockedBy(pendingCancel, { kind: 'cancel-order', orderId: 5 }), null);
        assert.equal(blockedBy(pendingCancel, { kind: 'create-order' }), null);

        assert.equal(blockedBy(toMap(reserve(4, 7)), { kind: 'cancel-order', orderId: 4 })?.kind, 'reserve');
        assert.equal(blockedBy(toMap(release(4, 7, 11)), { kind: 'cancel-order', orderId: 4 })?.kind, 'release');
        assert.equal(blockedBy(toMap(reserve(5, 1)), { kind: 'cancel-order', orderId: 4 }), null);
        assert.deepEqual(commandsForLine(pendingCancel, 4, 7), [], 'a cancellation is not a line command');
        assert.deepEqual(commandsForOrder(toMap(cancel(4), reserve(4, 7), reserve(5, 1)), 4).map((command) => command.kind).sort(), ['cancel-order', 'reserve']);
    });

    test('cancel-order payload: non-negative integer version, non-blank actor and reason, bounded display', () => {
        assert.equal(isPendingCommand(cancel(4)), true);
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(cancel(4)))), toMap(cancel(4)));
        assert.deepEqual(parseStoredCommands(stored(cancel(4), (c) => { c.payload = { requestId: 'k-1' }; })), {});
        for (const field of ['expectedOrderVersion', 'actorReference', 'reason']) {
            assert.deepEqual(parseStoredCommands(stored(cancel(4), (c) => { delete payloadOf(c)[field]; })), {}, `cancel without ${field}`);
        }
        const bad: [string, unknown][] = [
            ['expectedOrderVersion', -1], ['expectedOrderVersion', 1.5], ['expectedOrderVersion', '3'],
            ['actorReference', ''], ['reason', ''], ['reason', '   '], ['reason', null], ['reason', 7],
        ];
        for (const [field, value] of bad) {
            assert.deepEqual(parseStoredCommands(stored(cancel(4), (c) => { payloadOf(c)[field] = value; })), {}, `cancel ${field}=${String(value)}`);
        }
        assert.deepEqual(parseStoredCommands(stored(cancel(4), (c) => { c.orderId = 0; c.key = cancelKey(0); })), {});
        assert.deepEqual(parseStoredCommands(stored(cancel(4), (c) => { c.display = { customerName: 'X' }; })), {});
        assert.deepEqual(parseStoredCommands(stored(cancel(4), (c) => { c.display = { customerName: 'X', activeReservationCount: -1 }; })), {});
        assert.equal(Object.keys(parseStoredCommands(stored(cancel(4), (c) => { payloadOf(c).expectedOrderVersion = 0; }))).length, 1);
    });

    test('an unresolved dispatch blocks the lines and the cancellation of that order, and is blocked by any command on it', () => {
        const pendingDispatch = toMap(dispatch(4));
        assert.equal(blockedBy(pendingDispatch, { kind: 'line', orderId: 4, lineId: 7 })?.kind, 'dispatch');
        assert.equal(blockedBy(pendingDispatch, { kind: 'cancel-order', orderId: 4 })?.kind, 'dispatch');
        assert.equal(blockedBy(pendingDispatch, { kind: 'dispatch', orderId: 4 })?.kind, 'dispatch');
        assert.equal(blockedBy(pendingDispatch, { kind: 'dispatch', orderId: 5 }), null);
        assert.equal(blockedBy(toMap(reserve(4, 7)), { kind: 'dispatch', orderId: 4 })?.kind, 'reserve');
        assert.equal(blockedBy(toMap(cancel(4)), { kind: 'dispatch', orderId: 4 })?.kind, 'cancel-order');
        assert.deepEqual(commandsForOrder(toMap(dispatch(4), cancel(4)), 4).map((command) => command.kind).sort(), ['cancel-order', 'dispatch']);
    });

    test('dispatch payload: version, actor, bounded reference and well-formed distinct lines', () => {
        assert.equal(isPendingCommand(dispatch(4)), true);
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(dispatch(4)))), toMap(dispatch(4)));
        assert.deepEqual(parseStoredCommands(stored(dispatch(4), (c) => { c.payload = { requestId: 'd-1' }; })), {});
        for (const field of ['expectedOrderVersion', 'actorReference', 'reference', 'lines']) {
            assert.deepEqual(parseStoredCommands(stored(dispatch(4), (c) => { delete payloadOf(c)[field]; })), {}, `dispatch without ${field}`);
        }
        const bad: [string, unknown][] = [
            ['expectedOrderVersion', -1], ['actorReference', ''], ['reference', 'x'.repeat(101)], ['reference', 7],
            ['lines', []], ['lines', [{ stockReservationId: 0, expectedReservationVersion: 0 }]], ['lines', [{ stockReservationId: 11 }]],
            ['lines', [{ stockReservationId: 11, expectedReservationVersion: -1 }]], ['lines', [{ stockReservationId: 11, expectedReservationVersion: 0 }, { stockReservationId: 11, expectedReservationVersion: 0 }]],
        ];
        for (const [field, value] of bad) {
            assert.deepEqual(parseStoredCommands(stored(dispatch(4), (c) => { payloadOf(c)[field] = value; })), {}, `dispatch ${field}=${JSON.stringify(value)}`);
        }
        assert.equal(Object.keys(parseStoredCommands(stored(dispatch(4), (c) => { payloadOf(c).reference = null; }))).length, 1);
        assert.deepEqual(parseStoredCommands(stored(dispatch(4), (c) => { c.display = { customerName: 'X', lineCount: 0 }; })), {});
    });

    test('an unresolved dispatch correction is an order-level command: it blocks lines, cancel and dispatch, and is blocked by them', () => {
        const pendingCorrection = toMap(correct(4, 9));
        assert.equal(blockedBy(pendingCorrection, { kind: 'line', orderId: 4, lineId: 7 })?.kind, 'correct-dispatch');
        assert.equal(blockedBy(pendingCorrection, { kind: 'cancel-order', orderId: 4 })?.kind, 'correct-dispatch');
        assert.equal(blockedBy(pendingCorrection, { kind: 'dispatch', orderId: 4 })?.kind, 'correct-dispatch');
        assert.equal(blockedBy(pendingCorrection, { kind: 'correct-dispatch', orderId: 4 })?.kind, 'correct-dispatch');
        assert.equal(blockedBy(pendingCorrection, { kind: 'correct-dispatch', orderId: 5 }), null);
        assert.equal(blockedBy(toMap(reserve(4, 7)), { kind: 'correct-dispatch', orderId: 4 })?.kind, 'reserve');
        assert.equal(blockedBy(toMap(dispatch(4)), { kind: 'correct-dispatch', orderId: 4 })?.kind, 'dispatch');
    });

    test('correct-dispatch payload requires the explicit confirmation, a reason, versions and well-formed lines', () => {
        assert.equal(isPendingCommand(correct(4, 9)), true);
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(correct(4, 9)))), toMap(correct(4, 9)));
        for (const field of ['expectedOrderVersion', 'actorReference', 'reason', 'goodsNeverLeftWarehouse', 'lines']) {
            assert.deepEqual(parseStoredCommands(stored(correct(4, 9), (c) => { delete payloadOf(c)[field]; })), {}, `correction without ${field}`);
        }
        const bad: [string, unknown][] = [
            ['goodsNeverLeftWarehouse', false], ['goodsNeverLeftWarehouse', 'true'], ['goodsNeverLeftWarehouse', 1],
            ['reason', ''], ['reason', '  '], ['expectedOrderVersion', -1],
            ['lines', []], ['lines', [{ stockReservationId: 0, expectedReservationVersion: 1 }]], ['lines', [{ stockReservationId: 11, expectedReservationVersion: 1 }, { stockReservationId: 11, expectedReservationVersion: 1 }]],
        ];
        for (const [field, value] of bad) {
            assert.deepEqual(parseStoredCommands(stored(correct(4, 9), (c) => { payloadOf(c)[field] = value; })), {}, `correction ${field}=${JSON.stringify(value)}`);
        }
        assert.deepEqual(parseStoredCommands(stored(correct(4, 9), (c) => { c.dispatchId = 0; c.key = correctDispatchKey(4, 0); })), {});
        assert.deepEqual(parseStoredCommands(stored(correct(4, 9), (c) => { c.key = correctDispatchKey(4, 8); })), {});
    });

    test('a create-customer command has one fixed key, blocks only itself and never blocks or is blocked by order commands', () => {
        const pendingCustomer = toMap(customer());
        assert.equal(blockedBy(pendingCustomer, { kind: 'create-customer' })?.kind, 'create-customer');
        assert.equal(blockedBy(pendingCustomer, { kind: 'create-order' }), null);
        assert.equal(blockedBy(pendingCustomer, { kind: 'line', orderId: 4, lineId: 7 }), null);
        assert.equal(blockedBy(pendingCustomer, { kind: 'cancel-order', orderId: 4 }), null);
        assert.equal(blockedBy(toMap(dispatch(4), reserve(4, 7)), { kind: 'create-customer' }), null);
        // Een tweede klantopdracht krijgt dezelfde sleutel: de opslag kan er maar één bevatten.
        assert.equal(customer('c-2').key, customer('c-1').key);
    });

    test('create-customer payload is validated field by field; blanks must be null and the acknowledgement a boolean', () => {
        assert.equal(isPendingCommand(customer()), true);
        assert.equal(isPendingCommand(customer('c-9', true)), true);
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(customer()))), toMap(customer()));
        for (const field of ['name', 'contactPerson', 'email', 'phone', 'shippingAddress', 'acknowledgeSimilarNames']) {
            assert.deepEqual(parseStoredCommands(stored(customer(), (c) => { delete payloadOf(c)[field]; })), {}, `customer without ${field}`);
        }
        const bad: [string, unknown][] = [
            ['name', ''], ['name', '   '], ['name', 'x'.repeat(201)], ['contactPerson', ''], ['contactPerson', '   '], ['email', 'x'.repeat(201)],
            ['phone', 'x'.repeat(41)], ['shippingAddress', 'x'.repeat(301)], ['acknowledgeSimilarNames', 'true'], ['acknowledgeSimilarNames', 1],
        ];
        for (const [field, value] of bad) {
            assert.deepEqual(parseStoredCommands(stored(customer(), (c) => { payloadOf(c)[field] = value; })), {}, `customer ${field}=${JSON.stringify(value)}`);
        }
        assert.deepEqual(parseStoredCommands(stored(customer(), (c) => { c.key = 'create-customer:2'; })), {});
    });

    test('attempts are counted on the same frozen requestId and payload; the command itself never changes', () => {
        const original = reserve(4, 7);
        const retried = withAttempt(original, 'HTTP 503');
        assert.equal(retried.requestId, original.requestId);
        assert.deepEqual(retried.payload, original.payload);
        assert.equal(retried.attempts, 2);
        assert.equal(retried.lastError, 'HTTP 503');
        assert.equal(original.attempts, 1);
    });

    test('storage round trip keeps the identical payload and requestId, oldest first', () => {
        const commands = toMap(reserve(4, 7), create(), release(4, 7, 11));
        const restored = parseStoredCommands(serializeCommands(commands));
        assert.deepEqual(restored, commands);
        assert.deepEqual(sortedCommands(restored).map((command) => command.kind), ['create-order', 'release', 'reserve']);
    });

    test('a payload with only a requestId, or with a missing or wrongly typed field, is never restored', () => {
        assert.equal(isPendingCommand(reserve(4, 7)), true);
        assert.deepEqual(parseStoredCommands(stored(reserve(4, 7), (c) => { c.payload = { requestId: 'r-1' }; })), {});
        assert.deepEqual(parseStoredCommands(stored(create(), (c) => { c.payload = { requestId: 'c-1' }; })), {});
        assert.deepEqual(parseStoredCommands(stored(release(4, 7, 11), (c) => { c.payload = { requestId: 'x-1' }; })), {});
        for (const field of ['palletContentLineId', 'quantity', 'unit', 'netWeightKg', 'actorReference', 'reason']) {
            assert.deepEqual(parseStoredCommands(stored(reserve(4, 7), (c) => { delete payloadOf(c)[field]; })), {}, `reserve without ${field}`);
        }
        for (const field of ['customerId', 'customerReference', 'requestedDeliveryDate', 'actorReference', 'lines']) {
            assert.deepEqual(parseStoredCommands(stored(create(), (c) => { delete payloadOf(c)[field]; })), {}, `create without ${field}`);
        }
        for (const field of ['expectedReservationVersion', 'actorReference', 'reason']) {
            assert.deepEqual(parseStoredCommands(stored(release(4, 7, 11), (c) => { delete payloadOf(c)[field]; })), {}, `release without ${field}`);
        }
    });

    test('reserve payload: positive integer id, positive quantity and kg, allowed unit, non-blank actor, nullable reason', () => {
        const bad: [string, unknown][] = [
            ['palletContentLineId', 0], ['palletContentLineId', -2], ['palletContentLineId', 2.5], ['palletContentLineId', '2'],
            ['quantity', 0], ['quantity', -1], ['quantity', '5'], ['quantity', Number.NaN],
            ['netWeightKg', 0], ['netWeightKg', -20], ['netWeightKg', '20'],
            ['unit', 'kg'], ['unit', 'Box'], ['unit', ''], ['unit', 1],
            ['actorReference', ''], ['actorReference', '   '], ['actorReference', 7],
            ['reason', 5], ['reason', undefined],
        ];
        for (const [field, value] of bad) {
            assert.deepEqual(parseStoredCommands(stored(reserve(4, 7), (c) => { payloadOf(c)[field] = value; })), {}, `reserve ${field}=${String(value)}`);
        }
        assert.equal(Object.keys(parseStoredCommands(stored(reserve(4, 7), (c) => { payloadOf(c).reason = 'spoed'; }))).length, 1);
        assert.equal(Object.keys(parseStoredCommands(stored(reserve(4, 7), (c) => { payloadOf(c).quantity = 0.5; payloadOf(c).netWeightKg = 2.25; }))).length, 1);
    });

    test('create-order payload: positive customer, nullable reference, valid date, actor and well-formed non-empty lines', () => {
        const bad: [string, unknown][] = [
            ['customerId', 0], ['customerId', '1'], ['customerId', 1.5],
            ['customerReference', 12], ['customerReference', undefined],
            ['requestedDeliveryDate', 'gisteren'], ['requestedDeliveryDate', 20261029],
            ['actorReference', ''], ['actorReference', null],
            ['lines', []], ['lines', 'lines'], ['lines', [{ productVariantId: 2, quantity: 10 }]],
            ['lines', [{ productVariantId: 2, quantity: 10, unit: 'kg' }]], ['lines', [{ productVariantId: 0, quantity: 10, unit: 'box' }]],
            ['lines', [{ productVariantId: 2, quantity: 0, unit: 'box' }]], ['lines', [{ productVariantId: 2, quantity: '10', unit: 'box' }]],
            ['lines', [{ productVariantId: 2, quantity: 10, unit: 'box' }, null]],
        ];
        for (const [field, value] of bad) {
            assert.deepEqual(parseStoredCommands(stored(create(), (c) => { payloadOf(c)[field] = value; })), {}, `create ${field}=${JSON.stringify(value)}`);
        }
        assert.equal(Object.keys(parseStoredCommands(stored(create(), (c) => { payloadOf(c).customerReference = 'PO-1'; }))).length, 1);
    });

    test('release payload: non-negative integer version, non-blank actor and a required non-blank reason', () => {
        const bad: [string, unknown][] = [
            ['expectedReservationVersion', -1], ['expectedReservationVersion', 1.5], ['expectedReservationVersion', '0'], ['expectedReservationVersion', null],
            ['actorReference', ''], ['reason', ''], ['reason', '  '], ['reason', null], ['reason', 3],
        ];
        for (const [field, value] of bad) {
            assert.deepEqual(parseStoredCommands(stored(release(4, 7, 11), (c) => { payloadOf(c)[field] = value; })), {}, `release ${field}=${String(value)}`);
        }
        assert.equal(Object.keys(parseStoredCommands(stored(release(4, 7, 11), (c) => { payloadOf(c).expectedReservationVersion = 0; }))).length, 1);
    });

    test('command metadata: positive ids, matching key, non-negative attempts, timestamps and display texts', () => {
        assert.deepEqual(parseStoredCommands(stored(reserve(4, 7), (c) => { c.orderId = 0; c.key = reserveKey(0, 7); })), {});
        assert.deepEqual(parseStoredCommands(stored(release(4, 7, 11), (c) => { c.reservationId = -11; c.key = releaseKey(4, 7, -11); })), {});
        assert.deepEqual(parseStoredCommands(stored(reserve(4, 7), (c) => { c.attempts = -1; })), {});
        assert.deepEqual(parseStoredCommands(stored(reserve(4, 7), (c) => { c.frozenAt = 'ooit'; })), {});
        assert.deepEqual(parseStoredCommands(stored(reserve(4, 7), (c) => { c.requestId = ''; payloadOf(c).requestId = ''; })), {});
        assert.deepEqual(parseStoredCommands(stored(reserve(4, 7), (c) => { c.display = { palletReference: 'P' }; })), {});
        assert.deepEqual(parseStoredCommands(stored(create(), (c) => { c.display = {}; })), {});
        assert.deepEqual(parseStoredCommands(stored(reserve(4, 7), (c) => { c.lastError = 5; })), {});
    });

    test('corrupt or foreign storage is ignored instead of restoring a wrong command', () => {
        assert.deepEqual(parseStoredCommands(null), {});
        assert.deepEqual(parseStoredCommands('not json'), {});
        assert.deepEqual(parseStoredCommands('[]'), {});
        const mismatchedKey = { 'reserve:9:9': reserve(4, 7) };
        assert.deepEqual(parseStoredCommands(JSON.stringify(mismatchedKey)), {});
        const differentRequestId = { [reserveKey(4, 7)]: { ...reserve(4, 7), payload: { ...reserve(4, 7).payload, requestId: 'other' } } };
        assert.deepEqual(parseStoredCommands(JSON.stringify(differentRequestId)), {});
        const unknownKind = { x: { ...reserve(4, 7), kind: 'cancel', key: 'x' } };
        assert.deepEqual(parseStoredCommands(JSON.stringify(unknownKind)), {});
    });
});

describe('direct trade allocation commands', () => {
    const allocate = (orderId: number, lineId: number, requestId = 'a-1'): PendingCommand => ({
        kind: 'allocate-direct-trade', key: allocateKey(orderId, lineId), orderId, lineId, requestId, attempts: 1, lastError: 'network down', frozenAt: '2026-10-20T09:00:00.000Z',
        payload: { requestId, supplyCommitmentLineId: 2, quantity: 10, unit: 'box', acknowledgeExpectedSupply: true, actorReference: 'office-1', reason: null },
        display: { sourceLabel: 'Supplier B · Peru → Turkije', lotNumber: 'LOT-DT-SRC', productLabel: 'Avocado · Hass', expectedAt: '2026-10-27T22:00:00.000Z' },
    });
    const stored = (command: PendingCommand, mutate: (copy: Record<string, unknown>) => void): string => {
        const copy = JSON.parse(JSON.stringify(command)) as Record<string, unknown>;
        mutate(copy);
        return JSON.stringify({ [String(copy.key)]: copy });
    };
    const payloadOf = (copy: Record<string, unknown>) => copy.payload as Record<string, unknown>;

    test('an allocation is a line command: it blocks and is blocked on its own line and by order commands, like a reservation', () => {
        assert.equal(allocateKey(4, 7), 'allocate:4:7');
        const commands = toMap(allocate(4, 7));
        assert.equal(blockedBy(commands, { kind: 'line', orderId: 4, lineId: 7 })?.requestId, 'a-1');
        assert.equal(blockedBy(commands, { kind: 'line', orderId: 4, lineId: 8 }), null);
        assert.equal(blockedBy(commands, { kind: 'cancel-order', orderId: 4 })?.requestId, 'a-1');
        assert.equal(blockedBy(commands, { kind: 'dispatch', orderId: 4 })?.requestId, 'a-1');
        assert.equal(blockedBy(commands, { kind: 'cancel-order', orderId: 5 }), null);
        assert.deepEqual(commandsForLine(commands, 4, 7).map((c) => c.requestId), ['a-1']);
        assert.deepEqual(commandsForOrder(commands, 4).map((c) => c.requestId), ['a-1']);
        // Een onafgeronde annulering blokkeert een nieuwe toewijzing op elke regel van die order.
        assert.equal(blockedBy(toMap(cancel(4)), { kind: 'line', orderId: 4, lineId: 7 })?.requestId, 'k-1');
        assert.deepEqual(sortedCommands(toMap(allocate(4, 7), reserve(4, 8))).map((c) => c.kind), ['reserve', 'allocate-direct-trade']);
    });

    test('a stored allocation is restored only when it is complete and the acknowledgement is literally true', () => {
        const command = allocate(4, 7);
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(command))), toMap(command));
        assert.equal(isPendingCommand(command), true);
        const rejected: [string, (copy: Record<string, unknown>) => void][] = [
            ['acknowledgement false', (copy) => { payloadOf(copy).acknowledgeExpectedSupply = false; }],
            ['acknowledgement missing', (copy) => { delete payloadOf(copy).acknowledgeExpectedSupply; }],
            ['acknowledgement as string', (copy) => { payloadOf(copy).acknowledgeExpectedSupply = 'true'; }],
            ['quantity zero', (copy) => { payloadOf(copy).quantity = 0; }],
            ['unit kg', (copy) => { payloadOf(copy).unit = 'kg'; }],
            ['source line missing', (copy) => { payloadOf(copy).supplyCommitmentLineId = 0; }],
            ['requestId mismatch', (copy) => { payloadOf(copy).requestId = 'other'; }],
            ['key mismatch', (copy) => { copy.key = allocateKey(4, 8); }],
            ['display incomplete', (copy) => { copy.display = { sourceLabel: 'x' }; }],
            ['expected date snapshot missing', (copy) => { delete (copy.display as Record<string, unknown>).expectedAt; }],
            ['expected date snapshot not a date', (copy) => { (copy.display as Record<string, unknown>).expectedAt = 'morgen'; }],
            ['actor blank', (copy) => { payloadOf(copy).actorReference = ' '; }],
        ];
        for (const [name, mutate] of rejected) {
            assert.deepEqual(parseStoredCommands(stored(command, mutate)), {}, name);
        }
        assert.equal(withAttempt(command, 'HTTP 502').attempts, 2);
        const withoutDate = { ...command, display: { ...(command.display as Record<string, unknown>), expectedAt: null } } as PendingCommand;
        assert.equal(isPendingCommand(withoutDate), true, 'a null snapshot (no date known) is still a valid command');
    });
});

describe('DirectTrade shipment commands', () => {
    const ship = (orderId: number, requestId = 's-1'): PendingCommand => ({
        kind: 'ship-direct-trade', key: shipDirectTradeKey(orderId), orderId, requestId, attempts: 1, lastError: 'network down', frozenAt: '2026-10-20T10:00:00.000Z',
        payload: { requestId, expectedOrderVersion: 3, actorReference: 'office-1', shippedOn: '2026-10-19', reference: 'CMR-1',
            lines: [{ stockReservationId: 21, expectedReservationVersion: 0, quantity: 20, unit: 'box', netWeightKg: 81.4, lotNumber: 'LOT-DT-SRC', lotDeviationReason: null }] },
        display: { customerName: 'Customer T', lineCount: 1 },
    });
    const stored = (command: PendingCommand, mutate: (copy: Record<string, unknown>) => void): string => {
        const copy = JSON.parse(JSON.stringify(command)) as Record<string, unknown>;
        mutate(copy);
        return JSON.stringify({ [String(copy.key)]: copy });
    };
    const payloadOf = (copy: Record<string, unknown>) => copy.payload as Record<string, unknown>;
    const lineOf = (copy: Record<string, unknown>) => (payloadOf(copy).lines as Record<string, unknown>[])[0];

    test('a DirectTrade shipment is an order command: it blocks line commands, cancel and warehouse dispatch, and is blocked by them', () => {
        assert.equal(shipDirectTradeKey(4), 'ship-direct-trade:4');
        const commands = toMap(ship(4));
        assert.equal(blockedBy(commands, { kind: 'line', orderId: 4, lineId: 7 })?.requestId, 's-1');
        assert.equal(blockedBy(commands, { kind: 'cancel-order', orderId: 4 })?.requestId, 's-1');
        assert.equal(blockedBy(commands, { kind: 'dispatch', orderId: 4 })?.requestId, 's-1');
        assert.equal(blockedBy(commands, { kind: 'ship-direct-trade', orderId: 5 }), null);
        assert.equal(blockedBy(toMap(reserve(4, 7)), { kind: 'ship-direct-trade', orderId: 4 })?.requestId, 'r-1');
        assert.equal(blockedBy(toMap(dispatch(4)), { kind: 'ship-direct-trade', orderId: 4 })?.requestId, 'd-1');
    });

    test('a stored shipment is restored only when complete: positive kg, lot, calendar date, unique lines', () => {
        const command = ship(4);
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(command))), toMap(command));
        const rejected: [string, (copy: Record<string, unknown>) => void][] = [
            ['kg zero', (copy) => { lineOf(copy).netWeightKg = 0; }],
            ['kg missing', (copy) => { delete lineOf(copy).netWeightKg; }],
            ['lot blank', (copy) => { lineOf(copy).lotNumber = ' '; }],
            ['reason blank string', (copy) => { lineOf(copy).lotDeviationReason = ' '; }],
            ['date with time', (copy) => { payloadOf(copy).shippedOn = '2026-10-19T00:00:00Z'; }],
            ['date missing', (copy) => { delete payloadOf(copy).shippedOn; }],
            ['duplicate lines', (copy) => { payloadOf(copy).lines = [lineOf(copy), lineOf(copy)]; }],
            ['key mismatch', (copy) => { copy.key = shipDirectTradeKey(5); }],
        ];
        for (const [name, mutate] of rejected) assert.deepEqual(parseStoredCommands(stored(command, mutate)), {}, name);
    });
});

describe('DirectTrade shipment correction commands', () => {
    const correctDt = (orderId: number, shipmentId: number, requestId = 'c-dt'): PendingCommand => ({
        kind: 'correct-dt-shipment', key: correctDtShipmentKey(orderId, shipmentId), orderId, shipmentId, requestId, attempts: 1, lastError: 'network down', frozenAt: '2026-10-20T11:00:00.000Z',
        payload: { requestId, expectedOrderVersion: 4, actorReference: 'office-1', reason: 'Vervoerder heeft niet geladen', shipmentDidNotTakePlace: true,
            lines: [{ stockReservationId: 21, expectedReservationVersion: 1 }, { stockReservationId: 22, expectedReservationVersion: 1 }] },
        display: { customerName: 'Customer T', lineCount: 2 },
    });
    const stored = (command: PendingCommand, mutate: (copy: Record<string, unknown>) => void): string => {
        const copy = JSON.parse(JSON.stringify(command)) as Record<string, unknown>;
        mutate(copy);
        return JSON.stringify({ [String(copy.key)]: copy });
    };
    const payloadOf = (copy: Record<string, unknown>) => copy.payload as Record<string, unknown>;

    test('a take-back is an order command with its own key per shipment', () => {
        assert.equal(correctDtShipmentKey(4, 7), 'correct-dt-shipment:4:7');
        const commands = toMap(correctDt(4, 7));
        assert.equal(blockedBy(commands, { kind: 'line', orderId: 4, lineId: 1 })?.requestId, 'c-dt');
        assert.equal(blockedBy(commands, { kind: 'cancel-order', orderId: 4 })?.requestId, 'c-dt');
        assert.equal(blockedBy(commands, { kind: 'ship-direct-trade', orderId: 4 })?.requestId, 'c-dt');
        assert.equal(blockedBy(commands, { kind: 'correct-dt-shipment', orderId: 5 }), null);
        assert.equal(blockedBy(toMap(reserve(4, 1)), { kind: 'correct-dt-shipment', orderId: 4 })?.requestId, 'r-1');
    });

    test('a stored take-back is restored only with the literal statement, a reason and a unique full line set', () => {
        const command = correctDt(4, 7);
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(command))), toMap(command));
        const rejected: [string, (copy: Record<string, unknown>) => void][] = [
            ['statement false', (copy) => { payloadOf(copy).shipmentDidNotTakePlace = false; }],
            ['statement missing', (copy) => { delete payloadOf(copy).shipmentDidNotTakePlace; }],
            ['statement as string', (copy) => { payloadOf(copy).shipmentDidNotTakePlace = 'true'; }],
            ['reason blank', (copy) => { payloadOf(copy).reason = ' '; }],
            ['no lines', (copy) => { payloadOf(copy).lines = []; }],
            ['duplicate lines', (copy) => { const lines = payloadOf(copy).lines as unknown[]; payloadOf(copy).lines = [lines[0], lines[0]]; }],
            ['key mismatch', (copy) => { copy.key = correctDtShipmentKey(4, 8); }],
        ];
        for (const [name, mutate] of rejected) assert.deepEqual(parseStoredCommands(stored(command, mutate)), {}, name);
    });
});

describe('delivery confirmation commands', () => {
    const confirm = (orderId: number, kind: 'Warehouse' | 'DirectTrade', shipmentId: number, requestId = 'd-1'): PendingCommand => ({
        kind: 'confirm-delivery', key: confirmDeliveryKey(orderId, kind, shipmentId), orderId, shipmentKind: kind, shipmentId, requestId, attempts: 1, lastError: 'network down', frozenAt: '2026-10-20T11:00:00.000Z',
        payload: { requestId, expectedOrderVersion: 6, actorReference: 'office-1', deliveredOn: '2026-10-20', receiptReference: 'CMR-1', deliveredInFull: true },
        display: { customerName: 'Customer T', lineCount: 1 },
    });
    const stored = (command: PendingCommand, mutate: (copy: Record<string, unknown>) => void): string => {
        const copy = JSON.parse(JSON.stringify(command)) as Record<string, unknown>;
        mutate(copy);
        return JSON.stringify({ [String(copy.key)]: copy });
    };
    const payloadOf = (copy: Record<string, unknown>) => copy.payload as Record<string, unknown>;

    test('the key carries the kind, so equal shipment numbers never collide, and the command blocks other order actions', () => {
        assert.equal(confirmDeliveryKey(4, 'Warehouse', 5), 'confirm-delivery:4:warehouse:5');
        assert.equal(confirmDeliveryKey(4, 'DirectTrade', 5), 'confirm-delivery:4:direct-trade:5');
        const both = { ...toMap(confirm(4, 'Warehouse', 5, 'w')), ...toMap(confirm(4, 'DirectTrade', 5, 'dt')) };
        assert.equal(Object.keys(parseStoredCommands(serializeCommands(both))).length, 2);
        const commands = toMap(confirm(4, 'Warehouse', 5));
        assert.equal(blockedBy(commands, { kind: 'cancel-order', orderId: 4 })?.requestId, 'd-1');
        assert.equal(blockedBy(commands, { kind: 'correct-dispatch', orderId: 4 })?.requestId, 'd-1');
        assert.equal(blockedBy(commands, { kind: 'correct-dt-shipment', orderId: 4 })?.requestId, 'd-1');
        assert.equal(blockedBy(commands, { kind: 'confirm-delivery', orderId: 5 }), null);
        assert.equal(blockedBy(toMap(correct(4, 9)), { kind: 'confirm-delivery', orderId: 4 })?.kind, 'correct-dispatch');
    });

    test('a stored confirmation is restored only with the literal statement, a calendar date and a matching key', () => {
        const command = confirm(4, 'DirectTrade', 7);
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(command))), toMap(command));
        const withoutReference = JSON.parse(JSON.stringify(command)) as PendingCommand & { payload: { receiptReference: string | null } };
        withoutReference.payload.receiptReference = null;
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(withoutReference))), toMap(withoutReference), 'the reference is optional');
        const rejected: [string, (copy: Record<string, unknown>) => void][] = [
            ['statement false', (copy) => { payloadOf(copy).deliveredInFull = false; }],
            ['statement missing', (copy) => { delete payloadOf(copy).deliveredInFull; }],
            ['statement as string', (copy) => { payloadOf(copy).deliveredInFull = 'true'; }],
            ['date with time', (copy) => { payloadOf(copy).deliveredOn = '2026-10-20T00:00:00Z'; }],
            ['date missing', (copy) => { delete payloadOf(copy).deliveredOn; }],
            ['reference too long', (copy) => { payloadOf(copy).receiptReference = 'r'.repeat(101); }],
            ['reference blank', (copy) => { payloadOf(copy).receiptReference = ' '; }],
            ['actor blank', (copy) => { payloadOf(copy).actorReference = ' '; }],
            ['version missing', (copy) => { delete payloadOf(copy).expectedOrderVersion; }],
            ['unknown kind', (copy) => { copy.shipmentKind = 'Truck'; }],
            ['kind does not match key', (copy) => { copy.shipmentKind = 'Warehouse'; }],
            ['request id mismatch', (copy) => { payloadOf(copy).requestId = 'other'; }],
        ];
        for (const [name, mutate] of rejected) assert.deepEqual(parseStoredCommands(stored(command, mutate)), {}, name);
    });
});

describe('delivery retraction commands', () => {
    const retract = (orderId: number, kind: 'Warehouse' | 'DirectTrade', shipmentId: number, confirmationId: number, requestId = 'rt-1'): PendingCommand => ({
        kind: 'retract-delivery', key: retractDeliveryKey(orderId, kind, shipmentId, confirmationId), orderId, shipmentKind: kind, shipmentId, confirmationId, requestId,
        attempts: 1, lastError: 'network down', frozenAt: '2026-10-20T11:00:00.000Z',
        payload: { requestId, expectedOrderVersion: 7, actorReference: 'office-1', reason: 'Verkeerde verzending aangeklikt', recordedInError: true },
        display: { customerName: 'Customer T', sequence: 1 },
    });
    const stored = (command: PendingCommand, mutate: (copy: Record<string, unknown>) => void): string => {
        const copy = JSON.parse(JSON.stringify(command)) as Record<string, unknown>;
        mutate(copy);
        return JSON.stringify({ [String(copy.key)]: copy });
    };
    const payloadOf = (copy: Record<string, unknown>) => copy.payload as Record<string, unknown>;

    test('the key carries kind, shipment and confirmation, and the command blocks every other order action', () => {
        assert.equal(retractDeliveryKey(4, 'Warehouse', 5, 9), 'retract-delivery:4:warehouse:5:9');
        assert.equal(retractDeliveryKey(4, 'DirectTrade', 5, 9), 'retract-delivery:4:direct-trade:5:9');
        const commands = toMap(retract(4, 'Warehouse', 5, 9));
        for (const kind of ['confirm-delivery', 'correct-dispatch', 'correct-dt-shipment', 'cancel-order', 'dispatch', 'ship-direct-trade'] as const) {
            assert.equal(blockedBy(commands, { kind, orderId: 4 })?.requestId, 'rt-1', kind);
        }
        assert.equal(blockedBy(commands, { kind: 'retract-delivery', orderId: 5 }), null);
        assert.equal(blockedBy(toMap(correct(4, 9)), { kind: 'retract-delivery', orderId: 4 })?.kind, 'correct-dispatch');
    });

    test('a stored retraction is restored only with the literal statement, a bounded reason and a matching key', () => {
        const command = retract(4, 'DirectTrade', 7, 3);
        assert.deepEqual(parseStoredCommands(serializeCommands(toMap(command))), toMap(command));
        const rejected: [string, (copy: Record<string, unknown>) => void][] = [
            ['statement false', (copy) => { payloadOf(copy).recordedInError = false; }],
            ['statement missing', (copy) => { delete payloadOf(copy).recordedInError; }],
            ['statement as string', (copy) => { payloadOf(copy).recordedInError = 'true'; }],
            ['reason blank', (copy) => { payloadOf(copy).reason = '  '; }],
            ['reason too long', (copy) => { payloadOf(copy).reason = 'r'.repeat(501); }],
            ['reason missing', (copy) => { delete payloadOf(copy).reason; }],
            ['actor blank', (copy) => { payloadOf(copy).actorReference = ' '; }],
            ['version missing', (copy) => { delete payloadOf(copy).expectedOrderVersion; }],
            ['confirmation id missing', (copy) => { delete copy.confirmationId; }],
            ['confirmation does not match key', (copy) => { copy.confirmationId = 4; }],
            ['kind does not match key', (copy) => { copy.shipmentKind = 'Warehouse'; }],
            ['request id mismatch', (copy) => { payloadOf(copy).requestId = 'other'; }],
            ['sequence missing', (copy) => { delete (copy.display as Record<string, unknown>).sequence; }],
        ];
        for (const [name, mutate] of rejected) assert.deepEqual(parseStoredCommands(stored(command, mutate)), {}, name);
    });
});

