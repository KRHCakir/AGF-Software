import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { setInputLocale } from '../../lib/decimal.ts';
import { purchaseOrderChain } from '../../lib/chain.ts';
import { Mounted, json, setValue, stubFetch } from '../../test/reactHarness.ts';
import type { Posted } from '../../test/reactHarness.ts';
import type { PurchaseOrderDetail, PurchaseOrderListItem } from '../../types/purchaseOrder';
import PurchaseOrderDocument, { PurchaseOrderChanges } from './PurchaseOrderDocument';
import PurchaseOrderList from './PurchaseOrderList';

// Documentstatus op de order (stap 4a): Open = Aanpassen en Definitief maken (met bevestiging); Definitief = de melding wie en
// wanneer, alleen-lezen, en Correctie met verplichte reden. De lijst toont de badge en Aanpassen; de keten-strip de status.

setInputLocale('nl');
const order = (over: Partial<PurchaseOrderDetail> = {}): PurchaseOrderDetail => ({
    id: 5, orderNumber: 'IO-2026-0005', vendorId: 3, vendorName: 'Supplier B', supplierReference: '45-21', orderedOn: '2026-11-10', orderChannel: null, note: null,
    route: 'Warehouse', customerName: null, origin: '', destination: null, expectedAt: null, status: 'Ordered', storedStatus: 'Expected', invoiceIndicator: 'None',
    expectedQuantity: 20, expectedNetWeightKg: 80, receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0,
    lines: [{ id: 7, productVariantId: 1, productName: 'Avocado', productVariantName: 'Hass', lotNumber: null, expectedQuantity: 20, unit: 'colli', expectedNetWeightKg: 80, unitPrice: 12.5,
        receivedQuantity: 0, receivedNetWeightKg: 0, rejectedQuantity: 0, rejectedNetWeightKg: 0 }],
    receipts: [], invoiceRefs: [], rejections: [], history: [], documentStatus: 'Open', editVersion: 2, editable: true, ...over,
});
const definitive = (over: Partial<PurchaseOrderDetail> = {}) => order({ documentStatus: 'Definitive', finalizedAt: '2026-11-12T09:30:00Z', finalizedBy: 'Jan', finalizedVia: 'Manual', editable: false, editVersion: 3, ...over });

let mounted: Mounted;
let posted: Posted[];
let answer: () => Promise<Response>;
let changed: PurchaseOrderDetail[];
const $ = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);

describe('purchase order document status', () => {
    beforeEach(() => {
        changed = [];
        answer = async () => json(200, definitive());
        posted = stubFetch({ onGet: () => json(404, {}), nextPost: () => answer() });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('an open order offers Edit and Make definitive; the question comes first on the page and only yes sends', async () => {
        await mounted.render(<PurchaseOrderDocument order={order()} onChanged={(o) => changed.push(o)} />);
        assert.equal($('[data-tour="po-edit"]')!.getAttribute('href'), '/purchase-orders/edit?id=5');
        await act(async () => { $('[data-tour="po-finalize"]')!.click(); });
        assert.ok($('[data-testid="po-finalize-question"]')!.textContent!.includes('po_finalize_confirm'));
        assert.equal(posted.length, 0, 'the question comes first');
        await act(async () => { ([...mounted.container.querySelectorAll<HTMLButtonElement>('[data-testid="po-finalize-question"] button')].find((b) => b.textContent === 'cancel'))!.click(); });
        assert.equal($('[data-testid="po-finalize-question"]'), null);
        assert.equal(posted.length, 0);

        await act(async () => { $('[data-tour="po-finalize"]')!.click(); });
        await act(async () => { $('[data-tour="po-finalize-yes"]')!.click(); });
        await mounted.waitFor(() => changed.length === 1, 'definitive');
        assert.ok(posted[0].url.endsWith('/purchase-orders/5/finalize'));
        assert.equal(JSON.parse(posted[0].body).expectedVersion, 2);
    });

    test('a definitive order is read-only with who and when; a correction needs a reason and sends only what changed', async () => {
        answer = async () => json(200, definitive({ editVersion: 4 }));
        await mounted.render(<PurchaseOrderDocument order={definitive()} onChanged={(o) => changed.push(o)} />);
        assert.equal($('[data-tour="po-edit"]'), null);
        assert.ok($('[data-testid="po-definitive"]')!.textContent!.includes('po_definitive_since'));
        assert.ok($('[data-testid="po-definitive"]')!.textContent!.includes('po_definitive_readonly'));

        await act(async () => { $('[data-tour="po-correct"]')!.click(); });
        const form = $('[data-testid="po-correction"]') as HTMLFormElement;
        const submit = () => act(async () => { form.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await submit();
        assert.ok(mounted.hasText('po_correct_nothing'));
        assert.ok(mounted.hasText('po_correct_reason_required'));
        assert.equal(posted.length, 0);

        await act(async () => { setValue($('[id="price-7-input"]') as HTMLInputElement, '13,10'); });
        await act(async () => { setValue($('#correction-reason-input') as HTMLTextAreaElement, 'Prijs volgens factuur'); });
        await submit();
        await mounted.waitFor(() => changed.length === 1, 'corrected');
        const body = JSON.parse(posted[0].body);
        assert.ok(posted[0].url.endsWith('/purchase-orders/5/corrections'));
        assert.deepEqual([body.reason, body.expectedVersion, body.supplierReference, body.note, body.lines], ['Prijs volgens factuur', 3, '45-21', '', [{ id: 7, unitPrice: 13.1 }]]);
    });

    test('the changes list shows who and what, with numbers and dates in the screen notation', async () => {
        await mounted.render(<PurchaseOrderChanges order={order({ changes: [{
            id: 1, kind: 'Edit', reason: null, actorReference: 'Jan', createdAt: '2026-11-12T09:00:00Z',
            fields: [{ field: 'unitPrice', lineId: 7, action: 'changed', old: '12.5', new: '13' }, { field: 'expectedAt', lineId: null, action: 'changed', old: null, new: '2026-11-14' },
                { field: 'supplierReference', lineId: null, action: 'changed', old: '0045', new: '45-21' },
                { field: 'line', lineId: null, action: 'added', old: null, new: 'Mango Kent: 10 colli' }],
        }] })} />);
        const text = $('[data-testid="po-changes"]')!.textContent!;
        assert.ok(text.includes('po_change_price · Avocado Hass: 12,50 → 13,00'), text);
        assert.ok(text.includes('po_change_empty →'), text);
        assert.ok(text.includes('po_supplier_reference: 0045 → 45-21'), 'a supplier number stays as recorded');
        assert.ok(text.includes('po_change_added: Mango Kent: 10 colli'), text);
    });

    test('the list shows the badge and Edit only while open; the chain strip names the document status', async () => {
        const item = (over: Partial<PurchaseOrderListItem>): PurchaseOrderListItem => ({
            id: 5, orderNumber: 'IO-2026-0005', vendorId: 3, vendorName: 'Supplier B', supplierReference: null, orderedOn: '2026-11-10', expectedAt: null, route: 'Warehouse',
            customerName: null, status: 'Ordered', storedStatus: 'Expected', expectedQuantity: 20, expectedNetWeightKg: null, receivedQuantity: 0, rejectedQuantity: 0,
            invoiceNumbers: [], invoiceIndicator: 'None', documentStatus: 'Open', ...over,
        });
        await mounted.render(<PurchaseOrderList items={[item({}), item({ id: 6, orderNumber: 'IO-2026-0006', documentStatus: 'Definitive' })]} selectedId={null} onOpen={() => {}} />);
        const edits = [...mounted.container.querySelectorAll('[data-tour="po-list-edit"]')].map((link) => link.getAttribute('href'));
        assert.deepEqual(edits, ['/purchase-orders/edit?id=5']);
        assert.ok(mounted.container.querySelector('[data-tour="po-document-status"][data-tour-key="Definitive"]'));

        assert.deepEqual(purchaseOrderChain(definitive(), 'order')[0].parts.map((part) => part.key), ['chain_order_vendor', 'chain_order_document_definitive']);
        assert.deepEqual(purchaseOrderChain(order(), 'order')[0].parts.map((part) => part.key), ['chain_order_vendor', 'chain_order_document_open']);
    });
});
