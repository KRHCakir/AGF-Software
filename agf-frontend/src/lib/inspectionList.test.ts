import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { EVENT_LABEL_KEY_VALUES, HISTORY_STEP, INSPECTION_PAGE_SIZE, eventLabelKey, filterPallets, lastInspection, paginate, qualityDecisions, visibleLines } from './inspectionList.ts';
import type { PalletAuditEvent, PalletDetailLine } from '../types/inspection';
import type { QualityStatus } from '../types/availability';
import nl from '../i18n/nl.json';
import en from '../i18n/en.json';

// Keuringslijst: eerst zoeken en filteren, dan pagineren; pallets zonder passende regels verdwijnen; compacte historie.

const line = (id: number, qualityStatus: QualityStatus, productName: string, lotNumber: string, route: 'Warehouse' | 'DirectTrade' = 'Warehouse'): PalletDetailLine => ({
    id, palletId: 1, supplyCommitmentId: 1, supplyCommitmentLineId: 1, productVariantId: 1, productName, productVariantName: 'Var', lotNumber,
    quantity: 10, unit: 'box', netWeightKg: 40, onHandQuantity: 10, onHandNetWeightKg: 40, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, depleted: false,
    qualityStatus, qualityReason: null, route, origin: 'Receipt', version: 1,
} as unknown as PalletDetailLine);

const all: Record<QualityStatus, boolean> = { Quarantine: true, Blocked: true, Approved: true, Rejected: false };
const pallet = (id: number, reference: string, lines: PalletDetailLine[]) => ({ pallet: { id, reference, contentLines: lines } });
const contexts = [
    pallet(3, 'SMOKE-1', [line(31, 'Approved', 'Avocado', 'L-31')]),
    pallet(17, 'DEMO-REP', [line(30, 'Approved', 'Avocado', 'L-30'), line(31, 'Blocked', 'Mango', 'DEMO-KENT'), line(32, 'Quarantine', 'Avocado', 'L-32')]),
    pallet(5, 'SMOKE-2', [line(50, 'Rejected', 'Mango', 'L-50'), line(51, 'Quarantine', 'Citroen', 'L-51', 'DirectTrade')]),
];

describe('visibleLines and filterPallets', () => {
    test('shows only inspectable warehouse lines that match the status filter and the search', () => {
        assert.deepEqual(visibleLines(contexts[1].pallet, all, 'mango').map((l) => l.id), [31]);
        assert.deepEqual(visibleLines(contexts[1].pallet, { ...all, Approved: false }, '').map((l) => l.id), [32, 31]);
        assert.deepEqual(visibleLines(contexts[2].pallet, all, ''), []);
    });

    test('hides pallets without matching lines and puts the newest pallet first', () => {
        assert.deepEqual(filterPallets(contexts, all, '').map((c) => c.pallet.id), [17, 3]);
        assert.deepEqual(filterPallets(contexts, all, 'mango').map((c) => c.pallet.id), [17]);
        assert.deepEqual(filterPallets(contexts, all, 'citroen'), []);
        assert.deepEqual(filterPallets(contexts, all, 'demo-rep').map((c) => c.pallet.id), [17]);
    });
});

describe('paginate', () => {
    const items = Array.from({ length: 23 }, (_, i) => i + 1);
    test('10 per page with page number and total', () => {
        assert.equal(INSPECTION_PAGE_SIZE, 10);
        const first = paginate(items, 1);
        assert.deepEqual([first.items.length, first.page, first.pageCount, first.total], [10, 1, 3, 23]);
        assert.deepEqual(paginate(items, 3).items, [21, 22, 23]);
    });

    test('an out-of-range page is clamped so the list stays valid after items disappear', () => {
        assert.equal(paginate(items, 9).page, 3);
        assert.equal(paginate(items, 0).page, 1);
        assert.deepEqual(paginate([], 4), { items: [], page: 1, pageCount: 1, total: 0 });
    });
});

const event = (id: number, eventType: string, occurredAt: string, actor: string | null, newValue: string | null = null): PalletAuditEvent =>
    ({ id, palletId: 1, palletContentLineId: 30, eventType, oldValue: null, newValue, reason: null, actorReference: actor, occurredAt, requestId: null });
const events = [
    event(1, 'ContentLineAdded', '2026-10-20T10:00:00Z', 'x', '{"qualityStatus":"Quarantine"}'),
    event(2, 'QualityStatusChanged', '2026-10-20T11:00:00Z', 'Jan', '{"qualityStatus":"Approved"}'),
    event(3, 'ReservationCreated', '2026-10-20T12:00:00Z', 'office'),
    event(4, 'QualityStatusChanged', '2026-10-20T13:00:00Z', null, '{"qualityStatus":"Blocked"}'),
];

describe('history', () => {
    test('quality decisions only, newest first; last inspection carries the real actor, time and result', () => {
        assert.deepEqual(qualityDecisions(events).map((e) => e.id), [4, 2]);
        assert.deepEqual(lastInspection(events), { actor: null, occurredAt: '2026-10-20T13:00:00Z', toStatus: 'Blocked' });
        assert.deepEqual(lastInspection(events.slice(0, 3)), { actor: 'Jan', occurredAt: '2026-10-20T11:00:00Z', toStatus: 'Approved' });
        assert.equal(lastInspection([events[0], events[2]]), null);
        assert.equal(HISTORY_STEP, 5);
    });

    test('technical event names translate in both languages; unknown names fall back to null', () => {
        assert.equal(eventLabelKey('ReservationCreated'), 'event_reservation_created');
        assert.equal((nl as Record<string, string>).event_reservation_created, 'Voorraad gereserveerd');
        assert.equal(eventLabelKey('SomethingNew'), null);
        for (const key of EVENT_LABEL_KEY_VALUES) {
            assert.ok(key in nl, `${key} in nl`);
            assert.ok(key in en, `${key} in en`);
        }
    });
});
