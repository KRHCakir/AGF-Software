import '../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../test/reactHarness.ts';
import { GROUP_THEME, NAV_GROUPS, activeGroup, groupHome } from '../lib/navigation.ts';
import { SubNav, TopNav, TopNavExtras } from './TopNav';

// Het menu boven (feedback 8 november): de procesgroepen als tabs met elk een eigen kleur; een groep aanklikken opent haar eerste scherm;
// de gekleurde balk eronder toont de schermen van de groep waar je bent; Instellingen, Help en "Oud" rechts.

let mounted: Mounted;
const links = () => Array.from(mounted.container.querySelectorAll<HTMLAnchorElement>('a'));
const hrefs = () => links().map((a) => a.getAttribute('href'));
const current = () => links().filter((a) => a.getAttribute('aria-current') === 'page').map((a) => a.getAttribute('href'));

describe('top navigation', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('Start and the four process groups as tabs; a group opens its first screen; the group of the page has its colour', async () => {
        await mounted.render(<TopNav pathname="/receipts" />);
        assert.deepEqual(hrefs(), ['/dashboard', '/purchase-orders', '/availability', '/sales-orders', '/relations']);
        assert.deepEqual(NAV_GROUPS.map(groupHome), ['/purchase-orders', '/availability', '/sales-orders', '/relations']);
        const purchase = mounted.container.querySelector<HTMLElement>('[data-nav-group="purchase"]')!;
        assert.equal(purchase.dataset.active, 'true');
        assert.ok(purchase.className.includes(GROUP_THEME.purchase.tabActive));
        assert.equal(mounted.container.querySelectorAll('[data-active="true"]').length, 1);
        assert.equal(mounted.container.querySelector('nav')!.getAttribute('aria-label'), 'nav_main');
    });

    test('every group has its own colour', () => {
        const colours = NAV_GROUPS.map((group) => GROUP_THEME[group.id].tabActive);
        assert.equal(new Set(colours).size, colours.length);
    });

    test('the bar under the header shows the screens of the current group, the current one marked', async () => {
        await mounted.render(<SubNav pathname="/receipts" />);
        assert.deepEqual(hrefs(), ['/purchase-orders', '/purchase-orders/new', '/receipts', '/inspection', '/purchase-invoices']);
        assert.deepEqual(current(), ['/receipts']);
        const bar = mounted.container.querySelector<HTMLElement>('[data-testid="sub-nav"]')!;
        assert.equal(bar.dataset.navGroup, 'purchase');
        assert.ok(bar.className.includes(GROUP_THEME.purchase.bar));
        await mounted.render(<SubNav pathname="/partijen" />);
        assert.deepEqual(hrefs(), ['/availability', '/partijen']);
        await mounted.render(<SubNav pathname="/sales-orders" />);
        assert.deepEqual(current(), ['/sales-orders']);
    });

    test('no bar on Start, Settings and Help; the old screens get the grey bar', async () => {
        for (const path of ['/dashboard', '/settings', '/help']) {
            await mounted.render(<SubNav pathname={path} />);
            assert.equal(mounted.container.querySelector('[data-testid="sub-nav"]'), null, path);
            assert.equal(activeGroup(path), null);
        }
        await mounted.render(<SubNav pathname="/orders" />);
        assert.equal(mounted.container.querySelector<HTMLElement>('[data-testid="sub-nav"]')!.dataset.navGroup, 'legacy');
        assert.deepEqual(current(), ['/orders']);
    });

    test('Settings and Help on the right; "Oud" opens a list that Escape closes again (focus back on the button)', async () => {
        await mounted.render(<TopNavExtras pathname="/settings" />);
        assert.deepEqual(current(), ['/settings']);
        const button = mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="top-nav-legacy"]')!;
        const list = () => mounted.container.querySelector<HTMLElement>('#top-nav-legacy')!;
        assert.equal(list().hidden, true);
        await act(async () => { button.click(); });
        assert.equal(button.getAttribute('aria-expanded'), 'true');
        assert.equal(list().hidden, false);
        assert.ok(hrefs().includes('/orders'));
        assert.ok(mounted.hasText('nav_legacy_hint'));
        await act(async () => { window.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape' })); });
        assert.equal(list().hidden, true);
        assert.equal(document.activeElement, button);
    });

    test('a click outside the "Oud" list closes it', async () => {
        await mounted.render(<TopNavExtras pathname="/dashboard" />);
        const button = mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="top-nav-legacy"]')!;
        await act(async () => { button.click(); });
        await act(async () => { document.body.dispatchEvent(new window.MouseEvent('mousedown', { bubbles: true })); });
        assert.equal(mounted.container.querySelector<HTMLElement>('#top-nav-legacy')!.hidden, true);
    });
});
