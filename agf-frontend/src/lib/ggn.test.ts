import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { isGgn, normalizeGgn, resolveGgn } from './ggn.ts';

// GGN: leeg = niet vastgelegd, geldig = precies 13 cijfers (voorloopnullen blijven), alles anders is ongeldig.
describe('ggn', () => {
    test('empty means not recorded', () => {
        assert.deepEqual(resolveGgn(''), { ok: true, value: null });
        assert.deepEqual(resolveGgn('   '), { ok: true, value: null });
    });

    test('a valid number keeps its leading zeros and loses only spaces', () => {
        assert.deepEqual(resolveGgn('0049929123456'), { ok: true, value: '0049929123456' });
        assert.deepEqual(resolveGgn(' 0049 929 123456 '), { ok: true, value: '0049929123456' });
        assert.equal(normalizeGgn('4049929-123456'), '4049929-123456', 'other separators are kept so validation refuses them');
    });

    test('anything but exactly 13 digits is invalid', () => {
        for (const bad of ['404992912345', '40499291234567', '4049929A23456', '4049929-123456', '4049929.123456', '4049929\t123456']) {
            assert.deepEqual(resolveGgn(bad), { ok: false }, `${JSON.stringify(bad)} must be refused`);
            assert.equal(isGgn(normalizeGgn(bad)), false);
        }
    });
});
