import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { setInputLocale } from './decimal.ts';
import { amountInEuro, currencyPayload, draftFromDocument, effectiveRate, euroDraft, formatEuro, inverseLine, parseRate, rateLine, rateText } from './documentCurrency.ts';

// Valuta-ronde: koers in eenheden valuta per 1 euro (ECB-notatie), eurobedrag op centen, handmatige koers met hoogstens zes decimalen.
setInputLocale('nl');

describe('document currency', () => {
    test('the ECB notation and the other direction, in the screen language', () => {
        assert.equal(rateLine('USD', 1.0842, 'nl'), '1 EUR = 1,0842 USD');
        assert.equal(inverseLine('USD', 1.0842, 'nl'), '1 USD = 0,9223 EUR');
        assert.equal(rateLine('TRY', 37.123456, 'en'), '1 EUR = 37.123456 TRY');
        assert.equal(rateText(1.1, 'nl'), '1,10');
        assert.equal(rateText(1234.5, 'nl'), '1234,50', 'no thousands separator in an input');
    });

    test('the euro amount is the amount divided by the rate, rounded to cents, in nl notation', () => {
        const usd = { ...euroDraft(), currency: 'USD', rate: '1,0842', source: 'ECB' as const };
        // 8.757,08 USD / 1,0842 = 8.076,999 → 8.077,00
        assert.equal(amountInEuro(8757.08, usd), 8077);
        assert.equal(formatEuro(8077, 'nl').replace(/\s/g, ' '), '€ 8.077,00');
        assert.equal(amountInEuro(100, euroDraft()), 100);
        assert.equal(amountInEuro(100, { ...usd, rate: '' }), null, 'no rate yet');
        assert.equal(amountInEuro(null, usd), null);
    });

    test('a manual rate is a positive number with at most six decimals; never silently rounded', () => {
        assert.deepEqual(parseRate('1,0842'), { ok: true, rate: 1.0842 });
        // De getallenregel van fixes 5a: een punt met vier cijfers erachter is ongeldig (het veld stelt dan 1,0842 voor); 1.08 is 1,08.
        assert.equal(parseRate('1.0842').ok, false);
        assert.deepEqual(parseRate('1.08'), { ok: true, rate: 1.08 });
        assert.deepEqual(parseRate('37,123456'), { ok: true, rate: 37.123456 });
        assert.equal(parseRate('1,1234567').ok, false);
        assert.equal(parseRate('0').ok, false);
        assert.equal(parseRate('').ok, false);
        assert.equal(effectiveRate({ ...euroDraft(), currency: 'USD', rate: 'abc', source: 'Manual' }), null);
    });

    test('what goes to the server: euro without rate; ECB or Manual with the rate; a manual rate must be valid', () => {
        assert.deepEqual(currencyPayload(euroDraft()), { ok: true, value: { currency: 'EUR', exchangeRate: null, exchangeRateSource: null } });
        assert.deepEqual(currencyPayload({ ...euroDraft(), currency: 'usd', rate: '1,0842', source: 'ECB' }), { ok: true, value: { currency: 'USD', exchangeRate: 1.0842, exchangeRateSource: 'ECB' } });
        assert.deepEqual(currencyPayload({ ...euroDraft(), currency: 'USD', rate: '1,1', source: 'Manual' }), { ok: true, value: { currency: 'USD', exchangeRate: 1.1, exchangeRateSource: 'Manual' } });
        assert.deepEqual(currencyPayload({ ...euroDraft(), currency: 'USD', rate: '', source: 'Manual' }), { ok: false, field: 'exchangeRate' });
        assert.deepEqual(currencyPayload({ ...euroDraft(), currency: 'US', source: 'ECB' }), { ok: false, field: 'currency' });
    });

    test('a stored document opens with its fixed rate; existing euro documents stay euro', () => {
        assert.deepEqual(draftFromDocument('USD', 1.0842, 'ECB', '2026-11-18', 'nl'), { currency: 'USD', rate: '1,0842', source: 'ECB', rateDate: '2026-11-18', touched: true });
        assert.deepEqual(draftFromDocument('USD', 1.1, 'Manual', null, 'nl').source, 'Manual');
        assert.deepEqual(draftFromDocument('EUR', 1, 'NotApplicable', null, 'nl'), euroDraft());
        assert.deepEqual(draftFromDocument(undefined, undefined, undefined, undefined, 'nl'), euroDraft());
    });
});
