import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import {
    addDays, buildPurchaseInvoicePayload, changeDueDate, changeInvoiceDate, changePaymentTerms, describeVendor, draftTotals, dueDateDeviation,
    emptyDraft, emptyLine, followPaymentTerms, groupErrors, isBlankLine, lineAmounts, searchVendors, selectVendor,
} from './purchaseInvoiceRules.ts';
import type { InvoiceDraft } from './purchaseInvoiceRules.ts';
import type { PurchaseInvoiceSettings, PurchaseVendor } from '../types/purchaseInvoice';

const settings: PurchaseInvoiceSettings = {
    allOptionalFields: ['receivedDate', 'paymentReference', 'remarks', 'grossWeightKg', 'netWeightKg', 'containerNumber', 'transporter', 'driverName', 'licensePlate', 'incoterms', 'customsValue', 'originCountry', 'receiptReference'],
    optionalFields: ['remarks', 'receivedDate', 'grossWeightKg'],
    defaultVatPercentage: 21, defaultPaymentTermsDays: 30, defaultCurrency: 'EUR',
};
const greenery: PurchaseVendor = { id: 7, name: 'The Greenery', vatNumber: 'NL001', paymentTermsDays: 30, defaultCurrency: 'EUR', searchCode: null, isActive: true };
const importer: PurchaseVendor = { id: 12, name: 'Südfrucht Import', vatNumber: null, paymentTermsDays: 14, defaultCurrency: 'usd', searchCode: null, isActive: true };
const vendorWithoutCurrency: PurchaseVendor = { id: 3, name: 'Boer Jansen', vatNumber: null, paymentTermsDays: 7, defaultCurrency: null, searchCode: null, isActive: true };

const draft = (): InvoiceDraft => emptyDraft('2026-10-21', settings);

describe('vendor defaults and document deviations', () => {
    test('a new draft proposes today, the company terms and currency, and a due date from the terms', () => {
        const d = draft();
        assert.equal(d.header.invoiceDate, '2026-10-21');
        assert.equal(d.header.paymentTermsDays, '30');
        assert.equal(d.header.dueDate, '2026-11-20');
        assert.equal(d.header.currency, 'EUR');
        assert.equal(d.lines[0].vat, '21');
        assert.equal(d.lines[0].discount, '0');
    });

    test('selecting a vendor fills untouched defaults; a value typed on the document is kept when the vendor changes', () => {
        let d = selectVendor(draft(), importer, settings);
        assert.equal(d.header.paymentTermsDays, '14');
        assert.equal(d.header.currency, 'USD');
        assert.equal(d.header.dueDate, '2026-11-04');
        d = changePaymentTerms(d, '60');
        assert.equal(d.header.dueDate, '2026-12-20');
        d = selectVendor(d, greenery, settings);
        assert.equal(d.header.paymentTermsDays, '60', 'manual terms survive a vendor switch');
        assert.equal(d.header.dueDate, '2026-12-20');
        assert.equal(d.header.currency, 'EUR', 'untouched currency follows the new vendor');
        d = selectVendor(d, vendorWithoutCurrency, settings);
        assert.equal(d.header.currency, 'EUR', 'a vendor without currency falls back to the company default');
    });

    test('a manually changed due date is never reset silently, and the deviation from the terms stays visible', () => {
        let d = selectVendor(draft(), greenery, settings);
        d = changeDueDate(d, '2026-12-01');
        assert.equal(dueDateDeviation(d), 11);
        d = changePaymentTerms(d, '45');
        assert.equal(d.header.dueDate, '2026-12-01', 'terms change does not overwrite a manual due date');
        assert.equal(dueDateDeviation(d), -4);
        d = changeInvoiceDate(d, '2026-10-01');
        assert.equal(d.header.dueDate, '2026-12-01');
        d = followPaymentTerms(d);
        assert.equal(d.header.dueDate, '2026-11-15');
        assert.equal(dueDateDeviation(d), null);
    });

    test('changing the invoice date recomputes a due date that follows the terms', () => {
        const d = changeInvoiceDate(draft(), '2026-03-25');
        assert.equal(d.header.dueDate, '2026-04-24');
        assert.equal(addDays('2026-03-28', 2), '2026-03-30', 'DST change never shifts a day');
        assert.equal(addDays('niet-een-datum', 1), null);
    });
});

describe('vendor search', () => {
    const vendors = [greenery, importer, vendorWithoutCurrency];
    test('matches on name, VAT number and #number, accent-insensitive', () => {
        assert.deepEqual(searchVendors(vendors, 'green').map((v) => v.id), [7]);
        assert.deepEqual(searchVendors(vendors, 'nl001').map((v) => v.id), [7]);
        assert.deepEqual(searchVendors(vendors, 'sudfrucht').map((v) => v.id), [12]);
        assert.deepEqual(searchVendors(vendors, '#12').map((v) => v.id), [12]);
        assert.deepEqual(searchVendors(vendors, '3').map((v) => v.id), [3]);
        assert.deepEqual(searchVendors(vendors, ''), []);
        assert.equal(describeVendor(greenery), 'The Greenery · NL001 · #7');
        assert.equal(describeVendor(importer), 'Südfrucht Import · #12');
    });
});

describe('lines, amounts and payload', () => {
    const filled = (over: Partial<ReturnType<typeof emptyLine>>) => ({ ...emptyLine(21), description: 'Avocado Hass', quantity: '80', unit: 'box' as const, unitPrice: '12,50', ...over });

    test('live amounts are computed, never typed; incomplete lines have no amount', () => {
        assert.deepEqual(lineAmounts(filled({})), { net: 1000, vat: 210 });
        assert.deepEqual(lineAmounts(filled({ discount: '10', vat: '0', quantity: '3', unitPrice: '10' })), { net: 27, vat: 0 });
        assert.equal(lineAmounts(filled({ quantity: '' })), null);
        assert.deepEqual(draftTotals([filled({}), filled({ quantity: '' }), emptyLine(21)]), { net: 1000, vat: 210, gross: 1210 });
        assert.ok(isBlankLine(emptyLine(21)));
    });

    test('a valid draft becomes the payload with only enabled optional fields', () => {
        let d = selectVendor(draft(), greenery, settings);
        d = { ...d, header: { ...d.header, invoiceNumber: ' GR-1 ', extra: { remarks: 'Vrijdag', grossWeightKg: '320,5', containerNumber: 'MSCU1' } }, lines: [filled({ productVariantId: '4', description: '' }), emptyLine(21)] };
        const result = buildPurchaseInvoicePayload(d, settings);
        assert.equal(result.ok, true);
        if (!result.ok) return;
        assert.equal(result.value.invoiceNumber, 'GR-1');
        assert.equal(result.value.vendorId, 7);
        assert.equal(result.value.paymentTermsDays, 30);
        assert.equal(result.value.dueDate, '2026-11-20');
        assert.equal(result.value.remarks, 'Vrijdag');
        assert.equal(result.value.internalNote, null, 'no internal note by default');
        assert.equal(result.value.grossWeightKg, 320.5);
        const noted = buildPurchaseInvoicePayload({ ...d, header: { ...d.header, internalNote: '  Klant komt morgen langs ' } }, settings);
        if (noted.ok) assert.equal(noted.value.internalNote, 'Klant komt morgen langs', 'internal note trimmed and sent with the document');
        const tooLong = buildPurchaseInvoicePayload({ ...d, header: { ...d.header, internalNote: 'x'.repeat(1001) } }, settings);
        if (!tooLong.ok) assert.deepEqual(groupErrors(tooLong.errors).internalNote, ['too_large']);
        assert.equal('containerNumber' in result.value, false, 'disabled fields are not sent');
        assert.deepEqual(result.value.lines, [{ productVariantId: 4, description: null, quantity: 80, unit: 'box', unitPrice: 12.5, discountPercentage: 0, vatPercentage: 21 }]);
    });

    test('errors are reported per field and the blank trailing line is ignored', () => {
        const d = { ...draft(), lines: [filled({ description: '', unitPrice: '12.505', discount: '120' }), emptyLine(21)] };
        const result = buildPurchaseInvoicePayload(d, settings);
        assert.equal(result.ok, false);
        if (result.ok) return;
        assert.deepEqual(groupErrors(result.errors), {
            vendorId: ['required'], invoiceNumber: ['required'],
            'lines[0].description': ['required'], 'lines[0].unitPrice': ['too_many_decimals'], 'lines[0].discountPercentage': ['out_of_range'],
        });
        const noLines = buildPurchaseInvoicePayload({ ...selectVendor(draft(), greenery, settings), lines: [emptyLine(21)] }, settings);
        assert.equal(noLines.ok, false);
        if (!noLines.ok) assert.deepEqual(groupErrors(noLines.errors).lines, ['required']);
        const due = buildPurchaseInvoicePayload(changeDueDate(selectVendor(draft(), greenery, settings), '2026-10-01'), settings);
        if (!due.ok) assert.deepEqual(groupErrors(due.errors).dueDate, ['before_invoice_date']);
    });

    test('amounts beyond the money columns are reported per line or on the totals, like the server does', () => {
        const base = selectVendor(draft(), greenery, settings);
        base.header.invoiceNumber = 'GR-2';
        const line = buildPurchaseInvoicePayload({ ...base, lines: [filled({ quantity: '999999999999999', unitPrice: '999999999999' })] }, settings);
        assert.equal(line.ok, false);
        if (!line.ok) assert.deepEqual(groupErrors(line.errors), { 'lines[0].unitPrice': ['too_large'] });
        const big = filled({ quantity: '9999999999999', unitPrice: '999', vat: '0' });
        const sum = buildPurchaseInvoicePayload({ ...base, lines: [big, { ...big, key: 'line-b' }] }, settings);
        assert.equal(sum.ok, false);
        if (!sum.ok) assert.deepEqual(groupErrors(sum.errors), { lines: ['too_large'] });
        assert.equal(buildPurchaseInvoicePayload({ ...base, lines: [big] }, settings).ok, true, 'one line of 9.999.999.999.999.000 still fits');
    });
});
