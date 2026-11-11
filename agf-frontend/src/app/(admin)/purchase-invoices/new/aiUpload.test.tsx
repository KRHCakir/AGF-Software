import '../../../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { act } from 'react';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { Mounted, json, stubFetch } from '../../../../test/reactHarness.ts';
import type { Posted } from '../../../../test/reactHarness.ts';
import type { AiExtraction, AiReadingStatus } from '../../../../types/aiReading';
import NewPurchaseInvoicePage from './page';

// Factuur uploaden op Inkoopfactuur invoeren (stap 7a), met een nagebootste API: zonder AI een duidelijke melding en geen gegevens; een
// voorstel vult het formulier in nl-notatie met gele markeringen, het model en de voorgestelde order; Goedkeuren gaat via het gewone
// opslaan met de lezing; foutmeldingen met Handmatig invoeren; Afwijzen en Handmatig verder; het document boven het formulier en inklapbaar.

const settings = { allOptionalFields: [], optionalFields: [], defaultVatPercentage: 9, defaultPaymentTermsDays: 14, defaultCurrency: 'EUR' };
const vendors = [{ id: 3, name: 'Zuid Import', vatNumber: null, paymentTermsDays: 30, defaultCurrency: 'EUR', searchCode: 'ZUID', isActive: true,
    paymentConditionId: 1, paymentConditionCode: '30D', paymentConditionBasis: 'InvoiceDate', hasDefaultBankAccount: true, defaultBankAccountUnconfirmed: false }];
const model = { id: 'claude-haiku-5-5', name: 'Claude Haiku 5.5', inputPerMillionUsd: 0.1, outputPerMillionUsd: 0.5, priceCheckedOn: '2026-11-11' };
const status = (over: Partial<AiReadingStatus> = {}): AiReadingStatus => ({
    enabled: true, configured: true, notConfiguredReason: null, provider: 'Anthropic', primaryModel: model, fallbackEnabled: true,
    fallbackModel: { ...model, id: 'claude-sonnet-5-5', name: 'Claude Sonnet 5.5' }, monthSpentEur: 0, monthlyLimitEur: 25, limitReached: false, maxFileBytes: 10 * 1024 * 1024, ...over,
});
const extraction: AiExtraction = {
    id: 41, status: 'Proposed', fileName: 'factuur-zuid.pdf', mediaType: 'application/pdf', displayMediaType: 'application/pdf', sizeBytes: 2000, provider: 'Anthropic',
    model: 'claude-haiku-5-5', modelName: 'Claude Haiku 5.5', modelVersion: 'claude-haiku-5-5-20261101', usedFallback: false, costEur: 0.001,
    proposal: {
        vendorName: 'Fruitimport Zuid B.V.', vendorVatNumber: null, vendorRegistrationNumber: null, vendorIban: 'NL91ABNA0417164300', supplierOrderNumber: '45-21',
        invoiceNumber: 'F-2026-118', invoiceDate: '2026-11-10', currency: 'EUR',
        lines: [
            { description: 'Avocado Hass 4 kg', quantity: 1250, unitText: 'dozen', unit: 'box', unitPrice: 12.5, amount: 15625, vatPercentage: 9 },
            { description: 'Transport', quantity: 1, unitText: null, unit: null, unitPrice: 35, amount: 35, vatPercentage: 21 },
        ],
        totalNet: 15660, totalVat: 1413.6, totalGross: 17073.6,
    },
    problems: [{ field: 'invoiceDate', code: 'uncertain' }],
    matches: {
        vendorCandidates: [{ vendorId: 3, name: 'Zuid Import', matchedOn: ['iban'] }], suggestedVendorId: 3,
        orderCandidates: [{ id: 5, orderNumber: 'IO-2026-0005', supplierReference: '45-21', vendorId: 3, vendorName: 'Zuid Import' }], suggestedOrderId: 5,
        duplicateInvoice: null, lineCostArticles: [{ index: 1, costArticleId: 2, name: 'Transport' }],
    },
};

let mounted: Mounted;
let posted: Posted[];
let aiStatus: AiReadingStatus;
let reply: (url: string) => Response;

const $ = <T extends Element = HTMLElement>(selector: string) => mounted.container.querySelector<T>(selector);
const field = (id: string) => $<HTMLInputElement>(`[id="${id}-input"]`)!;
const click = (element: Element) => act(async () => { (element as HTMLElement).click(); });
const upload = async (file: File) => {
    const input = $<HTMLInputElement>('[data-testid="ai-upload-input"]')!;
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    await act(async () => { input.dispatchEvent(new window.Event('change', { bubbles: true })); });
};
const pdf = (size = 2000) => new File([new Uint8Array(size)], 'factuur-zuid.pdf', { type: 'application/pdf' });

describe('purchase invoice: upload and let AI fill in a proposal', () => {
    beforeEach(() => {
        window.sessionStorage.clear();
        aiStatus = status();
        reply = (url) => (url.endsWith('/purchase-invoices/ai/extractions') ? json(200, extraction) : url.includes('/close') ? json(200, {}) : json(201, { id: 90 }));
        posted = stubFetch({
            onGet: (url) => url.includes('/purchase-invoices/ai/status') ? json(200, aiStatus)
                : url.includes('/document') ? new Response(new Blob(['%PDF-1.7']), { status: 200, headers: { 'Content-Type': 'application/pdf' } })
                : url.includes('/purchase-invoices/vendors') ? json(200, vendors)
                : url.includes('/purchase-invoices/settings') ? json(200, settings)
                : url.includes('/products') ? json(200, [])
                : url.includes('/cost-articles') ? json(200, [{ id: 2, name: 'Transport', kind: 'Cost', isActive: true, isStandard: true, sortOrder: 2 }])
                : json(404, {}),
            nextPost: async () => reply(posted[posted.length - 1].url),
        });
        URL.createObjectURL = () => 'blob:document';
        URL.revokeObjectURL = () => undefined;
        window.confirm = () => true;
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('with AI off the button is disabled and says so; no proposal, no made-up data', async () => {
        aiStatus = status({ enabled: false });
        await mounted.render(<NewPurchaseInvoicePage />);
        await mounted.waitFor(() => $('[data-testid="ai-unavailable"]'), 'unavailable notice');
        assert.ok($('[data-testid="ai-unavailable"]')!.textContent!.includes('ai_off'));
        assert.equal($<HTMLButtonElement>('[data-testid="ai-upload-button"]')!.disabled, true);
        assert.equal(field('invoiceNumber').value, '');
    });

    test('a proposal fills the form in nl notation beside the document, marks the doubtful field yellow and names the model', async () => {
        await mounted.render(<NewPurchaseInvoicePage />);
        await mounted.waitFor(() => $<HTMLButtonElement>('[data-testid="ai-upload-button"]') && !$<HTMLButtonElement>('[data-testid="ai-upload-button"]')!.disabled, 'upload button');
        await upload(pdf());
        await mounted.waitFor(() => $('[data-testid="ai-review"]'), 'review');
        assert.ok(posted[0].url.endsWith('/purchase-invoices/ai/extractions'));
        assert.ok($('[data-testid="ai-review"]')!.textContent!.includes('ai_read_by'));
        assert.equal(field('invoiceNumber').value, 'F-2026-118');
        assert.equal(field('vendorId').value.includes('Zuid Import'), true);
        assert.deepEqual([field('lines[0].quantity').value, field('lines[0].unitPrice').value, field('lines[1].vatPercentage').value], ['1.250', '12,50', '21']);
        const date = field('invoiceDate').closest('div.ring-2');
        assert.ok(date, 'the doubtful date is marked yellow');
        assert.equal(date!.querySelector('[data-testid="ai-hint"]')!.textContent, 'ai_problem_uncertain');
        assert.equal($<HTMLInputElement>('[data-testid="ai-order-link"] input')!.checked, true, 'the order found on the supplier order number is proposed');
        assert.ok($('[data-testid="lines[1].ai-cost"]'), 'a cost article is only suggested, never assigned');
        // Op een telefoon staat het document boven het formulier en is het in te klappen.
        const viewer = $('[data-tour="pinvoice-ai-document"]')!;
        assert.ok(viewer.compareDocumentPosition($('form')!) & window.Node.DOCUMENT_POSITION_FOLLOWING);
        await mounted.waitFor(() => $('[data-testid="ai-document-pdf"]'), 'pdf');
        await click($('[data-testid="ai-document-toggle"]')!);
        assert.equal($('[data-testid="ai-document-pdf"]'), null);
        // Tab-volgorde van de kop blijft die van het formulier.
        const order = Array.from(mounted.container.querySelectorAll<HTMLElement>('form fieldset input')).filter((el) => el.tabIndex >= 0).map((el) => el.id).slice(0, 3);
        assert.deepEqual(order, ['vendorId-input', 'invoiceNumber-input', 'invoiceDate-input']);
    });

    test('Approve saves through the normal path with the reading and the confirmed order; a changed price goes as typed', async () => {
        await mounted.render(<NewPurchaseInvoicePage />);
        await mounted.waitFor(() => $<HTMLButtonElement>('[data-testid="ai-upload-button"]') && !$<HTMLButtonElement>('[data-testid="ai-upload-button"]')!.disabled, 'upload button');
        await upload(pdf());
        await mounted.waitFor(() => $('[data-testid="ai-review"]'), 'review');
        await act(async () => { mounted.form()!.dispatchEvent(new window.Event('submit', { bubbles: true, cancelable: true })); });
        await mounted.waitFor(() => posted.some((p) => p.url.endsWith('/purchase-invoices')), 'save');
        const body = JSON.parse(posted.find((p) => p.url.endsWith('/purchase-invoices'))!.body);
        assert.deepEqual([body.aiExtractionId, body.linkPurchaseOrderId, body.vendorId, body.invoiceNumber, body.invoiceDate], [41, 5, 3, 'F-2026-118', '2026-11-10']);
        assert.deepEqual(body.lines.map((l: { quantity: number; unitPrice: number }) => [l.quantity, l.unitPrice]), [[1250, 12.5], [1, 35]]);
    });

    test('errors explain in plain words with Enter manually; a too large file is never sent', async () => {
        reply = () => json(503, { code: 'AI_UNAVAILABLE', message: 'niet bereikbaar' });
        await mounted.render(<NewPurchaseInvoicePage />);
        await mounted.waitFor(() => $<HTMLButtonElement>('[data-testid="ai-upload-button"]') && !$<HTMLButtonElement>('[data-testid="ai-upload-button"]')!.disabled, 'upload button');
        await upload(pdf(11 * 1024 * 1024));
        assert.equal($('[data-testid="ai-error"]')!.textContent!.includes('ai_error_file_tooLarge'), true);
        assert.equal(posted.length, 0);
        await upload(pdf());
        await mounted.waitFor(() => $('[data-testid="ai-error"]')?.textContent?.includes('ai_error_AI_UNAVAILABLE'), 'unavailable');
        await click(Array.from($('[data-testid="ai-error"]')!.querySelectorAll('button')).find((b) => b.textContent === 'ai_manual_enter')!);
        assert.equal($('[data-testid="ai-error"]'), null);
        assert.equal($('[data-testid="ai-review"]'), null);
    });

    test('Continue manually keeps the values without AI approval; Reject throws the proposal away', async () => {
        await mounted.render(<NewPurchaseInvoicePage />);
        await mounted.waitFor(() => $<HTMLButtonElement>('[data-testid="ai-upload-button"]') && !$<HTMLButtonElement>('[data-testid="ai-upload-button"]')!.disabled, 'upload button');
        await upload(pdf());
        await mounted.waitFor(() => $('[data-testid="ai-review"]'), 'review');
        await click($('[data-tour="pinvoice-ai-manual"]')!);
        await mounted.waitFor(() => posted.some((p) => p.url.endsWith('/extractions/41/close')), 'close');
        assert.deepEqual(JSON.parse(posted.find((p) => p.url.endsWith('/close'))!.body), { outcome: 'Manual' });
        assert.equal($('[data-testid="ai-review"]'), null);
        assert.equal(field('invoiceNumber').value, 'F-2026-118');

        await upload(pdf());
        await mounted.waitFor(() => $('[data-testid="ai-review"]'), 'second review');
        await click($('[data-tour="pinvoice-ai-reject"]')!);
        await mounted.waitFor(() => posted.filter((p) => p.url.endsWith('/close')).length === 2, 'reject');
        assert.deepEqual(JSON.parse(posted.filter((p) => p.url.endsWith('/close'))[1].body), { outcome: 'Rejected' });
        await mounted.waitFor(() => field('invoiceNumber').value === '', 'empty form');
    });
});
