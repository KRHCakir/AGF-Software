import '../../../test/setupDom.ts';
import '../../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { navigate } from '../../../test/nextNavigationStub.ts';
import { Mounted, click, json } from '../../../test/reactHarness.ts';
import PurchaseInvoicesPage from './page';

// Valuta-ronde: de facturenlijst toont het totaal in euro (vastgelegde koers) met het bedrag in de factuurvaluta eronder; een oudere
// factuur in dollars zonder vastgelegde koers heeft nog geen eurobedrag. Nooit valuta door elkaar.
const invoice = (id: number, currency: string, totalAmount: number, totalAmountEur: number | null) => ({
    id, requestId: null, vendorId: 1, vendorName: 'DEMO Leverancier', invoiceNumber: `F-${id}`, invoiceDate: '2026-11-19T00:00:00Z', paymentTermsDays: 30, dueDate: null,
    currency, status: 'Draft', subTotal: totalAmount, vatAmount: 0, totalAmount, totalAmountEur, createdAt: '2026-11-19T10:00:00Z', internalNote: null, lines: [],
});

let mounted: Mounted;
const amount = (id: number) => mounted.container.querySelector(`[data-testid="purchase-invoice-row-${id}"] [data-testid="invoice-list-amount"]`)?.textContent ?? '';

describe('invoice list amounts', () => {
    beforeEach(() => {
        navigate('/purchase-invoices');
        window.localStorage.clear();
        globalThis.fetch = (async () => json(200, [invoice(1, 'EUR', 1000, 1000), invoice(2, 'USD', 1000, 800), invoice(3, 'USD', 500, null)])) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('euro first, the invoice currency underneath; no rate yet means no euro amount', async () => {
        await mounted.render(<PurchaseInvoicesPage />);
        await mounted.waitFor(() => amount(3) !== '', 'rows');
        assert.match(amount(1), /€\s?1[.,]000[.,]00$/);
        assert.match(amount(2), /^€\s?800[.,]00.*1[.,]000[.,]00/);
        assert.ok(amount(2).includes('$') || amount(2).includes('USD'), amount(2));
        assert.match(amount(3), /^–.*500[.,]00/);
    });

    // Feedback 22 november: sorteren op de kolomkop; het bedrag sorteert op euro, een factuur zonder eurobedrag staat onderaan.
    test('the total sorts on the euro amount, ascending, descending and back; without a euro amount last', async () => {
        await mounted.render(<PurchaseInvoicesPage />);
        await mounted.waitFor(() => amount(3) !== '', 'rows');
        const order = () => Array.from(mounted.container.querySelectorAll('tbody tr')).map((row) => row.getAttribute('data-testid'));
        const header = () => mounted.container.querySelector<HTMLButtonElement>('[data-sort-key="total"]')!;
        assert.deepEqual(order(), ['purchase-invoice-row-1', 'purchase-invoice-row-2', 'purchase-invoice-row-3']);
        await click(header());
        assert.deepEqual(order(), ['purchase-invoice-row-2', 'purchase-invoice-row-1', 'purchase-invoice-row-3']);
        await click(header());
        assert.deepEqual(order(), ['purchase-invoice-row-1', 'purchase-invoice-row-2', 'purchase-invoice-row-3']);
        assert.equal(header().closest('th')!.getAttribute('aria-sort'), 'descending');
        await click(header());
        assert.equal(header().closest('th')!.getAttribute('aria-sort'), null);
        assert.equal(window.localStorage.getItem('agf_sort_purchase-invoices'), null);
    });
});
