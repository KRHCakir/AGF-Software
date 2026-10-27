import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { formatMoney } from './availability.ts';

// Geldbedragen altijd met 2 decimalen in de notatie van de schermtaal.
describe('formatMoney', () => {
    test('two decimals with thousands separators, currency code optional', () => {
        assert.equal(formatMoney(42900, null, 'nl'), '42.900,00');
        assert.equal(formatMoney(12.5, 'EUR', 'nl'), '12,50 EUR');
        assert.equal(formatMoney(0.005, null, 'nl'), '0,01');
        assert.equal(formatMoney(42900, 'USD', 'en'), '42,900.00 USD');
    });
});
