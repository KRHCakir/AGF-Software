import '../../test/setupDom.ts';
import '../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, Posted, click, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import ReceiveOrder from './ReceiveOrder';

// Feedback 22 november: ontvangen in één scherm. Per regel besteld, ontvangen en open; één klik Ontvangen (Enter in het aantal), een
// afwijkend aantal, optioneel palletnummers, en Alles ontvangen. Een herhaling na een fout gebruikt dezelfde requestId.
const line = (id: number, open: number, extra: Record<string, unknown> = {}) => ({
    id, productId: id, productName: 'Mango', productVariantName: `Kent ${id}`, origin: 'Peru', size: null, partijNumber: '2611001', unit: 'box',
    orderedQuantity: 100, receivedQuantity: 100 - open, openQuantity: open, netKgPerPackage: 4, suggestedPalletCount: null, receivable: true, pallets: [], ...extra,
});
const order = (lines: unknown[], extra: Record<string, unknown> = {}) => ({
    orderId: 7, orderNumber: 'IO-2026-0007', supplierReference: null, vendorId: 1, vendorName: 'Fruit Peru', expectedAt: null, partijNumber: '2611001',
    status: 'Ordered', receivable: true, notReceivableReason: null, lines, certificateWarnings: [], ...extra,
});
const received = (lines: unknown[], quantity: number) => ({ lineId: 1, quantity, netWeightKg: quantity * 4, pallets: [{ palletId: 9, reference: 'P-000009', contentLineId: 3, quantity, netWeightKg: quantity * 4, qualityStatus: 'Quarantine' }], order: order(lines) });

let mounted: Mounted;
let posted: Posted[];
let replies: Response[];
const enter = (input: HTMLElement) => act(async () => { input.dispatchEvent(new window.KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true })); });
const quantityOf = (id: number) => mounted.container.querySelector<HTMLInputElement>(`[data-tour="receive-quantity"][data-tour-key="${id}"]`)!;
const bodies = () => posted.map((p) => JSON.parse(p.body) as Record<string, unknown>);

describe('ReceiveOrder', () => {
    afterEach(async () => { await mounted.unmount(); });

    function setup(start: unknown) {
        replies = [];
        posted = stubFetch({ onGet: () => json(200, start), nextPost: async () => replies.shift() ?? json(500, { message: 'geen antwoord' }) });
        mounted = new Mounted();
    }

    beforeEach(() => { replies = []; });

    test('shows ordered, received and open per line and receives the open quantity with Enter, without any question', async () => {
        setup(order([line(1, 100)]));
        await mounted.render(<ReceiveOrder orderId={7} />);
        await mounted.waitFor(() => mounted.hasText('receipt_expected_title'), 'expected list');
        assert.equal(quantityOf(1).value, '100');
        replies.push(json(201, received([line(1, 0, { pallets: [{ palletId: 9, reference: 'P-000009', contentLineId: 3, quantity: 100, netWeightKg: 400, qualityStatus: 'Quarantine' }] })], 100)));
        await enter(quantityOf(1));
        await mounted.waitFor(() => mounted.hasText('P-000009'), 'received pallet');
        assert.equal(posted[0].url.endsWith('/receipts/orders/7/lines/1'), true);
        assert.deepEqual({ ...bodies()[0], requestId: 'x' }, { requestId: 'x', quantity: 100, netWeightKg: null, palletCount: null, palletReferences: null, certificateWarningToken: null });
        assert.ok(mounted.container.querySelector('[data-tour="receive-line-inspect"]'), 'Keuren / afwijzen offered for the quarantine pallet');
        assert.equal(mounted.container.querySelector('[role="dialog"]'), null, 'no control questions');
    });

    test('a different quantity is sent as typed, and a retry after an error reuses the same requestId', async () => {
        setup(order([line(1, 100)]));
        await mounted.render(<ReceiveOrder orderId={7} />);
        await mounted.waitFor(() => quantityOf(1), 'quantity');
        await act(async () => setValue(quantityOf(1), '60'));
        replies.push(json(409, { message: 'Gelijktijdig gewijzigd' }));
        await click(mounted.button('receipt_receive')!);
        await mounted.waitFor(() => mounted.container.querySelector('[role="alert"]'), 'error shown');
        replies.push(json(201, received([line(1, 40)], 60)));
        await click(mounted.button('receipt_receive')!);
        await mounted.waitFor(() => posted.length === 2 && quantityOf(1).value === '40', 'retried, then the new open quantity');
        assert.equal(bodies()[0].quantity, 60);
        assert.equal(bodies()[0].requestId, bodies()[1].requestId);
    });

    test('pallet numbers are optional: count and numbers are sent only when opened, empty numbers are made by the server', async () => {
        setup(order([line(1, 100)]));
        await mounted.render(<ReceiveOrder orderId={7} />);
        await mounted.waitFor(() => quantityOf(1), 'quantity');
        await click(mounted.button('receipt_pallet_numbers')!);
        const count = mounted.container.querySelector<HTMLInputElement>('[data-tour="receive-pallet-count"]')!;
        await act(async () => setValue(count, '2'));
        const numbers = () => Array.from(mounted.container.querySelectorAll<HTMLInputElement>('[data-tour="receive-pallet-number"]'));
        assert.equal(numbers().length, 2);
        await act(async () => setValue(numbers()[0], 'LDR-1'));
        replies.push(json(201, received([line(1, 0)], 100)));
        await click(mounted.button('receipt_receive')!);
        await mounted.waitFor(() => posted.length === 1, 'posted');
        assert.deepEqual([bodies()[0].palletCount, bodies()[0].palletReferences], [2, ['LDR-1', '']]);
    });

    test('Receive all receives every open line; a line without kg per package asks for the net kg first', async () => {
        setup(order([line(1, 100), line(2, 0), line(3, 50, { netKgPerPackage: null })], {
            certificateWarnings: [{ productId: 1, productName: 'Mango', supplierName: 'Fruit Peru', kinds: ['NotCovered'], deliveryDate: '2026-11-23', certificates: [{ certificateId: 1, number: '4049928000001', validUntil: null, productNames: ['Limoen'] }], message: 'x' }],
        }));
        await mounted.render(<ReceiveOrder orderId={7} />);
        await mounted.waitFor(() => quantityOf(1), 'quantity');
        assert.ok(mounted.container.querySelector('[data-tour="receive-certificate-warning"]'), 'certificate warning is a line on the screen');
        assert.equal(quantityOf(2).value, '', 'nothing open on line 2');
        replies.push(json(201, received([line(1, 0), line(2, 0), line(3, 50, { netKgPerPackage: null })], 100)));
        await click(mounted.button('receipt_receive_all')!);
        await mounted.waitFor(() => mounted.hasText('receipt_error_kg_required'), 'kg required for line 3');
        assert.equal(posted.length, 1, 'line 1 received, line 3 waits for its kg, line 2 has nothing open');
        await act(async () => setValue(mounted.container.querySelector<HTMLInputElement>('[data-tour="receive-net-kg"]')!, '201,5'));
        replies.push(json(201, received([line(1, 0), line(2, 0), line(3, 0, { netKgPerPackage: null })], 50)));
        await click(mounted.button('receipt_receive_all')!);
        await mounted.waitFor(() => posted.length === 2, 'line 3 posted');
        assert.deepEqual([bodies()[1].quantity, bodies()[1].netWeightKg], [50, 201.5]);
    });

    test('feedback 22 november: on a location in transit (direct to a customer) the buttons say Delivered and the location is shown', async () => {
        setup(order([line(1, 100)], { locationId: 5, locationName: 'Onderweg naar Griekenland', locationKind: 'InTransit' }));
        await mounted.render(<ReceiveOrder orderId={7} />);
        await mounted.waitFor(() => mounted.button('receipt_deliver'), 'Delivered button');
        assert.ok(mounted.button('receipt_deliver_all'));
        assert.equal(mounted.button('receipt_receive'), null);
        assert.ok(mounted.container.querySelector('[data-tour="receive-location"]'));
    });

    test('the token of the shown GGN warning goes along; a changed warning is loaded again and the input stays', async () => {
        const warning = { productId: 1, productName: 'Mango', supplierName: 'Fruit Peru', kinds: ['NotCovered'], deliveryDate: '2026-11-23',
            certificates: [{ certificateId: 1, number: '4049928000001', validUntil: null, productNames: ['Limoen'] }], message: 'x' };
        setup(order([line(1, 100)], { certificateWarnings: [warning], certificateWarningToken: 'abc' }));
        await mounted.render(<ReceiveOrder orderId={7} />);
        await mounted.waitFor(() => quantityOf(1), 'quantity');
        await act(async () => setValue(quantityOf(1), '60'));
        replies.push(json(409, { code: 'CERTIFICATE_CONFIRMATION_REQUIRED', checks: [warning], message: 'x' }));
        await click(mounted.button('receipt_receive')!);
        await mounted.waitFor(() => mounted.hasText('receipt_certificate_changed'), 'changed warning');
        assert.equal(bodies()[0].certificateWarningToken, 'abc');
        assert.equal(quantityOf(1).value, '60', 'the typed quantity stays');
    });

    test('an order that cannot be received says why and offers no button', async () => {
        setup(order([line(1, 100)], { receivable: false, notReceivableReason: 'This purchase order is cancelled.' }));
        await mounted.render(<ReceiveOrder orderId={7} />);
        await mounted.waitFor(() => mounted.hasText('receipt_not_receivable'), 'reason');
        assert.equal(mounted.button('receipt_receive'), null);
        assert.equal(mounted.button('receipt_receive_all'), null);
    });
});
