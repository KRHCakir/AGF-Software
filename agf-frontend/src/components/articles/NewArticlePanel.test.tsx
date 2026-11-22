import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { Article } from '../../types/article';
import NewArticlePanel from './NewArticlePanel';

// Feedback 22 november: een nieuw artikel vanuit een order maak je in een zijpaneel. Compact (product, herkomst, klasse, btw, eenheid,
// kg per colli), "Meer velden" voor de rest; Escape of Annuleren sluit, met een vraag als er al iets is ingevuld.
const reference = { productNames: ['Gember'], packagingUnits: ['box', 'colli', 'tray', 'piece', 'dozen'], weightMethods: ['Weighed', 'Calculated'], goodsCodeSystems: ['HS', 'CN', 'TARIC'], tradeClassSuggestions: ['Extra', 'I', 'II'] };
const countries = { countries: [{ code: 'CN', nameNl: 'China', nameEn: 'China' }, { code: 'PE', nameNl: 'Peru', nameEn: 'Peru' }], aliases: [] };
const ginger: Article = {
    id: 77, productId: 9, productName: 'Gember', variantName: '', description: 'Gember · China', shortcutCode: null, origin: 'China', originCountryCode: 'CN', brand: null,
    sizeCategory: null, packaging: null, packagingUnit: 'box', season: null, notes: null, weightMethod: 'Weighed', defaultNetKgPerPackage: 5, defaultGrossKgPerPackage: null,
    defaultPackagesPerPallet: null, goodsCode: null, goodsCodeSystem: null, tradeClass: 'I', defaultPrice: 0, vatPercentage: 9,
};

let mounted: Mounted;
let posted: unknown[];
let saved: Article[];
let closed: number;
const panel = () => window.document.querySelector<HTMLElement>('[data-testid="new-article-panel"]');
const field = (id: string) => window.document.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const escape = async () => { await act(async () => { (window.document.activeElement ?? panel()!).dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true })); }); };
const enter = async (element: HTMLElement) => { await act(async () => { element.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); }); };

describe('NewArticlePanel', () => {
    beforeEach(async () => {
        posted = [];
        saved = [];
        closed = 0;
        globalThis.fetch = (async (url: RequestInfo | URL, init?: RequestInit) => {
            if (init?.method === 'POST') { posted.push(JSON.parse(String(init.body))); return json(201, ginger); }
            if (String(url).includes('/articles/reference')) return json(200, reference);
            if (String(url).includes('/countries')) return json(200, countries);
            return json(404, {});
        }) as typeof fetch;
        mounted = new Mounted();
        await mounted.render(<NewArticlePanel onSaved={(article) => saved.push(article)} onClose={() => { closed += 1; }} />);
        await mounted.waitFor(() => window.document.querySelector('[id="productName-input"]'), 'form loaded');
    });
    afterEach(async () => { await mounted.unmount(); });

    test('a dialog over the page (outside the page form) with only the minimal fields; "More fields" shows the rest', async () => {
        assert.equal(panel()!.getAttribute('role'), 'dialog');
        assert.equal(panel()!.getAttribute('aria-modal'), 'true');
        assert.equal(mounted.container.contains(panel()), false, 'rendered in a portal, not inside the order form');
        for (const id of ['productName', 'originCountryCode', 'tradeClass', 'vatPercentage', 'packagingUnit', 'defaultNetKgPerPackage']) assert.ok(window.document.querySelector(`[id="${id}-input"]`), id);
        for (const id of ['shortcutCode', 'sizeCategory', 'brand', 'defaultPackagesPerPallet']) assert.equal(window.document.querySelector(`[id="${id}-input"]`), null, id);
        assert.equal(window.document.activeElement, field('productName'), 'the product gets the focus');
        await act(async () => { window.document.querySelector<HTMLButtonElement>('[data-tour="article-more-fields"]')!.click(); });
        assert.ok(window.document.querySelector('[id="sizeCategory-input"]'));
    });

    test('Escape with nothing filled in closes at once', async () => {
        await escape();
        assert.equal(closed, 1);
    });

    test('Escape with something filled in asks first; "Keep filling in" keeps everything, "Discard" closes', async () => {
        await act(async () => { setValue(field('productName'), 'Gember'); });
        await escape();
        assert.equal(closed, 0);
        assert.ok(window.document.querySelector('[data-testid="new-article-discard"]'));
        await act(async () => { window.document.querySelector<HTMLButtonElement>('[data-testid="new-article-discard"] button')!.click(); });
        assert.equal(window.document.querySelector('[data-testid="new-article-discard"]'), null);
        assert.equal(field('productName').value, 'Gember', 'nothing lost');
        await act(async () => { window.document.querySelector<HTMLButtonElement>('[data-tour="article-panel-cancel"]')!.click(); });
        await act(async () => { window.document.querySelector<HTMLButtonElement>('[data-tour="new-article-discard"]')!.click(); });
        assert.equal(closed, 1);
    });

    test('saving creates the article (9% by default) and hands it back', async () => {
        await act(async () => { setValue(field('productName'), 'Gember'); });
        await act(async () => { setValue(field('originCountryCode'), 'CN'); });
        await enter(field('originCountryCode'));
        await act(async () => { setValue(field('tradeClass'), 'I'); });
        await enter(field('tradeClass'));
        await act(async () => { window.document.querySelector<HTMLFormElement>('[data-testid="new-article-panel"] form')!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => saved.length === 1, 'saved');
        assert.equal(saved[0].id, 77);
        const body = posted[0] as { productName: string; originCountryCode: string; tradeClass: string; vatPercentage: number; sizeCategory: string | null };
        assert.deepEqual([body.productName, body.originCountryCode, body.tradeClass, body.vatPercentage, body.sizeCategory], ['Gember', 'CN', 'I', 9, null]);
        assert.equal(closed, 0, 'closing after saving is up to the order');
    });
});
