import '../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../test/reactHarness.ts';
import AppMenu from './AppMenu';

// Het menu op een smal scherm: dezelfde vier groepen met icoon, grote aanraakvlakken (44 px), Instellingen en Help onderaan, geen "Oud".
let mounted: Mounted;
const links = () => Array.from(mounted.container.querySelectorAll<HTMLAnchorElement>('a'));
const groupTitles = () => Array.from(mounted.container.querySelectorAll('h3')).map((h) => h.textContent);

describe('AppMenu', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('shows the four groups with an icon and marks the current page and its group', async () => {
        await mounted.render(<AppMenu pathname="/receipts" idPrefix="menu" />);
        assert.deepEqual(groupTitles(), ['nav_group_purchase', 'nav_group_stock', 'nav_group_sales', 'nav_group_master']);
        assert.ok(Array.from(mounted.container.querySelectorAll('h3')).every((h) => h.querySelector('svg[data-nav-icon]')));
        const current = links().filter((a) => a.getAttribute('aria-current') === 'page');
        assert.deepEqual(current.map((a) => a.getAttribute('href')), ['/receipts']);
        assert.ok(mounted.container.querySelector('#menu-purchase')!.className.includes('text-blue-800'), 'the group of the current page stands out');
    });

    test('every entry is a touch target of at least 44 px with an icon; Settings and Help come last; no "Oud"', async () => {
        await mounted.render(<AppMenu pathname="/dashboard" idPrefix="menu" />);
        for (const link of links()) {
            assert.ok(link.className.includes('min-h-11'), `${link.getAttribute('href')} is 44 px high`);
            assert.ok(link.querySelector('svg[data-nav-icon]'), `${link.getAttribute('href')} has an icon`);
        }
        assert.deepEqual(links().slice(-2).map((a) => a.getAttribute('href')), ['/settings', '/help']);
        assert.equal(links().length, 1 + 5 + 2 + 2 + 2 + 2);
        assert.equal(mounted.container.querySelector('button'), null);
        assert.equal(mounted.hasText('legacy'), false);
    });

    test('the mobile menu closes after choosing a page', async () => {
        let closed = 0;
        await mounted.render(<AppMenu pathname="/dashboard" idPrefix="menu-mobile" onNavigate={() => { closed += 1; }} />);
        const link = links().find((a) => a.getAttribute('href') === '/availability')!;
        await act(async () => { link.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true })); });
        assert.equal(closed, 1);
    });
});
