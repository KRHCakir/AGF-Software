import '../../test/setupDom.ts';
import assert from 'node:assert/strict';
import { afterEach, beforeEach, describe, test } from 'node:test';
import { act } from 'react';
import { Mounted, json, stubFetch } from '../../test/reactHarness.ts';
import type { AiExtraction } from '../../types/aiReading';
import AiReviewBanner from './AiReviewBanner';
import AiUploadPanel from './AiUploadPanel';

// De bouwstenen van het AI-voorstel: de voorgestelde order hoort bij één leverancier (bij een andere leverancier is er geen voorstel), en lukt het ophalen van de AI-status niet, dan zegt het paneel dat, met Opnieuw proberen.

const extraction: AiExtraction = {
    id: 41, status: 'Proposed', fileName: 'factuur.pdf', mediaType: 'application/pdf', displayMediaType: 'application/pdf', sizeBytes: 2000, provider: 'Anthropic',
    model: 'claude-haiku-5-5', modelName: 'Claude Haiku 5.5', modelVersion: 'claude-haiku-5-5-20261101', usedFallback: false, costEur: 0.001,
    proposal: {
        vendorName: 'Fruitimport Zuid B.V.', vendorVatNumber: null, vendorRegistrationNumber: null, vendorIban: null, supplierOrderNumber: '45-21',
        invoiceNumber: 'F-1', invoiceDate: '2026-11-10', currency: 'EUR', lines: [], totalNet: 10, totalVat: 0.9, totalGross: 10.9,
    },
    problems: [],
    matches: {
        vendorCandidates: [{ vendorId: 3, name: 'Zuid Import', matchedOn: ['iban'] }], suggestedVendorId: 3,
        orderCandidates: [{ id: 5, orderNumber: 'IO-2026-0005', supplierReference: '45-21', vendorId: 3, vendorName: 'Zuid Import' }], suggestedOrderId: 5,
        duplicateInvoice: null, lineCostArticles: [],
    },
};
const model = { id: 'claude-haiku-5-5', name: 'Claude Haiku 5.5', inputPerMillionUsd: 0.1, outputPerMillionUsd: 0.5, priceCheckedOn: '2026-11-11' };
const status = {
    enabled: true, configured: true, notConfiguredReason: null, provider: 'Anthropic', primaryModel: model, fallbackEnabled: false, fallbackModel: null,
    monthSpentEur: 0, monthlyLimitEur: 25, limitReached: false, maxFileBytes: 10 * 1024 * 1024,
};

let mounted: Mounted;
let statusReply: () => Response;
const $ = (selector: string) => mounted.container.querySelector<HTMLElement>(selector);
const settle = () => act(async () => { await new Promise((resolve) => setTimeout(resolve, 0)); });

describe('AI proposal building blocks', () => {
    beforeEach(() => {
        statusReply = () => json(200, status);
        stubFetch({ onGet: () => statusReply(), nextPost: async () => json(500, {}) });
        mounted = new Mounted();
    });
    afterEach(async () => { await mounted.unmount(); });

    test('the suggested order shows only for its own vendor', async () => {
        const links: (number | null)[] = [];
        const banner = (vendorId: string, linkOrderId: number | null) => (
            <AiReviewBanner extraction={extraction} vendorId={vendorId} onVendor={() => undefined} linkOrderId={linkOrderId} onLinkOrder={(id) => links.push(id)} />
        );
        await mounted.render(banner('3', 5));
        assert.ok($('[data-testid="ai-order-link"]'));
        assert.deepEqual(links, []);

        await mounted.render(banner('8', 5));
        assert.equal($('[data-testid="ai-order-link"]'), null);
        assert.ok($('[data-testid="ai-order-none"]'));
        assert.deepEqual(links, [], 'showing never changes the link by itself');
    });

    test('a failed status check says so and can be retried', async () => {
        statusReply = () => json(500, {});
        await mounted.render(<AiUploadPanel onProposal={() => undefined} />);
        await settle();
        assert.ok($('[data-testid="ai-status-failed"]'));
        assert.equal(($('[data-testid="ai-upload-button"]') as HTMLButtonElement).disabled, true);

        statusReply = () => json(200, status);
        await act(async () => { $('[data-testid="ai-status-failed"] button')!.click(); });
        await settle();
        assert.equal($('[data-testid="ai-status-failed"]'), null);
        assert.equal(($('[data-testid="ai-upload-button"]') as HTMLButtonElement).disabled, false);
    });
});
