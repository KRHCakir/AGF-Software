import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setValue, stubFetch, type Posted } from '../../test/reactHarness.ts';
import InvoiceRefActions from './InvoiceRefActions';

// "Factuur ontvangen": nummer verplicht, bedrag in de schermtaal (1.234,50), een dubbel nummer is een duidelijke weigering
// en na opslaan krijgt de pagina de bijgewerkte order.

let mounted: Mounted;
let posted: Posted[];
let answers: (() => Promise<Response>)[];
const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;

describe('InvoiceRefActions', () => {
    beforeEach(() => {
        window.localStorage.setItem('agf_actor_reference', 'Jan');
        answers = [];
        posted = stubFetch({ onGet: () => json(404, {}), nextPost: () => (answers.shift() ?? (async () => json(500, {})))() });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('records an invoice number with date and amount; a duplicate is refused clearly', async () => {
        const done: unknown[] = [];
        await mounted.render(<InvoiceRefActions orderId={7} onDone={(order) => done.push(order)} />);
        await click(mounted.button('+ po_invoice_received_action')!);
        await click(mounted.button('po_invoice_save')!);
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('supply_error_required'));

        await act(async () => { setValue(input('invoiceRef-number'), ' F-4711 '); setValue(input('invoiceRef-date'), '2026-10-27'); setValue(input('invoiceRef-amount'), '1.234,50'); });
        answers.push(async () => json(409, { message: 'Invoice F-4711 is already recorded on this purchase order.', code: 'INVOICE_REF_DUPLICATE' }));
        await click(mounted.button('po_invoice_save')!);
        await mounted.waitFor(() => mounted.hasText('po_invoice_duplicate'), 'duplicate notice');
        const body = JSON.parse(posted[0].body);
        assert.match(posted[0].url, /\/purchase-orders\/7\/invoice-refs$/);
        assert.deepEqual({ ...body, requestId: 'x' }, { requestId: 'x', kind: 'Invoice', invoiceNumber: 'F-4711', invoiceDate: '2026-10-27', amountExclVat: 1234.5, actorReference: 'Jan' });

        await act(async () => { setValue(input('invoiceRef-number'), 'F-4712'); });
        answers.push(async () => json(201, { id: 7 }));
        await click(mounted.button('po_invoice_save')!);
        await mounted.waitFor(() => posted.length === 2, 'second POST');
        assert.notEqual(JSON.parse(posted[1].body).requestId, body.requestId);
    });

    test('a credit note is its own kind', async () => {
        await mounted.render(<InvoiceRefActions orderId={7} onDone={() => {}} />);
        await click(mounted.button('+ po_credit_note_received_action')!);
        assert.ok(mounted.hasText('po_credit_note_number'));
        await act(async () => { setValue(input('invoiceRef-number'), 'CN-12'); });
        await click(mounted.button('po_invoice_save')!);
        await mounted.waitFor(() => posted.length === 1, 'POST');
        assert.equal(JSON.parse(posted[0].body).kind, 'CreditNote');
        assert.equal(JSON.parse(posted[0].body).amountExclVat, null);
    });
});
