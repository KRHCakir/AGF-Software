import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { type NumberLocale, formatDecimalInput, parseLocaleDecimal, reformatDecimal } from './decimal.ts';

const read = (locale: NumberLocale, input: string, options = {}) => {
    const result = parseLocaleDecimal(input, locale, options);
    return result.ok ? result.value : result.error;
};
const expect = (locale: NumberLocale, cases: [string, number | string][], options = {}) => {
    for (const [input, expected] of cases) assert.equal(read(locale, input, options), expected, `${locale} "${input}"`);
};

describe('parseLocaleDecimal', () => {
    test('nl: comma for decimals, dot only in valid groups of three (never a guess)', () => {
        expect('nl', [['15.000', 15000], ['15.000,5', 15000.5], ['1.234.567', 1234567], ['1.234.567,5', 1234567.5], ['15,5', 15.5], ['15000', 15000], ['15000,5', 15000.5],
            ['15.5', 'separator'], ['1.23', 'separator'], ['15.0000', 'separator'], ['0.500', 'separator'], ['15.', 'separator'], ['1,5,5', 'invalid'], ['15,', 'invalid'], ['abc', 'invalid'],
            ['15 000', 15000], ['1 234 567,5', 1234567.5], ['15 00', 'invalid'],
            ['', 'empty'], ['  ', 'empty'], ['1,2345', 'too_many_decimals'], ['1234567890123456', 'too_large'], ['0', 'not_positive'], ['-5', 'not_positive']]);
        expect('nl', [['1.560', 1560], ['1,5', 'too_many_decimals']], { maxDecimals: 0 });
        expect('nl', [['0', 0], ['-5', 'negative']], { allowZero: true });
        expect('nl', [['-1.500,5', -1500.5]], { allowNegative: true });
    });

    test('en: the same rules with the separators reversed', () => {
        expect('en', [['15,000', 15000], ['15,000.5', 15000.5], ['1,234,567', 1234567], ['15.5', 15.5], ['15 000', 15000],
            ['15,5', 'separator'], ['1,23', 'separator'], ['15,0000', 'separator'], ['', 'empty'], ['1.2345', 'too_many_decimals'], ['-5', 'not_positive']]);
    });

    test('values are written back in screen notation and read back identically; blur keeps the typed decimals', () => {
        assert.equal(formatDecimalInput(15000, 3, 'nl'), '15.000');
        assert.equal(formatDecimalInput(4.25, 3, 'nl'), '4,25');
        for (const value of [1.125, 15000, 1234567.5]) for (const locale of ['nl', 'en'] as const) assert.equal(read(locale, formatDecimalInput(value, 3, locale)), value);
        assert.equal(reformatDecimal('15000', 'nl'), '15.000');
        assert.equal(reformatDecimal('15600,0', 'nl'), '15.600,0');
        assert.equal(reformatDecimal('15000.50', 'en'), '15,000.50');
        assert.equal(reformatDecimal('15.5', 'nl'), '15.5', 'invalid input stays as typed');
    });
});
