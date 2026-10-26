import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { VAT_EXAMPLES, VAT_PATTERNS, checkVat, greekVatSuggestion } from './vatRules.ts';

// Spiegel van VatRulesTests: vorm per land (EU + XI), GR -> EL-suggestie, onbekend land valt terug op de algemene vorm.

describe('VAT shape per country', () => {
    test('every example matches its own country pattern (table and examples stay in step)', () => {
        assert.deepEqual(Object.keys(VAT_EXAMPLES).sort(), Object.keys(VAT_PATTERNS).sort());
        for (const [country, example] of Object.entries(VAT_EXAMPLES)) {
            if (country === 'ES' || country === 'FR' || country === 'CY' || country === 'IE') continue; // voorbeelden met letters als plaatshouder
            assert.equal(checkVat(example), 'none', country);
        }
    });

    test('country patterns, Northern Ireland and fallbacks', () => {
        assert.equal(checkVat('NL812345678B01'), 'none');
        assert.equal(checkVat('NL812345678'), 'country_pattern');
        assert.equal(checkVat('DE12345678'), 'country_pattern');
        assert.equal(checkVat('ATU12345678'), 'none');
        assert.equal(checkVat('XI123456789'), 'none');
        assert.equal(checkVat('XIGD123'), 'none');
        assert.equal(checkVat('CHE123456789'), 'none', 'country outside the table: general shape only');
        assert.equal(checkVat('12345'), 'shape');
        assert.equal(checkVat(null), 'none');
    });

    test('Greece uses EL: GR is refused with the EL suggestion when that has the Greek shape', () => {
        assert.equal(checkVat('GR123456789'), 'greece_uses_el');
        assert.equal(greekVatSuggestion('GR123456789'), 'EL123456789');
        assert.equal(greekVatSuggestion('GR12'), null);
        assert.equal(checkVat('EL123456789'), 'none');
    });
});
