import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { PurchaseInvoiceSettings } from '../types/purchaseInvoice';
import { setInputLocale } from './decimal.ts';
import { candidate } from '../test/invoiceCandidate.ts';
import { addOrderLines, linkedOrderCurrency, lockedInvoiceCurrency, orderCurrencyFits, ordersMissingCurrency, withOrderCurrency } from './invoiceOrderLink.ts';
import { buildPurchaseInvoicePayload, changeCurrency, changeRate, emptyDraft, invoiceCurrencyDraft } from './purchaseInvoiceRules.ts';

// Valuta-ronde, de inkoopfactuur: de koers van de factuurdatum (ECB, aanpasbaar: Manual) gaat mee bij opslaan; een andere valuta begint
// opnieuw met de ECB-koers; de eerste gekoppelde order bepaalt de valuta; een order in een andere valuta komt er niet bij.
setInputLocale('nl');
const settings: PurchaseInvoiceSettings = { allOptionalFields: [], optionalFields: [], defaultVatPercentage: 9, defaultPaymentTermsDays: 30, defaultCurrency: 'EUR' };

describe('purchase invoice currency', () => {
    test('euro sends no rate; a dollar invoice sends ECB (the server fixes the rate) or a valid manual rate', () => {
        const base = addOrderLines(emptyDraft('2026-11-19', settings), candidate(), 9);
        const filled = { ...base, header: { ...base.header, vendorId: '3', invoiceNumber: 'F-1' } };
        const euro = buildPurchaseInvoicePayload(filled, settings);
        assert.ok(euro.ok);
        assert.equal(euro.ok && 'exchangeRateSource' in euro.value, false);

        const usd = changeRate(changeCurrency(filled, 'usd'), { currency: 'USD', rate: '1,0842', source: 'ECB', rateDate: '2026-11-19', touched: true });
        const ecb = buildPurchaseInvoicePayload(usd, settings);
        assert.ok(ecb.ok && ecb.value.currency === 'USD' && ecb.value.exchangeRateSource === 'ECB' && ecb.value.exchangeRate === 1.0842);

        const manual = changeRate(usd, { ...invoiceCurrencyDraft(usd.header), rate: '1,1', source: 'Manual', rateDate: null });
        const sent = buildPurchaseInvoicePayload(manual, settings);
        assert.ok(sent.ok && sent.value.exchangeRateSource === 'Manual' && sent.value.exchangeRate === 1.1);

        const bad = changeRate(usd, { ...invoiceCurrencyDraft(usd.header), rate: 'abc', source: 'Manual', rateDate: null });
        const refused = buildPurchaseInvoicePayload(bad, settings);
        assert.ok(!refused.ok && refused.errors.some((error) => error.field === 'exchangeRate'));
    });

    test('another currency starts again with the ECB rate; the same currency keeps the rate', () => {
        const usd = changeRate(changeCurrency(emptyDraft('2026-11-19', settings), 'USD'), { currency: 'USD', rate: '1,1', source: 'Manual', rateDate: null, touched: true });
        assert.deepEqual([usd.header.exchangeRate, usd.header.rateSource], ['1,1', 'Manual']);
        assert.deepEqual([changeCurrency(usd, 'usd').header.exchangeRate, changeCurrency(usd, 'usd').header.rateSource], ['1,1', 'Manual']);
        const gbp = changeCurrency(usd, 'GBP');
        assert.deepEqual([gbp.header.exchangeRate, gbp.header.rateSource, invoiceCurrencyDraft(gbp.header).source], ['', undefined, 'ECB']);
    });

    test('the first linked order sets the invoice currency; an order in another currency does not fit', () => {
        const draft = emptyDraft('2026-11-19', settings);
        assert.equal(linkedOrderCurrency(draft), null);
        const dollars = addOrderLines(draft, candidate({ currency: 'USD', exchangeRate: 1.2, exchangeRateSource: 'Manual' }), 9);
        assert.deepEqual([dollars.header.currency, dollars.header.rateSource, linkedOrderCurrency(dollars)], ['USD', undefined, 'USD']);
        assert.deepEqual([dollars.lines[0].orderLine?.orderRate, dollars.lines[0].orderLine?.orderRateSource], [1.2, 'Manual']);
        assert.equal(orderCurrencyFits(dollars, { currency: 'EUR' }), false);
        assert.equal(orderCurrencyFits(dollars, { currency: 'USD' }), true);
        const refused = addOrderLines(dollars, candidate({ id: 57, currency: 'EUR', lines: [{ ...candidate().lines[0], id: 99 }] }), 9);
        assert.equal(refused, dollars, 'nothing added');
        // Een oudere order zonder valuta in het antwoord is euro.
        assert.equal(linkedOrderCurrency(addOrderLines(draft, candidate(), 9)), 'EUR');
    });

    test('an old draft without order currency is not treated as euro: it follows the invoice until the order is fetched again', () => {
        const dollars = addOrderLines(emptyDraft('2026-11-19', settings), candidate({ currency: 'USD', exchangeRate: 1.2, exchangeRateSource: 'Manual' }), 9);
        const old = { ...dollars, lines: dollars.lines.map((line) => ({ ...line, orderLine: { ...line.orderLine!, orderCurrency: undefined, orderRate: undefined, orderRateSource: undefined } })) };
        assert.equal(linkedOrderCurrency(old), 'USD');
        assert.equal(orderCurrencyFits(old, { currency: 'USD' }), true);
        const orderId = old.lines[0].orderLine!.orderId;
        assert.deepEqual(ordersMissingCurrency(old), [orderId]);
        const fixed = withOrderCurrency(old, candidate({ id: orderId, currency: 'USD', exchangeRate: 1.2, exchangeRateSource: 'Manual' }));
        assert.deepEqual([fixed.lines[0].orderLine?.orderCurrency, fixed.lines[0].orderLine?.orderRate, ordersMissingCurrency(fixed)], ['USD', 1.2, []]);
    });

    test('an old draft in another currency than its restored order follows the order, with a new rate', () => {
        const euros = addOrderLines(emptyDraft('2026-11-19', settings), candidate(), 9);
        const orderId = euros.lines[0].orderLine!.orderId;
        const old = { ...euros, header: { ...euros.header, currency: 'USD', exchangeRate: '1,1', rateSource: 'Manual' as const },
            lines: euros.lines.map((line) => ({ ...line, orderLine: { ...line.orderLine!, orderCurrency: undefined, orderRate: undefined, orderRateSource: undefined } })) };
        const fixed = withOrderCurrency(old, candidate({ id: orderId }));
        assert.deepEqual([fixed.header.currency, fixed.header.exchangeRate, fixed.header.rateSource, linkedOrderCurrency(fixed)], ['EUR', '', undefined, 'EUR']);
        assert.equal(withOrderCurrency(fixed, candidate({ id: orderId })), fixed, 'nothing left to restore');
    });

    test('existing manual lines keep their currency: an order in another currency is refused', () => {
        const manual = { ...emptyDraft('2026-11-19', settings), lines: [{ ...addOrderLines(emptyDraft('2026-11-19', settings), candidate(), 9).lines[0], orderLine: undefined }] };
        assert.equal(lockedInvoiceCurrency(manual), 'EUR');
        assert.equal(orderCurrencyFits(manual, { currency: 'USD' }), false);
        assert.equal(addOrderLines(manual, candidate({ currency: 'USD' }), 9), manual);
        assert.equal(addOrderLines(manual, candidate(), 9).header.currency, 'EUR');
    });

    test('an order without new lines changes nothing, not even the currency', () => {
        const draft = emptyDraft('2026-11-19', settings);
        const usd = candidate({ currency: 'USD' });
        const again = addOrderLines(addOrderLines(draft, usd, 9), usd, 9);
        assert.equal(addOrderLines(again, usd, 9), again);
        const none = candidate({ currency: 'USD', lines: [] });
        assert.equal(addOrderLines(draft, none, 9), draft);
    });
});
