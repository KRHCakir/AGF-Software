import '../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../test/reactHarness.ts';
import AppMenu from './AppMenu';

// Het hoofdmenu: vier procesgroepen, de actieve pagina gemarkeerd (aria-current), "Oud" dichtgeklapt en met het toetsenbord te openen.
let mounted: Mounted;
const links = () => Array.from(mounted.container.querySelectorAll<HTMLAnchorElement>('a'));
const groupTitles = () => Array.from(mounted.container.querySelectorAll('h3')).map((h) => h.textContent);
const legacyToggle = () => mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="menu-legacy"]')!;

describe('AppMenu', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('shows the four groups and marks the current page and its group', async () => {
        await mounted.render(<AppMenu pathname="/receipts" idPrefix="menu" />);
        assert.deepEqual(groupTitles(), ['nav_group_purchase', 'nav_group_stock', 'nav_group_sales', 'nav_group_master']);
        const current = links().filter((a) => a.getAttribute('aria-current') === 'page');
        assert.deepEqual(current.map((a) => a.getAttribute('href')), ['/receipts']);
        assert.ok(mounted.container.querySelector('#menu-purchase')!.className.includes('text-blue-800'), 'the group of the current page stands out');
    });

    test('"Oud" is closed by default, opens with a button (keyboard: Enter/Space on a button) and says it is no longer updated', async () => {
        await mounted.render(<AppMenu pathname="/sales-orders" idPrefix="menu" />);
        assert.equal(links().some((a) => a.getAttribute('href') === '/orders'), false);
        assert.equal(legacyToggle().getAttribute('aria-expanded'), 'false');
        await act(async () => { legacyToggle().click(); });
        assert.equal(legacyToggle().getAttribute('aria-expanded'), 'true');
        assert.ok(links().some((a) => a.getAttribute('href') === '/orders'));
        assert.ok(mounted.hasText('nav_legacy_hint'));
    });

    test('on an old screen "Oud" is open, so the current page stays visible', async () => {
        await mounted.render(<AppMenu pathname="/orders" idPrefix="menu" />);
        assert.equal(legacyToggle().getAttribute('aria-expanded'), 'true');
        assert.equal(links().find((a) => a.getAttribute('aria-current') === 'page')?.getAttribute('href'), '/orders');
    });

    test('the mobile menu closes after choosing a page', async () => {
        let closed = 0;
        await mounted.render(<AppMenu pathname="/dashboard" idPrefix="menu-mobile" onNavigate={() => { closed += 1; }} />);
        const link = links().find((a) => a.getAttribute('href') === '/availability')!;
        await act(async () => { link.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true })); });
        assert.equal(closed, 1);
    });
});
