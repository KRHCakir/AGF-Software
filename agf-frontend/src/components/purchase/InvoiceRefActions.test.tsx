import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setValue, stubFetch, type Posted } from '../../test/reactHarness.ts';
import { PURCHASE_COMMANDS_STORAGE_KEY, parseStoredCommands } from '../../lib/pendingCommands.ts';
import type { PurchaseOrderDetail } from '../../types/purchaseOrder';
import { HydratedGate, PendingCommandsProvider } from '../sales/PendingCommandsContext';
import InvoiceRefActions from './InvoiceRefActions';

// "Factuur ontvangen": nummer verplicht, bedrag in de schermtaal (1.234,50), een dubbel nummer is een duidelijke weigering
// en na opslaan krijgt de pagina de bijgewerkte order. Een onbekende uitkomst blijft in de opslag per tabblad staan.

const order = { id: 7, orderNumber: 'IO-2026-0007', vendorName: null, orderedOn: '2026-10-27', status: 'Received', invoiceIndicator: 'Received', expectedQuantity: 1,
    receivedQuantity: 1, rejectedQuantity: 0, rejectedNetWeightKg: 0, origin: 'Peru', orderChannel: null, note: null, supplierReference: null, expectedAt: null,
    lines: [], receipts: [], invoiceRefs: [], rejections: [], history: [] } as unknown as PurchaseOrderDetail;
const view = (onDone: (order: PurchaseOrderDetail) => void = () => {}) => (
    <PendingCommandsProvider storageKey={PURCHASE_COMMANDS_STORAGE_KEY}>
        <HydratedGate><InvoiceRefActions orderId={7} orderNumber="IO-2026-0007" onDone={onDone} /></HydratedGate>
    </PendingCommandsProvider>
);
const stored = () => parseStoredCommands(window.sessionStorage.getItem(PURCHASE_COMMANDS_STORAGE_KEY));

let mounted: Mounted;
let posted: Posted[];
let answers: (() => Promise<Response>)[];
const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;

describe('InvoiceRefActions', () => {
    beforeEach(() => {
        window.localStorage.setItem('agf_actor_reference', 'Jan');
        window.sessionStorage.clear();
        answers = [];
        posted = stubFetch({ onGet: () => json(404, {}), nextPost: () => (answers.shift() ?? (async () => json(500, {})))() });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('records an invoice number with date and amount; a duplicate is refused clearly', async () => {
        const done: unknown[] = [];
        await mounted.render(view((updated) => done.push(updated)));
        await click(mounted.button('+ po_invoice_received_action')!);
        await click(mounted.button('po_invoice_save')!);
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('supply_error_required'));

        await act(async () => { setValue(input('invoiceRef-Invoice-number'), ' F-4711 '); setValue(input('invoiceRef-Invoice-date'), '2026-10-27'); setValue(input('invoiceRef-Invoice-amount'), '1.234,50'); });
        answers.push(async () => json(409, { message: 'Invoice F-4711 is already recorded on this purchase order.', code: 'INVOICE_REF_DUPLICATE' }));
        await click(mounted.button('po_invoice_save')!);
        await mounted.waitFor(() => mounted.hasText('po_invoice_duplicate'), 'duplicate notice');
        const body = JSON.parse(posted[0].body);
        assert.match(posted[0].url, /\/purchase-orders\/7\/invoice-refs$/);
        assert.deepEqual({ ...body, requestId: 'x' }, { requestId: 'x', kind: 'Invoice', invoiceNumber: 'F-4711', invoiceDate: '2026-10-27', amountExclVat: 1234.5, actorReference: 'Jan' });

        await act(async () => { setValue(input('invoiceRef-Invoice-number'), 'F-4712'); });
        answers.push(async () => json(201, order));
        await click(mounted.button('po_invoice_save')!);
        await mounted.waitFor(() => posted.length === 2, 'second POST');
        assert.notEqual(JSON.parse(posted[1].body).requestId, body.requestId);
    });

    test('a credit note is its own kind', async () => {
        await mounted.render(view());
        await click(mounted.button('+ po_credit_note_received_action')!);
        assert.ok(mounted.hasText('po_credit_note_number'));
        await act(async () => { setValue(input('invoiceRef-CreditNote-number'), 'CN-12'); });
        await click(mounted.button('po_invoice_save')!);
        await mounted.waitFor(() => posted.length === 1, 'POST');
        assert.equal(JSON.parse(posted[0].body).kind, 'CreditNote');
        assert.equal(JSON.parse(posted[0].body).amountExclVat, null);
    });

    test('unknown outcome survives unmount: the remounted form is locked to the frozen number; resolved clears it', async () => {
        await mounted.render(view());
        await click(mounted.button('+ po_credit_note_received_action')!);
        await act(async () => { setValue(input('invoiceRef-CreditNote-number'), 'CN-12'); setValue(input('invoiceRef-CreditNote-amount'), '-48,50'); });
        answers.push(async () => { throw new TypeError('network down'); });
        await click(mounted.button('po_invoice_save')!);
        await mounted.waitFor(() => Object.keys(stored()).length === 1, 'frozen in storage');
        assert.equal(Object.keys(stored())[0], 'invoice-ref:7:credit-note');

        await mounted.unmount();
        mounted = new Mounted();
        const done: PurchaseOrderDetail[] = [];
        await mounted.render(view((updated) => done.push(updated)));
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after remount');
        assert.equal(input('invoiceRef-CreditNote-number').value, 'CN-12');
        assert.equal(input('invoiceRef-CreditNote-number').disabled, true);
        assert.equal(mounted.button('po_invoice_save'), null);
        assert.equal(mounted.button('po_cancel'), null);
        assert.equal(mounted.button('+ po_invoice_received_action'), null);

        answers.push(async () => json(201, order));
        await click(mounted.button('supply_retry_same')!);
        await mounted.waitFor(() => done.length === 1, 'resolved');
        assert.equal(posted[1].body, posted[0].body);
        assert.equal(JSON.parse(posted[0].body).amountExclVat, -48.5);
        assert.deepEqual(stored(), {});
        await mounted.waitFor(() => mounted.button('+ po_credit_note_received_action'), 'buttons back');
    });
});
