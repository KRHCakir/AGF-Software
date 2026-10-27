import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act, useState } from 'react';
import { LanguageProvider } from '../../i18n/LanguageContext';
import type { QuantityInputMode } from '../../lib/palletInput';
import type { PackageKgDefaults } from '../../lib/quantityKg';
import { Mounted, click, setValue } from '../../test/reactHarness.ts';
import QuantityInput from './QuantityInput';

// Aantal-invoer door de echte levenscyclus, met de Nederlandse teksten (LanguageProvider, opgeslagen taal nl).
let mode: QuantityInputMode = 'packages';
function Line({ kg }: { kg: PackageKgDefaults }) {
    const [line, setLine] = useState({ quantity: '', unit: 'colli' as const, mode: 'packages' as QuantityInputMode, palletCount: '', packagesPerPallet: '', loosePackages: '' });
    mode = line.mode;
    return <QuantityInput prefix="l" index={0} line={line} locked={false} errors={() => undefined} defaultPackagesPerPallet={120} kgDefaults={kg}
        onChange={(field, value) => setLine((current) => ({ ...current, [field]: value }))} />;
}

describe('QuantityInput', () => {
    let mounted: Mounted;
    beforeEach(() => { window.localStorage.setItem('agf_locale', 'nl'); mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); window.localStorage.removeItem('agf_locale'); });

    const render = (kg: PackageKgDefaults) => mounted.render(<LanguageProvider><Line kg={kg} /></LanguageProvider>);
    const input = (name: string) => mounted.container.querySelector<HTMLInputElement>(`[id="l.${name}-input"]`);
    const shown = (testId: string) => mounted.container.querySelector(`[data-testid="${testId}"]`)?.textContent ?? null;
    const type = (name: string, value: string) => act(async () => { setValue(input(name)!, value); });
    const press = (testId: string) => click(mounted.container.querySelector<HTMLButtonElement>(`[data-testid="${testId}"]`)!);

    test('one field with the unit behind it and the live kg line (net and gross per colli); no radios, no technical hint', async () => {
        await render({ defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 });
        assert.equal(mounted.container.querySelectorAll('input[type="radio"]').length, 0);
        assert.equal(shown('l.unit-suffix'), 'colli');
        await type('quantity', '1.560');
        assert.equal(shown('l.quantity-kg'), '1.560 colli × 10 kg = 15.600,0 kg netto · 16.068,0 kg bruto');
        assert.ok(!mounted.hasText('fysieke pallets') && !mounted.hasText('pallet_input_hint'));
    });

    test('only net without gross per colli; without net per colli (weighed) just the echo', async () => {
        await render({ defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: null });
        await type('quantity', '1560');
        assert.equal(shown('l.quantity-kg'), '1.560 colli × 10 kg = 15.600,0 kg netto');
        await mounted.render(<LanguageProvider><Line key="weighed" kg={{ defaultNetKgPerPackage: null }} /></LanguageProvider>);
        await type('quantity', '1560');
        assert.equal(shown('l.quantity-kg'), null);
        assert.equal(shown('l.quantity-echo'), '= 1.560 colli');
    });

    test('per pallet: 3 × 120 (article) + 15 loose colli = 375; 3,5 pallets gives no total; back to plain keeps 375', async () => {
        await render({ defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 });
        await press('l.per-pallet');
        assert.equal(input('packagesPerPallet')!.value, '120');
        await type('palletCount', '3,5');
        assert.equal(shown('l.pallet-calculation'), 'Vul aantal pallets en verpakkingen per pallet in.');
        await type('palletCount', '3');
        await type('loosePackages', '15');
        assert.equal(shown('l.pallet-calculation'), '3 pallets × 120 colli + 15 losse colli = 375 colli');
        assert.equal(shown('l.quantity-kg'), '375 colli × 10 kg = 3.750,0 kg netto · 3.862,5 kg bruto');
        await press('l.plain-quantity');
        assert.equal(mode, 'packages');
        assert.equal(input('quantity')!.value, '375');
    });

    test('back to per pallet: an unchanged total keeps the split; a retyped total clears pallets and loose colli', async () => {
        await render({ defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 });
        await press('l.per-pallet');
        await type('palletCount', '3');
        await type('loosePackages', '15');
        await press('l.plain-quantity');
        await press('l.per-pallet');
        assert.equal(shown('l.pallet-calculation'), '3 pallets × 120 colli + 15 losse colli = 375 colli');
        await press('l.plain-quantity');
        await type('quantity', '400');
        await press('l.per-pallet');
        assert.deepEqual([input('palletCount')!.value, input('packagesPerPallet')!.value, input('loosePackages')!.value], ['', '120', '']);
        assert.equal(shown('l.quantity-kg'), null);
    });

    test('per pallet emptied, then a retyped total: back to per pallet refills the default and still clears the old split', async () => {
        await render({ defaultNetKgPerPackage: 10, defaultGrossKgPerPackage: 10.3 });
        await press('l.per-pallet');
        await type('palletCount', '3');
        await type('loosePackages', '15');
        await type('packagesPerPallet', '');
        await press('l.plain-quantity');
        await type('quantity', '400');
        await press('l.per-pallet');
        assert.deepEqual([input('palletCount')!.value, input('packagesPerPallet')!.value, input('loosePackages')!.value], ['', '120', '']);
        assert.notEqual(shown('l.pallet-calculation'), '3 pallets × 120 colli + 15 losse colli = 375 colli');
    });
});
