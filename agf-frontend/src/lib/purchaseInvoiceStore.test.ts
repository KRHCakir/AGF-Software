import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { parseDraft, parsePending } from './purchaseInvoiceStore.ts';
import { emptyDraft } from './purchaseInvoiceRules.ts';

const settings = { allOptionalFields: [], optionalFields: [], defaultVatPercentage: 21, defaultPaymentTermsDays: 30, defaultCurrency: 'EUR' } as const;

describe('purchase invoice store guards', () => {
    test('a stored draft round-trips; anything malformed is dropped instead of restoring a broken form', () => {
        const draft = emptyDraft('2026-10-21', { ...settings, allOptionalFields: [], optionalFields: [] });
        assert.deepEqual(parseDraft(JSON.stringify(draft)), draft);
        assert.equal(parseDraft(null), null);
        assert.equal(parseDraft('{'), null);
        assert.equal(parseDraft(JSON.stringify({ ...draft, lines: [{ key: 'x' }] })), null);
        assert.equal(parseDraft(JSON.stringify({ ...draft, touched: { paymentTermsDays: 'yes', currency: false, dueDate: false } })), null);
    });

    test('a pending save is only restored when the frozen payload carries its own requestId', () => {
        const payload = { requestId: 'r-1', vendorId: 7, invoiceNumber: 'GR-1', invoiceDate: '2026-10-21', paymentTermsDays: 30, dueDate: '2026-11-20', currency: 'EUR', lines: [] };
        const pending = { requestId: 'r-1', payload, mode: 'saveAndNew', attempts: 1, lastError: null, frozenAt: '2026-10-21T10:00:00.000Z' };
        assert.deepEqual(parsePending(JSON.stringify(pending)), pending);
        assert.equal(parsePending(JSON.stringify({ ...pending, payload: { ...payload, requestId: 'other' } })), null);
        assert.equal(parsePending(JSON.stringify({ ...pending, mode: 'later' })), null);
    });
});
