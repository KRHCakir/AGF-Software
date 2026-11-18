import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { formatKg, setWeightDecimals, weightDecimals } from '../../lib/weightDisplay';
import { Mounted, json } from '../../test/reactHarness.ts';
import WeightDisplayProvider from '../shared/WeightDisplayProvider';
import WeightDisplaySetting from './WeightDisplaySetting';

// De instelling Gewichten tonen: standaard hele kilo's; kiezen voor één decimaal slaat op en geldt direct voor de schermen.
let mounted: Mounted;
let stored: number;
let puts: unknown[];
let fail: boolean;
const $ = <T extends HTMLElement = HTMLElement>(selector: string) => mounted.container.querySelector<T>(selector);
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });

describe('weight display setting', () => {
    beforeEach(() => {
        stored = 0;
        puts = [];
        fail = false;
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            assert.ok(String(input).endsWith('/settings/weight-display'));
            if (fail) return json(500, {});
            if (init?.method === 'PUT') { const body = JSON.parse(String(init.body)) as { decimals: number }; puts.push(body); stored = body.decimals; }
            return json(200, { decimals: stored });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); setWeightDecimals(0); });

    test('whole kilos by default; choosing one decimal saves it and the screens follow at once', async () => {
        await mounted.render(<WeightDisplaySetting />);
        await settle();
        assert.equal($<HTMLInputElement>('[data-testid="weight-decimals-0"]')!.checked, true);
        assert.ok($('[data-testid="weight-display-example"]')!.textContent!.includes('2.273 kg') || $('[data-testid="weight-display-example"]')!.textContent!.includes('settings_weight_example'));
        await act(async () => { $('[data-testid="weight-decimals-1"]')!.click(); });
        await settle();
        assert.deepEqual(puts, [{ decimals: 1 }]);
        assert.equal($<HTMLInputElement>('[data-testid="weight-decimals-1"]')!.checked, true);
        assert.equal(weightDecimals(), 1);
        assert.equal(formatKg(2272.5, 'nl'), '2.272,5 kg');
        assert.ok(mounted.hasText('settings_weight_saved'));
    });

    test('the pages get the setting of the company, and whole kilos when the server does not answer', async () => {
        stored = 1;
        await mounted.render(<WeightDisplayProvider><p id="page">{'pagina'}</p></WeightDisplayProvider>);
        await settle();
        assert.ok($('#page'));
        assert.equal(weightDecimals(), 1);

        await mounted.unmount();
        fail = true;
        mounted = new Mounted();
        await mounted.render(<WeightDisplayProvider><p id="page">{'pagina'}</p></WeightDisplayProvider>);
        await settle();
        assert.ok($('#page'));
        assert.equal(weightDecimals(), 0);
    });
});
