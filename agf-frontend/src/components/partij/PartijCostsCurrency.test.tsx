import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json } from '../../test/reactHarness.ts';
import type { PartijDetail } from '../../types/partij';
import PartijCostsSection from './PartijCostsSection';

// Op de partij: het eurobedrag met het bedrag in USD en de koers eronder. (De koers tijdens het invoeren van een factuur staat sinds de
// valuta-ronde in ExchangeRateField, getest in DocumentCurrencyField.test.tsx.)

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
