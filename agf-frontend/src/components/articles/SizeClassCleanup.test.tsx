import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import type { Article } from '../../types/article';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import SizeClassCleanup from './SizeClassCleanup';

// "Maat en klasse opschonen": oudere artikelen zonder maat of met een klasse buiten de lijst. Opslaan stuurt de vaste klasse;
// er wordt nooit automatisch iets opgeslagen.

const items = [
    { id: 3, description: 'Mango · Kent · 4kg Box · Peru', shortcutCode: null, sizeCategory: null, tradeClass: null },
    { id: 9, description: 'Peer · Conference · Nederland · 65+', shortcutCode: 'peer', sizeCategory: '65+', tradeClass: 'klasse A' },
];
const saved: Article = {
    id: 3, productId: 2, productName: 'Mango', variantName: 'Kent', description: 'Mango · Kent · 4kg Box · Peru · n.v.t.', shortcutCode: null, origin: 'Peru', originCountryCode: 'PE',
    brand: null, sizeCategory: 'n.v.t.', packaging: '4kg Box', packagingUnit: 'box', season: null, notes: null, weightMethod: 'Weighed', defaultNetKgPerPackage: null,
    defaultGrossKgPerPackage: null, defaultPackagesPerPallet: null, goodsCode: null, goodsCodeSystem: null, tradeClass: 'II', defaultPrice: 0, isArchived: false,
};

interface Call { method: string; url: string; body: unknown }
let calls: Call[];
let mounted: Mounted;
let changed: Article[];

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const row = (id: number) => mounted.container.querySelector<HTMLElement>(`[data-testid="size-class-cleanup-${id}"]`)!;
const saveButton = (id: number) => row(id).querySelector<HTMLButtonElement>('button:not([aria-label])')!;

describe('SizeClassCleanup', () => {
    beforeEach(() => {
        calls = [];
        changed = [];
        globalThis.fetch = (async (url: RequestInfo | URL, init?: RequestInit) => {
            const call = { method: init?.method ?? 'GET', url: String(url), body: init?.body ? JSON.parse(String(init.body)) : null };
            calls.push(call);
            return call.method === 'GET' ? json(200, items) : json(200, saved);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('lists what is missing, saves only with a size and a class from the list, and sends the fixed class', async () => {
        await mounted.render(<SizeClassCleanup reloadToken={0} onArticleChanged={(article) => changed.push(article)} />);
        await mounted.waitFor(() => row(3), 'list loaded');
        assert.equal(saveButton(3).disabled, true, 'nothing to save yet');
        assert.equal(saveButton(9).disabled, true, 'an unrecognised old class is never proposed');

        const size = row(3).querySelector<HTMLInputElement>('input')!;
        await act(async () => { setValue(size, 'n.v.t.'); });
        await act(async () => { setValue(input('size-class-cleanup-3-class'), '2'); });
        await act(async () => { input('size-class-cleanup-3-class').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
        assert.equal(saveButton(3).disabled, false);
        await act(async () => { saveButton(3).click(); });
        await mounted.waitFor(() => changed.length === 1, 'saved');
        const post = calls.find((call) => call.method === 'POST')!;
        assert.ok(post.url.endsWith('/articles/3/size-class'));
        assert.deepEqual(post.body, { sizeCategory: 'n.v.t.', tradeClass: 'II' });
        assert.equal(mounted.container.querySelector('[data-testid="size-class-cleanup-3"]'), null, 'the article leaves the list');
    });
});
