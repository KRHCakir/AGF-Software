import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, setValue } from '../../test/reactHarness.ts';
import CurrencyField from './CurrencyField';
import { cleanCurrencyInput, suggestCurrencies } from '../../lib/currencies.ts';

// Valutaveld: typ E en EUR staat bovenaan; Tab of Enter neemt de gemarkeerde suggestie over, pijltjes kiezen, Escape sluit;
// een andere code van drie letters blijft gewoon staan.

let mounted: Mounted;
let value: string;

function Harness({ initial = '' }: { initial?: string }) {
    const [current, setCurrent] = useState(initial);
    value = current;
    return <CurrencyField id="currency-input" value={current} onChange={(next) => { value = next; setCurrent(next); }} />;
}

const input = () => mounted.container.querySelector<HTMLInputElement>('#currency-input')!;
const listbox = () => mounted.container.querySelector<HTMLElement>('[role="listbox"]')!;
const options = () => Array.from(mounted.container.querySelectorAll<HTMLElement>('[role="option"]')).map((option) => option.textContent);
const key = (name: string) => act(async () => { input().dispatchEvent(new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })); });
const type = (text: string) => act(async () => { input().focus(); setValue(input(), text); });

describe('currency suggestions', () => {
    test('E gives EUR first; code matches before name matches; only letters, at most three', () => {
        assert.deepEqual(suggestCurrencies('e', 'nl').map((option) => option.code), ['EUR', 'EGP']);
        assert.equal(suggestCurrencies('us', 'nl')[0].code, 'USD');
        assert.deepEqual(suggestCurrencies('dol', 'nl').map((option) => option.code), ['USD', 'CAD', 'AUD', 'NZD']);
        assert.equal(suggestCurrencies('', 'nl')[0].code, 'EUR');
        assert.equal(suggestCurrencies('xyz', 'nl').length, 0);
        assert.equal(cleanCurrencyInput(' eu-r1d '), 'EUR');
    });
});

describe('CurrencyField', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('typing E shows EUR on top; Tab takes it over', async () => {
        await mounted.render(<Harness />);
        await type('e');
        assert.equal(value, 'E');
        assert.equal(listbox().hidden, false);
        assert.equal(options()[0], 'EUREuro');
        assert.equal(input().getAttribute('aria-expanded'), 'true');
        await key('Tab');
        assert.equal(value, 'EUR');
        assert.equal(listbox().hidden, true);
    });

    test('Enter takes the highlighted suggestion and does not reach the form', async () => {
        await mounted.render(<Harness />);
        await type('u');
        let reachedForm = false;
        mounted.container.addEventListener('keydown', (event) => { if (event.key === 'Enter' && !event.defaultPrevented) reachedForm = true; });
        await key('Enter');
        assert.equal(value, 'USD');
        assert.equal(reachedForm, false);
    });

    test('arrows choose another suggestion; Escape closes without changing what was typed', async () => {
        await mounted.render(<Harness />);
        await type('e');
        await key('ArrowDown');
        await key('Tab');
        assert.equal(value, 'EGP');
        await type('g');
        await key('Escape');
        assert.equal(listbox().hidden, true);
        assert.equal(value, 'G');
    });

    test('a filled-in field: Tab passes through without changing it; another three-letter code stays as typed', async () => {
        await mounted.render(<Harness initial="USD" />);
        await key('Tab');
        assert.equal(value, 'USD');
        await type('xaf');
        assert.equal(listbox().hidden, true, 'no suggestion for an unknown code');
        await key('Tab');
        assert.equal(value, 'XAF');
    });

    test('a name-only first suggestion can be taken: with fewer than three letters, or after choosing it with the arrows', async () => {
        await mounted.render(<Harness />);
        await type('do');
        await key('Tab');
        assert.equal(value, 'USD');
        await type('dol');
        const marked = () => mounted.container.querySelectorAll('[role="option"][aria-selected="true"]').length;
        assert.equal(marked(), 0, 'a typed three-letter code: nothing marked, because Tab will not take it');
        assert.ok(mounted.container.querySelector('[data-testid="currency-name-note"]'), 'the list says so visibly');
        assert.equal(input().getAttribute('aria-activedescendant'), null);
        await key('Tab');
        assert.equal(value, 'DOL', 'a typed three-letter code is not replaced silently');
        await type('dol');
        await key('ArrowDown');
        assert.equal(marked(), 1, 'arrow down chooses the top suggestion');
        assert.equal(mounted.container.querySelector('[data-testid="currency-name-note"]'), null);
        await key('Enter');
        assert.equal(value, 'USD');
    });

    test('a click on a suggestion takes it over', async () => {
        await mounted.render(<Harness />);
        await type('gb');
        const option = mounted.container.querySelector<HTMLElement>('[role="option"]')!;
        await act(async () => { option.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true })); });
        assert.equal(value, 'GBP');
    });
});
