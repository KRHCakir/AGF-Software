import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, stubFetch, type Posted } from '../../test/reactHarness.ts';
import type { SalesInvoice } from '../../types/salesInvoice';
import SalesInvoiceDetail from './SalesInvoiceDetail';

// Het detail van een verkoopfactuur: regels met partij en GGN, btw per tarief, Definitief alleen na een bevestiging en nooit zonder prijzen,
// weggooien met een reden.

const invoice = (over: Partial<SalesInvoice> = {}): SalesInvoice => ({
    id: 5, kind: 'Delivery', status: 'Concept', invoiceNumber: null, invoiceDate: '2026-11-25T09:00:00Z', deliveryDate: '2026-11-24T09:00:00Z', dueDate: null,
    paymentTermDays: null, customerId: 1, customerName: 'DEMO Groothandel Noord', customerAddress: 'Veilingweg 5', customerVatNumber: 'NL001234567B01',
    referenceNumber: 'PO-778', salesLotNumber: null,
    deliveries: [{ salesDeliveryId: 9, salesOrderId: 3, orderNumber: 'VO-2026-0003', sequence: 1, partijNumber: 'V-2611001', deliveredAt: '2026-11-24T09:00:00Z', customerReference: 'PO-778' }],
    lines: [
        { id: 41, lineKind: 'Product', sortOrder: 0, salesDeliveryId: 9, salesOrderLineId: 7, salesOrderServiceLineId: null, salesOrderPackagingLineId: null,
            productVariantId: 2, partijId: null, partijNumber: null, costArticleId: null, revenueTypeId: null, revenueTypeName: null, description: 'Ananas MD2, 12 kg doos',
            quantity: 30, unit: 'box', unitPrice: 12.5, discountPercentage: 10, vatPercentage: 9, amount: 337.5, vatAmount: 30.38, netWeightKg: 360, origin: 'CR',
            lotNumber: 'I-2611001', ggn: '4049929999999', maxQuantity: 30 },
        { id: 42, lineKind: 'Service', sortOrder: 1, salesDeliveryId: null, salesOrderLineId: null, salesOrderServiceLineId: 3, salesOrderPackagingLineId: null,
            productVariantId: null, partijId: null, partijNumber: null, costArticleId: 4, revenueTypeId: 1, revenueTypeName: 'Omzet diensten', description: 'Handling',
            quantity: 1, unit: 'piece', unitPrice: 25, discountPercentage: 0, vatPercentage: 21, amount: 25, vatAmount: 5.25, netWeightKg: null, origin: null,
            lotNumber: null, ggn: null, maxQuantity: 1 },
    ],
    totals: { net: 362.5, vatRates: [{ percentage: 9, net: 337.5, vat: 30.38 }, { percentage: 21, net: 25, vat: 5.25 }], vat: 35.63, gross: 398.13, linesWithoutPrice: 0 },
    createdAt: '2026-11-25T09:00:00Z', createdBy: 'Jan', finalizedAt: null, finalizedBy: null, discardedAt: null, discardedBy: null, discardReason: null, stale: false, version: 2,
    ...over,
});

let mounted: Mounted;
let posted: Posted[];
let changed: SalesInvoice[];

describe('sales invoice detail', () => {
    beforeEach(() => {
        changed = [];
        posted = stubFetch({
            onGet: () => json(404, {}),
            nextPost: async () => {
                const last = posted[posted.length - 1];
                return last.url.endsWith('/discard')
                    ? json(200, invoice({ status: 'Discarded', discardedAt: '2026-11-25T10:00:00Z', discardedBy: 'Jan', discardReason: 'Verkeerde klant', version: 3 }))
                    : json(200, invoice({ status: 'Final', invoiceNumber: 'VF-2026-00002', version: 3 }));
            },
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('lines with lot and GGN, extras apart and VAT per rate', async () => {
        await mounted.render(<SalesInvoiceDetail invoice={invoice()} onChanged={(next) => changed.push(next)} />);
        assert.equal(mounted.container.querySelector('[data-testid="si-title"]')!.textContent, 'si_concept');
        assert.equal(mounted.container.querySelectorAll('[data-testid="si-goods"] [data-testid="si-line"]').length, 1);
        assert.equal(mounted.container.querySelectorAll('[data-testid="si-extras"] [data-testid="si-line"]').length, 1);
        assert.ok(mounted.hasText('GGN 4049929999999'));
        assert.ok(mounted.hasText('Omzet diensten'));
        assert.equal(mounted.container.querySelectorAll('[data-testid="si-vat-rate"]').length, 2);
        assert.ok(mounted.hasText('V-2611001'));
    });

    test('Make final only after the confirmation, with the version that was seen', async () => {
        await mounted.render(<SalesInvoiceDetail invoice={invoice()} onChanged={(next) => changed.push(next)} />);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-finalize"]')!);
        assert.equal(posted.length, 0);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-finalize-confirm"]')!);
        await mounted.waitFor(() => (changed.length === 1 ? true : null), 'changed');
        assert.ok(posted[0].url.endsWith('/sales-invoices/5/finalize'));
        assert.deepEqual(JSON.parse(posted[0].body), { expectedVersion: 2 });
        assert.equal(changed[0].invoiceNumber, 'VF-2026-00002');
    });

    test('without prices there is no Make final; a final invoice offers only the PDF', async () => {
        await mounted.render(<SalesInvoiceDetail invoice={invoice({ lines: [{ ...invoice().lines[0], unitPrice: null, amount: null }], totals: { net: null, vatRates: [], vat: null, gross: null, linesWithoutPrice: 1 } })} onChanged={() => undefined} />);
        assert.ok(mounted.container.querySelector('[data-testid="si-blockers"]'));
        assert.equal(mounted.container.querySelector<HTMLButtonElement>('[data-tour="si-finalize"]')!.disabled, true);
        await mounted.unmount();
        mounted = new Mounted();
        await mounted.render(<SalesInvoiceDetail invoice={invoice({ status: 'Final', invoiceNumber: 'VF-2026-00001' })} onChanged={() => undefined} />);
        assert.equal(mounted.container.querySelector('[data-tour="si-finalize"]'), null);
        assert.equal(mounted.container.querySelector('[data-tour="si-discard"]'), null);
        assert.ok(mounted.container.querySelector('[data-tour="si-pdf"]'));
    });

    test('discarding asks for confirmation and sends the reason', async () => {
        await mounted.render(<SalesInvoiceDetail invoice={invoice()} onChanged={(next) => changed.push(next)} />);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-discard"]')!);
        const reason = mounted.container.querySelector<HTMLInputElement>('#si-discard-reason')!;
        const { setValue } = await import('../../test/reactHarness.ts');
        setValue(reason, 'Verkeerde klant');
        await click([...mounted.container.querySelectorAll<HTMLButtonElement>('[data-testid="si-confirm-discard"] button')][0]);
        await mounted.waitFor(() => (changed.length === 1 ? true : null), 'discarded');
        assert.deepEqual(JSON.parse(posted[0].body), { expectedVersion: 2, reason: 'Verkeerde klant' });
    });
});
