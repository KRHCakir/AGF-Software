import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import { setInputLocale } from '../../lib/decimal.ts';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// Valuta-ronde: een inkooporder in dollars. De leverancier stelt haar standaardvaluta voor, het scherm haalt de ECB-koers op, prijzen en
// totalen staan in USD met het bedrag in euro eronder, en bij opslaan gaan valuta en bron mee (de server legt de koers vast).

const variant = (id: number, name: string, extra: object) => ({ id, name, origin: 'Peru', packaging: '4 kg box', sizeCategory: '18', shortcutCode: null, packagingUnit: 'box' as const, defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: 252, ...extra });
const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true, defaultCurrency: 'USD' }],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [
        variant(1, 'Hass', { brand: 'Westfalia', shortcutCode: 'avohassper18' }),
        variant(2, 'Kent', { origin: 'Brazilië', defaultPackagesPerPallet: 264, shortcutCode: '2' }),
        variant(3, 'Lime', { defaultPackagesPerPallet: null, defaultGrossKgPerPackage: null, shortcutCode: '3' }),
        variant(4, 'Oud', { packagingUnit: null, shortcutCode: '4' }),
    ] }],
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const cell = (row: number, column: string) => mounted.container.querySelectorAll('tbody tr')[row].querySelector<HTMLElement>(`[data-column="${column}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const key = (id: string, keyName: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true })); });
const pick = async (index: number, code: string) => { await type(`lines[${index}].productVariantId-input`, code); await key(`lines[${index}].productVariantId-input`, 'Enter'); };
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

// Zonder LanguageProvider is de schermtaal Engels: de invoer volgt die taal.
setInputLocale('en');

describe('purchase order in another currency', () => {
    beforeEach(() => {
        posted = stubFetch({
            onGet: (url) => url.includes('/exchange-rates') ? json(200, { currency: 'USD', unitsPerEuro: 1.25, rateDate: '2026-11-18', forDate: '2026-11-18', source: 'ECB' })
                : url.includes('/settings/purchase-currencies') ? json(200, { currencies: [{ code: 'EUR', hasEcbRate: false }, { code: 'USD', hasEcbRate: true }] })
                : json(404, {}),
            nextPost: async () => json(500, { message: 'stop' }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the vendor proposes USD with the ECB rate; totals are in USD with the euro amount; currency and source go along when saving', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await type('vendorId-input', 'supb');
        await key('vendorId-input', 'Enter');
        await mounted.waitFor(() => field('exchangeRate-input')?.value === '1.25', 'ECB rate filled for USD');
        assert.equal(mounted.container.querySelector<HTMLSelectElement>('#currency-input')!.value, 'USD');
        await type('expectedAt-input', '30-10-2026');
        await pick(0, 'avohassper18');
        await type('lines[0].expectedQuantity-input', '100');
        await type('lines[0].unitPrice-input', '10');
        assert.equal(cell(0, 'total').textContent, '1,000.00 USD');
        // Het eurobedrag eronder (1.000 USD / 1,25 = € 800,00; de bedragen zelf rekent documentCurrency.test.ts na).
        assert.ok(mounted.container.querySelector('[data-testid="line-totals-euro"]'), 'euro amount under the total');
        await submit();
        await mounted.waitFor(() => posted.length > 0, 'POST');
        const body = JSON.parse(posted[0].body);
        assert.deepEqual([body.currency, body.exchangeRate, body.exchangeRateSource], ['USD', 1.25, 'ECB']);
    });

    test('a manual rate goes along as Manual; euro sends no rate', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={{ ...reference, vendors: [{ ...reference.vendors[0], defaultCurrency: null }] }} onReceive={() => {}} />);
        await type('vendorId-input', 'supb');
        await key('vendorId-input', 'Enter');
        assert.equal(mounted.container.querySelector<HTMLSelectElement>('#currency-input')!.value, 'EUR');
        assert.equal(field('exchangeRate-input'), null);
        await type('expectedAt-input', '30-10-2026');
        await pick(0, 'avohassper18');
        await type('lines[0].expectedQuantity-input', '100');
        await submit();
        await mounted.waitFor(() => posted.length > 0, 'POST euro');
        assert.deepEqual([JSON.parse(posted[0].body).currency, JSON.parse(posted[0].body).exchangeRate], ['EUR', null]);
    });
});
