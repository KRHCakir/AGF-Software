import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act, useState } from 'react';
import { reformatDecimalFieldsOnBlur } from '../../lib/decimal';
import type { QuantityInputMode } from '../../lib/palletInput';
import { Mounted, setValue } from '../../test/reactHarness.ts';
import QuantityInput from './QuantityInput';

// Echo en blur door de echte levenscyclus van het hoeveelheidsveld (zonder LanguageProvider: nl-invoer, i18n-sleutels als tekst).
function Line() {
    const [line, setLine] = useState({ quantity: '', unit: 'box' as const, mode: 'packages' as QuantityInputMode, palletCount: '', packagesPerPallet: '' });
    return <QuantityInput prefix="l" index={0} line={line} locked={false} errors={() => undefined} defaultPackagesPerPallet={null}
        onChange={(field, value) => setLine((current) => ({ ...current, [field]: value }))} />;
}

describe('NumberEcho and reformat on blur', () => {
    let mounted: Mounted;
    let uninstall: () => void;
    beforeEach(async () => { uninstall = reformatDecimalFieldsOnBlur(window.document); mounted = new Mounted(); await mounted.render(<Line />); });
    afterEach(async () => { uninstall(); await mounted.unmount(); });

    const input = () => mounted.container.querySelector<HTMLInputElement>('#l\\.quantity-input')!;
    const echo = () => mounted.container.querySelector('[data-testid="l.quantity-echo"]')?.textContent ?? null;
    const type = (value: string) => act(async () => { setValue(input(), value); });
    const blur = () => act(async () => { input().dispatchEvent(new window.FocusEvent('focusout', { bubbles: true })); });

    test('the echo shows the interpreted value; blur rewrites it in screen notation through onChange', async () => {
        assert.equal(echo(), null, 'empty field: no echo');
        await type('15000');
        assert.equal(echo(), '= 15.000 unit_box');
        await blur();
        assert.equal(input().value, '15.000');
        assert.equal(echo(), '= 15.000 unit_box', '15.000 stays fifteen thousand, not fifteen');
    });

    test('an invalid dot shows the separator message and stays as typed after blur', async () => {
        await type('15.5');
        assert.equal(echo(), 'supply_error_decimal_separator');
        await blur();
        assert.equal(input().value, '15.5');
    });
});
