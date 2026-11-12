import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { purchaseOrderChain } from '../../lib/chain.ts';
import { Mounted } from '../../test/reactHarness.ts';
import type { PurchaseOrderDetail as Order, PurchaseOrderListItem } from '../../types/purchaseOrder';
import PurchaseOrderDetail from './PurchaseOrderDetail';
import PurchaseOrderList from './PurchaseOrderList';

// Stap 4b op de order: de gekoppelde facturen (met link en het verschil met de bon), per regel gefactureerd en open, de badge
// Gefactureerd / Deels / Niet in lijst en detail, en de stap Factuur in de keten-strip. Een oude "Factuur ontvangen" zonder factuur blijft staan.

const order = (over: Partial<Order> = {}): Order => ({
    id: 56, orderNumber: 'IO-2026-0056', vendorId: 3, vendorName: 'Fruitimport Zuid', supplierReference: 'NP-1', orderedOn: '2026-11-10', orderChannel: null, note: null,
    route: 'Warehouse', customerName: null, origin: '', destination: null, expectedAt: null, status: 'Ordered', storedStatus: 'Expected', invoiceIndicator: 'Received',
    expectedQuantity: 264, expectedNetWeightKg: null, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0,
    lines: [{ id: 81, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, expectedQuantity: 264, unit: 'colli', expectedNetWeightKg: null, unitPrice: 13.5,
        receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0, invoicedQuantity: 200, openToInvoiceQuantity: 64 }],
    receipts: [], rejections: [], history: [], salesOrders: [], deviations: [],
    invoiceRefs: [
        { id: 1, kind: 'Invoice', invoiceNumber: 'F-1', invoiceDate: '2026-11-12', amountExclVat: 2500, incomingInvoiceId: 90, actorReference: 'Jan', createdAt: '2026-11-12T10:00:00Z' },
        { id: 2, kind: 'Invoice', invoiceNumber: 'OUD-7', invoiceDate: null, amountExclVat: null, incomingInvoiceId: null, actorReference: 'Jan', createdAt: '2026-11-01T10:00:00Z' },
    ],
    billingStatus: 'Partial', documentStatus: 'Definitive', finalizedVia: 'Invoice',
    invoices: [{ id: 90, invoiceNumber: 'F-1', invoiceDate: '2026-11-12T00:00:00Z', status: 'Draft', currency: 'EUR', amountExclVat: 2500, difference: -200, decision: 'RecordDifference' }],
    ...over,
} as Order);

let mounted: Mounted;
const $ = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);

describe('purchase order invoices', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('the order shows the linked invoice with its difference, the line invoiced and open, and an old reference without invoice', async () => {
        await mounted.render(<PurchaseOrderDetail order={order()} onChanged={() => undefined} />);
        const linked = $('[data-tour="po-linked-invoice"][data-tour-key="F-1"]')!;
        assert.equal(linked.querySelector('a')!.getAttribute('href'), '/purchase-invoices?id=90');
        assert.ok($('[data-testid="po-invoice-difference"]')!.textContent!.includes('po_invoice_difference_recorded'));
        assert.ok($('[data-testid="po-line-invoiced-81"]')!.textContent!.includes('po_line_invoiced'));
        assert.ok($('[data-tour="po-billing-status"][data-tour-key="Partial"]'));
        // De verwijzing van de ingevoerde factuur staat niet dubbel; een oude "Factuur ontvangen" zonder factuur wel.
        assert.deepEqual([...mounted.container.querySelectorAll('[data-tour="po-invoice-ref"]')].map((ref) => ref.getAttribute('data-tour-key')), ['OUD-7']);
    });

    test('the chain strip shows the invoice step with links; the list shows the billing badge', async () => {
        const invoice = purchaseOrderChain(order(), 'order').find((step) => step.key === 'invoice')!;
        assert.deepEqual([invoice.state, invoice.parts[0].key, invoice.links.map((link) => link.href)], ['partial', 'chain_billing_partial', ['/purchase-invoices?id=90']]);
        assert.equal(purchaseOrderChain(order({ billingStatus: 'Full' }), 'order').find((step) => step.key === 'invoice')!.state, 'done');
        assert.equal(purchaseOrderChain(order({ billingStatus: undefined }), 'order').find((step) => step.key === 'invoice')!.state, 'unavailable', 'an older server: not yet');

        const item = (over: Partial<PurchaseOrderListItem>): PurchaseOrderListItem => ({
            id: 56, orderNumber: 'IO-2026-0056', vendorId: 3, vendorName: 'Fruitimport Zuid', supplierReference: 'NP-1', orderedOn: '2026-11-10', expectedAt: null, route: 'Warehouse',
            customerName: null, status: 'Ordered', storedStatus: 'Expected', expectedQuantity: 264, expectedNetWeightKg: null, receivedQuantity: 0, rejectedQuantity: 0,
            invoiceNumbers: [], invoiceIndicator: 'None', documentStatus: 'Open', ...over,
        });
        await mounted.render(<PurchaseOrderList items={[item({ billingStatus: 'None' }), item({ id: 57, orderNumber: 'IO-2026-0057', billingStatus: 'Full' })]} selectedId={null} onOpen={() => {}} />);
        // De lijst staat er als tabel en (smal scherm) als kaarten: elke order heeft haar badge.
        const keys = [...mounted.container.querySelectorAll('[data-tour="po-billing-status"]')].map((badge) => badge.getAttribute('data-tour-key'));
        assert.deepEqual([...new Set(keys)], ['None', 'Full']);
    });
});
