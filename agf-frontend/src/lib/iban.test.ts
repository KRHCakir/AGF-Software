import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { checkIban, formatIban, isBic, mod97, normalizeBic, normalizeIban } from './iban.ts';

// Spiegel van BankAccountRulesTests: normaliseren (spaties weg, hoofdletters), controle in vaste volgorde vorm -> land -> lengte
// -> mod-97, BIC van 8 of 11 tekens.

describe('IBAN', () => {
    test('normalization removes all whitespace (also non-breaking) and uppercases; empty becomes null', () => {
        assert.equal(normalizeIban(' nl91 abna 0417 1643 00 '), 'NL91ABNA0417164300');
        assert.equal(normalizeIban('   '), null);
    });

    test('valid IBANs from several countries pass the checksum', () => {
        for (const iban of ['NL91ABNA0417164300', 'DE89370400440532013000', 'BE68539007547034', 'GB29NWBK60161331926819', 'FR1420041010050500013M02606', 'NO9386011117947']) {
            assert.equal(checkIban(iban), 'none', iban);
            assert.equal(mod97(iban), 1);
        }
    });

    test('problems are reported in order: shape, unknown country, length, checksum', () => {
        assert.equal(checkIban(null), 'shape');
        assert.equal(checkIban('NLAB1234'), 'shape');
        assert.equal(checkIban('US12345678901234'), 'unknown_country');
        assert.equal(checkIban('NL91ABNA041716430'), 'length');
        assert.equal(checkIban('NL92ABNA0417164300'), 'checksum');
    });

    test('display groups of four', () => {
        assert.equal(formatIban('NL91ABNA0417164300'), 'NL91 ABNA 0417 1643 00');
    });
});

describe('BIC', () => {
    test('8 or 11 characters: bank, country, location, optional branch', () => {
        assert.deepEqual(['ABNANL2A', 'abna nl 2a xxx', 'ABNANL2', 'ABN1NL2A', 'ABNANL2AXX'].map((b) => isBic(normalizeBic(b))), [true, true, false, false, false]);
        assert.equal(isBic(null), false);
    });
});
