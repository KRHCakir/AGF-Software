import assert from 'node:assert/strict';
import { test } from 'node:test';
import { calculatedKg, kgPerPackage, lineKgRows, lineWeightTotals, resolveLineWeight, weightPayload } from './lineWeight.ts';

const avocado = { defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 };

test('calculated kg per field: quantity × article default (3 decimals, half up), nothing without a default or a valid quantity', () => {
    assert.deepEqual(calculatedKg(1560, avocado), { net: 15600, gross: 16068 });
    assert.deepEqual(calculatedKg(3, { defaultNetKgPerPackage: 1.0005, defaultGrossKgPerPackage: null }), { net: 3.002, gross: null });
    assert.deepEqual(calculatedKg(221, { defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null }), { net: null, gross: null });
    assert.deepEqual(calculatedKg(null, avocado), { net: null, gross: null });
});

test('without typing the fields follow the calculation and the source is Calculated; any typed field makes the line Entered', () => {
    assert.deepEqual(resolveLineWeight({}, 1560, avocado), { ok: true, value: { netWeightKg: 15600, grossWeightKg: 16068, weightSource: 'Calculated' } });
    assert.deepEqual(resolveLineWeight({ grossKg: '16.100', grossKgEntered: true }, 1560, avocado), { ok: true, value: { netWeightKg: 15600, grossWeightKg: 16100, weightSource: 'Entered' } });
    // Leeg gemaakt ingevuld veld valt terug op de berekening.
    assert.deepEqual(resolveLineWeight({ netKg: ' ', netKgEntered: true }, 1560, avocado), { ok: true, value: { netWeightKg: 15600, grossWeightKg: 16068, weightSource: 'Calculated' } });
});

test('article without default: empty is allowed (no kg, no source); typed kg is Entered', () => {
    const green = { defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null };
    assert.deepEqual(resolveLineWeight({}, 221, green), { ok: true, value: { netWeightKg: null, grossWeightKg: null, weightSource: null } });
    assert.deepEqual(resolveLineWeight({ netKg: '2.210', netKgEntered: true }, 221, green), { ok: true, value: { netWeightKg: 2210, grossWeightKg: null, weightSource: 'Entered' } });
});

test('invalid kg and gross below net are refused with the field', () => {
    assert.deepEqual(resolveLineWeight({ netKg: '0', netKgEntered: true }, 10, undefined), { ok: false, errors: [{ field: 'net', code: 'not_positive' }] });
    assert.deepEqual(resolveLineWeight({ grossKg: '1,0001', grossKgEntered: true }, 10, undefined), { ok: false, errors: [{ field: 'gross', code: 'too_many_decimals' }] });
    assert.deepEqual(resolveLineWeight({ grossKg: '15.000', grossKgEntered: true }, 1560, avocado), { ok: false, errors: [{ field: 'gross', code: 'gross_below_net' }] });
});

test('payload carries kg and source only when there is kg (old payload shape otherwise)', () => {
    assert.deepEqual(weightPayload({ netWeightKg: null, grossWeightKg: null, weightSource: null }, 'netWeightKg', 'grossWeightKg'), {});
    assert.deepEqual(weightPayload({ netWeightKg: 15600, grossWeightKg: null, weightSource: 'Calculated' }, 'expectedNetWeightKg', 'expectedGrossWeightKg'), { expectedNetWeightKg: 15600, weightSource: 'Calculated' });
});

test('kg per package check and totals per unit without merging units', () => {
    assert.equal(kgPerPackage(2210, 221), 10);
    assert.equal(kgPerPackage(2210, null), null);
    assert.deepEqual(lineWeightTotals([
        { quantity: 1560, unit: 'colli', netWeightKg: 15600, grossWeightKg: 16068 },
        { quantity: 221, unit: 'colli', netWeightKg: 2210, grossWeightKg: null },
        { quantity: 30, unit: 'box', netWeightKg: null, grossWeightKg: null },
    ]), { quantities: [{ unit: 'colli', quantity: 1781 }, { unit: 'box', quantity: 30 }], netKg: 17810, grossKg: 16068, linesWithoutNet: 1, linesWithoutGross: 2 });
});

test('form rows: the net kg for the per-colli check stays available while gross has an error', () => {
    const line = { productVariantId: '2', unit: 'colli', mode: 'packages' as const, quantity: '221', palletCount: '', packagesPerPallet: '', netKg: '2.210', netKgEntered: true, grossKg: '2.000', grossKgEntered: true };
    const { rows, totals } = lineKgRows([line], () => undefined);
    assert.equal(rows[0].weight, null, 'gross below net makes the line invalid');
    assert.equal(rows[0].netKg, 2210);
    assert.deepEqual(totals.quantities, [{ unit: 'colli', quantity: 221 }]);
});
