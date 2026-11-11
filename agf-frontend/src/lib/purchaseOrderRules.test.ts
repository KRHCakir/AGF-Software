import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { invoiceIndicator, isFullyRejected, labelKey, lineAmountParts, normalizeInvoiceNumber, orderNumbers, orderStatus, ORDER_STATUS_LABEL_KEYS, supplierReferenceKey } from './purchaseOrderRules.ts';

// Dezelfde gevallen als PurchaseOrderServiceTests in de backend (het ordernummer zelf komt altijd van de server).

describe('purchaseOrderRules', () => {
    test('display status follows received quantities; Delivered counts as received', () => {
        const line = (received: number) => [{ expected: 10, received }];
        assert.equal(orderStatus('Expected', line(0)), 'Ordered');
        assert.equal(orderStatus('Expected', line(4)), 'PartiallyReceived');
        assert.equal(orderStatus('Expected', line(10)), 'Received');
        assert.equal(orderStatus('Expected', line(12)), 'Received');
        assert.equal(orderStatus('Cancelled', line(0)), 'Cancelled');
        assert.equal(orderStatus('Rejected', line(10)), 'Rejected');
        assert.equal(orderStatus('Delivered', line(0)), 'Received');
        assert.equal(isFullyRejected(0, 0), false);
        assert.equal(isFullyRejected(60, 59), false);
        assert.equal(isFullyRejected(60, 60), true);
    });

    test('credit note expected until one is recorded after the rejection', () => {
        const t = (minute: number) => `2026-10-27T10:0${minute}:00Z`;
        assert.equal(invoiceIndicator([], []), 'None');
        assert.equal(invoiceIndicator([{ kind: 'Invoice', createdAt: t(0) }], [{ decision: 'Dispose', rejectedAt: t(0) }]), 'Received');
        assert.equal(invoiceIndicator([{ kind: 'Invoice', createdAt: t(0) }], [{ decision: 'ReturnToSupplier', rejectedAt: t(1) }]), 'CreditNoteExpected');
        assert.equal(invoiceIndicator([{ kind: 'Invoice', createdAt: t(0) }, { kind: 'CreditNote', createdAt: t(2) }], [{ decision: 'ClaimDiscount', rejectedAt: t(1) }]), 'Received');
        assert.equal(invoiceIndicator([{ kind: 'CreditNote', createdAt: t(0) }], [{ decision: 'ClaimDiscount', rejectedAt: t(1) }]), 'CreditNoteExpected');
    });

    test('invoice numbers are trimmed; unknown labels have no key', () => {
        assert.equal(normalizeInvoiceNumber('  F-1 '), 'F-1');
        assert.equal(normalizeInvoiceNumber('   '), null);
        assert.equal(labelKey(ORDER_STATUS_LABEL_KEYS, 'Ordered'), 'po_status_ordered');
        assert.equal(labelKey(ORDER_STATUS_LABEL_KEYS, 'Somewhere'), null);
        assert.equal(labelKey(ORDER_STATUS_LABEL_KEYS, null), null);
    });

    test('order line shows kg next to each amount: ordered, received and rejected', () => {
        // Mango IO-2026-0008: 40 besteld (160 kg), 20 ontvangen (80 kg), 10 afgekeurd (40 kg).
        assert.deepEqual(lineAmountParts({ expectedQuantity: 40, expectedNetWeightKg: 160, receivedQuantity: 20, receivedNetWeightKg: 80, rejectedQuantity: 10, rejectedNetWeightKg: 40 }), [
            { key: 'po_line_ordered', quantity: 40, kg: 160 },
            { key: 'po_line_received', quantity: 20, kg: 80 },
            { key: 'po_line_rejected', quantity: 10, kg: 40 },
        ]);
        assert.deepEqual(lineAmountParts({ expectedQuantity: 5, expectedNetWeightKg: null, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0 }), [
            { key: 'po_line_ordered', quantity: 5, kg: null },
            { key: 'po_line_received', quantity: 0, kg: null },
        ]);
    });

    // Zelfde gevallen als PurchaseOrderSupplierReferenceTests.Key_IgnoresCaseSpacesAndDashes.
    test('supplier order number key ignores case, spaces and dashes; empty is no number', () => {
        assert.equal(supplierReferenceKey('45-21'), '4521');
        assert.equal(supplierReferenceKey(' 45 21 '), '4521');
        assert.equal(supplierReferenceKey('ab-12 x'), 'AB12X');
        assert.equal(supplierReferenceKey(' - '), null);
        assert.equal(supplierReferenceKey(null), null);
    });

    test('main number: the supplier order number with the own IO number as internal number; without it the IO number alone', () => {
        assert.deepEqual(orderNumbers({ orderNumber: 'IO-2026-0001', supplierReference: '45-21' }), { main: '45-21', internal: 'IO-2026-0001' });
        assert.deepEqual(orderNumbers({ orderNumber: 'IO-2026-0001', supplierReference: null }), { main: 'IO-2026-0001', internal: null });
        assert.deepEqual(orderNumbers({ orderNumber: 'IO-2026-0001', supplierReference: '   ' }), { main: 'IO-2026-0001', internal: null });
        assert.deepEqual(orderNumbers({ id: 9, orderNumber: null }), { main: '#9', internal: null });
    });
});
