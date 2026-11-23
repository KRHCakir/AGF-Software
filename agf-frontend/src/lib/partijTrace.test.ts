import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { csvCell, deliveryCsv, recallCsv } from './partijTrace.ts';

const number = (value: number) => String(value).replace('.', ',');
const date = (iso: string) => iso.slice(0, 10);
const recallLabels = { lot: 'Partij', received: 'Ontvangen', shipped: 'Geleverd', returned: 'Teruggenomen', rejected: 'Afgekeurd', onHand: 'Op voorraad', difference: 'Verschil',
    salesLot: 'Verkoop-partij', customer: 'Klant', reference: 'Order', quantity: 'Colli', date: 'Datum', directInvoice: 'Directe factuur' };

describe('traceability export', () => {
    test('a cell with a separator, quote or line break is quoted', () => {
        assert.equal(csvCell('Groothandel; Noord', number), '"Groothandel; Noord"');
        assert.equal(csvCell('12"', number), '"12"""');
        assert.equal(csvCell(12.5, number), '12,5');
        assert.equal(csvCell(null, number), '');
    });

    test('the recall has the sum and a line per destination and direct invoice', () => {
        const csv = recallCsv('I-2611010', {
            unit: 'colli', received: 38, shipped: 12, returned: 0, rejected: 8, onHand: 18, difference: 0,
            destinations: [{ salesPartijId: 4, salesPartijNumber: 'V-2611001', salesOrderId: 9, reference: 'VO-2026-0009', customerName: 'Groothandel G', quantity: 12, unit: 'colli', date: '2026-11-23T10:00:00Z' }],
            directInvoices: [{ salesPartijId: 5, salesPartijNumber: 'V-2611002', salesOrderId: null, reference: 'FAC-1', customerName: 'Balieklant', quantity: 4, unit: 'colli', date: '2026-11-23T00:00:00Z' }],
        }, recallLabels, { number, date });
        const lines = csv.trimEnd().split('\r\n');
        assert.equal(lines[0], 'Partij;I-2611010');
        assert.equal(lines[1], 'Ontvangen;38');
        assert.equal(lines[9], 'V-2611001;Groothandel G;VO-2026-0009;12;2026-11-23');
        assert.equal(lines[10], 'V-2611002;Balieklant;Directe factuur FAC-1;4;2026-11-23');
    });

    test('the delivery has a line per order line and purchase lot', () => {
        const csv = deliveryCsv('V-2611001', {
            customerName: 'Groothandel G', salesOrderId: 9, reference: 'VO-2026-0009', sequence: 1, deliveryDate: '2026-11-23T10:00:00Z', shipped: true,
            lines: [{ product: 'Tomaat Ronde', quantity: 25, unit: 'colli', sources: [
                { partijId: 1, number: 'I-2611010', vendorName: 'Kweker K', origin: 'Spanje', quantity: 12, status: 'Consumed' },
                { partijId: 2, number: 'I-2611011', vendorName: 'Kweker K', origin: 'Marokko', quantity: 13, status: 'Active' }] }],
        }, { lot: 'Partij', customer: 'Klant', reference: 'Order', date: 'Leverdatum', product: 'Product', quantity: 'Colli', purchaseLot: 'Inkoop-partij', supplier: 'Leverancier', origin: 'Herkomst' }, { number, date });
        const lines = csv.trimEnd().split('\r\n');
        assert.equal(lines[3], 'Leverdatum;2026-11-23');
        assert.deepEqual(lines.slice(6), ['Tomaat Ronde;12;I-2611010;Kweker K;Spanje', 'Tomaat Ronde;13;I-2611011;Kweker K;Marokko']);
    });
});
