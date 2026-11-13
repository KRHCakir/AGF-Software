import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { NAV_GROUPS, SETTINGS_ITEMS, START_ITEM, activeNav, supplyRedirect } from './navigation.ts';

describe('process menu', () => {
    test('four process groups in order, with only screens that exist', () => {
        assert.deepEqual(NAV_GROUPS.map((group) => group.id), ['purchase', 'stock', 'sales', 'master']);
        assert.deepEqual(NAV_GROUPS[0].items.map((item) => item.href), ['/purchase-orders', '/purchase-orders/new', '/receipts', '/inspection', '/purchase-invoices']);
        assert.deepEqual(NAV_GROUPS[1].items.map((item) => item.href), ['/availability', '/partijen']);
        assert.deepEqual(NAV_GROUPS[2].items.map((item) => item.href), ['/sales-orders', '/direct-trade', '/mail-drafts']);
        assert.deepEqual(NAV_GROUPS[3].items.map((item) => item.href), ['/relations', '/articles']);
        const all = NAV_GROUPS.flatMap((group) => group.items.map((item) => item.href));
        for (const missing of ['/lots', '/deliveries', '/sales-invoices']) assert.equal(all.includes(missing), false, `${missing} has no page yet`);
    });

    test('"Oud" is gone: no old screen is in the menu and an old screen has no active menu item', () => {
        const all = [START_ITEM, ...NAV_GROUPS.flatMap((group) => group.items), ...SETTINGS_ITEMS].map((item) => item.href);
        for (const old of ['/orders', '/inventory', '/incoming-invoices', '/invoices', '/products', '/reports', '/tracking']) {
            assert.equal(all.includes(old), false, old);
            assert.equal(activeNav(old), null, old);
        }
    });

    test('the active page and its group are found, also on detail routes; "Inkooporder aanmaken" is not "Inkooporders"', () => {
        assert.deepEqual(activeNav('/purchase-orders'), { itemId: 'purchase-orders', groupId: 'purchase' });
        assert.deepEqual(activeNav('/purchase-orders/new'), { itemId: 'purchase-order-new', groupId: 'purchase' });
        assert.deepEqual(activeNav('/purchase-invoices/new'), { itemId: 'purchase-invoices', groupId: 'purchase' });
        assert.deepEqual(activeNav('/availability'), { itemId: 'availability', groupId: 'stock' });
        assert.deepEqual(activeNav('/partijen'), { itemId: 'partijen', groupId: 'stock' });
        assert.deepEqual(activeNav('/direct-trade'), { itemId: 'direct-trade', groupId: 'sales' });
        assert.deepEqual(activeNav('/dashboard'), { itemId: 'start', groupId: null });
        assert.deepEqual(activeNav('/settings'), { itemId: 'settings', groupId: null });
        assert.equal(activeNav('/sales-orders-archive'), null, 'a prefix of another word is not a match');
    });

    test('old /supply addresses go to the screen with the new name', () => {
        assert.equal(supplyRedirect(''), '/purchase-orders/new');
        assert.equal(supplyRedirect('#plan'), '/purchase-orders/new');
        assert.equal(supplyRedirect('#receive'), '/receipts');
        assert.equal(supplyRedirect('#RECEIVE'), '/receipts');
        assert.equal(supplyRedirect('#direct'), '/direct-trade');
        assert.equal(supplyRedirect('#onzin'), '/purchase-orders/new');
    });
});
