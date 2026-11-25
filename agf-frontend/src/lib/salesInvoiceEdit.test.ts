import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { buildLineUpdates, draftOf } from './salesInvoiceEdit.ts';
import type { SalesInvoiceLine } from '../types/salesInvoice';

const line = (over: Partial<SalesInvoiceLine> = {}) => ({ id: 41, lineKind: 'Product', quantity: 30, unitPrice: 12.5, discountPercentage: 10, vatPercentage: 9, maxQuantity: 30, ...over } as SalesInvoiceLine);

describe('editing a sales invoice draft', () => {
    test('a line becomes Dutch text and back, with the server keys', () => {
        const draft = draftOf(line(), 'nl');
        assert.deepEqual([draft.quantity, draft.unitPrice, draft.discount, draft.vat], ['30', '12,50', '10', '9']);
        assert.deepEqual(buildLineUpdates([{ ...draft, quantity: '20', unitPrice: '13,1' }], 'nl'),
            { ok: true, value: [{ id: 41, quantity: 20, unitPrice: 13.1, discountPercentage: 10, vatPercentage: 9 }] });
    });

    test('never more than delivered, prices with two decimals, whole packaging and only 0, 9 or 21 VAT', () => {
        const result = buildLineUpdates([
            { ...draftOf(line(), 'nl'), quantity: '31' },
            { ...draftOf(line({ id: 42, lineKind: 'Packaging', quantity: 2, maxQuantity: 2 }), 'nl'), quantity: '1,5', unitPrice: '7,505', vat: '6' },
        ], 'nl');
        assert.equal(result.ok, false);
        assert.deepEqual(!result.ok && result.errors, {
            'lines[0].quantity': 'si_edit_quantity_too_much', 'lines[1].quantity': 'si_edit_quantity_invalid',
            'lines[1].unitPrice': 'si_edit_price_invalid', 'lines[1].vatPercentage': 'si_edit_vat_invalid',
        });
    });

    test('an empty price means no price yet; a removed line sends only remove', () => {
        assert.deepEqual(buildLineUpdates([{ ...draftOf(line(), 'nl'), unitPrice: '', discount: '' }], 'nl'),
            { ok: true, value: [{ id: 41, quantity: 30, unitPrice: null, discountPercentage: null, vatPercentage: 9 }] });
        assert.deepEqual(buildLineUpdates([{ ...draftOf(line(), 'nl'), remove: true, quantity: 'x' }], 'nl'), { ok: true, value: [{ id: 41, remove: true }] });
    });
});
