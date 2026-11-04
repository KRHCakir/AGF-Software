import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, STORAGE_KEY, click, json, setValue, storedCommands, stubFetch } from '../../test/reactHarness.ts';
import { SalesOrder, SalesOrderLine } from '../../types/salesOrder';
import OrderLinePanel from './OrderLinePanel';
import { HydratedGate, PendingCommandsProvider } from './PendingCommandsContext';

// "Reserveren (oudste partij eerst)" door de echte componenten: het voorstel over twee pallets en twee partijen, aanpassen, bevestigen als
// één batch; een onbekende uitkomst bevriest de opdracht (alleen exact dezelfde herhaling).

const line: SalesOrderLine = {
    id: 50, productVariantId: 7, productName: 'Tomaat', productVariantName: 'Ronde 67-82', quantity: 50, unit: 'colli', palletCount: null, packagesPerPallet: null,
    reservedQuantity: 0, reservedNetWeightKg: 0, allocatedQuantity: 0, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, directTradeShippedQuantity: 0, deliveredQuantity: 0,
    delivered: false, openQuantity: 50, fulfilled: false, version: 0, reservations: [], supplySource: 'Expected',
};
const order: SalesOrder = {
    id: 5, orderNumber: 'VO-2026-0005', requestId: 'o-5', customerId: 1, customerName: 'Groothandel G', status: 'Open', customerReference: null, internalNote: null,
    requestedDeliveryDate: '2026-11-06T06:00:00Z', actorReference: 'verkoop-1', createdAt: '2026-11-03T10:00:00Z', version: 0,
    cancelledAt: null, cancelReason: null, cancelledBy: null, fulfilled: false, delivered: false, lines: [line],
};
const candidate = (id: number, lot: string, available: number, receivedAt: string, method: 'Weighed' | 'Calculated') => ({
    palletContentLineId: id, palletId: id, palletReference: `P-${id}`, warehouseLocation: null, lotNumber: lot, ggn: null, unit: 'colli', quantity: available, netWeightKg: available * 6,
    weightMethod: method, netKgPerPackage: method === 'Calculated' ? 6 : null, dispatchedQuantity: 0, dispatchedNetWeightKg: 0, onHandQuantity: available, onHandNetWeightKg: available * 6,
    reservedQuantity: 0, reservedNetWeightKg: 0, availableQuantity: available, availableNetWeightKg: available * 6, qualityStatus: 'Approved', contentLineVersion: 0,
    receivedAt, supplyCommitmentId: id, supplyOrderNumber: `IO-2026-000${id}`,
});
const proposal = {
    salesOrderId: 5, salesOrderLineId: 50, unit: 'colli', neededQuantity: 50, proposedQuantity: 50,
    candidates: [candidate(1, 'LOT-A', 40, '2026-10-19T07:00:00Z', 'Weighed'), candidate(2, 'LOT-B', 20, '2026-10-22T07:00:00Z', 'Calculated')],
    items: [{ palletContentLineId: 1, quantity: 40, netWeightKg: 240 }, { palletContentLineId: 2, quantity: 10, netWeightKg: 60 }],
};

let mounted: Mounted;
let posted: { url: string; body: string }[];
let nextPost: () => Promise<Response>;
let changed: number;
const element = () => (
    <PendingCommandsProvider>
        <HydratedGate>
            <OrderLinePanel order={order} line={line} readOnly={false} openFifo onOrderChanged={async () => { changed++; return true; }} />
        </HydratedGate>
    </PendingCommandsProvider>
);
const qty = (id: number) => mounted.container.querySelector<HTMLInputElement>(`#fifo-50-${id}-quantity`)!;
const kg = (id: number) => mounted.container.querySelector<HTMLInputElement>(`#fifo-50-${id}-kg`)!;
const confirm = () => mounted.container.querySelector<HTMLButtonElement>('[data-tour="sales-fifo-confirm"]');

describe('FifoReserveForm through OrderLinePanel', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        changed = 0;
        nextPost = async () => json(201, []);
        posted = stubFetch({
            onGet: (url) => (url.includes('/fifo-proposal') ? json(200, proposal) : url.includes('/availability') ? json(200, { generatedAt: 'x', basis: {}, variants: [] }) : json(404, {})),
            nextPost: () => nextPost(),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('oldest lot first over two pallets: LOT-A 40 and LOT-B 10, calculated kg follows; confirming posts one batch', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => confirm(), 'proposal');
        const rows = Array.from(mounted.container.querySelectorAll('[data-fifo-row]')).map((row) => row.getAttribute('data-fifo-row'));
        assert.deepEqual(rows, ['1', '2']);
        assert.deepEqual([qty(1).value, kg(1).value, qty(2).value, kg(2).value], ['40', '240', '10', '60']);
        assert.equal(kg(2).readOnly, true, 'calculated weight is not typed');
        assert.ok(mounted.hasText('IO-2026-0001'));

        await click(confirm()!);
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.ok(posted[0].url.endsWith('/sales-orders/5/lines/50/reservations/batch'));
        const body = JSON.parse(posted[0].body);
        assert.deepEqual(body.items, proposal.items);
        assert.equal(body.unit, 'colli');
        await mounted.waitFor(() => changed > 0, 'order reloaded');
    });

    test('another lot may be chosen: LOT-B 20 and LOT-A 30; more than the line needs is refused before sending', async () => {
        await mounted.render(element());
        await mounted.waitFor(() => confirm(), 'proposal');
        await act(async () => { setValue(qty(2), '20'); });
        assert.equal(kg(2).value, '120', 'the full rest takes the remaining kg');
        await click(confirm()!);
        assert.equal(posted.length, 0, '60 > 50: nothing sent');
        assert.ok(mounted.container.querySelector('[id="fifo-50-items"]')?.textContent?.includes('sales_error_exceeds_demand'));
        await act(async () => { setValue(qty(1), '30'); setValue(kg(1), '180'); });
        await click(confirm()!);
        await mounted.waitFor(() => posted.length > 0, 'POST');
        assert.deepEqual(JSON.parse(posted[0].body).items.map((i: { quantity: number }) => i.quantity), [30, 20]);
    });

    test('an unknown outcome freezes the batch: only the identical retry, nothing editable', async () => {
        nextPost = async () => { throw new TypeError('network down'); };
        await mounted.render(element());
        await mounted.waitFor(() => confirm(), 'proposal');
        await click(confirm()!);
        await mounted.waitFor(() => storedCommands()['reserve-batch:5:50'], 'frozen command');
        assert.ok(window.sessionStorage.getItem(STORAGE_KEY)?.includes('reserve-batch'));
        assert.equal(qty(1).disabled, true);
        const first = JSON.parse(posted[0].body);
        nextPost = async () => json(200, []);
        await click(mounted.button('supply_retry_same')!);
        await mounted.waitFor(() => posted.length > 1, 'retry');
        assert.equal(JSON.parse(posted[1].body).requestId, first.requestId);
    });
});
