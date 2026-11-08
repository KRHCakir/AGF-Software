import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json } from '../../test/reactHarness.ts';
import type { PartijDetail } from '../../types/partij';
import PartijCostsSection from '../partij/PartijCostsSection';
import ExchangeRatePreview from './ExchangeRatePreview';

// Tijdens het invoeren van een factuur in USD: de ECB-koers voor de factuurdatum als informatie (geen invoerveld); bij EUR niets.
// Op de partij: het eurobedrag met het bedrag in USD en de koers eronder.

let mounted: Mounted;
let urls: string[];

describe('ECB rate while entering an invoice', () => {
    beforeEach(() => {
        urls = [];
        globalThis.fetch = (async (input: RequestInfo | URL) => {
            urls.push(String(input));
            return json(200, { currency: 'USD', unitsPerEuro: 1.1224, rateDate: '2026-11-05', forDate: '2026-11-05', source: 'ECB' });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('USD with an invoice date: asks the server for the rate of that date and shows it, without a field to type a rate', async () => {
        await mounted.render(<ExchangeRatePreview currency="usd" invoiceDate="2026-11-05" net={5000} />);
        await act(async () => { await new Promise((resolve) => setTimeout(resolve, 350)); });
        await mounted.waitFor(() => mounted.hasText('exchange_rate_preview'), 'rate shown');
        assert.equal(urls.length, 1);
        const url = new URL(urls[0]);
        assert.equal(url.pathname.replace(/^\/api/, ''), '/exchange-rates');
        assert.deepEqual([url.searchParams.get('currency'), url.searchParams.get('date')], ['USD', '2026-11-05']);
        assert.ok(mounted.hasText('exchange_rate_preview_hint'));
        assert.equal(mounted.container.querySelector('input'), null);
    });

    test('EUR (or a half-typed code) shows nothing and asks nothing', async () => {
        await mounted.render(<ExchangeRatePreview currency="EUR" invoiceDate="2026-11-05" net={5000} />);
        await mounted.render(<ExchangeRatePreview currency="US" invoiceDate="2026-11-05" net={5000} />);
        await new Promise((resolve) => setTimeout(resolve, 350));
        assert.equal(mounted.container.querySelector('[data-testid="exchange-rate-preview"]'), null);
        assert.equal(urls.length, 0);
    });

    test('a cost from a USD invoice on a lot shows the USD amount and the rate under the euro amount', async () => {
        const partij = {
            kind: 'Sales', costTotal: 500, linkedCosts: [],
            costs: [{ kind: 'Zeevracht', invoiceId: 9, invoiceNumber: 'MAERSK-1', vendorName: 'Maersk', invoiceDate: '2026-11-05T00:00:00Z', description: 'Zeevracht',
                amount: 500, currency: 'USD', amountInCurrency: 558.25, exchangeRate: 1.1165 }],
        } as unknown as PartijDetail;
        await mounted.render(<PartijCostsSection partij={partij} />);
        assert.ok(mounted.container.querySelector('[data-testid="partij-cost-in-currency"]')!.textContent!.includes('partij_cost_in_currency'));
    });
});
