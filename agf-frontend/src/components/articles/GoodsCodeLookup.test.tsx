import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, deferred, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import type { GoodsCodeSystem } from '../../types/article';
import GoodsCodeLookup from './GoodsCodeLookup';

// De referentielijst zoekt per codestelsel: dezelfde term geeft onder HS, GN en TARIC alleen codes van dát stelsel; HS-treffers
// zijn samengevoegde 6-cijferige posten; TARIC zonder onderbouwde codes is eerlijk leeg met officiële raadpleging en handmatige
// invoer; een stelselwissel wist oude treffers en een laat antwoord van een eerdere zoekopdracht komt nooit terug.

const source = { system: 'CN', name: 'GN 2026', url: null, version: 'GN 2026', validFrom: '2026-01-01', retrievedAt: '2026-10-21', note: null, lookupUrl: 'https://ec.europa.eu/taxation_customs/dds2/taric/taric_consultation.jsp?Lang=nl' };
const bySystem: Record<string, unknown> = {
    HS: { system: 'HS', source, complete: false, totalEntries: 25, matches: [{ code: '080440', system: 'HS', hsCode: '080440', descriptionNl: "Avocado's, vers of gedroogd", descriptionEn: 'Avocados, fresh or dried', mergedCodes: ['08044000'] }] },
    CN: { system: 'CN', source, complete: false, totalEntries: 34, matches: [{ code: '08044000', system: 'CN', hsCode: '080440', descriptionNl: "Avocado's, vers of gedroogd", descriptionEn: 'Avocados, fresh or dried', mergedCodes: null }] },
    TARIC: { system: 'TARIC', source, complete: false, totalEntries: 0, matches: [] },
};

interface Call { url: string }
let mounted: Mounted;
let calls: Call[];
let respond: (url: string) => Promise<Response>;
let picked: { code: string; system: GoodsCodeSystem }[];
let manual: number;
let system: GoodsCodeSystem | '';

const systemOf = (url: string) => new URL(url, 'http://x').searchParams.get('system') ?? '';
const text = () => mounted.container.textContent ?? '';
const query = () => mounted.container.querySelector<HTMLInputElement>('[id="goodsCodeQuery-input"]')!;
const select = () => mounted.container.querySelector<HTMLSelectElement>('[id="goodsCodeLookupSystem-input"]')!;
const render = () => mounted.render(<GoodsCodeLookup system={system} onSystemChange={(next) => { system = next; void render(); }} onPick={(code, s) => picked.push({ code, system: s })} onManual={() => { manual += 1; }} />);
const typeAndSearch = async (term: string) => {
    await act(async () => { setValue(query(), term); });
    await act(async () => { query().dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
};
const switchSystem = async (next: string) => { await act(async () => { setSelectValue(select(), next); }); };

describe('GoodsCodeLookup', () => {
    beforeEach(() => {
        calls = []; picked = []; manual = 0; system = 'CN';
        respond = async (url) => json(200, bySystem[systemOf(url)]);
        globalThis.fetch = (async (input: RequestInfo | URL) => { const url = String(input); calls.push({ url }); return respond(url); }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the same term under HS, CN and TARIC sends the system and shows only codes of that system with the right length', async () => {
        await render();
        await typeAndSearch('avocado');
        await mounted.waitFor(() => text().includes('0804 40 00'), 'CN match');
        assert.equal(systemOf(calls[0].url), 'CN');
        assert.ok(!text().includes('0804 40 ') || text().includes('0804 40 00'), 'no bare HS code under CN');

        await switchSystem('HS');
        assert.equal(system, 'HS');
        assert.equal(mounted.container.querySelector('[data-testid^="goods-code-results-"]'), null, 'old results disappear on a system switch');
        await typeAndSearch('avocado');
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="goods-code-results-HS"]') !== null, 'HS results');
        assert.equal(systemOf(calls[1].url), 'HS');
        const hsRow = mounted.container.querySelector('[data-testid="goods-code-results-HS"] li')!;
        assert.ok(hsRow.textContent!.includes('0804 40') && !hsRow.textContent!.includes('0804 40 00 '), 'six-digit HS code');
        assert.ok(hsRow.textContent!.includes('articles_goods_merged'), 'merged CN codes are shown');
        await act(async () => { mounted.button('articles_goods_pick')!.click(); });
        assert.deepEqual(picked, [{ code: '080440', system: 'HS' }]);
        assert.equal(system, 'HS', 'picking never changes the chosen system');

        await switchSystem('TARIC');
        await typeAndSearch('avocado');
        await mounted.waitFor(() => text().includes('articles_goods_none_taric'), 'honest TARIC message');
        assert.equal(systemOf(calls[2].url), 'TARIC');
        assert.equal(mounted.container.querySelectorAll('[data-testid="goods-code-results-TARIC"] li').length, 0, 'no invented 10-digit codes');
        assert.ok(mounted.container.querySelector('a[href*="taric_consultation"]'), 'official consultation offered');
        await act(async () => { mounted.button('articles_goods_manual')!.click(); });
        assert.equal(manual, 1, 'manual entry offered');
        assert.ok(text().includes('articles_goods_incomplete'), 'never presented as complete');
    });

    test('a late answer of an earlier search never puts results of the old system back after switching', async () => {
        const slow = deferred<Response>();
        respond = (url) => (systemOf(url) === 'CN' ? slow.promise : Promise.resolve(json(200, bySystem[systemOf(url)])));
        await render();
        await typeAndSearch('avocado');
        assert.equal(systemOf(calls[0].url), 'CN');
        await switchSystem('HS');
        await typeAndSearch('avocado');
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="goods-code-results-HS"]') !== null, 'HS results');
        // Nu komt het oude CN-antwoord alsnog binnen.
        await act(async () => { slow.resolve(json(200, bySystem.CN)); await new Promise((resolve) => setTimeout(resolve, 0)); });
        assert.equal(mounted.container.querySelector('[data-testid="goods-code-results-CN"]'), null, 'late CN answer ignored');
        assert.ok(mounted.container.querySelector('[data-testid="goods-code-results-HS"]'), 'HS results stay');
        assert.ok(!text().includes('0804 40 00'), 'no CN code shown');
    });

    test('without a code system the search is disabled and the user is asked to choose one first', async () => {
        system = '';
        await render();
        assert.ok(text().includes('articles_goods_choose_system'));
        await typeAndSearch('avocado');
        assert.equal(calls.length, 0, 'nothing sent without a system');
        assert.equal(mounted.button('articles_goods_search')!.disabled, true);
    });
});
