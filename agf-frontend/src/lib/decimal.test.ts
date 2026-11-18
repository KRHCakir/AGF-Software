import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { type NumberLocale, decimalShapeProblem, formatDecimalInput, parseLocaleDecimal, reformatDecimal, typedDecimalSeparator, typedThousands } from './decimal.ts';

const result = (locale: NumberLocale, input: string, options = {}) => parseLocaleDecimal(input, locale, options);
const read = (locale: NumberLocale, input: string, options = {}) => {
    const parsed = result(locale, input, options);
    return parsed.ok ? parsed.value : parsed.error;
};
const expect = (locale: NumberLocale, cases: [string, number | string][], options = {}) => {
    for (const [input, expected] of cases) assert.equal(read(locale, input, options), expected, `${locale} "${input}"`);
};
const suggestion = (locale: NumberLocale, input: string) => {
    const parsed = result(locale, input);
    return parsed.ok ? 'geldig' : parsed.suggestion ?? null;
};

describe('parseLocaleDecimal', () => {
    test('nl: a dot followed by 1 or 2 digits at the end is the decimal sign (7.50 is 7,50), also in the total', () => {
        expect('nl', [['7.50', 7.5], ['7,50', 7.5], ['7.5', 7.5], ['0.79', 0.79], ['.5', 0.5], [',5', 0.5], ['15.5', 15.5], ['1.23', 1.23], ['5.', 5], ['5,', 5], ['15,', 15], ['15.000.', 15000]]);
        expect('nl', [['-7.5', -7.5], ['-.5', -0.5], ['−1.500,5', -1500.5]], { allowNegative: true });
        expect('nl', [['7.5', 'too_many_decimals']], { maxDecimals: 0 });
    });

    test('nl: a dot in valid groups of three stays a thousands sign, also with spaces or apostrophes (pasted numbers)', () => {
        expect('nl', [['15.000', 15000], ['7.500', 7500], ['15.000,5', 15000.5], ['1.234.567', 1234567], ['1.234.567,5', 1234567.5], ['15,5', 15.5], ['15000', 15000], ['15000,5', 15000.5],
            ['15 000', 15000], ['1 234 567,5', 1234567.5], ['15 000,50', 15000.5], ['15 000', 15000], ["15'000,5", 15000.5], ['1’234’567', 1234567], [' 15.000 ', 15000]]);
        expect('nl', [['1.560', 1560], ['1,5', 'too_many_decimals']], { maxDecimals: 0 });
    });

    test('nl: everything in between is invalid, never a guess; where it can, with the value that was probably meant', () => {
        expect('nl', [['2.272.5', 'separator'], ['1.2.3', 'separator'], ['15.0000', 'separator'], ['0.500', 'separator'], ['15,000.5', 'separator'], ['1.23,5', 'separator'],
            ['1,5,5', 'invalid'], ['abc', 'invalid'], ['15 00', 'invalid'], ['1,2.', 'invalid'], [',', 'invalid'], ['.', 'separator'],
            ['', 'empty'], ['  ', 'empty'], ['1,2345', 'too_many_decimals'], ['1234567890123456', 'too_large'], ['0', 'not_positive'], ['-5', 'not_positive']]);
        expect('nl', [['0', 0], ['-5', 'negative']], { allowZero: true });
        assert.deepEqual(['2.272.5', '15.0000', '0.500', '15,000.5', '1.234.56', '-2.272.5', '1.2.3', '1,5,5', 'abc', '7.50', ''].map((input) => suggestion('nl', input)),
            ['2.272,5', '15,0000', '0,500', '15.000,5', '1.234,56', '-2.272,5', null, null, null, 'geldig', null]);
        assert.deepEqual(decimalShapeProblem('2.272.5', 'nl'), { suggestion: '2.272,5' });
        assert.deepEqual(decimalShapeProblem('1.2.3', 'nl'), { suggestion: null });
        assert.equal(decimalShapeProblem('7.50', 'nl'), null);
        assert.equal(decimalShapeProblem('', 'nl'), null);
        assert.equal(decimalShapeProblem('1,2345', 'nl'), null, 'too many decimals is for the field itself, the shape is fine');
    });

    test('en: the same rules with the separators reversed', () => {
        expect('en', [['15,000', 15000], ['15,000.5', 15000.5], ['1,234,567', 1234567], ['15.5', 15.5], ['15 000', 15000], ['15,5', 15.5], ['1,23', 1.23],
            ['15,0000', 'separator'], ['2,272,5', 'separator'], ['', 'empty'], ['1.2345', 'too_many_decimals'], ['-5', 'not_positive']]);
        assert.equal(suggestion('en', '2,272,5'), '2,272.5');
        assert.equal(suggestion('en', '15.000,5'), '15,000.5');
    });

    test('values are written back in screen notation and read back identically; leaving the field gives the fixed decimals and never rounds', () => {
        assert.equal(formatDecimalInput(15000, 3, 'nl'), '15.000');
        assert.equal(formatDecimalInput(4.25, 3, 'nl'), '4,25');
        assert.equal(formatDecimalInput(4, 3, 'nl', 2), '4,00');
        assert.equal(formatDecimalInput(1.125, 3, 'nl', 2), '1,125');
        for (const value of [1.125, 15000, 1234567.5]) for (const locale of ['nl', 'en'] as const) assert.equal(read(locale, formatDecimalInput(value, 3, locale)), value);
        assert.equal(reformatDecimal('15000', 'nl'), '15.000');
        assert.equal(reformatDecimal('15600,0', 'nl'), '15.600,0');
        assert.equal(reformatDecimal('15000.50', 'en'), '15,000.50');
        assert.equal(reformatDecimal('2.272.5', 'nl'), '2.272.5', 'invalid input stays as typed');
        assert.deepEqual(['4', '7.5', '7.50', '0.79', '.5', '5.', '15.000', '15 000,5', '1,125', '0', '-4', '-0', '007'].map((input) => reformatDecimal(input, 'nl', 2)),
            ['4,00', '7,50', '7,50', '0,79', '0,50', '5,00', '15.000,00', '15.000,50', '1,125', '0,00', '-4,00', '0,00', '7,00']);
        assert.equal(reformatDecimal('12345678901234567,5', 'nl', 2), '12.345.678.901.234.567,50', 'digits are kept as typed, also beyond what a number holds');
        assert.equal(reformatDecimal('4', 'nl', 1), '4,0');
    });

    test('while typing, a dot with 1 or 2 digits becomes a comma at once; a typed thousands number gets its dot back at the third digit', () => {
        assert.deepEqual(['7', '7.', '7.5', '7.50', '0.79', '.5', '-7.5', '15.000', '7.500', '2.272.5', '7,5', '1.2.3', 'abc'].map((text) => typedDecimalSeparator(text, 'nl')),
            ['7', '7.', '7,5', '7,50', '0,79', ',5', '-7,5', '15.000', '7.500', '2.272.5', '7,5', '1.2.3', 'abc']);
        assert.deepEqual(['15,000', '7,500', '0,500', '15,00', '1234,567', '15,0000'].map((text) => typedThousands(text, 'nl')), ['15.000', '7.500', '0,500', '15,00', '1234,567', '15,0000']);
        assert.equal(typedDecimalSeparator('7,5', 'en'), '7.5');
        assert.equal(typedThousands('15.000', 'en'), '15,000');
    });
});
