import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { emptyRow, recipientProblems, recipientsChanged, recipientsFrom, rowsFrom, typoHint } from './documentRecipients.ts';

describe('document recipient rows', () => {
    test('to and from the API: blank rows are left out, the scope is an address id or the whole relation', () => {
        const rows = rowsFrom([{ id: 4, name: 'Planning', email: 'planning@noord.nl', role: 'To', kinds: ['LoadingSlip', 'TransportOrder'], relationAddressId: null },
            { id: 5, name: null, email: 'winkel@noord.nl', role: 'Cc', kinds: ['OrderConfirmation'], relationAddressId: 9 }]);
        assert.deepEqual(rows.map((r) => [r.name, r.scope]), [['Planning', ''], ['', '9']]);
        const sent = recipientsFrom([...rows, emptyRow()]);
        assert.equal(sent.length, 2);
        assert.deepEqual([sent[1].name, sent[1].relationAddressId, sent[1].role], [null, 9, 'Cc']);
        assert.equal(recipientsChanged(rows, rows), false);
        assert.equal(recipientsChanged([...rows, emptyRow()], rows), false, 'a blank row is no change');
        assert.equal(recipientsChanged([{ ...rows[0], kinds: ['LoadingSlip'] }, rows[1]], rows), true);
    });

    test('problems like the server: a strict address, at least one document, no address twice for the same scope', () => {
        const rows = [
            { ...emptyRow(['LoadingSlip']), email: 'geen-adres' },
            { ...emptyRow([]), email: 'a@b.nl' },
            { ...emptyRow(['Other']), email: 'dubbel@noord.nl' },
            { ...emptyRow(['Invoice']), email: 'DUBBEL@noord.nl' },
            { ...emptyRow(['Invoice']), email: 'dubbel@noord.nl', scope: '9' },
            emptyRow(),
        ];
        assert.deepEqual(recipientProblems(rows).map((p) => `${p.index}:${p.code}`), ['0:invalid', '1:kinds', '3:duplicate']);
        assert.equal(typoHint({ ...emptyRow(), email: 'planning@gmial.com' }), 'planning@gmail.com');
        assert.equal(typoHint({ ...emptyRow(), email: 'planning@gmail.com' }), null);
    });
});
