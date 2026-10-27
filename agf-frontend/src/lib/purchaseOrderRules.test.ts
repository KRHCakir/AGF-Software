import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { invoiceIndicator, isFullyRejected, labelKey, normalizeInvoiceNumber, orderStatus, ORDER_STATUS_LABEL_KEYS } from './purchaseOrderRules.ts';

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
});
