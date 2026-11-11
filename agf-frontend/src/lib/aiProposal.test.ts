import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { AiExtraction } from '../types/aiReading';
import type { PurchaseInvoiceSettings, PurchaseVendor } from '../types/purchaseInvoice';
import { draftFromProposal, needsAttention, problemsByField } from './aiProposal.ts';

const settings: PurchaseInvoiceSettings = { allOptionalFields: [], optionalFields: [], defaultVatPercentage: 9, defaultPaymentTermsDays: 14, defaultCurrency: 'EUR' };
const vendor = (id: number, over: Partial<PurchaseVendor> = {}): PurchaseVendor => ({
    id, name: `Leverancier ${id}`, vatNumber: null, paymentTermsDays: 30, defaultCurrency: 'EUR', searchCode: null, isActive: true,
    paymentConditionId: 1, paymentConditionCode: '30D', paymentConditionBasis: 'InvoiceDate', hasDefaultBankAccount: true, defaultBankAccountUnconfirmed: false, ...over,
});
export const extraction = (over: Partial<AiExtraction> = {}): AiExtraction => ({
    id: 7, status: 'Proposed', fileName: 'factuur.pdf', mediaType: 'application/pdf', displayMediaType: 'application/pdf', sizeBytes: 1000, provider: 'Anthropic',
    model: 'claude-haiku-5-5', modelName: 'Claude Haiku 5.5', modelVersion: 'claude-haiku-5-5-20261101', usedFallback: false, costEur: 0.001,
    proposal: {
        vendorName: 'Fruitimport Zuid B.V.', vendorVatNumber: null, vendorRegistrationNumber: null, vendorIban: 'NL91ABNA0417164300', supplierOrderNumber: '45-21',
        invoiceNumber: 'F-2026-118', invoiceDate: '2026-11-10', currency: 'USD',
        lines: [
            { description: 'Avocado Hass 4 kg', quantity: 1250, unitText: 'dozen', unit: 'box', unitPrice: 12.5, amount: 15625, vatPercentage: 9 },
            { description: null, quantity: 1, unitText: null, unit: null, unitPrice: 0.1234, amount: 0.12, vatPercentage: null },
        ],
        totalNet: 15625.12, totalVat: 1406.25, totalGross: 17031.37,
    },
    problems: [{ field: 'lines[1].description', code: 'missing' }, { field: 'vendor', code: 'uncertain' }, { field: 'vendor', code: 'uncertain' }],
    matches: { vendorCandidates: [{ vendorId: 3, name: 'Leverancier 3', matchedOn: ['iban'] }], suggestedVendorId: 3, orderCandidates: [], suggestedOrderId: null, duplicateInvoice: null, lineCostArticles: [] },
    ...over,
});

describe('aiProposal', () => {
    test('the proposal fills the form in nl notation; the supplier only when found; missing stays empty and nothing is invented', () => {
        const draft = draftFromProposal(extraction(), settings, [vendor(3)], '2026-11-12');
        assert.deepEqual([draft.header.vendorId, draft.header.invoiceNumber, draft.header.invoiceDate, draft.header.currency, draft.header.paymentTermsDays, draft.header.dueDate],
            ['3', 'F-2026-118', '2026-11-10', 'USD', '30', '2026-12-10']);
        assert.deepEqual(draft.lines.map((line) => [line.description, line.quantity, line.unit, line.unitPrice, line.vat, line.costArticleId]), [
            ['Avocado Hass 4 kg', '1.250', 'box', '12,50', '9', ''],
            ['', '1', '', '0,1234', '9', ''],
        ]);
        const unknown = draftFromProposal(extraction({ matches: { ...extraction().matches!, suggestedVendorId: null } }), settings, [vendor(3)], '2026-11-12');
        assert.equal(unknown.header.vendorId, '');
        assert.equal(draftFromProposal(extraction(), settings, [vendor(3, { isActive: false })], '2026-11-12').header.vendorId, '', 'never an inactive supplier');
        assert.deepEqual(draftFromProposal(extraction({ proposal: null }), settings, [], '2026-11-12').lines.length, 1);
    });

    test('problems are keyed by the input field, once per code; the description sits in the item field', () => {
        assert.deepEqual(problemsByField(extraction()), { 'lines[1].item': ['missing'], vendorId: ['uncertain'] });
        assert.deepEqual(problemsByField(null), {});
    });

    test('a yellow warning on top only when totals do not add up or a required field is missing', () => {
        assert.equal(needsAttention(extraction()), false);
        assert.equal(needsAttention(extraction({ problems: [{ field: 'totalNet', code: 'linesMismatch' }] })), true);
        assert.equal(needsAttention(extraction({ problems: [{ field: 'invoiceNumber', code: 'missing' }] })), true);
    });
});
