import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import type { CountryReference } from '../types/country';
import { countryKey, countryName, searchCountries } from './countries.ts';

const reference: CountryReference = {
    countries: [
        { code: 'PE', nameNl: 'Peru', nameEn: 'Peru' },
        { code: 'ZA', nameNl: 'Zuid-Afrika', nameEn: 'South Africa' },
        { code: 'BR', nameNl: 'Brazilië', nameEn: 'Brazil' },
        { code: 'CL', nameNl: 'Chili', nameEn: 'Chile' },
        { code: 'PY', nameNl: 'Paraguay', nameEn: 'Paraguay' },
    ],
    aliases: [{ id: 1, alias: 'Kaap', countryCode: 'ZA' }],
};
const codes = (query: string) => searchCountries(reference, query).map((match) => match.country.code);

describe('countries', () => {
    test('the shortcut "PE" finds Peru first, as an exact match', () => {
        const [first] = searchCountries(reference, 'PE');
        assert.equal(first.country.code, 'PE');
        assert.equal(first.exact, true);
        assert.equal(codes('pe')[0], 'PE');
    });

    test('Dutch and English names, without capitals or accents', () => {
        assert.deepEqual(codes('brazilie'), ['BR']);
        assert.deepEqual(codes('south africa'), ['ZA']);
        assert.deepEqual(codes('zuid'), ['ZA']);
        assert.deepEqual(codes('chile'), ['CL']);
    });

    test('an own alias finds its country and says so; the country is what is chosen', () => {
        const [match] = searchCountries(reference, 'kaap');
        assert.deepEqual([match.country.code, match.alias, match.exact], ['ZA', 'Kaap', true]);
    });

    test('ranking: exact, then starts with, then contains', () => {
        assert.deepEqual(codes('p'), ['PY', 'PE', 'ZA']); // Kaap bevat een p
        assert.deepEqual(codes('ru'), ['PE']);
        assert.deepEqual(codes('xx'), []);
    });

    test('helpers', () => {
        assert.equal(countryKey('  Côte   d’Ivoire '), 'cote d’ivoire');
        assert.equal(countryName(reference, 'ZA'), 'Zuid-Afrika');
        assert.equal(countryName(reference, 'XX'), 'XX');
        assert.equal(countryName(null, null), '');
    });
});
