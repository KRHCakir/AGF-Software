import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { emptyLogistics, isInTransit, locationChoices, logisticsOf, logisticsPayload } from './orderLogistics.ts';
import type { StockLocation } from '../types/stockLocation';

const location = (id: number, kind: StockLocation['kind'], isActive = true): StockLocation => ({ id, name: `L${id}`, kind, address: null, countryCode: null, isActive, inUse: false, version: 0 });

describe('order logistics', () => {
    test('empty = the own warehouse and no addresses; texts are trimmed, empty becomes null', () => {
        assert.deepEqual(logisticsPayload(emptyLogistics()), { receiveLocationId: null, loadingAddress: null, deliveryAddress: null });
        assert.deepEqual(logisticsPayload({ receiveLocationId: '4', loadingAddress: '  Teler\\nLima ', deliveryAddress: ' ', pickUp: true }),
            { receiveLocationId: 4, loadingAddress: 'Teler\\nLima', deliveryAddress: null });
    });

    test('an order opens with its stored values; a loading address means we collect', () => {
        assert.deepEqual(logisticsOf({ receiveLocationId: 4, loadingAddress: 'Teler', deliveryAddress: null }), { receiveLocationId: '4', loadingAddress: 'Teler', deliveryAddress: '', pickUp: true });
        assert.deepEqual(logisticsOf({}), emptyLogistics());
    });

    test('the list shows active locations plus the stored inactive one; in transit is recognised', () => {
        const list = [location(1, 'Crossdock'), location(2, 'InTransit', false), location(3, 'InTransit')];
        assert.deepEqual(locationChoices(list, '').map((l) => l.id), [1, 3]);
        assert.deepEqual(locationChoices(list, '2').map((l) => l.id), [1, 2, 3]);
        assert.equal(isInTransit(list, '3'), true);
        assert.equal(isInTransit(list, '1'), false);
        assert.equal(isInTransit(list, ''), false);
    });
});
