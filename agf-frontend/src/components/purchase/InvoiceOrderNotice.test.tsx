import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, json, stubFetch } from '../../test/reactHarness.ts';
import InvoiceOrderNotice from './InvoiceOrderNotice';

// Nieuwe inkoopfactuur: hangt het getypte nummer al aan een order, dan "Hoort bij order" met link; anders niets.

let mounted: Mounted;
let gets: string[];
const wait = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 350)); });

describe('InvoiceOrderNotice', () => {
    beforeEach(() => {
        gets = [];
        stubFetch({
            onGet: (url) => {
                gets.push(url);
                return url.includes('invoiceNumber=F-4711') ? json(200, [{ id: 7, orderNumber: 'IO-2026-0001', kind: 'Invoice', vendorName: "Nature's Pride" }]) : json(200, []);
            },
            nextPost: async () => json(500, {}),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('shows the order with a link when the number is known, nothing otherwise', async () => {
        await mounted.render(<InvoiceOrderNotice invoiceNumber=" F-4711 " vendorId={3} />);
        await wait();
        assert.match(gets[0], /\/purchase-orders\/by-invoice-number\?invoiceNumber=F-4711&vendorId=3$/);
        assert.ok(mounted.hasText('po_belongs_to_order'));
        assert.equal(mounted.container.querySelector('a')?.getAttribute('href'), '/purchase-orders?id=7');
        assert.ok(mounted.hasText('IO-2026-0001'));

        await mounted.render(<InvoiceOrderNotice invoiceNumber="F-9999" vendorId={null} />);
        await wait();
        assert.equal(mounted.container.querySelector('[data-testid="invoice-order-notice"]'), null);
        await mounted.render(<InvoiceOrderNotice invoiceNumber="  " vendorId={null} />);
        await wait();
        assert.equal(gets.length, 2);
    });
});
