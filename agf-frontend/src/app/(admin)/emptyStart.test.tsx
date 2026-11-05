import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { navigate } from '../../test/nextNavigationStub.ts';
import { emptyApiResponse } from '../../test/emptyApi.ts';
import { Mounted, json } from '../../test/reactHarness.ts';
import ArticlesPage from './articles/page';
import AvailabilityPage from './availability/page';
import InspectionPage from './inspection/page';
import PartijenPage from './partijen/page';
import PurchaseInvoicesPage from './purchase-invoices/page';
import PurchaseOrdersPage from './purchase-orders/page';
import ReceiptsPage from './receipts/page';
import RelationsPage from './relations/page';
import SalesOrdersPage from './sales-orders/page';

// Schone start: elke lijst op een verse, lege database (de echte lege API-antwoorden) toont de lege toestand met één zin en één knop
// voor de eerste stap. Niets crasht en er staat nergens NaN of undefined. De Beginnen-kaart en de formulieren: firstSteps.test.tsx.
let mounted: Mounted;

const text = () => mounted.container.textContent ?? '';
const found = (testId: string) => mounted.waitFor(() => mounted.container.querySelector(`[data-testid="${testId}"]`), testId);
const linkOf = (element: Element) => element.querySelector('a')?.getAttribute('href');

describe('empty start', () => {
    beforeEach(() => {
        globalThis.fetch = (async (input: RequestInfo | URL) => json(200, emptyApiResponse(String(input)))) as typeof fetch;
        navigate('/');
        mounted = new Mounted();
    });
    afterEach(async () => {
        assert.doesNotMatch(text(), /NaN|undefined/);
        await mounted.unmount();
    });

    test('Relaties: "Nog geen relaties" with a button that opens a new relation', async () => {
        await mounted.render(<RelationsPage />);
        const empty = await found('relations-empty');
        assert.ok(empty.textContent?.includes('relations_empty_text'));
        await act(async () => (empty.querySelector('button') as HTMLButtonElement).click());
        await found('relation-form');
    });

    test('Artikelen: "Nog geen artikelen" with a button, and no cleanup blocks for articles that do not exist', async () => {
        await mounted.render(<ArticlesPage />);
        const empty = await found('articles-empty');
        assert.equal(empty.querySelector('button')?.textContent, 'articles_empty_action');
        assert.ok(!text().includes('size_class_cleanup_title'));
    });

    test('?new=1 (from the Beginnen card or a form that misses something) opens a new relation or article straight away', async () => {
        window.history.replaceState(null, '', '/relations?new=1');
        await mounted.render(<RelationsPage />);
        await found('relation-form');
        await mounted.render(<></>);
        window.history.replaceState(null, '', '/articles?new=1');
        await mounted.render(<ArticlesPage />);
        await found('article-form');
        window.history.replaceState(null, '', '/');
    });

    test('Inkooporders, Partijen and Beschikbaarheid: the first step is Inkooporder aanmaken', async () => {
        for (const [Page, testId] of [[PurchaseOrdersPage, 'po-empty'], [PartijenPage, 'partij-empty'], [AvailabilityPage, 'availability-empty']] as const) {
            await mounted.render(<Page />);
            assert.equal(linkOf(await found(testId)), '/purchase-orders/new', testId);
            await mounted.render(<></>);
        }
    });

    test('Ontvangst: no open purchase order yet, with a link to create one', async () => {
        await mounted.render(<ReceiptsPage />);
        assert.equal(linkOf(await found('receipts-empty')), '/purchase-orders/new');
    });

    test('Keuring: nothing to inspect yet, with a link to Ontvangst', async () => {
        await mounted.render(<InspectionPage />);
        assert.equal(linkOf(await found('inspection-empty')), '/receipts');
    });

    test('Verkooporders: no sales orders yet; the button opens a new order that says a customer and an article are needed first', async () => {
        await mounted.render(<SalesOrdersPage />);
        const empty = await found('sales-empty');
        await act(async () => (empty.querySelector('button') as HTMLButtonElement).click());
        const missing = await found('missing-master-data');
        assert.deepEqual(Array.from(missing.querySelectorAll('li')).map((item) => item.getAttribute('data-testid')), ['missing-customer', 'missing-article']);
    });

    test('Inkoopfacturen: no invoices yet, with Inkoopfactuur invoeren', async () => {
        await mounted.render(<PurchaseInvoicesPage />);
        assert.equal(linkOf(await found('purchase-invoices-empty')), '/purchase-invoices/new');
    });
});
