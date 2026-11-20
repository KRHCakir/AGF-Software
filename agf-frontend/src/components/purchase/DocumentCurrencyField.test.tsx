import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act, useState } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import { setInputLocale } from '../../lib/decimal.ts';
import { type CurrencyDraft, euroDraft } from '../../lib/documentCurrency';
import DocumentCurrencyField from './DocumentCurrencyField';

// Valuta-ronde: euro standaard; een andere valuta haalt de ECB-koers voor de documentdatum op en vult hem in ("1 EUR = 1,0842 USD · ECB,
// datum"); aanpassen maakt de bron Handmatig zonder verdere waarschuwing; geen ECB-koers of de ECB onbereikbaar zegt dat en vraagt de
// koers zelf. Nooit het echte netwerk.

// Zonder LanguageProvider is de schermtaal Engels: de invoer volgt die taal (zoals in de app de LanguageProvider doet).
setInputLocale('en');

let mounted: Mounted;
let urls: string[];
let respond: (url: URL) => Response;
let seen: CurrencyDraft;

const currencies = [{ code: 'EUR', hasEcbRate: false }, { code: 'USD', hasEcbRate: true }, { code: 'AED', hasEcbRate: false }];

let setDate: (date: string) => void;

function Field({ initial = euroDraft(), fetchOnOpen = true }: { initial?: CurrencyDraft; fetchOnOpen?: boolean }) {
    const [value, setValueState] = useState(initial);
    const [date, setDateState] = useState('2026-11-18');
    seen = value;
    setDate = setDateState;
    return <DocumentCurrencyField value={value} onChange={setValueState} documentDate={date} currencies={currencies} errors={{}} fetchOnOpen={fetchOnOpen} />;
}

const select = () => mounted.container.querySelector<HTMLSelectElement>('#currency-input')!;
const rate = () => mounted.container.querySelector<HTMLInputElement>('#exchangeRate-input');
const source = () => mounted.container.querySelector('[data-testid="currency-rate-source"]')?.textContent ?? '';

describe('DocumentCurrencyField', () => {
    beforeEach(() => {
        urls = [];
        respond = () => json(200, { currency: 'USD', unitsPerEuro: 1.0842, rateDate: '2026-11-18', forDate: '2026-11-18', source: 'ECB' });
        globalThis.fetch = (async (input: RequestInfo | URL) => {
            urls.push(String(input));
            return respond(new URL(String(input), 'http://localhost'));
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('euro is the default and needs no rate', async () => {
        await mounted.render(<Field />);
        assert.equal(select().value, 'EUR');
        assert.equal(rate(), null);
        assert.equal(urls.length, 0);
    });

    test('USD fetches the ECB rate of the document date and fills it in; changing it makes it Manual, and the ECB rate can come back', async () => {
        await mounted.render(<Field />);
        await act(async () => { setSelectValue(select(), 'USD'); });
        await mounted.waitFor(() => /^1[.,]0842$/.test(rate()?.value ?? ''), 'rate filled');
        const url = new URL(urls[0], 'http://localhost');
        assert.deepEqual([url.searchParams.get('currency'), url.searchParams.get('date')], ['USD', '2026-11-18']);
        assert.ok(source().includes('currency_rate_ecb'), source());
        assert.ok(source().includes('currency_rate_direction'));
        assert.deepEqual([seen.currency, seen.source, seen.rateDate], ['USD', 'ECB', '2026-11-18']);

        await act(async () => { setValue(rate()!, '1.1'); });
        assert.deepEqual([seen.rate, seen.source, seen.rateDate], ['1.1', 'Manual', null]);
        assert.ok(source().includes('currency_rate_manual'));
        assert.equal(mounted.container.querySelector('[role="status"]'), null, 'no warning after a manual rate');

        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-testid="currency-use-ecb"]')!.click(); });
        await mounted.waitFor(() => /^1[.,]0842$/.test(rate()?.value ?? ''), 'ECB rate back');
        assert.equal(seen.source, 'ECB');

        await act(async () => { setSelectValue(select(), 'EUR'); });
        assert.deepEqual([seen.currency, seen.source, rate()], ['EUR', 'NotApplicable', null]);
    });

    test('no ECB rate (400) or an unreachable ECB (503): the screen says so and the user enters the rate (Manual)', async () => {
        respond = () => json(400, { errors: { currency: ['no rate'] } });
        await mounted.render(<Field />);
        await act(async () => { setSelectValue(select(), 'AED'); });
        await mounted.waitFor(() => mounted.hasText('currency_rate_none'), 'no-rate message');
        assert.deepEqual([seen.source, seen.rate], ['Manual', '']);
        await act(async () => { setValue(rate()!, '3.98'); });
        assert.ok(mounted.hasText('currency_rate_none'), 'the explanation stays');

        respond = () => json(503, { message: 'down' });
        await act(async () => { setSelectValue(select(), 'USD'); });
        await mounted.waitFor(() => mounted.hasText('currency_rate_unavailable'), 'unavailable message');
        assert.equal(seen.source, 'Manual');
    });

    test('another date on a new order: the previous rate is cleared at once until the rate of the new date is in', async () => {
        await mounted.render(<Field />);
        await act(async () => { setSelectValue(select(), 'USD'); });
        await mounted.waitFor(() => /^1[.,]0842$/.test(rate()?.value ?? ''), 'first rate');
        let release: () => void = () => {};
        const gate = new Promise<void>((resolve) => { release = resolve; });
        const answer = json(200, { currency: 'USD', unitsPerEuro: 1.0992, rateDate: '2026-11-19', forDate: '2026-11-19', source: 'ECB' });
        globalThis.fetch = (async (input: RequestInfo | URL) => { urls.push(String(input)); await gate; return answer; }) as typeof fetch;
        await act(async () => { setDate('2026-11-19'); });
        assert.deepEqual([seen.rate, seen.rateDate, seen.source], ['', null, 'ECB'], 'no old rate while fetching');
        await act(async () => { release(); });
        await mounted.waitFor(() => /^1[.,]0992$/.test(rate()?.value ?? ''), 'rate of the new date');
        assert.equal(seen.rateDate, '2026-11-19');
    });

    test('a stored order keeps its fixed ECB rate when the date changes, as the server does', async () => {
        await mounted.render(<Field fetchOnOpen={false} initial={{ currency: 'USD', rate: '1.0842', source: 'ECB', rateDate: '2026-11-10', touched: true }} />);
        await act(async () => { setDate('2026-11-19'); });
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });
        assert.equal(urls.length, 0, 'no new rate for another date');
        assert.deepEqual([seen.rate, seen.rateDate], ['1.0842', '2026-11-10']);
    });

    test('a stored euro order fetches the ECB rate as soon as another currency is chosen', async () => {
        await mounted.render(<Field fetchOnOpen={false} />);
        await act(async () => { setSelectValue(select(), 'USD'); });
        await mounted.waitFor(() => /^1[.,]0842$/.test(rate()?.value ?? ''), 'rate fetched on the first change');
        assert.equal(urls.length, 1);
    });

    test('a stored order opens with its fixed rate and does not fetch until the currency changes', async () => {
        await mounted.render(<Field fetchOnOpen={false} initial={{ currency: 'USD', rate: '1,0842', source: 'ECB', rateDate: '2026-11-10', touched: true }} />);
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });
        assert.equal(urls.length, 0);
        assert.match(rate()!.value, /^1[.,]0842$/);
    });
});
