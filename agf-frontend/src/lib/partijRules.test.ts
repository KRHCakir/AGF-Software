import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { formatPartijNumber, partijFormatError, partijNumberKey, isSequenceDigits } from './partijRules.ts';

// Dezelfde gevallen als AgfAdmin.Tests/Services/PartijRulesTests.cs.

describe('partijRules', () => {
    test('key ignores case, spaces and hyphens; almost equal numbers stay different', () => {
        assert.equal(partijNumberKey('2610251'), '2610251');
        assert.equal(partijNumberKey('2610 251'), '2610251');
        assert.equal(partijNumberKey('2610-251'), '2610251');
        assert.equal(partijNumberKey(' 26-10 251 '), '2610251');
        assert.equal(partijNumberKey('ab-12'), 'AB12');
        assert.equal(partijNumberKey(' - '), null);
        assert.equal(partijNumberKey(null), null);
        assert.notEqual(partijNumberKey('2610251'), partijNumberKey('2610252'));
        assert.notEqual(partijNumberKey('2610251'), partijNumberKey('2610.251'));
    });

    test('format fills the placeholders', () => {
        assert.equal(formatPartijNumber('{JJ}{MM}{volgnr}', '2026-10-15', 251), '2610251');
        assert.equal(formatPartijNumber('{JJ}{MM}{volgnr}', '2026-11-15', 1), '2611001');
        assert.equal(formatPartijNumber('{JJJJ}-{volgnr}', '2026-11-15', 7), '2026-007');
        assert.equal(formatPartijNumber('P{JJ}/{volgnr}', '2027-01-15', 1234), 'P27/1234');
    });

    test('fixed text and the number of digits, as in AgfAdmin.Tests SalesPartijMigrationTests', () => {
        assert.equal(partijFormatError('5{JJ}{MM}{volgnr}'), null);
        assert.equal(formatPartijNumber('5{JJ}{MM}{volgnr}', '2026-11-05', 2), '52611002');
        assert.equal(formatPartijNumber('5{JJ}{MM}{volgnr}', '2026-11-05', 45, 2), '5261145');
        assert.equal(formatPartijNumber('5{JJ}{MM}{volgnr}', '2026-11-05', 100, 3), '52611100');
        assert.equal(formatPartijNumber('{JJ}{MM}{volgnr}', '2026-11-05', 40, 2), '261140');
        assert.equal(formatPartijNumber('{JJ}{MM}{volgnr}', '2026-11-05', 1234, 3), '26111234');
        assert.equal(partijNumberKey('5261-145'), partijNumberKey('5261145'));
        assert.deepEqual([0, 1, 9, 10, 2.5].map(isSequenceDigits), [false, true, true, false, false]);
    });

    test('invalid formats are refused, valid ones accepted', () => {
        assert.equal(partijFormatError('{JJ}{MM}'), 'sequence_once');
        assert.equal(partijFormatError('{JJ}{volgnr}{volgnr}'), 'sequence_once');
        assert.equal(partijFormatError('{JJ}{DD}{volgnr}'), 'unknown_placeholder');
        assert.equal(partijFormatError('{jj}{volgnr}'), 'unknown_placeholder');
        assert.equal(partijFormatError('{JJ}{volgnr'), 'stray_brace');
        assert.equal(partijFormatError('   '), 'required');
        assert.equal(partijFormatError('{JJ}{MM}{volgnr}'), null);
        assert.equal(partijFormatError('{JJJJ}-{volgnr}'), null);
        assert.equal(partijFormatError('{volgnr}'), null);
    });
});
