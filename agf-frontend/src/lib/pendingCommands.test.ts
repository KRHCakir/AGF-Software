import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
    CREATE_ORDER_KEY,
    blockedBy,
    commandsForLine,
    commandsForOrder,
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

const toMap = (...commands: PendingCommand[]): PendingCommands => Object.fromEntries(commands.map((command) => [command.key, command]));

describe('unresolved commands', () => {
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
