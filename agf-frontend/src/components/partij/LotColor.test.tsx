import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import ProductColors from '../articles/ProductColors';
import { LotColorBadge, LotColorField } from './LotColor';

// Feedback 22 november: kleur van de partij, gekozen uit de lijst van het product (Stamgegevens). Geldt voor de hele partij.

const partij = (color: string | null) => ({
    id: 7, number: 'I-2611005', kind: 'Purchase', status: 'Open', linked: true, vendors: [], products: [], purchasedQuantity: 0, onHandQuantity: 0, soldQuantity: 0,
    orders: [], receipts: [], history: [], color, allowedColors: [],
});

let mounted: Mounted;
let calls: { method: string; url: string; body: string | null }[];
let answer: (method: string, url: string) => Response;
const one = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);

describe('lot color', () => {
    beforeEach(() => {
        calls = [];
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            calls.push({ method: init?.method ?? 'GET', url: String(input), body: init?.body ? String(init.body) : null });
            return answer(init?.method ?? 'GET', String(input));
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the field offers the colors of the product and saves the choice for the whole lot', async () => {
        answer = (method) => (method === 'PUT'
            ? json(200, { id: 7, number: 'I-2611005', kind: 'Purchase', salesOrderId: null, status: 'Open', note: null, createdAt: '2026-11-23T09:00:00Z', createdBy: 'Jan', linked: true, color: '4' })
            : json(500, {}));
        let saved: string | null = null;
        await mounted.render(<LotColorField partijId={7} initial={{ color: null, allowedColors: ['1', '2', '3', '4', '5', '6'] }} onSaved={(color) => { saved = color; }} />);
        assert.equal(calls.length, 0, 'known colors: nothing is fetched');
        const select = one('[data-tour="lot-color"]') as HTMLSelectElement;
        assert.deepEqual(Array.from(select.options).map((o) => o.value), ['', '1', '2', '3', '4', '5', '6']);
        await act(async () => { select.value = '4'; select.dispatchEvent(new window.Event('change', { bubbles: true })); });
        await act(async () => { one('[data-tour="lot-color-save"]')!.click(); });
        await mounted.waitFor(() => (saved !== null ? true : null), 'saved');
        assert.ok(calls[0].url.endsWith('/partijen/7/color'));
        assert.deepEqual(JSON.parse(calls[0].body!), { color: '4' });
    });

    test('without a color list the field says where to set it; the badge shows a color only when there is one', async () => {
        await mounted.render(<><LotColorField partijId={7} initial={{ color: null, allowedColors: [] }} /><LotColorBadge color={null} /><LotColorBadge color="Geel" /></>);
        assert.ok(mounted.hasText('lot_color_no_list'));
        assert.equal(mounted.container.querySelectorAll('[data-testid="lot-color"]').length, 1);
    });

    test('in the inspection (compact) a product without colors shows nothing: no label, no explanation', async () => {
        // Twee partijen van hetzelfde product zonder kleuren; de tweede heeft al een kleur (van vroeger), dat bewijst dat het laden klaar is.
        answer = (_method, url) => json(200, partij(url.endsWith('/8') ? 'Geel' : null));
        await mounted.render(<><LotColorField partijId={7} partijNumber="I-2611005" compact /><LotColorField partijId={8} partijNumber="I-2611006" compact /></>);
        await mounted.waitFor(() => one('[data-testid="lot-color-text"]'), 'loaded');
        assert.equal(mounted.container.querySelectorAll('[data-testid="lot-color-text"]').length, 1);
        assert.equal(mounted.container.textContent, 'lot_color_text');
        assert.equal(one('[data-testid="lot-color-field"]'), null);
        assert.ok(!mounted.hasText('lot_color_no_list'));
        assert.ok(!mounted.hasText('lot_color_of'));
        assert.ok(!mounted.hasText('lot_color_hint'));
    });

    test('in the inspection (compact) a saved color without a list shows only that color as short text', async () => {
        await mounted.render(<LotColorField partijId={7} partijNumber="I-2611005" compact initial={{ color: 'Geel', allowedColors: [] }} />);
        assert.equal(one('[data-testid="lot-color-text"]')!.textContent, 'lot_color_text');
        assert.equal(one('select'), null);
        assert.ok(!mounted.hasText('lot_color_no_list'));
        assert.ok(!mounted.hasText('lot_color_hint'));
    });

    test('in the inspection (compact) a product with colors still offers the choice', async () => {
        await mounted.render(<LotColorField partijId={7} partijNumber="I-2611005" compact initial={{ color: null, allowedColors: ['1', '2'] }} />);
        assert.ok(one('[data-tour="lot-color"]'));
        assert.ok(mounted.hasText('lot_color_of'));
    });

    test('on the lot page (not compact) the explanation with Stamgegevens stays, also with a saved color', async () => {
        await mounted.render(<><LotColorField partijId={7} initial={{ color: null, allowedColors: [] }} /><LotColorField partijId={8} initial={{ color: 'Geel', allowedColors: [] }} /></>);
        assert.equal(mounted.container.querySelectorAll('[data-testid="lot-color-field"]').length, 2);
        assert.equal(Array.from(mounted.container.querySelectorAll('p')).filter((p) => p.textContent === 'lot_color_no_list').length, 2);
        assert.equal(one('[data-testid="lot-color-text"]'), null);
    });

    test('the product color list is typed with commas and saved trimmed', async () => {
        answer = (method) => (method === 'PUT' ? json(200, { productId: 3, productName: 'Ananas', colors: ['1', '2', '3'] }) : json(200, { productId: 3, productName: 'Ananas', colors: [] }));
        await mounted.render(<ProductColors productId={3} />);
        await mounted.waitFor(() => one('[data-tour="product-colors"]'), 'loaded');
        await act(async () => { setValue(one('[data-tour="product-colors"]') as HTMLInputElement, ' 1, 2 ,3, '); });
        await act(async () => { one('[data-tour="product-colors-save"]')!.click(); });
        await mounted.waitFor(() => (calls.some((call) => call.method === 'PUT') ? true : null), 'saved');
        const put = calls.find((call) => call.method === 'PUT')!;
        assert.ok(put.url.endsWith('/articles/products/3/colors'));
        assert.deepEqual(JSON.parse(put.body!), { colors: ['1', '2', '3'] });
    });
});
