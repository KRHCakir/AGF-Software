import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setSelectValue, setValue, stubFetch } from '../../test/reactHarness.ts';
import { PURCHASE_LINE_COLUMNS } from '../purchase/PurchaseLineTable';
import PlanSupplyForm, { type ReferenceData } from './PlanSupplyForm';

// De orderregel van Inkooporder aanmaken (feedback 11 november): alleen Product, Aantal, Prijs en (certificaten deel 2) GGN zijn invoer; merk, land, maat, kg en
// inhoud komen uit het artikel; Tab op het laatste veld maakt een nieuwe regel; totalen staan onder dezelfde kolommen.

const variant = (id: number, name: string, extra: object) => ({ id, name, origin: 'Peru', packaging: '4 kg box', sizeCategory: '18', shortcutCode: null, packagingUnit: 'box' as const, defaultNetKgPerPackage: 4, defaultGrossKgPerPackage: 4.4, defaultPackagesPerPallet: 252, ...extra });
const reference: ReferenceData = {
    vendors: [{ id: 3, name: 'Supplier B', searchCode: 'SUPB', vatNumber: null, isActive: true,
        deliveryTerm: { deliveryTermKind: 'Incoterm', incoterm: 'FCA', incotermPlace: 'Lima', deliveryTermText: null } }],
    customers: [],
    products: [{ id: 1, name: 'Avocado', variants: [
        variant(1, 'Hass', { brand: 'Westfalia', shortcutCode: 'avohassper18', vatPercentage: 9 }),
        variant(2, 'Kent', { origin: 'Brazilië', defaultPackagesPerPallet: 264, shortcutCode: '2' }),
        variant(3, 'Lime', { defaultPackagesPerPallet: null, defaultGrossKgPerPackage: null, shortcutCode: '3' }),
        variant(4, 'Oud', { packagingUnit: null, shortcutCode: '4' }),
    ] }],
};

// De enige keuzelijst op de regel is het btw-tarief; een eenheid kiezen kan niet (fixes 5b).
const UNIT_SELECT = 'tbody select:not([id$="vatPercentage-input"])';

let mounted: Mounted;
let posted: { url: string; body: string }[];
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const cell = (row: number, column: string) => mounted.container.querySelectorAll('tbody tr')[row].querySelector<HTMLElement>(`[data-column="${column}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const key = (id: string, keyName: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true })); });
const pick = async (index: number, code: string) => { await type(`lines[${index}].productVariantId-input`, code); await key(`lines[${index}].productVariantId-input`, 'Enter'); };
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

describe('purchase order lines: the new layout', () => {
    beforeEach(() => {
        posted = stubFetch({ onGet: () => json(404, {}), nextPost: async () => json(500, { message: 'stop' }) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('columns left to right, only product, quantity, price, discount, VAT and GGN are fields; the shortcut finds the article; the rest comes from it', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        const head = Array.from(mounted.container.querySelectorAll('thead th')).map((th) => th.textContent);
        assert.deepEqual(head.slice(0, 12), PURCHASE_LINE_COLUMNS.map((column) => `po_line_col_${column}`));
        await pick(0, 'AVOHASSPER18');
        assert.ok(field('lines[0].productVariantId-input').value.includes('Hass'), 'exact shortcut, case-insensitive');
        await type('lines[0].expectedQuantity-input', '100');
        assert.deepEqual(['brand', 'country', 'size', 'packaging'].map((column) => cell(0, column).textContent), ['Westfalia', 'Peru', '18', '4 kg box']);
        const inputs = Array.from(mounted.container.querySelectorAll<HTMLElement>('tbody tr input, tbody tr select')).filter((input) => input.tabIndex >= 0).map((input) => input.id);
        assert.deepEqual(inputs, ['lines[0].productVariantId-input', 'lines[0].expectedQuantity-input', 'lines[0].unitPrice-input', 'lines[0].discountPercentage-input', 'lines[0].vatPercentage-input', 'lines[0].ggn-input']);
        assert.equal(mounted.container.querySelector(UNIT_SELECT), null, 'unit comes from the article');
        assert.ok(Array.from(mounted.container.querySelectorAll<HTMLButtonElement>('tbody button')).every((button) => button.tabIndex === -1), 'buttons are outside the Tab order');
        assert.equal(cell(0, 'quantity').querySelector('[data-testid="lines[0].pallets"]')!.textContent, 'po_line_pallets');
    });

    test('feedback 22 november: discount lowers the amount, VAT is proposed from the article and can be changed, VAT per rate under the table', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await pick(0, 'AVOHASSPER18');
        await type('lines[0].expectedQuantity-input', '30');
        await type('lines[0].unitPrice-input', '13,35');
        await type('lines[0].discountPercentage-input', '2,5');
        const vat = mounted.container.querySelector<HTMLSelectElement>('[id="lines[0].vatPercentage-input"]')!;
        assert.equal(vat.value, '9', 'proposal from the article');
        assert.equal(cell(0, 'total').textContent, '390.49 EUR');
        const amounts = () => mounted.container.querySelector('[data-testid="order-amounts"]')!;
        assert.equal(amounts().querySelector('[data-testid="order-amounts-net"]')!.textContent, '390.49 EUR');
        assert.ok(amounts().querySelector('[data-testid="order-amounts-vat-9"]'), 'VAT 9% line');
        assert.equal(amounts().querySelector('[data-testid="order-amounts-gross"]')!.textContent, '425.63 EUR');
        await act(async () => { setSelectValue(vat, '21'); });
        assert.ok(amounts().querySelector('[data-testid="order-amounts-vat-21"]'));
        assert.equal(amounts().querySelector('[data-testid="order-amounts-gross"]')!.textContent, '472.49 EUR');
        await type('lines[0].discountPercentage-input', '120');
        await submit();
        assert.ok((mounted.container.textContent ?? '').includes('order_line_discount_range'));
    });

    test('fixes 5b: the unit is text from the article ("200 box"), never a choice', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await pick(0, 'AVOHASSPER18');
        await type('lines[0].expectedQuantity-input', '200');
        assert.equal(mounted.container.querySelector('[data-testid="lines[0].unit"]')!.textContent, 'unit_box');
        assert.equal(mounted.container.querySelector(UNIT_SELECT), null);
    });

    test('fixes 5b: an article without unit gives no choice on the line but a pointer to the article, and saving is blocked', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await pick(0, '4');
        await type('lines[0].expectedQuantity-input', '200');
        assert.equal(mounted.container.querySelector(UNIT_SELECT), null, 'no unit select, also without a unit on the article');
        assert.equal(mounted.container.querySelector('[data-testid="lines[0].unit"]'), null);
        await submit();
        assert.ok((mounted.container.textContent ?? '').includes('po_line_unit_missing'));
        assert.equal(posted.length, 0, 'not saved');
    });

    test('Tab on the GGN of the last line makes a new line with the cursor in its product; the price becomes 12,00; an empty line is not sent', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await type('vendorId-input', 'supb');
        await key('vendorId-input', 'Enter');
        await type('expectedAt-input', '30-10-2026');
        await pick(0, 'avohassper18');
        await type('lines[0].expectedQuantity-input', '100');
        await type('lines[0].unitPrice-input', '12');
        await act(async () => { field('lines[0].unitPrice-input').blur(); });
        assert.equal(field('lines[0].unitPrice-input').value, '12,00');
        await key('lines[0].ggn-input', 'Tab');
        assert.equal(mounted.container.querySelectorAll('tbody tr').length, 2);
        assert.equal(document.activeElement?.id, 'lines[1].productVariantId-input');
        await key('lines[1].ggn-input', 'Tab');
        assert.equal(mounted.container.querySelectorAll('tbody tr').length, 2, 'Tab on an empty last line does not add another');

        await submit();
        await mounted.waitFor(() => posted.length > 0, 'POST');
        const lines = JSON.parse(posted[0].body).lines;
        assert.equal(lines.length, 1);
        assert.deepEqual([lines[0].expectedQuantity, lines[0].expectedUnit, lines[0].unitPrice, lines[0].expectedNetWeightKg, lines[0].expectedGrossWeightKg, lines[0].weightSource], [100, 'box', 12, 400, 440, 'Calculated']);
    });

    test('feedback 22 november: the delivery term follows the supplier until changed and goes along with the new order', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await type('vendorId-input', 'supb');
        await key('vendorId-input', 'Enter');
        assert.equal(field('deliveryTerm.incotermPlace-input').value, 'Lima');
        assert.ok(mounted.container.querySelector('[data-testid="delivery-term-hint"]')!.textContent!.includes('delivery_term_from_vendor'));
        await type('deliveryTerm.incotermPlace-input', 'Callao');
        await type('expectedAt-input', '30-10-2026');
        await pick(0, 'avohassper18');
        await type('lines[0].expectedQuantity-input', '100');
        await submit();
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.deepEqual(JSON.parse(posted[0].body).deliveryTerm, { deliveryTermKind: 'Incoterm', incoterm: 'FCA', incotermPlace: 'Callao', deliveryTermText: null });
    });

    test('totals sit under the same columns; pallets are summed on two decimals; a line without packages per pallet says so', async () => {
        await mounted.render(<PlanSupplyForm route="Warehouse" reference={reference} onReceive={() => {}} />);
        await pick(0, 'avohassper18');
        await type('lines[0].expectedQuantity-input', '100');
        await type('lines[0].unitPrice-input', '12,50');
        await key('lines[0].ggn-input', 'Tab');
        await pick(1, '2');
        await type('lines[1].expectedQuantity-input', '50');
        await key('lines[1].ggn-input', 'Tab');
        await pick(2, '3');
        await type('lines[2].expectedQuantity-input', '10');
        assert.equal(cell(2, 'quantity').querySelector('[data-testid="lines[2].pallets"]')!.textContent, 'po_line_no_packages_per_pallet');
        const foot = mounted.container.querySelector('tfoot')!;
        assert.equal(foot.querySelector('[data-testid="line-totals-quantity"]')!.getAttribute('data-column'), 'quantity');
        assert.equal(foot.querySelector('[data-testid="line-totals-quantity"]')!.textContent, '160');
        assert.equal(foot.querySelector('[data-testid="line-totals-net"]')!.textContent!.startsWith('640 kg'), true);
        assert.equal(foot.querySelector('[data-testid="line-totals-gross"]')!.getAttribute('data-column'), 'gross');
        assert.ok(foot.querySelector('[data-testid="line-totals-amount"]')!.textContent!.startsWith('1,250.00'));
        assert.equal(foot.querySelector('[data-testid="line-totals-pallets"]')!.textContent, 'po_line_pallets_total');
        // Op een telefoon: elke cel draagt haar kolomnaam, de regel wordt een blokje.
        const row = mounted.container.querySelector<HTMLElement>('tbody tr')!;
        assert.ok(row.className.includes('max-md:block'));
        assert.ok(Array.from(row.querySelectorAll('td[data-column]')).every((td) => (td.getAttribute('data-label') ?? '') !== ''));
    });
});
