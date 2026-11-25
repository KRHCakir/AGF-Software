import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { finalizeBlockers, invoiceTitle, selectable, unitLabelKey, SALES_INVOICE_SORT_KEYS } from './salesInvoice.ts';
import type { SalesInvoiceLine, SalesInvoiceListItem } from '../types/salesInvoice';

const line = (unitPrice: number | null) => ({ unitPrice } as unknown as SalesInvoiceLine);
const totals = (linesWithoutPrice: number) => ({ net: 10, vat: 1, gross: 11, vatRates: [], linesWithoutPrice });

describe('sales invoice rules', () => {
    test('a concept needs lines and a price on every line before it is final', () => {
        assert.deepEqual(finalizeBlockers({ status: 'Concept', lines: [line(12.5)], totals: totals(0) }), []);
        assert.deepEqual(finalizeBlockers({ status: 'Concept', lines: [], totals: totals(0) }), ['si_needs_lines']);
        assert.deepEqual(finalizeBlockers({ status: 'Concept', lines: [line(null)], totals: totals(1) }), ['si_needs_prices']);
        assert.deepEqual(finalizeBlockers({ status: 'Final', lines: [line(1)], totals: totals(0) }), ['si_not_concept']);
    });

    test('only priced concepts can be selected, a concept is shown as Concept', () => {
        const item = (over: Partial<SalesInvoiceListItem>) => ({ status: 'Concept', linesWithoutPrice: 0, gross: 10, ...over } as SalesInvoiceListItem);
        assert.equal(selectable(item({})), true);
        assert.equal(selectable(item({ linesWithoutPrice: 1 })), false);
        assert.equal(selectable(item({ status: 'Final' })), false);
        assert.equal(invoiceTitle({ invoiceNumber: null, status: 'Concept' }, 'Concept'), 'Concept');
        assert.equal(invoiceTitle({ invoiceNumber: 'VF-2026-00001', status: 'Final' }, 'Concept'), 'VF-2026-00001');
    });

    test('units and sort keys match the server', () => {
        assert.equal(unitLabelKey('box'), 'si_unit_box');
        assert.equal(unitLabelKey('kist'), null);
        assert.deepEqual([...SALES_INVOICE_SORT_KEYS], ['number', 'customer', 'invoiceDate', 'deliveryDate', 'dueDate', 'lots', 'status', 'net', 'gross']);
    });
});
