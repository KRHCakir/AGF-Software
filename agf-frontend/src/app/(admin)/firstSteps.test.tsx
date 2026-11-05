import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { navigate } from '../../test/nextNavigationStub.ts';
import { emptyApiResponse } from '../../test/emptyApi.ts';
import { Mounted, json } from '../../test/reactHarness.ts';
import Home from './dashboard/page';
import DirectTradePage from './direct-trade/page';
import NewPurchaseInvoicePage from './purchase-invoices/new/page';
import NewPurchaseOrderPage from './purchase-orders/new/page';
import SalesOrdersPage from './sales-orders/page';

// Schone start, de eerste stappen: op een verse, lege database (de echte lege API-antwoorden) staat de Beginnen-kaart op Start, en een
// formulier dat een leverancier, klant of artikel nodig heeft zegt wat eerst moet en linkt ernaartoe.
let mounted: Mounted;

const found = (testId: string) => mounted.waitFor(() => mounted.container.querySelector(`[data-testid="${testId}"]`), testId);
// Een artikel zoals /api/articles het geeft (de lijst wordt op vorm gecontroleerd).
const article = (id: number, isArchived: boolean) => ({
    id, productId: 1, productName: 'Avocado', variantName: 'Hass', description: 'Avocado · Hass', shortcutCode: null, origin: 'Peru', brand: null,
    sizeCategory: '18', packaging: null, packagingUnit: 'colli', season: null, notes: null, weightMethod: 'Calculated', defaultNetKgPerPackage: 4,
    defaultGrossKgPerPackage: null, defaultPackagesPerPallet: null, goodsCode: null, goodsCodeSystem: null, tradeClass: 'I', defaultPrice: 0, isArchived,
});
const needs = (notice: Element) => Array.from(notice.querySelectorAll('li')).map((item) => item.getAttribute('data-testid'));

describe('first steps on an empty system', () => {
    beforeEach(() => {
        globalThis.fetch = (async (input: RequestInfo | URL) => json(200, emptyApiResponse(String(input)))) as typeof fetch;
        navigate('/');
        mounted = new Mounted();
    });
    afterEach(async () => {
        assert.doesNotMatch(mounted.container.textContent ?? '', /NaN|undefined/);
        await mounted.unmount();
    });

    test('Start: the Beginnen card with relations, articles and the first purchase order, in that order', async () => {
        await mounted.render(<Home />);
        const card = await found('getting-started');
        assert.deepEqual(Array.from(card.querySelectorAll('li a')).map((link) => link.getAttribute('href')), ['/relations?new=1', '/articles?new=1', '/purchase-orders/new']);
        assert.deepEqual(Array.from(card.querySelectorAll('li')).map((item) => item.getAttribute('data-done')), ['no', 'no', 'no']);
    });

    test('Start: no card once there are relations and articles', async () => {
        globalThis.fetch = (async (input: RequestInfo | URL) => {
            const url = String(input);
            if (url.includes('/relations')) return json(200, { items: [], total: 3, page: 1, pageSize: 1 });
            if (url.includes('/articles')) { articlesRead = true; return json(200, [article(1, false)]); }
            return json(200, emptyApiResponse(url));
        }) as typeof fetch;
        let articlesRead = false;
        await mounted.render(<Home />);
        await mounted.waitFor(() => articlesRead, 'articles read');
        for (let i = 0; i < 10; i++) await act(async () => { await Promise.resolve(); });
        assert.equal(mounted.container.querySelector('[data-testid="getting-started"]'), null);
    });

    test('Start: only active relations and non-archived articles count, so the card stays with only archived articles', async () => {
        const urls: string[] = [];
        globalThis.fetch = (async (input: RequestInfo | URL) => {
            const url = String(input);
            urls.push(url);
            if (url.includes('/relations')) return json(200, { items: [], total: 3, page: 1, pageSize: 1 });
            if (url.includes('/articles')) return json(200, [article(1, true)]);
            return json(200, emptyApiResponse(url));
        }) as typeof fetch;
        await mounted.render(<Home />);
        const card = await found('getting-started');
        assert.deepEqual(Array.from(card.querySelectorAll('li')).map((item) => item.getAttribute('data-done')), ['yes', 'no', 'no']);
        assert.ok(urls.some((url) => url.includes('/relations') && url.includes('includeInactive=false')));
    });

    test('Inkooporder aanmaken needs a supplier and an article; Klantgebonden aanvoer also a customer', async () => {
        await mounted.render(<NewPurchaseOrderPage />);
        assert.deepEqual(needs(await found('missing-master-data')), ['missing-vendor', 'missing-article']);
        await mounted.render(<DirectTradePage />);
        await found('missing-customer');
        assert.deepEqual(needs(await found('missing-master-data')), ['missing-vendor', 'missing-customer', 'missing-article']);
    });

    test('A new sales order says a customer and an article are needed first', async () => {
        await mounted.render(<SalesOrdersPage />);
        const create = await mounted.waitFor(() => mounted.container.querySelector<HTMLButtonElement>('[data-tour="sales-new"]'), 'new button');
        await act(async () => create.click());
        assert.deepEqual(needs(await found('missing-master-data')), ['missing-customer', 'missing-article']);
    });

    test('Inkoopfactuur invoeren says a supplier is needed first and links to a new relation', async () => {
        await mounted.render(<NewPurchaseInvoicePage />);
        assert.equal((await found('missing-master-data')).querySelector('a')?.getAttribute('href'), '/relations?new=1');
    });
});
