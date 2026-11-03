import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { supplyRedirect } from './navigation.ts';

describe('old supply addresses', () => {
    test('old /supply addresses go to the screen with the new name', () => {
        assert.equal(supplyRedirect(''), '/purchase-orders/new');
        assert.equal(supplyRedirect('#plan'), '/purchase-orders/new');
        assert.equal(supplyRedirect('#receive'), '/receipts');
        assert.equal(supplyRedirect('#RECEIVE'), '/receipts');
        assert.equal(supplyRedirect('#direct'), '/direct-trade');
        assert.equal(supplyRedirect('#onzin'), '/purchase-orders/new');
    });
});
