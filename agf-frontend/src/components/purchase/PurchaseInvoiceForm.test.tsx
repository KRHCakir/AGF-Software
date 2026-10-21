import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, deferred, flush, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import type { Posted } from '../../test/reactHarness.ts';
import type { ProductOption } from '../../types/referenceData';
import type { PurchaseInvoice, PurchaseInvoiceSettings, PurchaseVendor } from '../../types/purchaseInvoice';
import PurchaseInvoiceForm from './PurchaseInvoiceForm';

// Inkoopfactuurwerkplek door de echte componentlevenscyclus: leveranciersdefaults, wisselen met aangepaste velden,
// vervaldatum, drie regels zonder muis, artikelkeuze zonder opslaan, regel toevoegen/verwijderen met focusherstel,
// uitgeschakelde velden, conceptbehoud bij fouten, dubbele opslag en herstel na verloren antwoord.

const settings: PurchaseInvoiceSettings = {
    allOptionalFields: ['receivedDate', 'paymentReference', 'remarks', 'grossWeightKg', 'netWeightKg', 'containerNumber', 'transporter', 'driverName', 'licensePlate', 'incoterms', 'customsValue', 'originCountry', 'receiptReference'],
    optionalFields: ['remarks', 'receivedDate'],
    defaultVatPercentage: 21, defaultPaymentTermsDays: 30, defaultCurrency: 'EUR',
};
const vendors: PurchaseVendor[] = [
    { id: 7, name: 'The Greenery', vatNumber: 'NL001', paymentTermsDays: 30, defaultCurrency: 'EUR' },
    { id: 12, name: 'Südfrucht Import', vatNumber: null, paymentTermsDays: 14, defaultCurrency: 'USD' },
];
const products: ProductOption[] = [{ id: 1, name: 'Avocado', variants: [
    { id: 1, name: 'Hass', origin: 'Peru', packaging: '4kg Box', sizeCategory: null, shortcutCode: 'avohassper22', defaultPackagesPerPallet: null, packagingUnit: 'box' },
    { id: 2, name: 'Green', origin: 'Spain', packaging: '10kg Crate', sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: null },
] }, { id: 2, name: 'Mango', variants: [{ id: 3, name: 'Kent', origin: 'Brazil', packaging: '4kg Box', sizeCategory: null, shortcutCode: null, defaultPackagesPerPallet: null, packagingUnit: null }] }];

const saved = (over: Partial<PurchaseInvoice> = {}): PurchaseInvoice => ({
    id: 90, requestId: 'r', vendorId: 7, vendorName: 'The Greenery', invoiceNumber: 'GR-1', invoiceDate: '2026-10-21T00:00:00Z', paymentTermsDays: 30, dueDate: '2026-11-20T00:00:00Z',
    currency: 'EUR', status: 'Draft', subTotal: 1000, vatAmount: 210, totalAmount: 1210, createdAt: '2026-10-21T10:00:00Z', internalNote: null, lines: [], ...over,
});

let mounted: Mounted;
let posted: Posted[];
let nextPost: () => Promise<Response>;

const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}-input"]`)!;
const type = async (element: HTMLInputElement | HTMLTextAreaElement, text: string) => { await act(async () => { setValue(element, text); }); };
const key = async (element: HTMLElement, name: string, over: Partial<KeyboardEventInit> = {}) => {
    const event = new window.KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true, ...over });
    await act(async () => { element.dispatchEvent(event); });
    return event.defaultPrevented;
};
const chooseVendor = async (text: string) => { const field = input('vendorId'); await type(field, text); await key(field, 'Tab'); };
const text = () => mounted.container.textContent ?? '';

describe('PurchaseInvoiceForm', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        posted = stubFetch({ onGet: () => json(404, {}), nextPost: () => nextPost() });
        nextPost = async () => json(201, saved());
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('vendor defaults fill the document; a value typed on the document survives a vendor switch; due date follows terms until edited by hand', async () => {
        await mounted.render(<PurchaseInvoiceForm vendors={vendors} products={products} settings={settings} />);
        await chooseVendor('sudfrucht');
        assert.equal(input('vendorId').value, 'Südfrucht Import · #12');
        assert.equal(input('paymentTermsDays').value, '14');
        assert.equal(input('currency').value, 'USD');
        await type(input('paymentTermsDays'), '60');
        await key(input('vendorId'), 'Backspace');
        await chooseVendor('green');
        assert.equal(input('paymentTermsDays').value, '60', 'manual terms kept after switching vendor');
        assert.equal(input('currency').value, 'EUR', 'untouched currency follows the new vendor');
        assert.ok(text().includes('purchase_manual_vs_default'));
        await type(input('invoiceDate'), '2026-10-01');
        assert.equal(input('dueDate').value, '2026-11-30');
        await type(input('dueDate'), '2026-12-15');
        await type(input('paymentTermsDays'), '30');
        assert.equal(input('dueDate').value, '2026-12-15', 'manual due date is never reset silently');
        assert.ok(text().includes('purchase_due_deviation'));
    });

    test('three lines entered with the keyboard only: item, quantity, unit, price, discount, VAT, Enter adds the next line; totals live; no save on item choice', async () => {
        await mounted.render(<PurchaseInvoiceForm vendors={vendors} products={products} settings={settings} />);
        await chooseVendor('#7');
        await type(input('invoiceNumber'), 'GR-2026-1');
        const fill = async (index: number, item: string, quantity: string, price: string, expectVariant: boolean) => {
            const itemInput = input(`lines[${index}].item`);
            itemInput.focus();
            await type(itemInput, item);
            await key(itemInput, 'Tab');
            if (expectVariant) assert.equal(itemInput.readOnly, true, `line ${index} item chosen`);
            await type(input(`lines[${index}].quantity`), quantity);
            await key(input(`lines[${index}].quantity`), 'Enter');
            await act(async () => { input(`lines[${index}].unit`).dispatchEvent(new window.Event('change', { bubbles: true })); });
            await type(input(`lines[${index}].unitPrice`), price);
            await key(input(`lines[${index}].unitPrice`), 'Enter');
            await key(input(`lines[${index}].discountPercentage`), 'Enter');
            const prevented = await key(input(`lines[${index}].vatPercentage`), 'Enter');
            assert.equal(prevented, true, 'Enter on the last cell is handled by the grid');
        };
        await fill(0, 'avohassper22', '80', '12,50', true);
        assert.equal(input('lines[0].unit').value, 'box', 'the packaging unit of the article prefilled the empty unit');
        assert.equal(posted.length, 0, 'choosing an item never saves');
        assert.ok(input('lines[1].item'), 'Enter on the last cell added line 2');
        assert.equal(window.document.activeElement, input('lines[1].item'), 'focus moved to the new line');
        await fill(1, 'Transport', '1', '150', false);
        await fill(2, 'kent', '10', '9,99', true);
        // Zonder LanguageProvider is de taal Engels: en-GB-notatie.
        assert.equal(mounted.container.querySelector('[data-testid="total-net"]')?.textContent, '1,249.90');
        assert.equal(mounted.container.querySelector('[data-testid="total-vat"]')?.textContent, '262.48');
        assert.equal(mounted.container.querySelector('[data-testid="total-gross"]')?.textContent, '1,512.38');
        assert.equal(posted.length, 0, 'nothing saved during entry');
    });

    test('remove and duplicate keep focus logical; a blank trailing line is ignored on save', async () => {
        await mounted.render(<PurchaseInvoiceForm vendors={vendors} products={products} settings={settings} />);
        await chooseVendor('#7');
        await type(input('invoiceNumber'), 'GR-3');
        await type(input('lines[0].item'), 'Kratten'); await key(input('lines[0].item'), 'Tab');
        await type(input('lines[0].quantity'), '2'); await type(input('lines[0].unitPrice'), '5');
        input('lines[0].quantity').focus();
        await key(input('lines[0].quantity'), 'd', { code: 'KeyD', altKey: true });
        assert.ok(input('lines[1].item'), 'duplicated');
        assert.equal(input('lines[1].quantity').value, '2');
        assert.equal(window.document.activeElement, input('lines[1].item'));
        await key(input('lines[1].quantity'), 'r', { code: 'KeyR', altKey: true });
        assert.equal(mounted.container.querySelectorAll('tbody tr').length, 1);
        assert.equal(window.document.activeElement, input('lines[0].item'), 'focus went to the remaining line');
        await key(input('lines[0].vatPercentage'), 'Enter');
        assert.equal(mounted.container.querySelectorAll('tbody tr').length, 2, 'blank trailing line present');
        await key(input('invoiceNumber'), 's', { code: 'KeyS', ctrlKey: true });
        await mounted.waitFor(() => posted.length === 1, 'saved');
        const body = JSON.parse(posted[0].body);
        assert.equal(body.lines.length, 1, 'blank line not sent');
        assert.equal(body.lines[0].description, 'Kratten');
        assert.equal('containerNumber' in body, false);
    });

    test('validation errors stay inline and keep the draft; a lost answer keeps the same requestId and never duplicates; save-and-new only after confirmation', async () => {
        await mounted.render(<PurchaseInvoiceForm vendors={vendors} products={products} settings={settings} />);
        await key(input('invoiceNumber'), 's', { code: 'KeyS', ctrlKey: true });
        assert.equal(posted.length, 0);
        assert.ok(text().includes('purchase_error_required'), 'inline error shown');
        await chooseVendor('#7');
        await type(input('invoiceNumber'), 'GR-4');
        await type(input('lines[0].item'), 'Dozen'); await key(input('lines[0].item'), 'Tab');
        await type(input('lines[0].quantity'), '10'); await type(input('lines[0].unitPrice'), '1');
        await type(input('internalNote'), 'Klant komt morgen langs');
        assert.equal(input('invoiceNumber').value, 'GR-4', 'draft kept after the error');
        assert.equal(input('internalNote').value, 'Klant komt morgen langs', 'internal note kept after the error');
        // Verloren antwoord: de opdracht blijft bevroren met dezelfde requestId.
        const slow = deferred<Response>();
        nextPost = () => slow.promise;
        await key(input('invoiceNumber'), 'Enter', { code: 'Enter', ctrlKey: true });
        await flush();
        assert.equal(posted.length, 1);
        slow.reject(new TypeError('network down'));
        await mounted.waitFor(() => text().includes('supply_error_outcome_unknown'), 'pending notice after the lost answer');
        assert.equal(input('invoiceNumber').value, 'GR-4', 'still the same document, not a new one');
        assert.equal(input('internalNote').value, 'Klant komt morgen langs', 'internal note survives the frozen command');
        assert.equal(JSON.parse(posted[0].body).internalNote, 'Klant komt morgen langs', 'internal note is part of the frozen payload');
        assert.equal(mounted.container.querySelector('fieldset')!.disabled, true, 'form locked while pending');
        nextPost = async () => json(200, saved({ invoiceNumber: 'GR-4' }));
        await act(async () => { mounted.button('supply_retry_same')!.click(); });
        await mounted.waitFor(() => posted.length === 2, 'retried');
        assert.equal(JSON.parse(posted[0].body).requestId, JSON.parse(posted[1].body).requestId, 'identical requestId on retry');
        await mounted.waitFor(() => input('invoiceNumber').value === '', 'new empty document after confirmation');
        assert.ok(text().includes('supply_saved_before'), 'replay note shown');
        assert.equal(window.sessionStorage.getItem('agf_purchase_invoice_pending'), null);
    });

    test('only enabled optional fields are shown and sent; the panel is collapsed by default', async () => {
        await mounted.render(<PurchaseInvoiceForm vendors={vendors} products={products} settings={settings} />);
        assert.equal(mounted.container.querySelector('#purchase-extra'), null, 'collapsed by default');
        await act(async () => { mounted.container.querySelector<HTMLButtonElement>('button[aria-controls="purchase-extra"]')!.click(); });
        assert.ok(mounted.container.querySelector('[id="remarks-input"]'));
        assert.equal(mounted.container.querySelector('[id="containerNumber-input"]'), null, 'disabled field absent');
        await type(mounted.container.querySelector<HTMLTextAreaElement>('[id="remarks-input"]')!, 'Vrijdag');
        await chooseVendor('#7'); await type(input('invoiceNumber'), 'GR-5');
        await type(input('lines[0].item'), 'Dozen'); await key(input('lines[0].item'), 'Tab');
        await type(input('lines[0].quantity'), '1'); await type(input('lines[0].unitPrice'), '1');
        await type(input('internalNote'), ' Klant komt morgen langs ');
        await key(input('invoiceNumber'), 's', { code: 'KeyS', ctrlKey: true });
        await mounted.waitFor(() => posted.length === 1, 'saved');
        const body = JSON.parse(posted[0].body);
        assert.equal(body.remarks, 'Vrijdag');
        assert.equal(body.internalNote, 'Klant komt morgen langs', 'internal note saved with the document, no separate confirmation');
        assert.equal(body.paymentTermsDays, 30);
        assert.equal(body.currency, 'EUR');
        await mounted.waitFor(() => text().includes('purchase_saved_title'), 'success');
        assert.ok(text().includes('purchase_no_stock_effect'));
    });
});
