import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { PurchaseInvoiceSettings } from '../types/purchaseInvoice';
import { setInputLocale } from './decimal.ts';
import { candidate } from '../test/invoiceCandidate.ts';
import { addOrderLines, compareLine, linesFromCandidate, linkCheck, orderLabel } from './invoiceOrderLink.ts';
import { buildPurchaseInvoicePayload, emptyDraft } from './purchaseInvoiceRules.ts';

// Stap 4b: de bon vult de factuur voor (artikel, eenheid, wat nog open is, de prijs op de bon); het verschil per regel en de vraag
// bij opslaan volgen uit wat de gebruiker aanpast. Gespiegeld van PurchaseInvoiceOrderLinkRules in de backend.

setInputLocale('nl');
const settings: PurchaseInvoiceSettings = { allOptionalFields: [], optionalFields: [], defaultVatPercentage: 9, defaultPaymentTermsDays: 30, defaultCurrency: 'EUR' };

describe('invoice linked to the purchase order', () => {
    test('the order fills the invoice: article, unit, what is open and the price on the order, in nl notation', () => {
        const [line] = linesFromCandidate(candidate(), 9);
        assert.deepEqual([line.productVariantId, line.description, line.quantity, line.unit, line.unitPrice, line.discount, line.vat], ['1', 'Avocado · Hass', '264', 'colli', '13,50', '0', '9']);
        assert.deepEqual([line.orderLine?.lineId, line.orderLine?.orderId, orderLabel(line.orderLine!)], [81, 56, 'NP-1 (IO-2026-0056)']);

        // Wat al gefactureerd is valt weg; staat niets meer open (gevonden via zoeken), dan zonder aantal.
        const partly = candidate({ lines: [{ ...candidate().lines[0], invoicedQuantity: 200, openQuantity: 64 }] });
        assert.equal(linesFromCandidate(partly, 9)[0].quantity, '64');
        const done = candidate({ lines: [{ ...candidate().lines[0], invoicedQuantity: 264, openQuantity: 0 }] });
        assert.equal(linesFromCandidate(done, 9)[0].quantity, '');
    });

    test('adding an order replaces an empty start and never adds an order line twice', () => {
        const draft = emptyDraft('2026-11-12', settings);
        const once = addOrderLines(draft, candidate(), 9);
        assert.equal(once.lines.length, 1);
        assert.equal(addOrderLines(once, candidate(), 9).lines.length, 1);
        const payload = buildPurchaseInvoicePayload({ ...once, header: { ...once.header, vendorId: '3', invoiceNumber: 'F-1' } }, settings);
        assert.ok(payload.ok);
        assert.deepEqual(payload.value.lines.map((l) => [l.productVariantId, l.quantity, l.unit, l.unitPrice, l.supplyCommitmentLineId]), [[1, 264, 'colli', 13.5, 81]]);
    });

    test('the same price is no difference; 12,50 against 13,50 is −1,00 per unit and −264,00 in total', () => {
        const [line] = linesFromCandidate(candidate(), 9);
        const same = compareLine(line)!;
        assert.deepEqual([same.orderAmount, same.invoiceAmount, same.differs, same.over, same.partial], [3564, 3564, false, false, false]);
        const cheaper = compareLine({ ...line, unitPrice: '12,50' })!;
        assert.deepEqual([cheaper.perUnit, cheaper.total, cheaper.differs], [-1, -264, true]);
        // Met korting telt de prijs na korting.
        assert.equal(compareLine({ ...line, discount: '10' })!.invoiceUnitPrice, 12.15);
        // Minder dan open is deels, meer dan besteld (zonder ontvangst) is te veel.
        assert.equal(compareLine({ ...line, quantity: '200' })!.partial, true);
        assert.equal(compareLine({ ...line, quantity: '265' })!.over, true);
        // Na ontvangst telt wat ontvangen is.
        const received = linesFromCandidate(candidate({ lines: [{ ...candidate().lines[0], receivedQuantity: 250, openQuantity: 250 }] }), 9)[0];
        assert.equal(compareLine({ ...received, quantity: '260' })!.over, true);
    });

    test('the question comes once per invoice when something differs; adjusting only when every order with a difference is open', () => {
        const draft = addOrderLines(emptyDraft('2026-11-12', settings), candidate(), 9);
        assert.equal(linkCheck(draft).needsQuestion, false);
        const cheaper = { ...draft, lines: draft.lines.map((l) => ({ ...l, unitPrice: '12,50' })) };
        assert.deepEqual([linkCheck(cheaper).needsQuestion, linkCheck(cheaper).adjustable, linkCheck(cheaper).totalDifference], [true, true, -264]);
        const definitive = addOrderLines(emptyDraft('2026-11-12', settings), candidate({ priceAdjustable: false, documentStatus: 'Definitive' }), 9);
        const changed = { ...definitive, lines: definitive.lines.map((l) => ({ ...l, unitPrice: '12,50' })) };
        assert.equal(linkCheck(changed).adjustable, false);
        // Een bon zonder prijs: de factuur zet de prijs, dat is ook een keuze.
        const noPrice = addOrderLines(emptyDraft('2026-11-12', settings), candidate({ lines: [{ ...candidate().lines[0], unitPrice: null }] }), 9);
        assert.equal(linkCheck({ ...noPrice, lines: noPrice.lines.map((l) => ({ ...l, unitPrice: '13,00' })) }).differing.length, 1);
    });
});
