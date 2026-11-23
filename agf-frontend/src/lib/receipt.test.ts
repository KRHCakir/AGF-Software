import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { setInputLocale } from './decimal.ts';
import { draftFor, openLines, palletCountOf, progressOf, referenceInputs, requestFor, requestKey } from './receipt.ts';
import type { ReceiptLine, ReceiptOrder } from '../types/receipt';

const line = (extra: Partial<ReceiptLine> = {}): ReceiptLine => ({
    id: 1, productId: 1, productName: 'Mango', productVariantName: 'Kent', origin: null, size: null, partijNumber: null, unit: 'box', orderedQuantity: 100,
    receivedQuantity: 40, openQuantity: 60, netKgPerPackage: 4, suggestedPalletCount: null, receivable: true, pallets: [], ...extra,
});

describe('receipt drafts', () => {
    setInputLocale('nl');

    test('start with the open quantity in nl notation, pallets closed', () => {
        assert.deepEqual(draftFor(line({ openQuantity: 10.5 })), { quantity: '10,5', netKg: '', palletsOpen: false, palletCount: '1', references: [] });
        assert.equal(draftFor(line({ openQuantity: 0 })).quantity, '');
        assert.equal(draftFor(line({ suggestedPalletCount: 3 })).palletCount, '3');
    });

    test('the request carries the typed quantity, kg only without kg per package, and pallet numbers only when opened', () => {
        assert.deepEqual(requestFor(line(), draftFor(line())), { ok: true, request: { quantity: 60, netWeightKg: null, palletCount: null, palletReferences: null } });
        const noKg = line({ netKgPerPackage: null });
        assert.deepEqual(requestFor(noKg, draftFor(noKg)), { ok: false, errors: [{ field: 'netKg', error: 'kg_required' }] });
        assert.deepEqual(requestFor(noKg, { ...draftFor(noKg), netKg: '201,5' }), { ok: true, request: { quantity: 60, netWeightKg: 201.5, palletCount: null, palletReferences: null } });
        const pallets = { ...draftFor(line()), palletsOpen: true, palletCount: '3', references: [' A1 ', '', 'A3'] };
        assert.deepEqual(requestFor(line(), pallets), { ok: true, request: { quantity: 60, netWeightKg: null, palletCount: 3, palletReferences: ['A1', '', 'A3'] } });
        assert.deepEqual(requestFor(line(), { ...pallets, references: [] }), { ok: true, request: { quantity: 60, netWeightKg: null, palletCount: 3, palletReferences: null } });
        assert.equal(requestFor(line(), { ...pallets, palletCount: '0' }).ok, false);
        assert.equal(requestFor(line(), { ...draftFor(line()), quantity: '' }).ok, false);
    });

    test('pallet inputs follow the count and keep typed numbers', () => {
        const draft = { ...draftFor(line()), palletsOpen: true, palletCount: '2', references: ['A1', 'A2', 'A3'] };
        assert.deepEqual(referenceInputs(draft), ['A1', 'A2']);
        assert.equal(palletCountOf({ ...draft, palletCount: '100' }), null);
        assert.equal(palletCountOf({ ...draft, palletsOpen: false, palletCount: 'x' }), 1);
    });

    test('same input = same key; receive all takes the open, receivable lines; progress', () => {
        const request = { quantity: 60, netWeightKg: null, palletCount: null, palletReferences: null };
        assert.equal(requestKey(1, request), requestKey(1, { ...request }));
        assert.notEqual(requestKey(1, request), requestKey(1, { ...request, quantity: 61 }));
        const order = { receivable: true, lines: [line({ id: 1 }), line({ id: 2, openQuantity: 0 }), line({ id: 3, receivable: false })] } as ReceiptOrder;
        assert.deepEqual(openLines(order).map((l) => l.id), [1]);
        assert.deepEqual(openLines({ ...order, receivable: false }), []);
        assert.deepEqual([progressOf(line({ receivedQuantity: 0 })), progressOf(line()), progressOf(line({ openQuantity: 0 }))], ['none', 'partial', 'complete']);
    });
});
