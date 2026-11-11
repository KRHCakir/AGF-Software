import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json } from '../../test/reactHarness.ts';
import PurchaseInvoiceDetail from './PurchaseInvoiceDetail';

// De bijlage van een goedgekeurd AI-voorstel: het venster gaat direct bij de klik open (vóór het ophalen, anders blokkeert de browser
// het als pop-up) en krijgt daarna het document; lukt ophalen niet, dan gaat het venster dicht en staat er een melding.

const invoice = {
    id: 9, requestId: null, vendorId: 1, vendorName: 'Zuid Import', invoiceNumber: 'F-1', invoiceDate: '2026-11-10T00:00:00Z', paymentTermsDays: 30, dueDate: null,
    currency: 'EUR', status: 'Draft', subTotal: 10, vatAmount: 0.9, totalAmount: 10.9, createdAt: '2026-11-10T10:00:00Z', internalNote: null, lines: [], aiReading: null,
    attachments: [{ id: 3, fileName: 'factuur.pdf', mediaType: 'application/pdf', sizeBytes: 2000, sha256: 'a'.repeat(64), createdAt: '2026-11-10T10:00:00Z' }],
};
const allocations = { invoiceId: 9, currency: 'EUR', exchangeRate: null, exchangeRateDate: null, exchangeRateSource: null, rateLocked: false, canAllocate: true, lines: [] };

let mounted: Mounted;
let events: string[];
let attachmentReply: () => Response;
let opened: { closed: boolean; opener: unknown; location: { href: string }; close: () => void };

const testId = (id: string) => mounted.container.querySelector<HTMLElement>(`[data-testid="${id}"]`);

describe('invoice attachment', () => {
    beforeEach(() => {
        events = [];
        attachmentReply = () => new Response(new Blob(['%PDF-1.7']), { status: 200, headers: { 'Content-Type': 'application/pdf' } });
        globalThis.fetch = (async (input: RequestInfo | URL) => {
            const path = new URL(String(input)).pathname.replace(/^\/api/, '');
            if (path === '/purchase-invoices/9') return json(200, invoice);
            if (path === '/purchase-invoices/9/allocations') return json(200, allocations);
            if (path === '/purchase-invoices/9/attachments/3') { events.push('fetch'); return attachmentReply(); }
            return json(404, {});
        }) as typeof fetch;
        opened = { closed: false, opener: {}, location: { href: '' }, close: () => { events.push('close'); opened.closed = true; } };
        window.open = (() => { events.push('open'); return opened; }) as unknown as typeof window.open;
        URL.createObjectURL = () => 'blob:attachment';
        URL.revokeObjectURL = () => undefined;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the window opens on the click, before the download, and then shows the document', async () => {
        await mounted.render(<PurchaseInvoiceDetail invoiceId={9} onClose={() => {}} />);
        await mounted.waitFor(() => testId('invoice-attachment'), 'attachment button');
        await act(async () => { testId('invoice-attachment')!.click(); });
        await mounted.waitFor(() => opened.location.href === 'blob:attachment', 'document in the window');
        assert.deepEqual(events, ['open', 'fetch']);
        assert.equal(opened.opener, null);
        assert.equal(testId('invoice-attachment-failed'), null);
    });

    test('a failed download closes the window and says so', async () => {
        attachmentReply = () => json(500, {});
        await mounted.render(<PurchaseInvoiceDetail invoiceId={9} onClose={() => {}} />);
        await mounted.waitFor(() => testId('invoice-attachment'), 'attachment button');
        await act(async () => { testId('invoice-attachment')!.click(); });
        await mounted.waitFor(() => testId('invoice-attachment-failed'), 'failure notice');
        assert.deepEqual(events, ['open', 'fetch', 'close']);
    });
});
