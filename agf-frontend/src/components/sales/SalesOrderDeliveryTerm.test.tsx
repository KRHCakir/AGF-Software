import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, setSelectValue, setValue } from '../../test/reactHarness.ts';
import type { SalesOrder } from '../../types/salesOrder';
import SalesOrderDeliveryTerm from './SalesOrderDeliveryTerm';

// De leveringsconditie in de kop van een verkooporder (feedback 22 november): tonen, en bij een open order per order aanpassen met de
// verwachte orderversie (PUT); de server legt het vast als auditregel.
const order = (over: Partial<SalesOrder> = {}): SalesOrder => ({
    id: 9, orderNumber: 'VO-2026-0009', requestId: 'r', customerId: 1, customerName: 'Fictieve Groothandel', status: 'Open', customerReference: null, internalNote: null,
    requestedDeliveryDate: '2026-11-30T00:00:00Z', actorReference: 'test', createdAt: '2026-11-23T08:00:00Z', version: 4, cancelledAt: null, cancelReason: null, cancelledBy: null,
    fulfilled: false, delivered: false, lines: [], deliveryTerm: { deliveryTermKind: 'Incoterm', incoterm: 'DAP', incotermPlace: 'Rotterdam', deliveryTermText: null }, ...over,
});

let mounted: Mounted;
let sent: { method: string; url: string; body: string }[];
const button = (text: string) => Array.from(mounted.container.querySelectorAll('button')).find((b) => b.textContent === text)!;

describe('SalesOrderDeliveryTerm', () => {
    beforeEach(() => {
        sent = [];
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            sent.push({ method: init?.method ?? 'GET', url: String(input), body: String(init?.body) });
            return json(200, { ...order(), version: 5, deliveryTerm: { deliveryTermKind: 'Franco', incoterm: null, incotermPlace: null, deliveryTermText: 'Venlo' } });
        }) as typeof fetch;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('shows the term and changes it with a PUT carrying the order version', async () => {
        let changed = 0;
        await mounted.render(<SalesOrderDeliveryTerm order={order()} onChanged={() => { changed++; }} />);
        assert.ok(mounted.container.textContent!.includes('DAP Rotterdam'));
        await act(async () => { button('delivery_term_change').click(); });
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('[id="deliveryTerm.deliveryTermKind-input"]')!, 'Franco'); });
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[id="deliveryTerm.deliveryTermText-input"]')!, 'Venlo'); });
        await act(async () => { button('delivery_term_save').click(); });
        await mounted.waitFor(() => changed === 1, 'saved');
        assert.equal(sent.length, 1);
        assert.equal(sent[0].method, 'PUT');
        assert.ok(sent[0].url.endsWith('/sales-orders/9/delivery-term'));
        const body = JSON.parse(sent[0].body);
        assert.equal(body.expectedOrderVersion, 4);
        assert.deepEqual(body.deliveryTerm, { deliveryTermKind: 'Franco', incoterm: null, incotermPlace: null, deliveryTermText: 'Venlo' });
    });

    test('an incomplete Incoterm is refused before sending; a cancelled order has no change button', async () => {
        await mounted.render(<SalesOrderDeliveryTerm order={order()} onChanged={() => {}} />);
        await act(async () => { button('delivery_term_change').click(); });
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('[id="deliveryTerm.incotermPlace-input"]')!, ''); });
        await act(async () => { button('delivery_term_save').click(); });
        assert.equal(sent.length, 0);
        assert.ok(mounted.container.textContent!.includes('relations_error_incoterm_place'));
        await mounted.render(<SalesOrderDeliveryTerm order={order({ status: 'Cancelled', deliveryTerm: null })} onChanged={() => {}} />);
        assert.ok(mounted.container.textContent!.includes('delivery_term_none'));
        assert.equal(Array.from(mounted.container.querySelectorAll('button')).some((b) => b.textContent === 'delivery_term_change'), false);
    });
});
