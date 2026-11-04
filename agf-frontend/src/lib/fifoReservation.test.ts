import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { calculatedKgFor, sumQuantities, validateFifo } from './fifoReservation.ts';

// FIFO over twee pallets en twee partijen: 40 van LOT-A (oudste) en 10 van LOT-B voor een regel van 50.
const lotA = { palletContentLineId: 1, availableQuantity: 40, availableNetWeightKg: 240, weightMethod: 'Weighed', netKgPerPackage: null };
const lotB = { palletContentLineId: 2, availableQuantity: 20, availableNetWeightKg: 120, weightMethod: 'Calculated', netKgPerPackage: 6 };

describe('validateFifo', () => {
    test('the proposal over two pallets is valid; calculated kg follows the quantity', () => {
        const result = validateFifo([lotA, lotB], { 1: { quantity: '40', netKg: '240' }, 2: { quantity: '10', netKg: '' } }, 50);
        assert.deepEqual(result, { ok: true, total: 50, items: [{ palletContentLineId: 1, quantity: 40, netWeightKg: 240 }, { palletContentLineId: 2, quantity: 10, netWeightKg: 60 }] });
    });

    test('adjusting is allowed: another split, an empty row is skipped', () => {
        const result = validateFifo([lotA, lotB], { 1: { quantity: '30', netKg: '180' }, 2: { quantity: '20', netKg: '' } }, 50);
        assert.equal(result.ok, true);
        assert.equal(validateFifo([lotA, lotB], { 1: { quantity: '', netKg: '' }, 2: { quantity: '5', netKg: '' } }, 50).ok, true);
    });

    test('never more than the line still needs, never more than on the pallet, no stranded rest, at least one row', () => {
        const over = validateFifo([lotA, lotB], { 1: { quantity: '40', netKg: '240' }, 2: { quantity: '20', netKg: '' } }, 50);
        assert.deepEqual(over.ok ? [] : over.errors, [{ field: 'items', code: 'exceeds_demand' }]);
        const tooMuch = validateFifo([lotA], { 1: { quantity: '41', netKg: '240' } }, 50);
        assert.ok(!tooMuch.ok && tooMuch.errors.some((e) => e.field === 'items[1].quantity' && e.code === 'exceeds_available'));
        const stranded = validateFifo([lotA], { 1: { quantity: '40', netKg: '239' } }, 50);
        assert.ok(!stranded.ok && stranded.errors.some((e) => e.code === 'remainder_mismatch'));
        const empty = validateFifo([lotA], {}, 50);
        assert.ok(!empty.ok && empty.errors[0].field === 'items');
    });

    test('helpers: calculated kg and the running total', () => {
        assert.equal(calculatedKgFor(lotB, '10'), 60);
        assert.equal(calculatedKgFor(lotB, '20'), 120, 'the full rest takes the remaining kg');
        assert.equal(calculatedKgFor(lotA, '10'), null, 'weighed: filled in');
        assert.equal(sumQuantities({ 1: { quantity: '40', netKg: '' }, 2: { quantity: '11', netKg: '' }, 3: { quantity: 'x', netKg: '' } }), 51);
    });
});
