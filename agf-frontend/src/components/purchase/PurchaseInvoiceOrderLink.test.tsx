import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { setInputLocale } from '../../lib/decimal.ts';
import { candidate } from '../../test/invoiceCandidate.ts';
import { Mounted, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import type { Posted } from '../../test/reactHarness.ts';
import type { PurchaseInvoiceSettings, PurchaseVendor } from '../../types/purchaseInvoice';
import type { ProductOption } from '../../types/referenceData';
import PurchaseInvoiceForm from './PurchaseInvoiceForm';

// Stap 4b in Inkoopfactuur invoeren: de open orders van de leverancier, regels overnemen uit de bon, het verschil in kleur en de vraag
// eenmaal bij opslaan. Regressie: "Hoort bij order …" bij een bekend factuurnummer bleef een melding; opslaan legde niets vast.

setInputLocale('nl');
const settings: PurchaseInvoiceSettings = { allOptionalFields: [], optionalFields: [], defaultVatPercentage: 9, defaultPaymentTermsDays: 30, defaultCurrency: 'EUR' };
const vendors: PurchaseVendor[] = [{ id: 3, name: 'Fruitimport Zuid', vatNumber: null, paymentTermsDays: 30, defaultCurrency: 'EUR', searchCode: null, isActive: true,
    paymentConditionId: 1, paymentConditionCode: '30D', paymentConditionBasis: 'InvoiceDate', hasDefaultBankAccount: true, defaultBankAccountUnconfirmed: false }];
const products: ProductOption[] = [{ id: 1, name: 'Avocado', variants: [{ id: 1, name: 'Hass', origin: 'Peru', packaging: null, sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: 'colli' }] }];

let mounted: Mounted;
let posted: Posted[];
let gets: string[];
const $ = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const type = (element: HTMLInputElement, text: string) => act(async () => { element.focus(); setValue(element, text); });
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 350)); });
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

describe('PurchaseInvoiceForm: linking to the purchase order', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        gets = [];
        posted = stubFetch({
            onGet: (url) => {
                gets.push(url);
                if (url.includes('/purchase-orders/invoice-candidates')) return json(200, [candidate()]);
                if (url.includes('/purchase-orders/by-invoice-number')) return json(200, [{ id: 56, orderNumber: 'IO-2026-0056', supplierReference: 'NP-1', kind: 'Invoice', vendorName: 'Fruitimport Zuid' }]);
                return json(404, {});
            },
            nextPost: async () => json(201, { id: 90, requestId: 'r', vendorId: 3, vendorName: 'Fruitimport Zuid', invoiceNumber: 'F-1', invoiceDate: '2026-11-12T00:00:00Z', paymentTermsDays: 30,
                dueDate: '2026-12-12T00:00:00Z', currency: 'EUR', status: 'Draft', subTotal: 3300, vatAmount: 297, totalAmount: 3597, createdAt: '2026-11-12T10:00:00Z', internalNote: null, lines: [] }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    const start = async () => {
        await mounted.render(<PurchaseInvoiceForm vendors={vendors} products={products} settings={settings} />);
        await type(field('vendorId'), 'fruitimport');
        await act(async () => { field('vendorId').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })); });
        await settle();
        await type(field('invoiceNumber'), 'F-1');
    };

    test('the open orders of the supplier fill the lines from the order; a price difference shows in colour and asks once on saving', async () => {
        await start();
        assert.ok(gets.some((url) => url.includes('invoice-candidates?vendorId=3')));
        await act(async () => { $('[data-tour="pinvoice-order-add"]')!.click(); });
        assert.deepEqual([field('lines[0].quantity').value, field('lines[0].unitPrice').value], ['264', '13,50']);
        assert.equal(field('lines[0].item').disabled, true, 'the article comes from the order line');
        assert.ok($('[data-testid="lines[0].order-info"]')!.textContent!.includes('invoice_link_on_order'));
        assert.equal($('[data-testid="lines[0].order-difference"]'), null);

        await type(field('lines[0].unitPrice'), '12,50');
        assert.ok($('[data-testid="lines[0].order-difference"]')!.textContent!.includes('invoice_link_difference'));

        await submit();
        assert.ok($('[data-testid="invoice-price-question"]'), 'the question comes first');
        assert.equal(posted.length, 0);
        await act(async () => { $('[data-tour="pinvoice-record-difference"]')!.click(); });
        await mounted.waitFor(() => posted.length === 1, 'saved');
        const body = JSON.parse(posted[0].body);
        assert.deepEqual([body.orderPriceDecision, body.lines[0].supplyCommitmentLineId, body.lines[0].unitPrice, body.lines[0].quantity], ['RecordDifference', 81, 12.5, 264]);
        assert.equal(body.acceptOverInvoicing, undefined);
    });

    test('the same price saves without a question; more than ordered asks to confirm', async () => {
        await start();
        await act(async () => { $('[data-tour="pinvoice-order-add"]')!.click(); });
        await type(field('lines[0].quantity'), '300');
        assert.ok($('[data-testid="lines[0].order-over"]'));
        await submit();
        assert.ok($('[data-testid="invoice-question-over"]'));
        await act(async () => { $('[data-tour="pinvoice-accept-over"]')!.click(); });
        await mounted.waitFor(() => posted.length === 1, 'saved');
        const body = JSON.parse(posted[0].body);
        assert.deepEqual([body.acceptOverInvoicing, body.orderPriceDecision], [true, undefined]);
    });

    test('a known invoice number on an order now takes over its lines, so the link is saved (it used to stay a notice)', async () => {
        await start();
        await settle();
        assert.ok($('[data-testid="invoice-order-notice"]'));
        await act(async () => { $('[data-testid="invoice-order-notice-use"]')!.click(); });
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="lines[0].order-info"]') !== null, 'lines from the order');
        assert.ok(gets.some((url) => url.includes('invoice-candidates?vendorId=3&orderId=56')));
        await submit();
        await mounted.waitFor(() => posted.length === 1, 'saved');
        assert.equal(JSON.parse(posted[0].body).lines[0].supplyCommitmentLineId, 81);
    });
});
