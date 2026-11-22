import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, setSelectValue } from '../../test/reactHarness.ts';
import { type SortColumns, sortRows } from '../../lib/listSort.ts';
import { SortHeader, SortSelect } from './SortHeader';
import { useListSort } from './useListSort';

// Klikbare kolomkoppen en de keuzelijst "Sorteren op" (390 px), via de echte React-levenscyclus en met onthouden per scherm.

interface Row { id: number; name: string; amount: number }
const rows: Row[] = [{ id: 1, name: 'Peer', amount: 3 }, { id: 2, name: 'appel', amount: 10 }, { id: 3, name: 'Kiwi', amount: 2 }];
const columns: SortColumns<Row> = { name: { kind: 'text', value: (row) => row.name }, amount: { kind: 'number', value: (row) => row.amount } };
const KEYS = Object.keys(columns);

const seen: { screen: string; ready: boolean; key: string | null }[] = [];

function List({ screen = 'test-list' }: { screen?: string }) {
    const { sort, toggle, setSort, ready } = useListSort(screen, KEYS);
    seen.push({ screen, ready, key: sort?.key ?? null });
    return (
        <>
            <button type="button" data-testid="unknown-column" onClick={() => toggle('id')}>x</button>
            <SortSelect id="sort-test" options={[{ key: 'name', label: 'Naam', kind: 'text' }, { key: 'amount', label: 'Bedrag', kind: 'number' }]} sort={sort} onChange={setSort} />
            <table>
                <thead><tr>
                    <SortHeader label="Naam" sortKey="name" sort={sort} onSort={toggle} />
                    <SortHeader label="Bedrag" sortKey="amount" sort={sort} onSort={toggle} />
                </tr></thead>
                <tbody>{sortRows(rows, columns, sort).map((row) => <tr key={row.id}><td>{row.name}</td><td>{row.amount}</td></tr>)}</tbody>
            </table>
        </>
    );
}

let mounted: Mounted;
const header = (key: string) => mounted.container.querySelector<HTMLButtonElement>(`[data-sort-key="${key}"]`)!;
const th = (key: string) => header(key).closest('th')!;
const names = () => Array.from(mounted.container.querySelectorAll('tbody tr')).map((row) => row.firstElementChild!.textContent);
const click = async (key: string) => { await act(async () => { header(key).click(); }); };
const select = () => mounted.container.querySelector<HTMLSelectElement>('#sort-test')!;

describe('SortHeader and SortSelect', () => {
    beforeEach(async () => {
        window.localStorage.clear();
        mounted = new Mounted();
        await mounted.render(<List />);
    });
    afterEach(async () => { await mounted.unmount(); });

    test('click cycles ascending, descending and back to the default, with arrow and aria-sort', async () => {
        assert.deepEqual(names(), ['Peer', 'appel', 'Kiwi']);
        assert.equal(th('name').getAttribute('aria-sort'), null);
        await click('name');
        assert.deepEqual(names(), ['appel', 'Kiwi', 'Peer']);
        assert.equal(th('name').getAttribute('aria-sort'), 'ascending');
        assert.equal(header('name').querySelector('[data-testid="sort-arrow"]')!.textContent, '▲');
        await click('name');
        assert.deepEqual(names(), ['Peer', 'Kiwi', 'appel']);
        assert.equal(th('name').getAttribute('aria-sort'), 'descending');
        assert.equal(header('name').querySelector('[data-testid="sort-arrow"]')!.textContent, '▼');
        await click('name');
        assert.deepEqual(names(), ['Peer', 'appel', 'Kiwi']);
        assert.equal(th('name').getAttribute('aria-sort'), null);
    });

    test('only the sorted column carries aria-sort', async () => {
        await click('amount');
        assert.deepEqual(names(), ['Kiwi', 'Peer', 'appel']);
        assert.equal(th('amount').getAttribute('aria-sort'), 'ascending');
        assert.equal(th('name').getAttribute('aria-sort'), null);
    });

    test('the header is a real button: reachable with Tab, Enter and space work natively', async () => {
        const button = header('name');
        assert.equal(button.tagName, 'BUTTON');
        assert.equal(button.getAttribute('type'), 'button');
        assert.equal(button.tabIndex, 0);
        // Een knop zet Enter en spatie in de browser om in een klik; jsdom doet dat niet, dus hier de klik die daaruit volgt.
        button.focus();
        assert.equal(window.document.activeElement, button);
        await act(async () => { button.dispatchEvent(new window.MouseEvent('click', { bubbles: true, detail: 0 })); });
        assert.equal(th('name').getAttribute('aria-sort'), 'ascending');
    });

    test('the choice is remembered per screen and comes back after a reload', async () => {
        await click('amount');
        await click('amount');
        assert.equal(window.localStorage.getItem('agf_sort_test-list'), 'amount:desc');
        await mounted.unmount();
        mounted = new Mounted();
        await mounted.render(<List />);
        assert.deepEqual(names(), ['appel', 'Peer', 'Kiwi']);
        assert.equal(th('amount').getAttribute('aria-sort'), 'descending');
        await mounted.unmount();
        mounted = new Mounted();
        await mounted.render(<List screen="other-list" />);
        assert.deepEqual(names(), ['Peer', 'appel', 'Kiwi']);
    });

    test('works without storage', async () => {
        const original = Object.getOwnPropertyDescriptor(window, 'localStorage')!;
        Object.defineProperty(window, 'localStorage', { configurable: true, get: () => { throw new Error('blocked'); } });
        try {
            await mounted.unmount();
            mounted = new Mounted();
            await mounted.render(<List />);
            await click('name');
            assert.deepEqual(names(), ['appel', 'Kiwi', 'Peer']);
        } finally {
            Object.defineProperty(window, 'localStorage', original);
        }
    });

    test('the select "Sorteren op" offers the same columns in both directions and follows the headers', async () => {
        const values = Array.from(select().options).map((option) => option.value);
        assert.deepEqual(values, ['', 'name:asc', 'name:desc', 'amount:asc', 'amount:desc']);
        assert.equal(mounted.container.querySelector('label[for="sort-test"]')!.textContent, 'sort_by');
        await act(async () => { setSelectValue(select(), 'amount:desc'); });
        assert.deepEqual(names(), ['appel', 'Peer', 'Kiwi']);
        assert.equal(th('amount').getAttribute('aria-sort'), 'descending');
        await click('name');
        assert.equal(select().value, 'name:asc');
        await act(async () => { setSelectValue(select(), ''); });
        assert.deepEqual(names(), ['Peer', 'appel', 'Kiwi']);
        assert.equal(window.localStorage.getItem('agf_sort_test-list'), null);
    });

    // Review #316: wisselt het scherm zonder te ontkoppelen, dan nooit de sortering van het vorige scherm met ready = true.
    test('switching screens never reports the previous screen\'s sort as ready', async () => {
        window.localStorage.setItem('agf_sort_other-list', 'name:desc');
        await click('amount');
        seen.length = 0;
        await mounted.render(<List screen="other-list" />);
        assert.ok(seen.every((entry) => !(entry.screen === 'other-list' && entry.ready && entry.key === 'amount')), JSON.stringify(seen));
        assert.equal(th('name').getAttribute('aria-sort'), 'descending');
        assert.deepEqual(names(), ['Peer', 'Kiwi', 'appel']);
    });

    test('an unknown column is ignored', async () => {
        await click('name');
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-testid="unknown-column"]')!.click(); });
        assert.equal(window.localStorage.getItem('agf_sort_test-list'), 'name:asc');
        assert.equal(th('name').getAttribute('aria-sort'), 'ascending');
    });
});
