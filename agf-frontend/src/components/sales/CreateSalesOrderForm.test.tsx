import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import type { CustomerOption, ProductOption } from '../../types/referenceData';
import CreateSalesOrderForm from './CreateSalesOrderForm';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';

// Kg op de orderregel door de echte componentlevenscyclus: berekend uit het artikel (grijs) en live met het aantal mee,
// zelf ingevuld (zwart) volgt het aantal niet meer, ↺ zet terug; zonder standaard leeg met hint en "= … per colli"; totaal
// onder de regels; de klant kiezen met "Lo" + Tab; geen treffer opent "Nieuwe klant" met de getypte naam.

const customers: CustomerOption[] = [{ id: 1, name: 'Local Market Stalls', searchCode: 'LMS', isActive: true, city: 'Rotterdam' }, { id: 2, name: 'DEMO Klant B', searchCode: null, isActive: true }];
const variant = (id: number, name: string, net: number | null, gross: number | null) =>
    ({ id, name, origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: 'colli' as const, defaultNetKgPerPackage: net, defaultGrossKgPerPackage: gross });
const products: ProductOption[] = [{ id: 1, name: 'Avocado', variants: [variant(1, 'Hass', 10, 10.3), variant(2, 'Green', null, null)] }];

let mounted: Mounted;
let posted: { url: string; body: string }[];

const element = () => (
    <PendingCommandsProvider>
        <HydratedGate>
            <CreateSalesOrderForm customers={customers} products={products} onSaved={() => {}} onOpen={() => {}} onCancel={() => {}} />
        </HydratedGate>
    </PendingCommandsProvider>
);
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const key = async (id: string, name: string) => {
    await act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true })); });
};
const chooseVariant = async (index: number, text: string) => {
    await type(`lines[${index}].productVariantId-input`, text);
    await key(`lines[${index}].productVariantId-input`, 'Enter');
};

describe('CreateSalesOrderForm: kg on the order line and customer search', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        posted = stubFetch({ onGet: () => json(404, { message: 'unexpected GET' }), nextPost: async () => json(500, { message: 'stop' }) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('no customer found: "new customer" opens the panel with the typed text as name', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => mounted.form(), 'order form');
        await type('customerId-input', 'Groentehal Jansen');
        const offer = mounted.container.querySelector<HTMLElement>('[data-testid="customer-no-match"]')!;
        await click(offer.querySelector('button')!);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="new-customer-panel"]'), 'panel');
        assert.equal(field('customer-name-input').value, 'Groentehal Jansen');
    });
});

// Emballage naast de goederen: 110 colli kiwi (120 per pallet) en "Pallet toevoegen" geeft 1 pallet als voorstel; een order met alleen
// emballage gaat zonder goederenregels de deur uit. Niets komt er vanzelf bij.
describe('CreateSalesOrderForm: packaging next to the goods', () => {
    const kiwi: ProductOption[] = [{ id: 5, name: 'Kiwi', variants: [{ ...variant(5, 'Hayward', 10, 10.5), defaultPackagesPerPallet: 120 }] }];
    const pallet = { id: 11, name: 'Pallet wegwerp', kind: 'Packaging', isStandard: true, isActive: true, sortOrder: 110, lineCount: 0, defaultUnitPrice: 7.5, vatPercentage: 9 };
    const kiwiForm = () => (
        <PendingCommandsProvider>
            <HydratedGate>
                <CreateSalesOrderForm customers={customers} products={kiwi} onSaved={() => {}} onOpen={() => {}} onCancel={() => {}} />
            </HydratedGate>
        </PendingCommandsProvider>
    );
    const header = async () => {
        await mounted.waitFor(() => mounted.button('+ packaging_add_pallet'), 'packaging articles loaded');
        await type('customerId-input', 'Lo');
        await key('customerId-input', 'Tab');
        await type('requestedDeliveryDate-input', '2026-11-12');
    };
    const submit = async () => {
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => posted.length > 0, 'POST');
        return JSON.parse(posted[0].body);
    };

    beforeEach(() => {
        window.sessionStorage.clear();
        posted = stubFetch({ onGet: (url) => (url.includes('/packaging-articles') ? json(200, [pallet]) : json(404, { message: 'unexpected GET' })), nextPost: async () => json(500, { message: 'stop' }) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('110 colli kiwi and Add pallet: 1 pallet proposed with its price, and the order carries the goods and the packaging line', async () => {
        await mounted.render(kiwiForm());
        await header();
        await chooseVariant(0, 'hayward');
        await type('lines[0].quantity-input', '110');
        assert.equal(mounted.container.querySelectorAll('[data-tour="sales-packaging-line"]').length, 0, 'nothing added by itself');
        await click(mounted.button('+ packaging_add_pallet')!);
        assert.equal(field('packagingLines[0].quantity-input').value, '1');
        assert.equal(field('packagingLines[0].unitPrice-input').value, '7,50');
        const body = await submit();
        assert.equal(body.lines[0].quantity, 110);
        assert.deepEqual(body.packagingLines, [{ costArticleId: 11, quantity: 1, unitPrice: 7.5 }]);
    });

    test('an order with only packaging (free Euro pallet exchange at 0,00) is sent without goods lines', async () => {
        await mounted.render(kiwiForm());
        await header();
        await click(mounted.button('+ packaging_add_pallet')!);
        await type('packagingLines[0].quantity-input', '4');
        await type('packagingLines[0].unitPrice-input', '0,00');
        const body = await submit();
        assert.deepEqual(body.lines, []);
        assert.deepEqual(body.packagingLines, [{ costArticleId: 11, quantity: 4, unitPrice: 0 }]);
    });

    // Feedback 22 november: "Artikel toevoegen" kiest uit de bestaande artikelen; Enter zet het direct op de regel met de eenheid van het
    // artikel en de cursor op Aantal; kiezen in de regel zelf springt ook naar Aantal.
    test('Enter in the article list puts the article on the line with its unit and the cursor on Aantal', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => mounted.form(), 'order form');
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('[data-tour="sales-article-add"]')!.click(); });
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[data-tour="article-add-search"]')!, 'green'); });
        await act(async () => { mounted.container.querySelector<HTMLInputElement>('[data-tour="article-add-search"]')!.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 5)); });
        assert.ok(field('lines[0].productVariantId-input').value.startsWith('Avocado · Green'));
        assert.equal(window.document.activeElement, field('lines[0].quantity-input'));
        await click(mounted.button('+ supply_add_line')!);
        await chooseVariant(1, 'hass');
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 5)); });
        assert.equal(window.document.activeElement, field('lines[1].quantity-input'));
    });

    // Review #314: Nieuw artikel per regel is ook met alleen het toetsenbord bereikbaar: Alt+A in een veld van de regel.
    test('Alt+A in a line opens the new-article side panel for that line', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => mounted.form(), 'order form');
        await act(async () => {
            field('lines[0].quantity-input').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'a', code: 'KeyA', altKey: true, bubbles: true, cancelable: true }));
        });
        await mounted.waitFor(() => window.document.querySelector('[data-testid="new-article-panel"]'), 'panel open');
    });
});
