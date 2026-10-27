import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { palletBreakdown, palletTotal, resolvePalletInput } from './palletInput.ts';

describe('palletTotal', () => {
    test('pallets × packages per pallet, rounded to 3 decimals like the server', () => {
        assert.equal(palletTotal(9, 120), 1080);
        assert.equal(palletTotal(1, 80), 80);
        assert.equal(palletTotal(2.5, 120), 300);
        assert.equal(palletTotal(3, 0.3333), 1);
    });
});

describe('resolvePalletInput', () => {
    test('package mode uses the quantity only and leaves the pallet fields empty', () => {
        assert.deepEqual(resolvePalletInput({ mode: 'packages', quantity: '1080', palletCount: '9', packagesPerPallet: '120' }),
            { ok: true, value: { quantity: 1080, palletCount: null, packagesPerPallet: null, loosePackages: null } });
        assert.deepEqual(resolvePalletInput({ mode: 'packages', quantity: '', palletCount: '', packagesPerPallet: '' }),
            { ok: false, errors: [{ field: 'quantity', code: 'empty' }] });
    });

    test('pallet mode computes the total and keeps the ratio', () => {
        assert.deepEqual(resolvePalletInput({ mode: 'pallets', quantity: '', palletCount: '9', packagesPerPallet: '120' }),
            { ok: true, value: { quantity: 1080, palletCount: 9, packagesPerPallet: 120, loosePackages: null } });
    });

    test('pallet mode requires both fields and reports each missing one', () => {
        const result = resolvePalletInput({ mode: 'pallets', quantity: '', palletCount: '', packagesPerPallet: '0' });
        assert.equal(result.ok, false);
        if (result.ok) return;
        assert.deepEqual(result.errors.map((e) => e.field), ['palletCount', 'packagesPerPallet']);
    });

    test('whole pallets plus optional loose packages (0 allowed); fractions are refused: 3,5 pallets × 120 is no longer 420', () => {
        const pallets = (palletCount: string, packagesPerPallet: string, loosePackages = '') => resolvePalletInput({ mode: 'pallets', quantity: '', palletCount, packagesPerPallet, loosePackages });
        assert.deepEqual(pallets('3', '120', '15'), { ok: true, value: { quantity: 375, palletCount: 3, packagesPerPallet: 120, loosePackages: 15 } });
        assert.deepEqual(pallets('3', '120', '0'), { ok: true, value: { quantity: 360, palletCount: 3, packagesPerPallet: 120, loosePackages: 0 } });
        assert.deepEqual(pallets('3,5', '120'), { ok: false, errors: [{ field: 'palletCount', code: 'not_whole' }] });
        assert.deepEqual(pallets('3', '120,5', '1,5'), { ok: false, errors: [{ field: 'packagesPerPallet', code: 'not_whole' }, { field: 'loosePackages', code: 'not_whole' }] });
    });
});

describe('palletBreakdown', () => {
    test('only lines entered per pallet have a breakdown', () => {
        assert.deepEqual(palletBreakdown({ palletCount: 9, packagesPerPallet: 120 }), { palletCount: 9, packagesPerPallet: 120 });
        assert.equal(palletBreakdown({ palletCount: null, packagesPerPallet: null }), null);
    });
});
