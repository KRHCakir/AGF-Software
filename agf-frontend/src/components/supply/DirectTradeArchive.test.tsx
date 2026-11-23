import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, stubFetch } from '../../test/reactHarness.ts';
import DirectTradeArchive from './DirectTradeArchive';

// Feedback 22 november: Direct trade is geen apart onderdeel meer; de klantgebonden aanvoer van vroeger blijft leesbaar (alleen lezen).
const order = { id: 9, requestId: 'r-9', vendorId: 1, vendorName: 'Fictieve Teler', customerId: 4, customerName: 'Fictieve Klant AE', route: 'DirectTrade', status: 'Expected',
    origin: 'Peru', destination: 'Athene', expectedAt: '2026-11-04T06:00:00.000Z', vendorGgns: [], orderNumber: 'IO-2026-0009', supplierReference: null,
    lines: [{ id: 90, productVariantId: 1, productName: 'Mango', productVariantName: 'Kent', lotNumber: null, expectedQuantity: 240, expectedUnit: 'box', expectedNetWeightKg: 960,
        weightMethod: 'Calculated', defaultNetKgPerPackage: 4, palletCount: null, packagesPerPallet: null, defaultPackagesPerPallet: null }] };
let mounted: Mounted;
let gets: string[];
let posted: { url: string }[];

describe('DirectTradeArchive', () => {
    beforeEach(() => {
        gets = [];
        posted = stubFetch({ onGet: (url) => { gets.push(url); return json(200, [order]); }, nextPost: async () => json(500, {}) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('lists the old customer-bound supply read-only and points to a normal purchase order', async () => {
        await mounted.render(<DirectTradeArchive />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="direct-trade-archive-9"]'), 'archive row');
        assert.ok(gets[0].includes('/supply-commitments?route=DirectTrade'));
        assert.ok(mounted.hasText('Fictieve Klant AE'));
        assert.ok(mounted.hasText('240 box'), 'quantity with its unit');
        assert.ok(mounted.container.querySelector('[data-testid="direct-trade-archive-status-9"]'), 'status shown');
        assert.equal(mounted.container.querySelectorAll('button').length, 0, 'read only: nothing to change here');
        assert.ok(mounted.container.querySelector('a[href="/purchase-orders/new"]'));
        assert.equal(posted.length, 0);
    });
});
