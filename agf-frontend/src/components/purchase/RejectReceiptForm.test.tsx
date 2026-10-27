import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setSelectValue, setValue, stubFetch, type Posted } from '../../test/reactHarness.ts';
import type { PurchaseOrderReceiptLine } from '../../types/purchaseOrder';
import RejectReceiptForm from './RejectReceiptForm';

// Afkeuren: berekend gewicht volgt het aantal, Anders vraagt een toelichting, een onbekende uitkomst bevriest het verzoek
// (opnieuw = zelfde requestId) en een definitieve weigering maakt een gewijzigde invoer tot een nieuw verzoek.

const receipt: PurchaseOrderReceiptLine = {
    palletContentLineId: 41, palletId: 9, palletReference: 'P-000009', palletStatus: 'Received', supplyCommitmentLineId: 70, productName: 'Mango', productVariantName: 'Kent',
    lotNumber: 'LOT-K', quantity: 20, unit: 'box', netWeightKg: 80, weightMethod: 'Calculated', netKgPerPackage: 4, onHandQuantity: 20, onHandNetWeightKg: 80,
    reservedQuantity: 0, reservedNetWeightKg: 0, qualityStatus: 'Quarantine', qualityReason: null, origin: 'Receipt', receivedAt: '2026-10-27T10:00:00Z',
    rejectable: true, rejectableQuantity: 20, rejectableNetWeightKg: 80, version: 0,
};

let mounted: Mounted;
let posted: Posted[];
let answers: (() => Promise<Response>)[];
const input = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const save = () => click(mounted.button('supply_retry_same') ?? mounted.button('po_reject_save')!);

describe('RejectReceiptForm', () => {
    beforeEach(() => {
        window.localStorage.setItem('agf_actor_reference', 'Steve');
        answers = [];
        posted = stubFetch({ onGet: () => json(404, {}), nextPost: () => (answers.shift() ?? (async () => json(500, {})))() });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('calculated kg follows the quantity; Other needs an explanation; unknown outcome retries the same request', async () => {
        await mounted.render(<RejectReceiptForm orderId={7} receipt={receipt} onDone={() => {}} onCancel={() => {}} />);
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
        assert.equal(mounted.button('po_cancel')!.disabled, true);
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
        await mounted.render(<RejectReceiptForm orderId={7} receipt={receipt} onDone={() => {}} onCancel={() => {}} />);
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
});
