import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { gettingStarted, missingMasterData } from './masterDataStatus.ts';

describe('missingMasterData', () => {
    test('an empty system misses every need that the form asks for', () => {
        assert.deepEqual(missingMasterData({ vendors: [], customers: [], products: [] }, ['vendor', 'article']), ['vendor', 'article']);
        assert.deepEqual(missingMasterData({ vendors: [], customers: [], products: [] }, ['vendor', 'customer', 'article']), ['vendor', 'customer', 'article']);
    });

    test('only active relations and non-archived articles count', () => {
        const input = {
            vendors: [{ isActive: false }],
            customers: [{ isActive: true }],
            products: [{ variants: [{ isArchived: true }] }, { variants: [] }],
        };
        assert.deepEqual(missingMasterData(input, ['vendor', 'customer', 'article']), ['vendor', 'article']);
        assert.deepEqual(missingMasterData({ ...input, vendors: [{ isActive: true }], products: [{ variants: [{}] }] }, ['vendor', 'customer', 'article']), []);
    });

    test('a list that is not loaded is never reported as missing', () => {
        assert.deepEqual(missingMasterData({}, ['vendor', 'customer', 'article']), []);
    });
});

describe('gettingStarted', () => {
    test('shows on an empty system with the three steps in order, none done', () => {
        const card = gettingStarted({ relations: 0, articles: 0, orders: 0 });
        assert.equal(card.show, true);
        assert.deepEqual(card.steps.map((step) => [step.id, step.done]), [['relations', false], ['articles', false], ['order', false]]);
        assert.deepEqual(card.steps.map((step) => step.href), ['/relations?new=1', '/articles?new=1', '/purchase-orders/new']);
    });

    test('stays while relations or articles are still missing, and disappears once both exist', () => {
        assert.equal(gettingStarted({ relations: 2, articles: 0, orders: 0 }).show, true);
        assert.equal(gettingStarted({ relations: 0, articles: 3, orders: 0 }).show, true);
        assert.equal(gettingStarted({ relations: 2, articles: 3, orders: 0 }).show, false);
        assert.deepEqual(gettingStarted({ relations: 2, articles: 0, orders: 0 }).steps.map((step) => step.done), [true, false, false]);
    });
});
