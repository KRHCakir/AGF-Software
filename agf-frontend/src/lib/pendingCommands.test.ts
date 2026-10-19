import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
    CREATE_ORDER_KEY,
    blockedBy,
    cancelKey,
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
