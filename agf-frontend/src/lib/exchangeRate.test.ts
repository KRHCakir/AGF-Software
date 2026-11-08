import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { formatRate, inCurrency, isEuro, toEuro } from './exchangeRate.ts';

// Gespiegeld van ExchangeRateRules (server): koers in eenheden valuta per euro, op centen half van nul af.
describe('exchange rate', () => {
    test('5.000 USD at 1,1165 is 4.478,28 EUR; 500 EUR of it is 558,25 USD; the whole line is exactly the line; halves round away from zero', () => {
        assert.equal(toEuro(5000, 1.1165), 4478.28);
        assert.equal(inCurrency(500, 5000, 4478.28), 558.25);
        assert.equal(inCurrency(2149.57, 2400, toEuro(2400, 1.1165)), 2400, 'not 2.399,99 as 2.149,57 x 1,1165');
        assert.equal(inCurrency(1, 2400, 0), 0);
        assert.equal(toEuro(0.01, 1.5), 0.01);
        assert.equal(toEuro(2400, 175.68), 13.66);
    });

    test('euro is EUR in any case, or nothing', () => {
        assert.deepEqual(['EUR', 'eur', ' EUR ', '', null, undefined, 'USD'].map(isEuro), [true, true, true, true, true, true, false]);
    });

    test('a rate shows the ECB decimals in the screen language', () => {
        assert.equal(formatRate(1.1165, 'nl'), '1,1165');
        assert.equal(formatRate(175.68, 'en'), '175.68');
        assert.equal(formatRate(1.123456, 'nl'), '1,123456');
    });
});
