import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setValue } from '../../test/reactHarness.ts';
import type { InvoiceAllocations, InvoiceLineAllocation } from '../../types/partijCost';
import AllocationPanel from './AllocationPanel';
import PurchaseInvoiceDetail from './PurchaseInvoiceDetail';

// Een factuur in USD (feedback 8 november): het scherm laat de server bij het openen de ECB-koers ophalen (nooit getypt), verdeelt in
// euro tot het omgerekende regelbedrag en toont bij elk bedrag het bedrag in USD. Zonder koers kan verdelen niet; is de ECB even niet
// bereikbaar, dan kun je opnieuw laten ophalen. Een factuur in euro vraagt nooit om een koers.

const line = (over: Partial<InvoiceLineAllocation> = {}): InvoiceLineAllocation => ({
    lineId: 41, description: 'Zeevracht', kind: 'Zeevracht', lineTotal: 5000, lineTotalEur: 4478.28, allocated: 0, general: 4478.28, version: 1, allocations: [], history: [], ...over,
});
const allocations = (over: Partial<InvoiceAllocations> = {}): InvoiceAllocations => ({
    invoiceId: 9, currency: 'USD', exchangeRate: 1.1165, exchangeRateDate: '2026-11-05', exchangeRateSource: 'ECB', rateLocked: false, canAllocate: true, lines: [line()], ...over,
});
const withoutRate = () => allocations({ exchangeRate: null, exchangeRateDate: null, exchangeRateSource: null, canAllocate: false, lines: [line({ lineTotalEur: null, general: null, version: 0 })] });
const invoice = (currency: string) => ({
    id: 9, requestId: null, vendorId: 1, vendorName: 'Maersk', invoiceNumber: 'MAERSK-1', invoiceDate: '2026-11-05T00:00:00Z', paymentTermsDays: 30, dueDate: null,
    currency, status: 'Draft', subTotal: 5000, vatAmount: 0, totalAmount: 5000, createdAt: '2026-11-05T10:00:00Z', internalNote: null, lines: [],
});

const lot = { id: 1, number: '261140', kind: 'Sales', salesOrderId: 101, salesOrderNumber: 'VO-2026-0001', salesOrderStatus: 'Open', customerName: 'Groothandel Jansen',
    orderedQuantity: 500, sourcePartijen: [], status: 'Open', createdAt: '2026-11-05T09:00:00Z', vendors: [], products: ['Avocado Hass'], supplierLotReferences: [], unit: 'colli',
    purchasedQuantity: 0, receivedQuantity: 0, onHandQuantity: 0, inInspectionQuantity: 0, openExpectedQuantity: 0, soldQuantity: 0, netKg: 2000 };

let mounted: Mounted;
let calls: { method: string; url: string }[];
let answers: Record<string, () => Response>;

const tour = (name: string, key?: string) => mounted.container.querySelector<HTMLElement>(`[data-tour="${name}"]${key === undefined ? '' : `[data-tour-key="${key}"]`}`);
const testId = (id: string) => mounted.container.querySelector<HTMLElement>(`[data-testid="${id}"]`);
const click = (element: HTMLElement | null) => act(async () => { element!.click(); });
const posts = () => calls.filter((call) => call.method === 'POST');

describe('invoice in another currency', () => {
    beforeEach(() => {
        calls = [];
        answers = {};
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            const method = init?.method ?? 'GET';
            const url = String(input);
            calls.push({ method, url });
            const path = new URL(url).pathname.replace(/^\/api/, '');
            const answer = answers[`${method} ${path}`];
            if (answer) return answer();
            if (path === '/partijen') return json(200, { total: 1, page: 1, pageSize: 8, items: [lot] });
            return json(404, { message: 'not stubbed' });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('opening a USD invoice fetches the ECB rate through the server and shows it with the line in euro', async () => {
        answers['GET /purchase-invoices/9'] = () => json(200, invoice('USD'));
        answers['GET /purchase-invoices/9/allocations'] = () => json(200, withoutRate());
        answers['POST /purchase-invoices/9/exchange-rate'] = () => json(200, allocations());
        await mounted.render(<PurchaseInvoiceDetail invoiceId={9} onClose={() => {}} />);
        await mounted.waitFor(() => testId('invoice-line-eur') !== null, 'line in euro after the rate');
        assert.equal(posts().length, 1);
        assert.ok(posts()[0].url.endsWith('/purchase-invoices/9/exchange-rate'));
        assert.ok(testId('invoice-exchange-rate')!.textContent!.includes('exchange_rate_line'));
        assert.ok(testId('invoice-exchange-rate')!.textContent!.includes('exchange_rate_live'), 'says it is fetched again until costs are allocated');
        assert.match(testId('invoice-line-eur')!.textContent!, /4[.,]478[.,]28/);
        assert.equal(mounted.container.querySelector('input[name="line-41-mode"]')!.hasAttribute('disabled'), false, 'allocating is possible');
    });

    test('ECB unreachable: allocating waits, and "fetch again" asks the server again', async () => {
        answers['GET /purchase-invoices/9'] = () => json(200, invoice('USD'));
        answers['GET /purchase-invoices/9/allocations'] = () => json(200, withoutRate());
        answers['POST /purchase-invoices/9/exchange-rate'] = () => json(503, { message: 'The ECB exchange rates could not be reached.' });
        await mounted.render(<PurchaseInvoiceDetail invoiceId={9} onClose={() => {}} />);
        await mounted.waitFor(() => mounted.hasText('exchange_rate_unavailable'), 'unavailable notice');
        assert.ok(testId('line-41-no-rate'), 'no rate: the panel says why it cannot allocate');
        assert.equal(testId('line-41-summary'), null, 'no "0,00 of 0,00" without a rate');
        assert.ok(mounted.container.querySelector<HTMLButtonElement>('[data-tour="allocation-save"]')!.disabled);
        answers['POST /purchase-invoices/9/exchange-rate'] = () => json(200, allocations());
        await click(mounted.buttons().find((button) => button.textContent === 'exchange_rate_retry')!);
        await mounted.waitFor(() => testId('invoice-line-eur') !== null, 'rate after retry');
        assert.equal(posts().length, 2);
        assert.equal(testId('line-41-no-rate'), null);
    });

    test('no current ECB rate for the currency: said plainly, everything stays general costs', async () => {
        answers['GET /purchase-invoices/9'] = () => json(200, invoice('MAD'));
        answers['GET /purchase-invoices/9/allocations'] = () => json(200, { ...withoutRate(), currency: 'MAD' });
        answers['POST /purchase-invoices/9/exchange-rate'] = () => json(400, { errors: { currency: ['The ECB publishes no current exchange rate for MAD on 2026-11-05.'] } });
        await mounted.render(<PurchaseInvoiceDetail invoiceId={9} onClose={() => {}} />);
        await mounted.waitFor(() => mounted.hasText('exchange_rate_none'), 'no rate notice');
        assert.equal(mounted.buttons().some((button) => button.textContent === 'exchange_rate_retry'), false);
    });

    test('a fixed rate (costs allocated) and a euro invoice are never fetched again', async () => {
        answers['GET /purchase-invoices/9'] = () => json(200, invoice('USD'));
        answers['GET /purchase-invoices/9/allocations'] = () => json(200, allocations({ rateLocked: true }));
        await mounted.render(<PurchaseInvoiceDetail invoiceId={9} onClose={() => {}} />);
        await mounted.waitFor(() => testId('invoice-exchange-rate') !== null, 'rate shown');
        assert.ok(testId('invoice-exchange-rate')!.textContent!.includes('exchange_rate_locked'));
        await mounted.unmount();
        mounted = new Mounted();
        answers['GET /purchase-invoices/9'] = () => json(200, invoice('EUR'));
        answers['GET /purchase-invoices/9/allocations'] = () => json(200, allocations({ currency: 'EUR', exchangeRate: null, exchangeRateDate: null, exchangeRateSource: null, lines: [line({ lineTotal: 5000, lineTotalEur: 5000, general: 5000 })] }));
        await mounted.render(<PurchaseInvoiceDetail invoiceId={9} onClose={() => {}} />);
        await mounted.waitFor(() => mounted.hasText('allocation_question'), 'loaded');
        assert.equal(posts().length, 0);
        assert.equal(testId('invoice-exchange-rate'), null);
    });

    test('the panel allocates in euro up to the converted line and shows the USD amount next to each amount', async () => {
        await mounted.render(<AllocationPanel invoiceId={9} line={line()} canAllocate currency="USD" exchangeRate={1.1165} onSaved={() => {}} onReload={() => {}} />);
        await click(tour('allocation-mode', 'Sales'));
        await mounted.waitFor(() => tour('allocation-pick', '261140') !== null, 'lot found');
        await click(tour('allocation-pick', '261140'));
        const amount = mounted.container.querySelector<HTMLInputElement>('#line-41-amount-1')!;
        await act(async () => { setValue(amount, '500'); });
        assert.ok(testId('line-41-in-currency-1')!.textContent!.includes('allocation_in_currency'));
        await act(async () => { setValue(amount, '4478,29'); });
        assert.ok(testId('line-41-over'), 'more than 4.478,28 EUR is refused next to Save');
        await act(async () => { setValue(amount, '4478,28'); });
        assert.equal(testId('line-41-over'), null, 'exactly the converted line amount is fine');
    });
});
