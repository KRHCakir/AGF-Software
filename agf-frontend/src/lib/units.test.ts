import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { UNIT_LABEL_KEYS, unitLabel, unitLabelKey } from './units.ts';
import nl from '../i18n/nl.json';
import en from '../i18n/en.json';

const dict = (d: Record<string, string>) => (key: string) => d[key] ?? key;

describe('unit labels', () => {
    test('every internal unit code has a Dutch and an English label', () => {
        for (const key of Object.values(UNIT_LABEL_KEYS)) {
            assert.ok(key in nl, `${key} in nl`);
            assert.ok(key in en, `${key} in en`);
        }
    });

    test('dozen is a dozen, never the Dutch plural of box', () => {
        assert.equal(unitLabel(dict(nl), 'dozen'), 'Dozijn (12 stuks)');
        assert.equal(unitLabel(dict(en), 'dozen'), 'Dozen (12 pieces)');
        assert.equal(unitLabel(dict(nl), 'box'), 'Doos');
        assert.equal(unitLabel(dict(nl), 'piece'), 'Stuk');
    });

    test('labels never change the stored code', () => {
        assert.equal(unitLabelKey('colli'), 'unit_colli');
    });
});
