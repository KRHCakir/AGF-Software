import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setSelectValue, setValue, stubFetch, type Posted } from '../../test/reactHarness.ts';
import { PURCHASE_COMMANDS_STORAGE_KEY, parseStoredCommands } from '../../lib/pendingCommands.ts';
import type { PurchaseOrderDetail, PurchaseOrderReceiptLine } from '../../types/purchaseOrder';
import { HydratedGate, PendingCommandsProvider } from '../sales/PendingCommandsContext';
import RejectReceiptForm from './RejectReceiptForm';

// Afkeuren: berekend gewicht volgt het aantal, Anders vraagt een toelichting, een onbekende uitkomst bevriest het verzoek
// in de opslag per tabblad (opnieuw = zelfde requestId, ook na weg- en terugnavigeren) en een definitieve weigering maakt een
// gewijzigde invoer tot een nieuw verzoek.

const receipt: PurchaseOrderReceiptLine = {
    palletContentLineId: 41, palletId: 9, palletReference: 'P-000009', palletStatus: 'Received', supplyCommitmentLineId: 70, productName: 'Mango', productVariantName: 'Kent',
    lotNumber: 'LOT-K', quantity: 20, unit: 'box', netWeightKg: 80, weightMethod: 'Calculated', netKgPerPackage: 4, onHandQuantity: 20, onHandNetWeightKg: 80,
    reservedQuantity: 0, reservedNetWeightKg: 0, qualityStatus: 'Quarantine', qualityReason: null, origin: 'Receipt', receivedAt: '2026-10-27T10:00:00Z',
    rejectable: true, rejectableQuantity: 20, rejectableNetWeightKg: 80, version: 0,
};

// Minimale geldige order als antwoord (alleen wat de vormcontrole vraagt).
const order = { id: 7, orderNumber: 'IO-2026-0007', vendorName: null, orderedOn: '2026-10-27', status: 'Received', invoiceIndicator: 'None', expectedQuantity: 20,
    receivedQuantity: 20, rejectedQuantity: 5, lines: [], receipts: [], invoiceRefs: [], rejections: [], history: [] } as unknown as PurchaseOrderDetail;

const view = (onDone: (order: PurchaseOrderDetail) => void = () => {}) => (
    <PendingCommandsProvider storageKey={PURCHASE_COMMANDS_STORAGE_KEY}>
        <HydratedGate><RejectReceiptForm orderId={7} orderNumber="IO-2026-0007" receipt={receipt} onDone={onDone} onCancel={() => {}} /></HydratedGate>
    </PendingCommandsProvider>
);
const stored = () => parseStoredCommands(window.sessionStorage.getItem(PURCHASE_COMMANDS_STORAGE_KEY));

let mounted: Mounted;
let posted: Posted[];
let answers: (() => Promise<Response>)[];
const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const save = () => click(mounted.button('supply_retry_same') ?? mounted.button('po_reject_save')!);

describe('RejectReceiptForm', () => {
    beforeEach(() => {
        window.localStorage.setItem('agf_actor_reference', 'Steve');
        window.sessionStorage.clear();
        answers = [];
        posted = stubFetch({ onGet: () => json(404, {}), nextPost: () => (answers.shift() ?? (async () => json(500, {})))() });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('calculated kg follows the quantity; Other needs an explanation; unknown outcome retries the same request', async () => {
        await mounted.render(view());
        await mounted.waitFor(() => input('reject-41-quantity').value === '20', 'default quantity');
        assert.equal(input('reject-41-kg').value, '80');
        assert.equal(input('reject-41-kg').readOnly, true);
        await act(async () => { setValue(input('reject-41-quantity'), '5'); });
        await mounted.waitFor(() => input('reject-41-kg').value === '20', 'calculated kg');

        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="reject-41-reason"]')!, 'Other'); });
        await save();
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('po_reject_explanation_required'));
        await act(async () => { setValue(input('reject-41-explanation'), 'Schimmel'); });
        await click(mounted.container.querySelector<HTMLInputElement>('input[value="Dispose"]')!);

        answers.push(async () => { throw new TypeError('network down'); });
        await save();
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry offered');
        assert.equal(input('reject-41-quantity').disabled, true);
        assert.equal(mounted.button('po_cancel'), null);
        assert.equal(mounted.button('po_reject_save'), null);
        answers.push(async () => json(201, {}));
        await save();
        await mounted.waitFor(() => posted.length === 2, 'second POST');
        assert.equal(JSON.parse(posted[0].body).requestId, JSON.parse(posted[1].body).requestId);
        assert.deepEqual(JSON.parse(posted[1].body), { ...JSON.parse(posted[0].body) });
        const body = JSON.parse(posted[0].body);
        assert.equal(body.decision, 'Dispose');
        assert.equal(body.explanation, 'Schimmel');
        assert.equal(body.actorReference, 'Steve');
        assert.deepEqual(body.lines[0], { palletContentLineId: 41, expectedContentLineVersion: 0, quantity: 5, netWeightKg: 20 });
    });

    test('a definitive 409 unlocks the form; a changed quantity is a new request', async () => {
        await mounted.render(view());
        await mounted.waitFor(() => input('reject-41-quantity'), 'form');
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="reject-41-reason"]')!, 'Quality'); });
        answers.push(async () => json(409, { message: 'stale' }));
        await save();
        await mounted.waitFor(() => mounted.hasText('supply_error_http_409'), '409 shown');
        assert.equal(input('reject-41-quantity').disabled, false);
        await act(async () => { setValue(input('reject-41-quantity'), '2'); });
        answers.push(async () => json(500, {}));
        await save();
        await mounted.waitFor(() => posted.length === 2, 'second POST');
        assert.notEqual(JSON.parse(posted[0].body).requestId, JSON.parse(posted[1].body).requestId);
        assert.equal(JSON.parse(posted[1].body).lines[0].quantity, 2);
    });

    test('unknown outcome survives unmount: the remounted form is locked to the frozen request; resolved clears it', async () => {
        await mounted.render(view());
        await mounted.waitFor(() => input('reject-41-quantity'), 'form');
        await act(async () => { setValue(input('reject-41-quantity'), '5'); });
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="reject-41-reason"]')!, 'Damaged'); });
        answers.push(async () => { throw new TypeError('network down'); });
        await save();
        await mounted.waitFor(() => Object.keys(stored()).length === 1, 'frozen in storage');
        const frozen = Object.values(stored())[0];
        assert.equal(frozen.key, 'reject-receipt:7:41');

        // Weg en terug: een nieuw opgebouwd formulier toont exact de bevroren opdracht en alleen "opnieuw".
        await mounted.unmount();
        mounted = new Mounted();
        const done: PurchaseOrderDetail[] = [];
        await mounted.render(view((updated) => done.push(updated)));
        await mounted.waitFor(() => mounted.button('supply_retry_same'), 'retry after remount');
        assert.equal(input('reject-41-quantity').value, '5');
        assert.equal(input('reject-41-quantity').disabled, true);
        assert.equal(mounted.container.querySelector<HTMLSelectElement>('[id="reject-41-reason"]')!.value, 'Damaged');
        assert.equal(mounted.button('po_reject_save'), null);
        assert.equal(mounted.button('po_cancel'), null);
        assert.ok(mounted.hasText('sales_pending_kept'));

        answers.push(async () => json(201, order));
        await save();
        await mounted.waitFor(() => done.length === 1, 'resolved');
        assert.equal(JSON.parse(posted[1].body).requestId, frozen.requestId);
        assert.equal(posted[1].body, posted[0].body);
        assert.deepEqual(stored(), {});
        assert.equal(window.sessionStorage.getItem(PURCHASE_COMMANDS_STORAGE_KEY), null);
    });
});
