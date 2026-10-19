import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, stubFetch } from '../../test/reactHarness.ts';
import OrderHistory from './OrderHistory';

// Gebeurtenissen in de orderhistorie krijgen een vertaalde naam; een onbekend type blijft onder zijn eigen naam zichtbaar.
let mounted: Mounted;
describe('OrderHistory', () => {
    beforeEach(() => {
        stubFetch({
            onGet: () => json(200, [
                { id: 1, salesOrderId: 4, eventType: 'SalesOrderDispatched', occurredAt: '2026-10-19T13:19:00.000Z', actorReference: 'office-1', requestId: 'a', reason: 'DEMO-RIT-A1', oldValue: null, newValue: null },
                { id: 2, salesOrderId: 4, eventType: 'SalesOrderDispatchCorrected', occurredAt: '2026-10-19T13:22:00.000Z', actorReference: 'office-1', requestId: 'b', reason: 'verkeerd gescand', oldValue: null, newValue: null },
                { id: 3, salesOrderId: 4, eventType: 'SalesOrderCancelled', occurredAt: '2026-10-19T13:23:00.000Z', actorReference: 'office-1', requestId: 'c', reason: null, oldValue: null, newValue: null },
                { id: 4, salesOrderId: 4, eventType: 'SomethingNew', occurredAt: '2026-10-19T13:24:00.000Z', actorReference: null, requestId: null, reason: null, oldValue: null, newValue: null },
            ]),
            nextPost: async () => json(500, {}),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('known event types are translated and unknown ones stay readable', async () => {
        await mounted.render(<OrderHistory orderId={4} token={0} />);
        await mounted.waitFor(() => mounted.hasText('sales_event_dispatched'), 'history');
        assert.ok(mounted.hasText('sales_event_dispatch_corrected'));
        assert.ok(mounted.hasText('sales_event_cancelled'));
        assert.ok(mounted.hasText('SomethingNew'));
        assert.ok(!mounted.hasText('SalesOrderDispatched'), 'raw event names are not shown for known types');
    });
});
