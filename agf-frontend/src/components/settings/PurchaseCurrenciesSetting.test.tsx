import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import PurchaseCurrenciesSetting from './PurchaseCurrenciesSetting';

// Valuta-ronde: de valuta's voor inkoop onder Instellingen. Euro altijd vooraan en niet te verwijderen; per valuta of er een ECB-koers is.
let mounted: Mounted;
let puts: unknown[];
let stored: { code: string; hasEcbRate: boolean }[];

describe('PurchaseCurrenciesSetting', () => {
    beforeEach(() => {
        puts = [];
        stored = [{ code: 'EUR', hasEcbRate: false }, { code: 'USD', hasEcbRate: true }];
        globalThis.fetch = (async (_input: RequestInfo | URL, init?: RequestInit) => {
            if (init?.method === 'PUT') {
                const codes = (JSON.parse(String(init.body)) as { codes: string[] }).codes;
                puts.push(codes);
                stored = codes.map((code) => ({ code, hasEcbRate: code !== 'EUR' && code !== 'AED' }));
            }
            return json(200, { currencies: stored });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('shows the list with ECB or manual per currency; adds and removes; euro cannot be removed; a bad code is refused', async () => {
        await mounted.render(<PurchaseCurrenciesSetting />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="currency-USD"]'), 'list');
        assert.ok(mounted.container.querySelector('[data-testid="currency-USD"]')!.textContent!.includes('settings_currencies_ecb'));
        assert.equal(mounted.container.querySelector('[data-testid="currency-EUR"] button'), null, 'euro stays');
        const input = mounted.container.querySelector<HTMLInputElement>('#newCurrency-input')!;
        await act(async () => { setValue(input, 'u$d'); });
        await act(async () => { mounted.container.querySelector('form')!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        assert.equal(puts.length, 0);
        assert.ok(mounted.hasText('settings_currencies_invalid'));
        await act(async () => { setValue(input, 'aed'); });
        await act(async () => { mounted.container.querySelector('form')!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="currency-AED"]'), 'added');
        assert.deepEqual(puts[0], ['EUR', 'USD', 'AED']);
        assert.ok(mounted.container.querySelector('[data-testid="currency-AED"]')!.textContent!.includes('settings_currencies_manual'));
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-testid="currency-USD"] button')!.click(); });
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="currency-USD"]') === null, 'removed');
        assert.deepEqual(puts[1], ['EUR', 'AED']);
    });
});
