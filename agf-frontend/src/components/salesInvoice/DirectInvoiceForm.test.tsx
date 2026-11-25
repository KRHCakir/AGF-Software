import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, click, json, setValue, stubFetch, type Posted } from '../../test/reactHarness.ts';
import type { CustomerOption, ProductOption } from '../../types/referenceData';
import type { RevenueType, SalesInvoice } from '../../types/salesInvoice';
import DirectInvoiceForm from './DirectInvoiceForm';

// De directe factuur op het scherm: dezelfde regelcomponent als de verkooporder, een productregel zonder partij wordt niet verstuurd, en
// productregels en vrije regels gaan samen (eerst product, dan vrij) met de partij, omzetsoort en een sleutel naar de server.

const customers: CustomerOption[] = [{ id: 1, name: 'DEMO Marktkraam De Zon', searchCode: null, isActive: true }];
const products: ProductOption[] = [{ id: 1, name: 'Ananas', variants: [{ id: 2, name: 'MD2', origin: 'CR', packaging: '12 kg doos', sizeCategory: '8', shortcutCode: null,
    defaultPackagesPerPallet: 60, packagingUnit: 'box' as const, defaultNetKgPerPackage: 12, defaultGrossKgPerPackage: 13, vatPercentage: 9 }] }];
const revenueTypes: RevenueType[] = [{ id: 1, name: 'Omzet diensten', externalCode: null, isActive: true, isDefault: true, version: 0 }];
const lots = [{ partijId: 7, number: 'I-2611001', unit: 'box', availableQuantity: 30, palletCount: 1, size: '8', color: null, origin: 'CR', locations: [null], bins: [], receivedAt: '2026-11-20T08:00:00Z', vendorName: 'DEMO Kwekerij' }];

let mounted: Mounted;
let posted: Posted[];
let created: SalesInvoice[];
const field = (id: string) => mounted.container.querySelector<HTMLInputElement>(`[id="${id}"]`)!;
const type = (id: string, text: string) => act(async () => { field(id).focus(); setValue(field(id), text); });
const key = (id: string, keyName: string) => act(async () => { field(id).dispatchEvent(new window.KeyboardEvent('keydown', { key: keyName, bubbles: true, cancelable: true })); });
const submit = () => act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });

describe('direct invoice form', () => {
    beforeEach(() => {
        created = [];
        posted = stubFetch({
            onGet: (url) => (url.includes('/lot-options') ? json(200, lots) : json(404, {})),
            nextPost: async () => json(201, { id: 12 }),
        });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    const render = () => mounted.render(<DirectInvoiceForm customers={customers} products={products} serviceArticles={[]} revenueTypes={revenueTypes}
        onCreated={(invoice) => created.push(invoice)} onCancel={() => undefined} />);

    test('nothing is sent without a customer and lines', async () => {
        await render();
        await submit();
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('si_direct_customer_required'));
        assert.ok(mounted.hasText('si_needs_lines'));
    });

    test('a product line without a lot is not sent; with the lot and a free line both go to the server', async () => {
        await render();
        await type('customerId-input', 'Marktkraam');
        await key('customerId-input', 'Tab');
        await type('lines[0].productVariantId-input', 'Ananas');
        await key('lines[0].productVariantId-input', 'Tab');
        await type('lines[0].quantity-input', '10');
        await type('lines[0].unitPrice-input', '12,50');
        await submit();
        assert.equal(posted.length, 0);
        assert.ok(mounted.hasText('si_direct_lot_required'));

        await type('lines[0].partijId-input', '2611001');
        await mounted.waitFor(() => (mounted.container.querySelector('[role="option"]') ? true : null), 'lot options');
        await key('lines[0].partijId-input', 'Enter');
        await click(mounted.container.querySelector<HTMLElement>('[data-tour="si-add-service"]')!);
        await type('services[0].description', 'Stroom koelcel november');
        await type('services[0].unitPrice', '120,50');
        await submit();
        await mounted.waitFor(() => (posted.length === 1 ? true : null), 'POST');
        const body = JSON.parse(posted[0].body);
        assert.equal(body.customerId, 1);
        assert.equal(typeof body.requestId, 'string');
        assert.deepEqual(body.lines, [
            { lineKind: 'Product', productVariantId: 2, partijId: 7, quantity: 10, unit: 'box', unitPrice: 12.5, discountPercentage: null, vatPercentage: 9 },
            { lineKind: 'Service', costArticleId: null, description: 'Stroom koelcel november', quantity: 1, unitPrice: 120.5, discountPercentage: null, vatPercentage: 21, revenueTypeId: 1 },
        ]);
    });
});
