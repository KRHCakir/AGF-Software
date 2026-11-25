import '../../../test/setupDom.ts';
import '../../../test/nextNavigationStub.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setSelectValue, setValue, stubFetch, type Posted } from '../../../test/reactHarness.ts';
import { navigate } from '../../../test/nextNavigationStub.ts';
import type { SalesInvoice, SalesInvoiceListItem } from '../../../types/salesInvoice';
import SalesInvoicesPage from './page';

// Verkoopfacturen (verkoopfactuur 1): de lijst zoekt en sorteert op de server, een concept heeft geen nummer, Definitief vraagt om een
// bevestiging en een selectie van concepten wordt samen definitief gemaakt.

const item = (over: Partial<SalesInvoiceListItem> = {}): SalesInvoiceListItem => ({
    id: 5, kind: 'Delivery', status: 'Concept', invoiceNumber: null, invoiceDate: '2026-11-25T09:00:00Z', deliveryDate: '2026-11-24T09:00:00Z', dueDate: null,
    customerId: 1, customerName: 'DEMO Groothandel Noord', salesLotNumbers: ['V-2611001'], purchaseLotNumbers: ['I-2611001'], orderNumbers: ['VO-2026-0003'],
    referenceNumber: 'PO-778', net: 337.5, vat: 30.38, gross: 367.88, linesWithoutPrice: 0, stale: false, createdAt: '2026-11-25T09:00:00Z', version: 2, ...over,
});

const invoice = (over: Partial<SalesInvoice> = {}): SalesInvoice => ({
    id: 5, kind: 'Delivery', status: 'Concept', invoiceNumber: null, invoiceDate: '2026-11-25T09:00:00Z', deliveryDate: '2026-11-24T09:00:00Z', dueDate: null,
    paymentTermDays: null, customerId: 1, customerName: 'DEMO Groothandel Noord', customerAddress: 'Veilingweg 5', customerVatNumber: 'NL001234567B01',
    referenceNumber: 'PO-778', salesLotNumber: null,
    deliveries: [{ salesDeliveryId: 9, salesOrderId: 3, orderNumber: 'VO-2026-0003', sequence: 1, partijNumber: 'V-2611001', deliveredAt: '2026-11-24T09:00:00Z', customerReference: 'PO-778' }],
    lines: [{ id: 41, lineKind: 'Product', sortOrder: 0, salesDeliveryId: 9, salesOrderLineId: 7, salesOrderServiceLineId: null, salesOrderPackagingLineId: null,
        productVariantId: 2, partijId: null, partijNumber: null, costArticleId: null, revenueTypeId: null, revenueTypeName: null, description: 'Ananas MD2, 12 kg doos',
        quantity: 30, unit: 'box', unitPrice: 12.5, discountPercentage: 10, vatPercentage: 9, amount: 337.5, vatAmount: 30.38, netWeightKg: 360, origin: 'CR',
        lotNumber: 'I-2611001', ggn: '4049929999999', maxQuantity: 30 }],
    totals: { net: 337.5, vatRates: [{ percentage: 9, net: 337.5, vat: 30.38 }], vat: 30.38, gross: 367.88, linesWithoutPrice: 0 },
    createdAt: '2026-11-25T09:00:00Z', createdBy: 'Jan', finalizedAt: null, finalizedBy: null, discardedAt: null, discardedBy: null, discardReason: null, stale: false, version: 2,
    ...over,
});

let mounted: Mounted;
let posted: Posted[];
let listUrls: string[];
let current: SalesInvoice;
let items: SalesInvoiceListItem[];

describe('Sales invoices page', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        window.localStorage.clear();
        listUrls = [];
        current = invoice();
        items = [item(), item({ id: 6, status: 'Final', invoiceNumber: 'VF-2026-00001', customerName: 'DEMO Marktkraam', version: 4 })];
        posted = stubFetch({
            onGet: (url) => {
                if (/\/sales-invoices\?/.test(url)) { listUrls.push(url); return json(200, { items, total: items.length, page: 1, pageSize: 50 }); }
                if (/\/sales-invoices\/5$/.test(url)) return json(200, current);
                return json(404, { message: `unexpected GET ${url}` });
            },
            nextPost: async () => {
                const last = posted[posted.length - 1];
                if (last.url.endsWith('/sales-invoices/finalize')) {
                    return json(200, { results: [{ id: 5, final: true, invoiceNumber: 'VF-2026-00002', version: 3, message: null, errors: null }] });
                }
                current = invoice({ status: 'Final', invoiceNumber: 'VF-2026-00002', finalizedAt: '2026-11-25T10:00:00Z', finalizedBy: 'Jan', dueDate: '2026-12-09T00:00:00Z', version: 3 });
                return json(200, current);
            },
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('lists drafts without a number, searches and filters on the server', async () => {
        navigate('/sales-invoices');
        await mounted.render(<SalesInvoicesPage />);
        await mounted.waitFor(() => mounted.hasText('VF-2026-00001'), 'list');
        assert.ok(listUrls[0].includes('status=all'));
        assert.ok(mounted.hasText('si_concept'));
        await act(async () => { setValue(mounted.container.querySelector<HTMLInputElement>('#si-search')!, '2611001'); await new Promise((resolve) => setTimeout(resolve, 300)); });
        await mounted.waitFor(() => listUrls.some((url) => url.includes('query=2611001')), 'search');
        await act(async () => { setSelectValue(mounted.container.querySelector<HTMLSelectElement>('#si-status')!, 'open'); });
        await mounted.waitFor(() => listUrls.some((url) => url.includes('status=open')), 'filter');
    });

    test('opens a draft with VAT per rate and makes it final only after the confirmation', async () => {
        navigate('/sales-invoices?id=5');
        await mounted.render(<SalesInvoicesPage />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="si-detail"]'), 'detail');
        assert.equal(mounted.container.querySelectorAll('[data-testid="si-vat-rate"]').length, 1);
        assert.ok(mounted.hasText('GGN 4049929999999'));
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-finalize"]')!);
        assert.equal(posted.length, 0, 'nothing happens before the confirmation');
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-finalize-confirm"]')!);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="si-title"]')?.textContent === 'VF-2026-00002' ? true : null, 'final');
        assert.deepEqual(JSON.parse(posted[0].body), { expectedVersion: 2 });
        assert.equal(mounted.container.querySelector('[data-tour="si-finalize"]'), null, 'a final invoice has no Make final');
    });

    test('a draft without prices cannot be made final', async () => {
        current = invoice({ lines: [{ ...invoice().lines[0], unitPrice: null, amount: null }], totals: { net: null, vatRates: [], vat: null, gross: null, linesWithoutPrice: 1 } });
        navigate('/sales-invoices?id=5');
        await mounted.render(<SalesInvoicesPage />);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="si-blockers"]'), 'blockers');
        assert.equal(mounted.container.querySelector<HTMLButtonElement>('[data-tour="si-finalize"]')!.disabled, true);
    });

    test('a selection of drafts is made final together after one confirmation', async () => {
        navigate('/sales-invoices');
        await mounted.render(<SalesInvoicesPage />);
        const box = await mounted.waitFor(() => mounted.container.querySelector<HTMLInputElement>('[data-tour="si-choose"]'), 'checkbox');
        await click(box);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-finalize-many"]')!);
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-finalize-many-confirm"]')!);
        await mounted.waitFor(() => mounted.container.querySelector('[data-testid="si-results"]'), 'results');
        assert.deepEqual(JSON.parse(posted[0].body), { invoices: [{ id: 5, expectedVersion: 2 }] });
        assert.ok(mounted.hasText('si_result_final'));
    });
});
