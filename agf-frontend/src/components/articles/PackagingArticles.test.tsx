import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import PackagingArticles from './PackagingArticles';

// Emballage bij Artikelen door de echte componentlevenscyclus: de standaardartikelen met prijs en btw, een prijs aanpassen (fout gemeld
// zonder te versturen), een eigen artikel toevoegen (dubbel gemeld zonder te versturen) en inactief maken; er is geen verwijderknop.

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let list: Record<string, unknown>[];

const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;

describe('PackagingArticles', () => {
    beforeEach(() => {
        calls = [];
        list = ['Pallet wegwerp', 'Euro-pallet', 'Krat'].map((name, index) => ({ id: 7 + index, name, kind: 'Packaging', defaultUnitPrice: null, vatPercentage: 9, isStandard: true, isActive: true, sortOrder: 110 + index * 10, lineCount: 0 }));
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const call: Call = { method: init?.method ?? 'GET', url: String(input), body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.method === 'POST') { const created = { ...list[0], ...call.body, id: 20, isStandard: false }; list = [...list, created]; return json(201, created); }
            if (call.method === 'PUT') {
                const id = Number(call.url.match(/packaging-articles\/(\d+)/)![1]);
                list = list.map((item) => (item.id === id ? { ...item, ...call.body } : item));
                return json(200, list.find((item) => item.id === id));
            }
            return json(200, list);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('a price is checked before saving and sent as a number with the VAT; a new article is added once; nothing can be deleted', async () => {
        await mounted.render(<PackagingArticles />);
        await mounted.waitFor(() => mounted.hasText('Euro-pallet'), 'list');
        assert.ok(calls[0].url.includes('/packaging-articles?includeInactive=true'));
        assert.equal(field('packaging-7.vatPercentage-input').value, '9');

        await act(async () => { setValue(field('packaging-7.defaultUnitPrice-input'), '7,505'); });
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-tour-key="Pallet wegwerp"] button')!.click(); });
        assert.equal(calls.filter((call) => call.method === 'PUT').length, 0, 'not sent with three decimals');
        assert.ok(mounted.hasText('packaging_error_price'));
        await act(async () => { setValue(field('packaging-7.defaultUnitPrice-input'), '7,50'); });
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-tour-key="Pallet wegwerp"] button')!.click(); });
        await mounted.waitFor(() => mounted.hasText('packaging_article_saved'), 'saved');
        assert.deepEqual(calls.find((call) => call.method === 'PUT')!.body, { defaultUnitPrice: 7.5, vatPercentage: 9 });

        await act(async () => { setValue(field('packaging-new.name-input'), ' KRAT '); });
        await act(async () => { mounted.button('cost_article_add')!.click(); });
        assert.equal(calls.filter((call) => call.method === 'POST').length, 0, 'not sent when the name exists');
        await act(async () => { setValue(field('packaging-new.name-input'), 'Fust CBL'); setValue(field('packaging-new.defaultUnitPrice-input'), '3,25'); });
        await act(async () => { mounted.button('cost_article_add')!.click(); });
        await mounted.waitFor(() => mounted.hasText('Fust CBL'), 'added');
        const post = calls.find((call) => call.method === 'POST')!;
        assert.deepEqual({ ...post.body, requestId: typeof post.body!.requestId }, { requestId: 'string', name: 'Fust CBL', defaultUnitPrice: 3.25, vatPercentage: 9 });

        await act(async () => { mounted.container.querySelectorAll<HTMLButtonElement>('[data-tour-key="Krat"] button')[1].click(); });
        await mounted.waitFor(() => calls.some((call) => call.url.endsWith('/packaging-articles/9/active')), 'inactive');
        assert.deepEqual(calls.find((call) => call.url.endsWith('/active'))!.body, { isActive: false });
        assert.equal(mounted.container.querySelector('[data-tour="packaging-delete"]'), null);
    });
});
