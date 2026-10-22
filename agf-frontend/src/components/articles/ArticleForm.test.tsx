import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import type { Article, ArticleReference } from '../../types/article';
import ArticleForm from './ArticleForm';

// Artikelformulier door de echte componentlevenscyclus: aanmaken met alleen een productsoort, inline fouten met behoud
// van invoer, goederencode via de referentielijst (bron zichtbaar, gebruiker neemt zelf over), bewerken via PUT.

const reference: ArticleReference = { productNames: ['Avocado', 'Mango'], packagingUnits: ['box', 'colli', 'tray', 'piece', 'dozen'], weightMethods: ['Weighed', 'Calculated'], goodsCodeSystems: ['HS', 'CN', 'TARIC'], tradeClassSuggestions: ['Extra', 'I', 'II'] };
const hass: Article = {
    id: 1, productId: 1, productName: 'Avocado', variantName: 'Hass', description: 'Avocado · Hass · 4kg Box · Peru', shortcutCode: 'avohassper22', origin: 'Peru', brand: null,
    sizeCategory: null, packaging: '4kg Box', packagingUnit: 'box', season: null, notes: null, weightMethod: 'Calculated', defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: null,
    defaultPackagesPerPallet: 120, goodsCode: null, goodsCodeSystem: null, tradeClass: null, defaultGgn: null, defaultPrice: 12.5,
};

interface Call { method: string; url: string; body: unknown }
let mounted: Mounted;
let calls: Call[];
let respond: (call: Call) => Response;

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const type = async (element: HTMLInputElement | HTMLTextAreaElement, value: string) => { await act(async () => { setValue(element, value); }); };
const text = () => mounted.container.textContent ?? '';
const save = async () => { await act(async () => { mounted.container.querySelector('form')!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); }); };

describe('ArticleForm', () => {
    beforeEach(() => {
        calls = [];
        respond = () => json(404, {});
        globalThis.fetch = (async (inputUrl: RequestInfo | URL, init?: RequestInit) => {
            const call = { method: init?.method ?? 'GET', url: String(inputUrl), body: init?.body ? JSON.parse(String(init.body)) : null };
            calls.push(call);
            return respond(call);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('a new article needs only a product kind; optional fields are sent as null and the trade section stays collapsed', async () => {
        let saved: Article | null = null;
        respond = (call) => json(201, { ...hass, id: 7, productName: call.body && typeof call.body === 'object' ? (call.body as { productName: string }).productName : '', variantName: '', description: 'Tomaat', shortcutCode: 'tom', packagingUnit: null, weightMethod: 'Weighed', defaultNetKgPerPackage: null, defaultPackagesPerPallet: null });
        await mounted.render(<ArticleForm article={null} reference={reference} onSaved={(article) => { saved = article; }} onClose={() => {}} />);
        assert.equal(window.document.activeElement, input('shortcutCode'), 'search code gets focus first');
        assert.equal(mounted.container.querySelector('#article-trade'), null, 'trade data collapsed');
        await type(input('shortcutCode'), 'tom');
        await type(input('productName'), 'Tomaat');
        await save();
        await mounted.waitFor(() => calls.length === 1, 'posted');
        assert.equal(calls[0].method, 'POST');
        assert.deepEqual(calls[0].body, {
            productName: 'Tomaat', variantName: null, shortcutCode: 'tom', origin: null, brand: null, sizeCategory: null, packaging: null, packagingUnit: null, season: null, notes: null,
            weightMethod: 'Weighed', defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null, defaultPackagesPerPallet: null, goodsCode: null, goodsCodeSystem: null, tradeClass: null, defaultGgn: null,
        });
        await mounted.waitFor(() => saved !== null, 'onSaved called');
        assert.ok(text().includes('articles_created'));
    });

    test('validation keeps the input and shows inline errors; gross below net and a goods code without system are refused before any request', async () => {
        await mounted.render(<ArticleForm article={null} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await type(input('productName'), 'Avocado');
        await type(input('defaultNetKgPerPackage'), '4');
        await type(input('defaultGrossKgPerPackage'), '3,5');
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="article-trade"]')!.click(); });
        await type(input('goodsCode'), '0804 40 00');
        await save();
        assert.equal(calls.length, 0, 'nothing sent');
        assert.ok(text().includes('articles_error_gross_below_net'));
        assert.ok(text().includes('articles_error_system_required'));
        assert.equal(input('goodsCode').value, '0804 40 00', 'input kept');
        assert.equal(input('defaultGrossKgPerPackage').value, '3,5', 'input kept');
        assert.equal(input('defaultNetKgPerPackage').getAttribute('aria-invalid'), 'false');
        assert.equal(input('defaultGrossKgPerPackage').getAttribute('aria-invalid'), 'true');
    });

    test('editing sends a PUT with the article id; the goods-code lookup searches within the chosen system, shows its source and the user takes a code over explicitly', async () => {
        respond = (call) => {
            if (call.method === 'GET' && call.url.includes('/articles/goods-codes')) {
                const system = new URL(call.url, 'http://x').searchParams.get('system');
                if (system !== 'CN') return json(200, { system, source: { system: 'CN', name: 'GN 2026', url: null, version: 'GN 2026', validFrom: '2026-01-01', retrievedAt: '2026-10-21', note: null, lookupUrl: null }, complete: false, totalEntries: 0, matches: [] });
                return json(200, { system: 'CN', source: { system: 'CN', name: 'GN 2026', url: null, version: 'GN 2026', validFrom: '2026-01-01', retrievedAt: '2026-10-21', note: 'Demonstratielijst', lookupUrl: null }, complete: false, totalEntries: 34,
                    matches: [{ code: '08044000', system: 'CN', hsCode: '080440', descriptionNl: "Avocado's, vers of gedroogd", descriptionEn: 'Avocados, fresh or dried', mergedCodes: null }] });
            }
            if (call.method === 'PUT') return json(200, { ...hass, goodsCode: '08044000', goodsCodeSystem: 'CN', tradeClass: 'I' });
            return json(404, {});
        };
        await mounted.render(<ArticleForm article={hass} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        assert.equal(input('shortcutCode').value, 'avohassper22');
        assert.equal(input('defaultPackagesPerPallet').value, '120');
        assert.ok(text().includes('box'), 'unit-specific labels mention the packaging unit');
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="article-trade"]')!.click(); });
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('button[aria-expanded][type="button"]:not([aria-controls])')!.click(); });
        // Zonder stelsel zoekt de lijst niet; het stelsel in de zoeker is hetzelfde veld als in het formulier.
        assert.ok(text().includes('articles_goods_choose_system'), 'asks for a system first');
        const lookupSystem = mounted.container.querySelector<HTMLSelectElement>('[id="goodsCodeLookupSystem-input"]')!;
        await act(async () => { setSelectValue(lookupSystem, 'CN'); });
        assert.equal(input('goodsCodeSystem').value, 'CN', 'form system follows the lookup system');
        const query = mounted.container.querySelector<HTMLInputElement>('[id="goodsCodeQuery-input"]')!;
        await type(query, 'avocado');
        await act(async () => { query.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => text().includes('0804 40 00'), 'match shown');
        assert.ok(calls.some((call) => call.url.includes('/articles/goods-codes') && call.url.includes('system=CN')), 'chosen system sent to the server');
        assert.ok(text().includes('articles_goods_source') && text().includes('Demonstratielijst'), 'source and note shown (t() returns keys without a LanguageProvider)');
        assert.ok(text().includes('articles_goods_incomplete'), 'never presented as complete');
        assert.equal(input('goodsCode').value, '', 'nothing filled without an explicit choice');
        await act(async () => { mounted.button('articles_goods_pick')!.click(); });
        assert.equal(input('goodsCode').value, '0804 40 00');
        assert.equal(input('goodsCodeSystem').value, 'CN', 'picking keeps the chosen system');
        await type(input('tradeClass'), 'I');
        await save();
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'updated');
        const put = calls.find((call) => call.method === 'PUT')!;
        assert.ok(put.url.endsWith('/articles/1'));
        const body = put.body as Record<string, unknown>;
        assert.equal(body.goodsCode, '08044000', 'digits only, leading zero kept');
        assert.equal(body.goodsCodeSystem, 'CN');
        assert.equal(body.tradeClass, 'I');
        assert.equal(body.defaultPackagesPerPallet, 120);
        await mounted.waitFor(() => text().includes('articles_saved'), 'saved notice');
    });

    test('the default GGN is a separate optional field: 13 digits with leading zeros are sent as text, anything else is refused inline', async () => {
        respond = (call) => (call.method === 'PUT' ? json(200, { ...hass, defaultGgn: '0049929123456' }) : json(404, {}));
        await mounted.render(<ArticleForm article={hass} reference={reference} onSaved={() => {}} onClose={() => {}} />);
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="article-trade"]')!.click(); });
        assert.ok(text().includes('articles_hint_ggn'), 'states "as given; certification not verified"');
        assert.equal(mounted.container.querySelector('input[type="checkbox"][id*="ggn" i], input[type="file"]'), null, 'no yes/no switch and no file upload');
        await type(input('defaultGgn'), '404992912345');
        await save();
        assert.equal(calls.length, 0, 'nothing sent');
        assert.ok(text().includes('articles_error_ggn_13_digits'));
        assert.equal(input('defaultGgn').value, '404992912345', 'input kept');
        await type(input('defaultGgn'), '0049 929 123456');
        await save();
        await mounted.waitFor(() => calls.some((call) => call.method === 'PUT'), 'updated');
        const body = calls.find((call) => call.method === 'PUT')!.body as Record<string, unknown>;
        assert.equal(body.defaultGgn, '0049929123456', 'text with leading zero, spaces removed');
        assert.equal(body.goodsCode, null, 'GGN is independent of the goods code');
        await mounted.waitFor(() => text().includes('articles_saved'), 'saved notice');
        assert.equal(input('defaultGgn').value, '0049929123456');
    });
});
