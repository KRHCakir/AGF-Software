import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, setValue } from '../../test/reactHarness.ts';
import type { SearchableProduct } from '../../lib/productSearch.ts';
import ProductVariantPicker from './ProductVariantPicker';

// Doorzoekbaar productveld door de echte componentlevenscyclus: pijltjes, Enter, Tab (alleen bij een duidelijke
// suggestie, nooit een toetsenbordval), Escape, snelcodes en de volledige omschrijving na selectie.

const variant = (id: number, name: string, origin: string, packaging: string, sizeCategory: string | null, shortcutCode: string | null) =>
    ({ id, name, origin, packaging, sizeCategory, shortcutCode });
const products: SearchableProduct[] = [
    { id: 1, name: 'Avocado', variants: [variant(1, 'Hass', 'Peru', '4kg Box', '22', 'avohassper22'), variant(4, 'Hass', 'Peru', '4kg Box', '13', 'avohassper13'), variant(2, 'Green', 'Spain', '10kg Crate', null, null)] },
    { id: 2, name: 'Mango', variants: [variant(3, 'Kent', 'Brazil', '4kg Box', null, null)] },
];

let mounted: Mounted;
let chosen: string[];

function Harness({ initial = '' }: { initial?: string }) {
    return <StatefulPicker initial={initial} />;
}

import { useState } from 'react';
function StatefulPicker({ initial }: { initial: string }) {
    const [value, setValueState] = useState(initial);
    return <ProductVariantPicker id="lines[0].productVariantId" ariaLabel="Product" products={products} value={value} onChange={(next) => { chosen.push(next); setValueState(next); }} />;
}

const input = () => mounted.container.querySelector<HTMLInputElement>('input[role="combobox"]')!;
const options = () => Array.from(mounted.container.querySelectorAll<HTMLLIElement>('li[role="option"]:not([aria-disabled="true"])'));
const activeOption = () => options().find((option) => option.getAttribute('aria-selected') === 'true')?.textContent ?? null;
const key = async (name: string) => {
    const event = new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
    await act(async () => { input().dispatchEvent(event); });
    return event.defaultPrevented;
};
const type = async (text: string) => { await act(async () => { setValue(input(), text); }); };

describe('ProductVariantPicker', () => {
    beforeEach(() => { chosen = []; mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('typing shows full descriptions; ambiguous input is not chosen on Tab and Tab is not blocked', async () => {
        await mounted.render(<Harness />);
        await type('hass');
        assert.deepEqual(options().map((o) => o.textContent), ['Avocado · Hass · 4kg Box · Peru · 13avohassper13', 'Avocado · Hass · 4kg Box · Peru · 22avohassper22']);
        const prevented = await key('Tab');
        assert.equal(prevented, false);
        assert.deepEqual(chosen, []);
        assert.equal(input().getAttribute('aria-expanded'), 'false');
    });

    test('arrows navigate, Enter chooses, and the description stays visible afterwards', async () => {
        await mounted.render(<Harness />);
        await type('avocado');
        assert.equal(activeOption(), 'Avocado · Green · 10kg Crate · Spain');
        await key('ArrowDown');
        await key('ArrowDown');
        assert.equal(activeOption(), 'Avocado · Hass · 4kg Box · Peru · 22avohassper22');
        await key('ArrowUp');
        assert.equal(await key('Enter'), true);
        assert.deepEqual(chosen, ['4']);
        assert.equal(input().value, 'Avocado · Hass · 4kg Box · Peru · 13');
        assert.equal(input().readOnly, true);
    });

    test('Tab accepts a single hit, an exact shortcut code, or an arrow-marked suggestion', async () => {
        await mounted.render(<Harness />);
        await type('kent');
        assert.equal(await key('Tab'), false);
        assert.deepEqual(chosen, ['3']);
        await click(mounted.container.querySelector<HTMLButtonElement>('button[aria-label="product_picker_change"]')!);
        assert.deepEqual(chosen, ['3', '']);
        await type('AVOHASSPER13');
        assert.equal(activeOption(), 'Avocado · Hass · 4kg Box · Peru · 13avohassper13');
        await key('Tab');
        assert.deepEqual(chosen, ['3', '', '4']);
        await key('Backspace');
        assert.deepEqual(chosen, ['3', '', '4', '']);
        await type('avocado');
        await key('ArrowDown');
        await key('ArrowDown');
        assert.equal(await key('Tab'), false);
        assert.deepEqual(chosen, ['3', '', '4', '', '1']);
    });

    test('Escape closes the suggestions and Enter then chooses nothing; a partial shortcut code is only text search', async () => {
        await mounted.render(<Harness />);
        await type('avohassper');
        assert.equal(options().length, 2);
        assert.equal(await key('Escape'), true);
        assert.equal(options().length, 0);
        await key('Enter');
        assert.deepEqual(chosen, []);
        await type('xyz');
        assert.ok(mounted.hasText('product_picker_none'));
    });

    test('an existing value renders its full description', async () => {
        await mounted.render(<Harness initial="2" />);
        assert.equal(input().value, 'Avocado · Green · 10kg Crate · Spain');
    });
});
