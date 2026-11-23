import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { deliveryTermDraftOf, deliveryTermPayload, describeDeliveryTerm, emptyDeliveryTerm, sameDeliveryTerm } from './deliveryTerm.ts';

// De leveringsconditie van een order, onafhankelijk uitgeschreven naar DeliveryTermRules.cs (feedback 22 november).
const t = (key: string) => key;

describe('delivery term of an order', () => {
    test('empty is no term; Incoterm needs code and place (upper case, trimmed); Other needs a description; fields that do not belong are emptied', () => {
        assert.deepEqual(deliveryTermPayload(emptyDeliveryTerm()), { ok: true, value: { deliveryTermKind: null, incoterm: null, incotermPlace: null, deliveryTermText: null } });
        assert.deepEqual(deliveryTermPayload({ kind: 'Incoterm', incoterm: 'fca', place: ' Venlo ', text: 'weg' }),
            { ok: true, value: { deliveryTermKind: 'Incoterm', incoterm: 'FCA', incotermPlace: 'Venlo', deliveryTermText: null } });
        assert.deepEqual(deliveryTermPayload({ kind: 'Incoterm', incoterm: 'XYZ', place: '', text: '' }),
            { ok: false, errors: [{ field: 'deliveryTerm.incoterm', code: 'incoterm' }, { field: 'deliveryTerm.incotermPlace', code: 'incoterm_place' }] });
        assert.deepEqual(deliveryTermPayload({ kind: 'Other', incoterm: 'FCA', place: 'x', text: ' ' }), { ok: false, errors: [{ field: 'deliveryTerm.deliveryTermText', code: 'delivery_text' }] });
        assert.deepEqual(deliveryTermPayload({ kind: 'Franco', incoterm: 'FCA', place: 'x', text: '' }),
            { ok: true, value: { deliveryTermKind: 'Franco', incoterm: null, incotermPlace: null, deliveryTermText: null } });
    });

    test('a draft from the relation, comparison on the normalized value, and the text on screen', () => {
        const draft = deliveryTermDraftOf({ deliveryTermKind: 'Incoterm', incoterm: 'DAP', incotermPlace: 'Rotterdam', deliveryTermText: null });
        assert.deepEqual(draft, { kind: 'Incoterm', incoterm: 'DAP', place: 'Rotterdam', text: '' });
        assert.ok(sameDeliveryTerm(draft, { ...draft, incoterm: 'dap', place: 'Rotterdam ' }));
        assert.equal(describeDeliveryTerm({ deliveryTermKind: 'Incoterm', incoterm: 'DAP', incotermPlace: 'Rotterdam', deliveryTermText: null }, t), 'DAP Rotterdam');
        assert.equal(describeDeliveryTerm({ deliveryTermKind: 'Franco', incoterm: null, incotermPlace: null, deliveryTermText: 'vanaf 10 pallets' }, t), 'relations_delivery_Franco · vanaf 10 pallets');
        assert.equal(describeDeliveryTerm(null, t), null);
    });
});
