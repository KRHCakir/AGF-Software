import '../../../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, stubFetch } from '../../../../test/reactHarness.ts';
import { currentUrl, navigate } from '../../../../test/nextNavigationStub.ts';
import PurchaseOrderEditPage from './page';

// Bewerkpagina: een definitieve order opent meteen op de order zelf (alleen-lezen, met de melding en Correctie).

const order = {
    id: 5, orderNumber: 'IO-2026-0005', vendorId: 3, vendorName: 'Supplier B', supplierReference: '45-21', orderedOn: '2026-11-10', orderChannel: null, note: null,
    route: 'Warehouse', customerName: null, origin: '', destination: null, expectedAt: null, status: 'Ordered', storedStatus: 'Expected', invoiceIndicator: 'None',
    expectedQuantity: 20, expectedNetWeightKg: 80, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0, lines: [], receipts: [],
    invoiceRefs: [], rejections: [], history: [], documentStatus: 'Definitive', finalizedAt: '2026-11-12T09:30:00Z', finalizedBy: 'Jan', finalizedVia: 'Manual', editable: false,
};

let mounted: Mounted;

describe('purchase order edit page', () => {
    beforeEach(() => {
        navigate('/purchase-orders/edit?id=5');
        stubFetch({ onGet: (url) => (url.includes('/purchase-orders/5') ? json(200, order) : json(200, [])), nextPost: async () => json(500, {}) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('a definitive order goes straight to the order itself', async () => {
        await mounted.render(<PurchaseOrderEditPage />);
        await mounted.waitFor(() => currentUrl() === '/purchase-orders?id=5', 'replace to the order');
        assert.ok(mounted.container.querySelector('[data-testid="po-edit-definitive"]'), 'the notice shows meanwhile');
    });
});
