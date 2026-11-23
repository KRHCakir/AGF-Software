import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted } from '../../test/reactHarness.ts';
import type { PartijDetail } from '../../types/partij';
import PartijTrace from './PartijTrace';

// Feedback 22 november: herleidbaarheid op het partijdetail. Inkoop-partij: de som (een verschil in rood) en per bestemming een regel met
// links; verkoop-partij: per regel de inkoop-partijen met leverancier en herkomst. Afdrukken en CSV staan bij beide.

const base = { id: 7, number: 'I-2611010', kind: 'Purchase', status: 'Open' } as unknown as PartijDetail;
let mounted: Mounted;
const all = (selector: string) => Array.from(mounted.container.querySelectorAll<HTMLElement>(selector));

describe('partij trace', () => {
    beforeEach(() => { mounted = new Mounted(); });
    afterEach(async () => { await mounted.unmount(); });

    test('a purchase lot shows the sum, every destination with links and a difference in red', async () => {
        await mounted.render(<PartijTrace partij={{ ...base, recall: {
            unit: 'colli', received: 38, shipped: 12, returned: 0, rejected: 8, onHand: 17, difference: 1,
            destinations: [{ salesPartijId: 40, salesPartijNumber: 'V-2611001', salesOrderId: 9, reference: 'VO-2026-0009', customerName: 'Groothandel G', quantity: 12, unit: 'colli', date: '2026-11-23T10:00:00Z' }],
            directInvoices: [{ salesPartijId: 41, salesPartijNumber: 'V-2611002', salesOrderId: null, reference: 'FAC-1', customerName: 'Balieklant', quantity: 4, unit: 'colli', date: '2026-11-23T00:00:00Z' }],
        } }} />);
        assert.equal(all('[data-testid="partij-recall-row"]').length, 2);
        const hrefs = all('a').map((a) => a.getAttribute('href'));
        assert.deepEqual(hrefs, ['/partijen?id=40', '/sales-orders?orderId=9', '/partijen?id=41']);
        assert.ok(mounted.container.querySelector('[data-testid="partij_trace_difference"]')!.className.includes('red'));
        assert.ok(mounted.hasText('partij_trace_difference_text'));
        assert.ok(mounted.container.querySelector('[data-tour="partij-trace-print"]') && mounted.container.querySelector('[data-tour="partij-trace-csv"]'));
    });

    test('a sales lot shows per line the purchase lots with supplier and origin', async () => {
        await mounted.render(<PartijTrace partij={{ ...base, number: 'V-2611001', kind: 'Sales', recall: null, delivery: {
            customerName: 'Groothandel G', salesOrderId: 9, reference: 'VO-2026-0009', sequence: 1, deliveryDate: '2026-11-23T10:00:00Z', shipped: true,
            lines: [{ product: 'Tomaat Ronde', quantity: 25, unit: 'colli', sources: [
                { partijId: 1, number: 'I-2611010', vendorName: 'Kweker K', origin: 'Spanje', quantity: 12, status: 'Consumed' },
                { partijId: 2, number: 'I-2611011', vendorName: 'Kweker K', origin: 'Marokko', quantity: 13, status: 'Active' }] }],
        } }} />);
        const rows = all('[data-testid="partij-delivery-row"]');
        assert.equal(rows.length, 2);
        assert.ok(rows[1].textContent!.includes('Marokko') && rows[1].textContent!.includes('I-2611011'));
        assert.ok(mounted.hasText('partij_trace_delivered_on'));
    });
});
