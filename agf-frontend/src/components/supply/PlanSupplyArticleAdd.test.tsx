import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { navigate } from '../../test/nextNavigationStub.ts';
import { Mounted, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import LeaveGuardProvider from '../shared/LeaveGuard';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// Feedback 22 november op de inkooporder: "Artikel toevoegen" is een lijst van bestaande artikelen; Enter zet het artikel direct op
// een regel en de cursor op Aantal. "Nieuw artikel" opent een zijpaneel over de order; na opslaan staat het nieuwe artikel op de regel
// waar de gebruiker was en is niets van de order kwijt. Het paneel is geen navigatie: de vraag bij weggaan (fixes 5b) komt niet.
const variant = (id: number, name: string, extra: Record<string, unknown> = {}) => ({
    id, name, origin: 'Peru', originCountryCode: 'PE', packaging: '4 kg doos', sizeCategory: '18', shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: 'colli' as const,
    tradeClass: 'I', brand: null, defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: null, ...extra,
});
const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true }],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [variant(10, 'Hass', { shortcutCode: 'avohass' }), variant(11, 'Fuerte')] }],
};
const countries = { countries: [{ code: 'CN', nameNl: 'China', nameEn: 'China' }, { code: 'PE', nameNl: 'Peru', nameEn: 'Peru' }], aliases: [] };
const articleReference = { productNames: ['Avocado'], packagingUnits: ['box', 'colli', 'tray', 'piece', 'dozen'], weightMethods: ['Weighed', 'Calculated'], goodsCodeSystems: ['HS', 'CN', 'TARIC'], tradeClassSuggestions: ['Extra', 'I', 'II'] };
const ginger = {
    id: 50, productId: 2, productName: 'Gember', variantName: '', description: 'Gember · China', shortcutCode: null, origin: 'China', originCountryCode: 'CN', brand: null,
    sizeCategory: null, packaging: null, packagingUnit: 'colli', season: null, notes: null, weightMethod: 'Weighed', defaultNetKgPerPackage: 5, defaultGrossKgPerPackage: null,
    defaultPackagesPerPallet: null, goodsCode: null, goodsCodeSystem: null, tradeClass: 'I', defaultPrice: 0, vatPercentage: 9, isArchived: false,
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
const field = (id: string) => window.document.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const tour = (name: string, tourKey?: number) => window.document.querySelector<HTMLElement>(`[data-tour="${name}"]${tourKey === undefined ? '' : `[data-tour-key="${tourKey}"]`}`)!;
const key = async (element: HTMLElement, name: string) => { await act(async () => { element.dispatchEvent(new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })); }); };
const type = async (id: string, text: string) => { await act(async () => { field(id).focus(); setValue(field(id), text); }); };
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 5)); });

describe('PlanSupplyForm: article from the list and a new article in a side panel', () => {
    beforeEach(async () => {
        navigate('/purchase-orders/new');
        window.history.replaceState(null, '', '/purchase-orders/new');
        posted = stubFetch({
            onGet: (url) => (url.includes('/countries') ? json(200, countries) : url.includes('/articles/reference') ? json(200, articleReference) : json(404, {})),
            nextPost: async () => json(201, ginger),
        });
        mounted = new Mounted();
        await mounted.render(
            <LeaveGuardProvider>
                <nav><a href="/availability">Beschikbaarheid</a></nav>
                <PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />
            </LeaveGuardProvider>,
        );
    });
    afterEach(async () => { await mounted.unmount(); });

    test('Enter in the list puts the article on the empty line at once and the cursor on Aantal; the next one gets a new line', async () => {
        await act(async () => { tour('supply-article-add').click(); });
        await act(async () => { setValue(tour('article-add-search') as HTMLInputElement, 'avohass'); });
        await key(tour('article-add-search'), 'Enter');
        await settle();
        assert.equal(field('lines[0].productVariantId-input').value, 'Avocado · Hass · 4 kg doos · Peru · 18');
        assert.equal(window.document.activeElement, field('lines[0].expectedQuantity-input'), 'cursor on Aantal');
        await type('lines[0].expectedQuantity-input', '10');
        await act(async () => { tour('supply-article-add').click(); });
        await act(async () => { setValue(tour('article-add-search') as HTMLInputElement, 'fuerte'); });
        await key(tour('article-add-search'), 'Enter');
        await settle();
        assert.ok(field('lines[1].productVariantId-input').value.startsWith('Avocado · Fuerte'));
        assert.equal(window.document.activeElement, field('lines[1].expectedQuantity-input'));
        assert.equal(field('lines[0].expectedQuantity-input').value, '10', 'the first line is untouched');
    });

    test('choosing in the line itself also jumps to Aantal', async () => {
        await type('lines[0].productVariantId-input', 'avohass');
        await key(field('lines[0].productVariantId-input'), 'Enter');
        await settle();
        assert.equal(window.document.activeElement, field('lines[0].expectedQuantity-input'));
    });

    // Met Tab zet de browser de cursor zelf verder; een keuze die later binnenkomt haalt hem nooit terug (e2e: toetsenbord alleen).
    test('a pick that arrives after the user moved on does not pull the cursor back to Aantal', async () => {
        await type('lines[0].productVariantId-input', 'avohass');
        await act(async () => {
            field('lines[0].productVariantId-input').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
            field('lines[0].unitPrice-input').focus();
        });
        await settle();
        assert.equal(window.document.activeElement, field('lines[0].unitPrice-input'));
    });

    test('a new article from the side panel lands on its line; the order keeps everything and no leave question comes', async () => {
        await type('vendorId-input', 'supb');
        await key(field('vendorId-input'), 'Enter');
        await type('supplierReference-input', 'NP-1');
        await type('expectedAt-input', '2026-11-30');
        // Een geldige regel: zou het opslaan in het paneel ook de order versturen (een submit bubbelt in React door een portal), dan zag
        // deze test een tweede POST.
        await type('lines[0].productVariantId-input', 'avohass');
        await key(field('lines[0].productVariantId-input'), 'Enter');
        await type('lines[0].expectedQuantity-input', '10');
        await act(async () => { (tour('supply-line-new-article', 0)).click(); });
        await mounted.waitFor(() => window.document.querySelector('[data-testid="new-article-panel"] [id="productName-input"]'), 'panel open');
        await type('productName-input', 'Gember');
        await type('originCountryCode-input', 'CN');
        await key(field('originCountryCode-input'), 'Enter');
        await type('tradeClass-input', 'I');
        await key(field('tradeClass-input'), 'Enter');
        await act(async () => { window.document.querySelector<HTMLFormElement>('[data-testid="new-article-panel"] form')!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => window.document.querySelector('[data-testid="new-article-panel"]') === null, 'panel closed after saving');
        await settle();
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });
        assert.deepEqual(posted.map((call) => call.url.replace(/^.*\/api/, '')), ['/articles'], 'only the article is saved, never the order');
        assert.equal(field('lines[0].productVariantId-input').value, 'Gember · China', 'the new article is on the line');
        assert.equal(field('lines[0].expectedQuantity-input').value, '10', 'the quantity of the line stays');
        assert.equal(window.document.activeElement, field('lines[0].expectedQuantity-input'));
        assert.equal(window.document.querySelector('[data-tour="supply-order-number"]'), null, 'the order is not saved');
        assert.equal(field('supplierReference-input').value, 'NP-1', 'nothing of the order is lost');
        assert.ok(field('vendorId-input').value.includes('Supplier B'));
        assert.equal(window.document.querySelector('[data-testid="leave-guard"]'), null, 'the panel is no navigation');
        // De order zelf is nog niet opgeslagen: echt weggaan vraagt nog steeds eerst.
        await act(async () => { window.document.querySelector<HTMLAnchorElement>('a[href="/availability"]')!.dispatchEvent(new window.MouseEvent('click', { bubbles: true, cancelable: true, button: 0 })); });
        assert.ok(window.document.querySelector('[data-testid="leave-guard"]'));
    });

    test('Escape on an untouched panel closes it and the order stays as it was', async () => {
        await type('supplierReference-input', 'NP-2');
        await act(async () => { tour('supply-article-add').click(); });
        await act(async () => { tour('article-add-new').click(); });
        await mounted.waitFor(() => window.document.querySelector('[data-testid="new-article-panel"] [id="productName-input"]'), 'panel open');
        await key(field('productName-input'), 'Escape');
        assert.equal(window.document.querySelector('[data-testid="new-article-panel"]'), null);
        assert.equal(field('supplierReference-input').value, 'NP-2');
        assert.equal(posted.length, 0);
    });
});
