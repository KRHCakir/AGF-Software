import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { normalizeTradeClass, searchTradeClasses, tradeClassKey } from './tradeClass.ts';

// Zelfde tabel als TradeClassTests in de backend: elke schrijfwijze van een klasse wordt de vaste waarde, al het andere null.

const RECOGNISED: [string, string][] = [
    ['I', 'I'], ['i', 'I'], ['1', 'I'], [' 1 ', 'I'], ['I.', 'I'], ['klasse 1', 'I'], ['Klasse I', 'I'], ['KLASSE  i', 'I'], ['class I', 'I'], ['Class 1', 'I'],
    ['cat. 1', 'I'], ['Cat I', 'I'], ['categorie 1', 'I'], ['category I', 'I'], ['kl. 1', 'I'], ['klasse:1', 'I'], ['handelsklasse I', 'I'],
    ['II', 'II'], ['ii', 'II'], ['2', 'II'], ['klasse 2', 'II'], ['Klasse II', 'II'], ['class II', 'II'], ['cat. 2', 'II'], ['I I', 'II'],
    ['Extra', 'Extra'], ['extra', 'Extra'], ['EXTRA', 'Extra'], ['0', 'Extra'], ['e', 'Extra'], ['E', 'Extra'], ['klasse extra', 'Extra'],
    ['Klasse Extra', 'Extra'], ['class extra', 'Extra'], ['cat. 0', 'Extra'],
];
const RUBBISH = [null, undefined, '', '   ', 'klasse', 'III', '3', 'A', 'klasse A', 'Industrie', '1e', 'I/II', '12', 'ex', 'extra fijn', 'klassement', 'constructor', 'toString'];

describe('trade class: a fixed list (Extra, I, II)', () => {
    test('every spelling of a class becomes the fixed value', () => {
        for (const [typed, expected] of RECOGNISED) assert.equal(normalizeTradeClass(typed), expected, typed);
    });

    test('anything else is never accepted silently', () => {
        for (const typed of RUBBISH) assert.equal(normalizeTradeClass(typed), null, String(typed));
    });

    test('I and 1 compare as the same class; old unrecognised text only as itself', () => {
        assert.equal(tradeClassKey('I'), tradeClassKey('1'));
        assert.equal(tradeClassKey('klasse extra'), tradeClassKey('Extra'));
        assert.notEqual(tradeClassKey('I'), tradeClassKey('II'));
        assert.equal(tradeClassKey('Klasse  A'), tradeClassKey('klasse a'));
        assert.notEqual(tradeClassKey('klasse A'), tradeClassKey('I'));
        assert.equal(tradeClassKey(null), '');
    });

    test('suggestions: the recognised class first (Tab takes it), then names that start with the text; nothing for rubbish', () => {
        assert.deepEqual(searchTradeClasses('').map((s) => s.tradeClass), ['Extra', 'I', 'II']);
        assert.deepEqual(searchTradeClasses('1'), [{ tradeClass: 'I', exact: true }]);
        assert.deepEqual(searchTradeClasses('i'), [{ tradeClass: 'I', exact: true }, { tradeClass: 'II', exact: false }]);
        assert.deepEqual(searchTradeClasses('klasse 2'), [{ tradeClass: 'II', exact: true }]);
        assert.deepEqual(searchTradeClasses('ex'), [{ tradeClass: 'Extra', exact: false }]);
        assert.deepEqual(searchTradeClasses('e'), [{ tradeClass: 'Extra', exact: true }]);
        assert.deepEqual(searchTradeClasses('3'), []);
        assert.deepEqual(searchTradeClasses('klasse A'), []);
    });
});
