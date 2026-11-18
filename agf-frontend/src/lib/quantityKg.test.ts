import assert from 'node:assert/strict';
import { test } from 'node:test';
import { packageKg } from './quantityKg.ts';

test('kg line: net and gross per colli from the article; only net without gross; nothing without net (weighed article)', () => {
    assert.deepEqual(packageKg(1560, { defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 }), { netKgPerPackage: 10, netKg: 15600, grossKg: 16068 });
    assert.deepEqual(packageKg(375, { defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: null }), { netKgPerPackage: 4, netKg: 1500, grossKg: null });
    assert.equal(packageKg(1560, { defaultNetKgPerPackage: null, defaultGrossKgPerPackage: 10.3 }), null);
    assert.equal(packageKg(null, { defaultNetKgPerPackage: 10 }), null);
    assert.equal(packageKg(1560, undefined), null);
});
