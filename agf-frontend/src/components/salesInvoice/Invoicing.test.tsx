import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, click, json, setValue, stubFetch, type Posted } from '../../test/reactHarness.ts';
import type { DeliveryInvoicing, SalesInvoice } from '../../types/salesInvoice';
import SalesDeliveries from '../sales/SalesDeliveries';
import type { SalesOrder } from '../../types/salesOrder';
import InvoiceConceptEditor from './InvoiceConceptEditor';
import ToInvoiceList from './ToInvoiceList';

// Verkoopfactuur 1 op het scherm: Factureren op de levering maakt een concept (en daarna staat er Concept openen), het concept is te
// bewerken (nooit meer dan geleverd, zonder verzoek naar de server), en de werklijst zet de aangevinkte leveringen van één klant in één concept.

const deliveryInvoicing = (over: Partial<DeliveryInvoicing> = {}): DeliveryInvoicing => ({
    salesDeliveryId: 9, salesOrderId: 3, orderNumber: 'VO-2026-0003', sequence: 1, partijNumber: 'V-2611001', customerId: 1, customerName: 'DEMO Groothandel Noord',
    deliveredAt: '2026-11-24T09:00:00Z', daysSinceDelivery: 1, lines: [{ salesOrderLineId: 7, description: 'Ananas MD2', unit: 'box', delivered: 30, invoiced: 0, rest: 30 }],
    hasRest: true, openConceptId: null, invoices: [], ...over,
});

const concept = (over: Partial<SalesInvoice> = {}): SalesInvoice => ({
    id: 5, kind: 'Delivery', status: 'Concept', invoiceNumber: null, invoiceDate: '2026-11-25T09:00:00Z', deliveryDate: null, dueDate: null, paymentTermDays: null,
    customerId: 1, customerName: 'DEMO Groothandel Noord', customerAddress: null, customerVatNumber: null, referenceNumber: null, salesLotNumber: null, deliveries: [],
    lines: [{ id: 41, lineKind: 'Product', sortOrder: 0, salesDeliveryId: 9, salesOrderLineId: 7, salesOrderServiceLineId: null, salesOrderPackagingLineId: null,
        productVariantId: 2, partijId: null, partijNumber: null, costArticleId: null, revenueTypeId: null, revenueTypeName: null, description: 'Ananas MD2',
        quantity: 30, unit: 'box', unitPrice: 12.5, discountPercentage: 0, vatPercentage: 9, amount: 375, vatAmount: 33.75, netWeightKg: 360, origin: 'CR',
        lotNumber: 'I-2611001', ggn: null, maxQuantity: 30 }],
    totals: { net: 375, vatRates: [{ percentage: 9, net: 375, vat: 33.75 }], vat: 33.75, gross: 408.75, linesWithoutPrice: 0 },
    createdAt: '2026-11-25T09:00:00Z', createdBy: 'Jan', finalizedAt: null, finalizedBy: null, discardedAt: null, discardedBy: null, discardReason: null, stale: false, version: 2,
    ...over,
});

const order = { id: 3, orderNumber: 'VO-2026-0003', status: 'Open', version: 1, lines: [], deliveries: [{ id: 9, sequence: 1, partijId: 40, partijNumber: 'V-2611001', last: true, loadAt: null, licensePlate: null, reservationIds: [], createdAt: '2026-11-24T08:00:00Z', createdBy: 'Jan' }] } as unknown as SalesOrder;

let mounted: Mounted;
let posted: Posted[];
let answer: () => Response;

describe('invoicing screens', () => {
    beforeEach(() => {
        answer = () => json(201, concept());
        posted = stubFetch({
            onGet: (url) => (/to-invoice$/.test(url)
                ? json(200, [{ customerId: 1, customerName: 'DEMO Groothandel Noord', deliveries: [deliveryInvoicing(), deliveryInvoicing({ salesDeliveryId: 10, sequence: 2, partijNumber: 'V-2611002' })] }])
                : json(404, {})),
            nextPost: async () => answer(),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('Invoice on a delivery makes a draft for that delivery and then offers Open draft', async () => {
        let refreshed = 0;
        await mounted.render(<SalesDeliveries order={order} readOnly={false} onChanged={() => undefined} invoicing={[deliveryInvoicing()]} onInvoicingChanged={() => { refreshed++; }} />);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="so-delivery-invoice"]')!);
        await mounted.waitFor(() => mounted.container.querySelector('[data-tour="so-delivery-invoice-open"]'), 'open draft');
        const body = JSON.parse(posted[0].body);
        assert.ok(posted[0].url.endsWith('/sales-invoices/from-deliveries'));
        assert.deepEqual(body.deliveryIds, [9]);
        assert.equal(typeof body.requestId, 'string');
        assert.equal(mounted.container.querySelector('[data-tour="so-delivery-invoice-open"]')!.getAttribute('href'), '/sales-invoices?id=5');
        assert.equal(refreshed, 1);
    });

    test('a fully invoiced delivery shows its invoice number and no Invoice button', async () => {
        await mounted.render(<SalesDeliveries order={order} readOnly onChanged={() => undefined}
            invoicing={[deliveryInvoicing({ hasRest: false, invoices: [{ id: 6, status: 'Final', invoiceNumber: 'VF-2026-00001' }] })]} />);
        assert.ok(mounted.hasText('VF-2026-00001'));
        assert.equal(mounted.container.querySelector('[data-tour="so-delivery-invoice"]'), null);
    });

    test('editing a draft: more than delivered is refused on the screen; a lower quantity and price go to the server with the version', async () => {
        const saved: SalesInvoice[] = [];
        await mounted.render(<InvoiceConceptEditor invoice={concept()} onChanged={(next) => saved.push(next)} />);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-edit"]')!);
        const quantity = mounted.container.querySelector<HTMLInputElement>('[id="lines[0].quantity"]')!;
        setValue(quantity, '31');
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-edit-save"]')!);
        assert.ok(mounted.hasText('si_edit_quantity_too_much'));
        assert.equal(posted.length, 0);

        const puts: Posted[] = [];
        globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
            puts.push({ url: String(input), body: String(init?.body) });
            return json(200, concept({ version: 3 }));
        }) as typeof fetch;
        setValue(quantity, '20');
        setValue(mounted.container.querySelector<HTMLInputElement>('[id="lines[0].unitPrice"]')!, '13,25');
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-edit-save"]')!);
        await mounted.waitFor(() => (saved.length === 1 ? true : null), 'saved');
        assert.ok(puts[0].url.endsWith('/sales-invoices/5'));
        assert.deepEqual(JSON.parse(puts[0].body), { expectedVersion: 2, referenceNumber: null, lines: [{ id: 41, quantity: 20, unitPrice: 13.25, discountPercentage: null, vatPercentage: 9 }] });
    });

    test('switching to another draft with fewer lines while editing does not crash and shows the new lines', async () => {
        const two = concept();
        two.lines = [two.lines[0], { ...two.lines[0], id: 42, description: 'Mango Kent', sortOrder: 1 }];
        await mounted.render(<InvoiceConceptEditor invoice={two} onChanged={() => undefined} />);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-edit"]')!);
        assert.equal(mounted.container.querySelectorAll('[data-testid="si-edit-line"]').length, 2);
        const other = concept({ id: 6 });
        other.lines = [{ ...other.lines[0], id: 50, description: 'Citroen Primofiori' }];
        await mounted.render(<InvoiceConceptEditor invoice={other} onChanged={() => undefined} />);
        assert.equal(mounted.container.querySelectorAll('[data-testid="si-edit-line"]').length, 1);
        assert.ok(mounted.hasText('Citroen Primofiori'));
    });

    test('the worklist puts the ticked deliveries of one customer in one draft', async () => {
        const created: number[] = [];
        await mounted.render(<ToInvoiceList token={0} onCreated={(id) => created.push(id)} />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-tour="si-to-invoice-create"]'), 'worklist');
        assert.equal(mounted.container.querySelectorAll<HTMLInputElement>('[data-tour="si-to-invoice-choose"]:checked').length, 2);
        assert.equal(mounted.container.querySelectorAll('[data-testid="si-days"]').length, 2);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-to-invoice-create"]')!);
        await mounted.waitFor(() => (created.length === 1 ? true : null), 'created');
        assert.deepEqual(JSON.parse(posted[0].body).deliveryIds, [9, 10]);
    });
});
