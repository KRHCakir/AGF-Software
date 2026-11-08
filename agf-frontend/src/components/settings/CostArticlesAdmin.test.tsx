import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import CostArticlesAdmin from './CostArticlesAdmin';

// Kostenartikelen beheren door de echte componentlevenscyclus: de standaardsoorten staan er, een eigen soort toevoegen (dubbel gemeld
// zonder te versturen), inactief maken; er is geen verwijderknop.

interface Call { method: string; url: string; body: Record<string, unknown> | null }
let mounted: Mounted;
let calls: Call[];
let list: Record<string, unknown>[];

describe('CostArticlesAdmin', () => {
    beforeEach(() => {
        calls = [];
        list = ['Handling', 'Transport', 'QC', 'Zeevracht', 'Afval', 'Overig'].map((name, index) => ({ id: index + 1, name, isStandard: true, isActive: true, sortOrder: (index + 1) * 10, lineCount: name === 'Handling' ? 2 : 0 }));
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const call: Call = { method: init?.method ?? 'GET', url: String(input), body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : null };
            calls.push(call);
            if (call.method === 'POST') { const created = { id: 7, name: call.body!.name, isStandard: false, isActive: true, sortOrder: 70, lineCount: 0 }; list = [...list, created]; return json(201, created); }
            if (call.method === 'PUT') { const id = Number(call.url.split('/').at(-2)); list = list.map((item) => (item.id === id ? { ...item, isActive: call.body!.isActive } : item)); return json(200, list.find((item) => item.id === id)); }
            return json(200, list);
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the standard kinds are listed; an own cost article is added once; a cost article can be made inactive; nothing can be deleted', async () => {
        await mounted.render(<CostArticlesAdmin />);
        await mounted.waitFor(() => mounted.hasText('Zeevracht'), 'list');
        assert.ok(mounted.hasText('cost_article_used'));
        const name = mounted.container.querySelector<HTMLInputElement>('[id="costArticleName-input"]')!;
        await act(async () => { setValue(name, '  ZEEVRACHT '); });
        await act(async () => { mounted.button('cost_article_add')!.click(); });
        assert.equal(calls.filter((call) => call.method === 'POST').length, 0, 'not sent when the name already exists');
        assert.ok(mounted.hasText('cost_article_exists'));
        await act(async () => { setValue(name, '  Autohuur '); });
        await act(async () => { mounted.button('cost_article_add')!.click(); });
        await mounted.waitFor(() => mounted.hasText('Autohuur'), 'added');
        const post = calls.find((call) => call.method === 'POST')!;
        assert.equal(post.body!.name, 'Autohuur');
        assert.ok(typeof post.body!.requestId === 'string');
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-tour-key="Afval"] button')!.click(); });
        await mounted.waitFor(() => mounted.hasText('cost_article_activate'), 'inactive');
        assert.deepEqual(calls.find((call) => call.method === 'PUT')!.body, { isActive: false });
        assert.equal(mounted.container.querySelector('[data-tour="cost-article-delete"]'), null);
    });
});
