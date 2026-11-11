import '../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../test/reactHarness.ts';
import { GROUP_THEME, NAV_GROUPS, activeGroup, groupHome } from '../lib/navigation.ts';
import { SubNav, TOP_TAB, TopNav } from './TopNav';

// Het menu boven (feedback 8 en 11 november): Start, de vier procesgroepen als grote tabs met icoon en kleurstip, en Instellingen en
// Help in dezelfde balk en stijl; een groep opent haar eerste scherm; de gekleurde balk eronder toont de schermen van die groep.

let mounted: Mounted;
const links = () => Array.from(mounted.container.querySelectorAll<HTMLAnchorElement>('a'));
const hrefs = () => links().map((a) => a.getAttribute('href'));
const current = () => links().filter((a) => a.getAttribute('aria-current') === 'page').map((a) => a.getAttribute('href'));

describe('top navigation', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('Start, the four groups, then Settings and Help in the same bar, all with the same tab shape and an icon', async () => {
        await mounted.render(<TopNav pathname="/receipts" />);
        assert.deepEqual(hrefs(), ['/dashboard', '/purchase-orders', '/availability', '/sales-orders', '/relations', '/settings', '/help']);
        assert.deepEqual(NAV_GROUPS.map(groupHome), ['/purchase-orders', '/availability', '/sales-orders', '/relations']);
        for (const link of links()) {
            assert.ok(link.className.includes(TOP_TAB), `${link.getAttribute('href')} has the shared tab style`);
            assert.ok(link.querySelector('svg[aria-hidden="true"][data-nav-icon]'), `${link.getAttribute('href')} has an icon`);
        }
        assert.equal(mounted.container.querySelectorAll('nav').length, 1, 'Settings and Help are inside the main nav');
    });

    test('the group of the page has its colour and aria-current; the colour dots stay on every group', async () => {
        await mounted.render(<TopNav pathname="/receipts" />);
        const purchase = mounted.container.querySelector<HTMLElement>('[data-nav-group="purchase"]')!;
        assert.equal(purchase.dataset.active, 'true');
        assert.equal(purchase.getAttribute('aria-current'), 'true');
        assert.ok(purchase.className.includes(GROUP_THEME.purchase.tabActive));
        assert.equal(mounted.container.querySelectorAll('[aria-current]').length, 1);
        assert.equal(mounted.container.querySelectorAll('[data-nav-group] span.rounded-full').length, 4);
        assert.ok(mounted.container.querySelector('[data-nav-group="stock"] span.rounded-full')!.className.includes(GROUP_THEME.stock.dot));
        assert.equal(mounted.container.querySelector('nav')!.getAttribute('aria-label'), 'nav_main');
        await mounted.render(<TopNav pathname="/help" />);
        assert.deepEqual(current(), ['/help']);
    });

    test('every group has its own colour', () => {
        const colours = NAV_GROUPS.map((group) => GROUP_THEME[group.id].tabActive);
        assert.equal(new Set(colours).size, colours.length);
    });

    test('the bar under the header shows the screens of the current group with an icon, the current one marked', async () => {
        await mounted.render(<SubNav pathname="/receipts" />);
        assert.deepEqual(hrefs(), ['/purchase-orders', '/purchase-orders/new', '/receipts', '/inspection', '/purchase-invoices']);
        assert.deepEqual(current(), ['/receipts']);
        assert.ok(links().every((a) => a.querySelector('svg[data-nav-icon]')));
        const bar = mounted.container.querySelector<HTMLElement>('[data-testid="sub-nav"]')!;
        assert.equal(bar.dataset.navGroup, 'purchase');
        assert.ok(bar.className.includes(GROUP_THEME.purchase.bar));
        await mounted.render(<SubNav pathname="/partijen" />);
        assert.deepEqual(hrefs(), ['/availability', '/partijen']);
        await mounted.render(<SubNav pathname="/sales-orders" />);
        assert.deepEqual(current(), ['/sales-orders']);
    });

    test('no bar on Start, Settings, Help and the old screens that still exist', async () => {
        for (const path of ['/dashboard', '/settings', '/help', '/orders', '/products']) {
            await mounted.render(<SubNav pathname={path} />);
            assert.equal(mounted.container.querySelector('[data-testid="sub-nav"]'), null, path);
            assert.equal(activeGroup(path), null);
        }
    });

    test('"Oud" is gone: no old screen and no dropdown in the bar', async () => {
        await mounted.render(<TopNav pathname="/orders" />);
        for (const old of ['/orders', '/inventory', '/incoming-invoices', '/invoices', '/products', '/reports', '/tracking']) assert.equal(hrefs().includes(old), false, old);
        assert.equal(mounted.container.querySelector('button'), null);
        assert.equal(mounted.hasText('legacy'), false);
        assert.equal(mounted.container.querySelectorAll('[aria-current]').length, 0);
    });
});
