import '../test/setupDom.ts';
import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { SUPPLY_MODE_EVENT, modeFromHash, requestSupplyMode, subscribeSupplyMode, supplyShortcutHref } from './supplyShortcut';

describe('supply shortcuts', () => {
    test('hash parsing accepts only plan and receive', () => {
        assert.equal(modeFromHash('#receive'), 'receive');
        assert.equal(modeFromHash('#plan'), 'plan');
        assert.equal(modeFromHash('#Receive'), 'receive');
        assert.equal(modeFromHash(''), null);
        assert.equal(modeFromHash('#other'), null);
        assert.equal(supplyShortcutHref('receive'), '/supply#receive');
    });

    test('a subscribed page receives the sidebar request and hash changes, and stops after unsubscribe', async () => {
        const seen: string[] = [];
        const stop = subscribeSupplyMode((mode) => seen.push(mode));
        requestSupplyMode('receive');
        requestSupplyMode('plan');
        window.dispatchEvent(new window.CustomEvent(SUPPLY_MODE_EVENT, { detail: 'nonsense' }));
        window.location.hash = '#receive';
        window.dispatchEvent(new window.HashChangeEvent('hashchange'));
        assert.deepEqual(seen, ['receive', 'plan', 'receive']);
        stop();
        requestSupplyMode('receive');
        assert.deepEqual(seen, ['receive', 'plan', 'receive']);
        window.location.hash = '';
    });
});
