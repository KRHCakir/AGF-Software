import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import type { PackageUnit } from '../../types/availability';
import UnitSelect from './UnitSelect';

// De eenheid in een orderregel met alleen het toetsenbord: pijlen kiezen direct (ook waar de browser anders alleen het menu opent),
// Enter gaat naar het volgende veld en verstuurt het formulier niet.

let mounted: Mounted;
let submitted: number;

function Harness({ initial }: { initial: PackageUnit | '' }) {
    const [unit, setUnit] = useState<PackageUnit | ''>(initial);
    return (
        <form onSubmit={(event) => { event.preventDefault(); submitted += 1; }}>
            <input id="quantity" />
            <UnitSelect fieldKey="lines[0].unit" index={0} value={unit} errors={undefined} onChange={setUnit} />
            <button type="button" tabIndex={-1}>per pallet</button>
            <input id="kg" />
        </form>
    );
}

const select = () => mounted.container.querySelector<HTMLSelectElement>('[id="lines[0].unit-input"]')!;
const press = (key: string, init: KeyboardEventInit = {}) => act(async () => {
    select().dispatchEvent(new window.KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ...init }));
});

describe('UnitSelect', () => {
    beforeEach(() => { mounted = new Mounted(); submitted = 0; });
    afterEach(async () => { await mounted.unmount(); });

    test('reachable with Tab, labelled per line, and empty until chosen', async () => {
        await mounted.render(<Harness initial="" />);
        assert.equal(select().tabIndex, 0);
        assert.equal(select().getAttribute('aria-label'), 'supply_unit 1');
        assert.equal(select().value, '');
    });

    test('arrow down and up choose the next and previous unit directly', async () => {
        await mounted.render(<Harness initial="" />);
        await press('ArrowDown');
        assert.equal(select().value, 'box');
        await press('ArrowDown');
        assert.equal(select().value, 'colli');
        await press('ArrowUp');
        assert.equal(select().value, 'box');
        await press('ArrowUp');
        assert.equal(select().value, 'box', 'never back to empty');
        for (let i = 0; i < 10; i++) await press('ArrowDown');
        assert.equal(select().value, 'dozen', 'stops at the last unit');
    });

    test('Enter moves on to the next field (skipping mouse helpers) and never submits the form', async () => {
        await mounted.render(<Harness initial="colli" />);
        select().focus();
        await press('Enter');
        assert.equal(document.activeElement?.id, 'kg');
        assert.equal(submitted, 0);
        assert.equal(select().value, 'colli');
    });

    test('with Alt the browser keeps its own behaviour (open the list)', async () => {
        await mounted.render(<Harness initial="" />);
        await press('ArrowDown', { altKey: true });
        assert.equal(select().value, '');
    });
});
