import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import type { CustomerOption, ProductOption } from '../../types/referenceData';
import CreateSalesOrderForm from './CreateSalesOrderForm';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';

// Verkopen vóór aankomst in het orderformulier: per regel "Voorraad 40 · Verwacht 20 · Vrij 60", de bron volgt het voorstel (uit voorraad
// als het past), 50 met 40 vrij op voorraad stelt splitsen voor (40 uit voorraad + 10 verwacht), 70 geeft een korte tekortmelding maar
// blokkeert niet, en de bron gaat mee in de payload. "= x pallets" staat erachter ter informatie.

const customers: CustomerOption[] = [{ id: 1, name: 'Groothandel G', searchCode: null, isActive: true }];
const products: ProductOption[] = [{ id: 1, name: 'Tomaat', variants: [{ id: 7, name: 'Ronde 67-82', origin: 'NL', packaging: null, sizeCategory: null, shortcutCode: null,
    defaultPackagesPerPallet: 20, packagingUnit: 'colli' as const, defaultNetKgPerPackage: null, defaultGrossKgPerPackage: null }] }];
const q = (quantity: number) => ({ quantity, netWeightKg: 0 });
const breakdown = () => ({ approved: q(0), quarantine: q(0), blocked: q(0), rejected: q(0) });
const overview = {
    generatedAt: '2026-11-03T10:00:00Z', basis: { source: 'x', reservationsApplied: true, legacyInventoryIncluded: false, stockLedgerIncluded: false },
    variants: [{ productVariantId: 7, productName: 'Tomaat', variantName: 'Ronde 67-82', directTradeSupply: [], warehouseSupply: [{
        unit: 'colli', weightBasis: null, availableBeforeReservations: q(40), reserved: { ...q(0), applied: true }, reservedOnHold: q(0), availableAfterReservations: q(40),
        warehouse: breakdown(), receivedOutsideWarehouse: breakdown(), inTransit: breakdown(), expectedOnPallets: breakdown(), expectedUnregistered: { quantity: 20, netWeightKg: null }, warnings: [],
        sales: { productVariantId: 7, unit: 'colli', stock: 40, expected: 20, expectedNotReceived: 20, expectedInInspection: 0, sold: 0, soldReserved: 0, free: 60, freeStock: 40, shortageQuantity: 0, shortage: false },
    }] }],
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
const element = () => (
    <PendingCommandsProvider>
        <HydratedGate>
            <CreateSalesOrderForm customers={customers} products={products} prefill={{ productVariantId: 7, unit: 'colli', quantity: 50 }}
                onSaved={() => {}} onOpen={() => {}} onCancel={() => {}} />
        </HydratedGate>
    </PendingCommandsProvider>
);
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const text = (testId: string) => mounted.container.querySelector(`[data-testid="${testId}"]`)?.textContent ?? '';
const source = (index: number) => mounted.container.querySelector<HTMLInputElement>(`input[name="lines[${index}].supplySource"]:checked`)?.value ?? null;

describe('CreateSalesOrderForm: source per line', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        posted = stubFetch({ onGet: (url) => (url.includes('/availability') ? json(200, overview) : json(404, {})), nextPost: async () => json(500, { message: 'stop' }) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('50 from Verkopen with 40 free in stock: the balance, a split proposal, and after splitting 40 from stock + 10 expected in the payload', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => text('lines[0].balance') !== '', 'balance');
        assert.equal(field('lines[0].quantity-input').value, '50');
        assert.ok(text('lines[0].balance').includes('sales_balance_line'));
        assert.ok(text('lines[0].pallets').includes('sales_pallet_equivalent'), '= 2,5 pallets as information');
        assert.equal(source(0), null, 'only partly free: no source chosen yet, the proposal is shown (the server never gets a 409 for this)');
        assert.ok(text('lines[0].split-proposal').includes('sales_stock_not_enough'));
        assert.equal(mounted.container.querySelector('[data-testid="lines[0].shortage"]'), null, '50 fits in 60: no shortage');

        await click(mounted.container.querySelector<HTMLButtonElement>('[data-tour="sales-line-split"]')!);
        assert.equal(field('lines[0].quantity-input').value, '40');
        assert.equal(field('lines[1].quantity-input').value, '10');
        assert.deepEqual([source(0), source(1)], ['Stock', 'Expected']);
        assert.equal(mounted.container.querySelector('[data-testid="lines[0].split-proposal"]'), null);

        await type('customerId-input', 'Groot');
        await act(async () => { field('customerId-input').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })); });
        await type('requestedDeliveryDate-input', '2026-11-06');
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => posted.length > 0, 'POST');
        const body = JSON.parse(posted[0].body);
        assert.deepEqual(body.lines.map((line: { quantity: number; supplySource: string }) => [line.quantity, line.supplySource]), [[40, 'Stock'], [10, 'Expected']]);
    });

    test('70: a short shortage message that never blocks; choosing "from expected supply" removes the split proposal', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => text('lines[0].balance') !== '', 'balance');
        await type('lines[0].quantity-input', '70');
        assert.ok(text('lines[0].shortage').includes('sales_shortage_warning'));
        const expected = mounted.container.querySelector<HTMLInputElement>('input[name="lines[0].supplySource"][value="Expected"]')!;
        await click(expected);
        assert.equal(source(0), 'Expected');
        assert.equal(mounted.container.querySelector('[data-testid="lines[0].split-proposal"]'), null);
        assert.ok(text('lines[0].shortage').includes('sales_shortage_warning'), 'still a warning, nothing disabled');
        assert.equal(mounted.container.querySelector<HTMLButtonElement>('[data-tour="sales-save"]')?.disabled, false);
    });

    test('saving 50 without choosing: no source is sent, the server decides (from stock only if it fits), never a 409', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => text('lines[0].balance') !== '', 'balance');
        await type('customerId-input', 'Groot');
        await act(async () => { field('customerId-input').dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true })); });
        await type('requestedDeliveryDate-input', '2026-11-06');
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.equal('supplySource' in JSON.parse(posted[0].body).lines[0], false);
    });

    test('30 fits in the free stock: from stock without a proposal', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => text('lines[0].balance') !== '', 'balance');
        await type('lines[0].quantity-input', '30');
        assert.equal(source(0), 'Stock');
        assert.equal(mounted.container.querySelector('[data-testid="lines[0].split-proposal"]'), null);
    });
});
