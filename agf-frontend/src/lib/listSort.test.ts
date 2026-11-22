import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { type SortColumns, ariaSort, nextSort, parseSort, readStoredSort, sortParams, sortRows, storeSort } from './listSort.ts';

// Sorteren op kolomkoppen (feedback 22 november): oplopend, aflopend, standaard; per soort de juiste volgorde; leeg onderaan.

interface Row { id: number; name: string | null; number: string; amount: number | null; date: string | null }
const columns: SortColumns<Row> = {
    name: { kind: 'text', value: (row) => row.name },
    number: { kind: 'natural', value: (row) => row.number },
    amount: { kind: 'number', value: (row) => row.amount },
    date: { kind: 'date', value: (row) => row.date },
};
const ids = (rows: Row[]) => rows.map((row) => row.id);

describe('nextSort', () => {
    test('a click sorts ascending, a second click descending, a third click back to the default', () => {
        const first = nextSort(null, 'name');
        assert.deepEqual(first, { key: 'name', direction: 'asc' });
        const second = nextSort(first, 'name');
        assert.deepEqual(second, { key: 'name', direction: 'desc' });
        assert.equal(nextSort(second, 'name'), null);
    });

    test('another column starts ascending', () => {
        assert.deepEqual(nextSort({ key: 'name', direction: 'desc' }, 'amount'), { key: 'amount', direction: 'asc' });
    });

    test('aria-sort only on the sorted column', () => {
        assert.equal(ariaSort({ key: 'name', direction: 'asc' }, 'name'), 'ascending');
        assert.equal(ariaSort({ key: 'name', direction: 'desc' }, 'name'), 'descending');
        assert.equal(ariaSort({ key: 'name', direction: 'asc' }, 'amount'), undefined);
        assert.equal(ariaSort(null, 'name'), undefined);
    });
});

describe('sortRows', () => {
    const rows: Row[] = [
        { id: 1, name: 'Zuid-Afrika', number: 'I-10', amount: 100, date: '2026-11-03' },
        { id: 2, name: 'éénhoorn', number: 'I-2', amount: 9.5, date: '2026-01-15' },
        { id: 3, name: 'appel', number: 'V-1', amount: null, date: null },
        { id: 4, name: 'Banaan', number: 'I-9', amount: 1000, date: '2025-12-31' },
        { id: 5, name: null, number: '', amount: -5, date: '2026-11-22T08:00:00Z' },
    ];

    test('without a sort the default order stays', () => {
        assert.deepEqual(ids(sortRows(rows, columns, null)), [1, 2, 3, 4, 5]);
        assert.deepEqual(ids(sortRows(rows, columns, { key: 'unknown', direction: 'asc' })), [1, 2, 3, 4, 5]);
    });

    test('text in Dutch order, capitals and accents do not count, empty last', () => {
        assert.deepEqual(ids(sortRows(rows, columns, { key: 'name', direction: 'asc' })), [3, 4, 2, 1, 5]);
        assert.deepEqual(ids(sortRows(rows, columns, { key: 'name', direction: 'desc' })), [1, 2, 4, 3, 5]);
    });

    test('numbers naturally: 2 before 10, with letters such as I- and V-', () => {
        assert.deepEqual(ids(sortRows(rows, columns, { key: 'number', direction: 'asc' })), [2, 4, 1, 3, 5]);
        assert.deepEqual(ids(sortRows(rows, columns, { key: 'number', direction: 'desc' })), [3, 1, 4, 2, 5]);
    });

    test('lot numbers sort naturally', () => {
        const lots = ['2611010', '261109', 'P-10', 'P-9', 'p-11'].map((number, index) => ({ id: index, name: null, number, amount: null, date: null }));
        assert.deepEqual(sortRows(lots, columns, { key: 'number', direction: 'asc' }).map((row) => row.number), ['261109', '2611010', 'P-9', 'P-10', 'p-11']);
    });

    test('amounts as numbers, empty last in both directions', () => {
        assert.deepEqual(ids(sortRows(rows, columns, { key: 'amount', direction: 'asc' })), [5, 2, 1, 4, 3]);
        assert.deepEqual(ids(sortRows(rows, columns, { key: 'amount', direction: 'desc' })), [4, 1, 2, 5, 3]);
    });

    test('dates as dates, with or without a time', () => {
        assert.deepEqual(ids(sortRows(rows, columns, { key: 'date', direction: 'asc' })), [4, 2, 1, 5, 3]);
        assert.deepEqual(ids(sortRows(rows, columns, { key: 'date', direction: 'desc' })), [5, 1, 2, 4, 3]);
    });

    test('equal values keep the default order (stable)', () => {
        const same: Row[] = [3, 1, 2].map((id) => ({ id, name: 'Gelijk', number: '1', amount: 1, date: null }));
        assert.deepEqual(ids(sortRows(same, columns, { key: 'name', direction: 'desc' })), [3, 1, 2]);
    });

    test('does not change the input', () => {
        const copy = [...rows];
        sortRows(rows, columns, { key: 'name', direction: 'asc' });
        assert.deepEqual(rows, copy);
    });
});

describe('remembering per screen', () => {
    const memory = () => {
        const values = new Map<string, string>();
        return { getItem: (key: string) => values.get(key) ?? null, setItem: (key: string, value: string) => { values.set(key, value); }, removeItem: (key: string) => { values.delete(key); }, values };
    };

    test('stores and reads back per screen', () => {
        const storage = memory();
        storeSort(() => storage, 'purchase-orders', { key: 'vendor', direction: 'desc' });
        assert.deepEqual(readStoredSort(() => storage, 'purchase-orders', ['vendor', 'number']), { key: 'vendor', direction: 'desc' });
        assert.equal(readStoredSort(() => storage, 'partijen', ['vendor']), null);
        storeSort(() => storage, 'purchase-orders', null);
        assert.equal(storage.values.size, 0);
    });

    test('an unknown column or direction in storage counts as the default', () => {
        assert.equal(parseSort('drop table:asc', ['vendor']), null);
        assert.equal(parseSort('vendor:sideways', ['vendor']), null);
        assert.equal(parseSort('', ['vendor']), null);
    });

    test('works without storage: reading gives the default, writing does nothing', () => {
        const broken = { getItem: () => { throw new Error('blocked'); }, setItem: () => { throw new Error('blocked'); }, removeItem: () => { throw new Error('blocked'); } };
        assert.equal(readStoredSort(() => broken, 'x', ['a']), null);
        assert.doesNotThrow(() => storeSort(() => broken, 'x', { key: 'a', direction: 'asc' }));
        assert.equal(readStoredSort(() => undefined, 'x', ['a']), null);
        assert.doesNotThrow(() => storeSort(() => { throw new Error('no window'); }, 'x', null));
    });

    test('server parameters only when sorted', () => {
        assert.deepEqual(sortParams(null), {});
        assert.deepEqual(sortParams({ key: 'vendor', direction: 'desc' }), { sort: 'vendor', direction: 'desc' });
    });
});
