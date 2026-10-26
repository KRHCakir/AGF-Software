import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { emailDomain, emailTypoSuggestion, isStrictEmail } from './emailRules.ts';

// Spiegel van EmailRulesTests: strikte vorm (één @, domein met punt, TLD van minstens 2 letters, max 254/64), tikfoutsuggestie.

describe('strict e-mail', () => {
    test('accepts normal addresses and refuses the usual mistakes', () => {
        assert.deepEqual(['info@fruit.nl', 'a.b+c@sub.example.co.uk', "o'neil@x.ie"].map(isStrictEmail), [true, true, true]);
        assert.deepEqual(['info@fruit', 'info@@fruit.nl', 'in fo@fruit.nl', '.info@fruit.nl', 'info.@fruit.nl', 'a..b@fruit.nl', 'info@-fruit.nl', 'info@fruit.n1', 'info@fruit.nl.'].map(isStrictEmail),
            [false, false, false, false, false, false, false, false, false]);
        assert.equal(isStrictEmail(`${'a'.repeat(65)}@fruit.nl`), false, 'local part max 64');
        assert.equal(isStrictEmail(`${'a'.repeat(64)}@fruit.nl`), true);
        assert.equal(isStrictEmail(`a@${'b'.repeat(63)}.${'c'.repeat(63)}.${'d'.repeat(63)}.${'e'.repeat(63)}.nl`), false, 'total max 254');
        assert.equal(isStrictEmail(`a@${'b'.repeat(63)}.${'c'.repeat(63)}.${'d'.repeat(63)}.${'e'.repeat(55)}.nl`), true);
    });

    test('a known typo in the domain suggests the intended domain with the same local part', () => {
        assert.equal(emailTypoSuggestion('Jan.Jansen@GMIAL.com'), 'Jan.Jansen@gmail.com');
        assert.equal(emailTypoSuggestion('inkoop@hotmail.nll'), 'inkoop@hotmail.nl');
        assert.equal(emailTypoSuggestion('info@fruit.nl'), null);
        assert.equal(emailTypoSuggestion('geen-at'), null);
        assert.equal(emailTypoSuggestion('x@constructor'), null, 'no prototype keys');
        assert.equal(emailDomain('a@B.nl'), 'b.nl');
    });
});
