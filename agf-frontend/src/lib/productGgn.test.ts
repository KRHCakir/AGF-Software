import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { applyGgnProposals, ggnHint, ggnProblem, proposedValue, type GgnLine } from './productGgn.ts';
import type { GgnProposal } from '../types/productGgn';

// Certificaten deel 2: het voorgestelde GGN van het product op de inkoopregel. Eén treffer wordt voorgevuld, meer treffers vragen een keuze,
// geen treffer blijft leeg; wat de gebruiker zelf zette (ook leeg) blijft staan.
const A = '4049928415684';
const B = '8712345678906';
const option = (ggn: string) => ({ ggn, certificateId: 1, validUntil: null, producerName: null });
const one: GgnProposal = { productId: 1, kind: 'One', options: [option(A)] };
const many: GgnProposal = { productId: 1, kind: 'Many', options: [option(A), option(B)] };
const none: GgnProposal = { productId: 1, kind: 'None', options: [] };
const line = (patch: Partial<GgnLine> = {}): GgnLine => ({ key: 'a', productVariantId: '7', ggn: '', ...patch });

describe('product GGN on the purchase line', () => {
    test('one match fills in, several or none stay empty', () => {
        assert.equal(proposedValue(one), A);
        assert.equal(proposedValue(many), '');
        assert.equal(proposedValue(none), '');
        assert.equal(proposedValue(undefined), '');
    });

    test('only untouched lines follow the proposal, and an unchanged list is the same list', () => {
        const lines = [line(), line({ key: 'b', ggn: B, ggnTouched: true }), line({ key: 'c', ggn: '', ggnTouched: true })];
        const next = applyGgnProposals(lines, () => one);
        assert.deepEqual(next.map((l) => l.ggn), [A, B, '']);
        assert.equal(applyGgnProposals(next, () => one), next);
        // Een ander product zonder treffer: het voorgevulde nummer gaat weer weg (de gebruiker had het niet aangeraakt).
        assert.deepEqual(applyGgnProposals(next, () => none).map((l) => l.ggn), ['', B, '']);
    });

    test('the hint says proposed, choose, chosen, own or none', () => {
        assert.equal(ggnHint(line({ ggn: A }), one), 'proposed');
        assert.equal(ggnHint(line({ ggn: A, ggnTouched: true }), one), 'chosen');
        assert.equal(ggnHint(line(), many), 'choose');
        assert.equal(ggnHint(line({ ggn: B, ggnTouched: true }), many), 'chosen');
        assert.equal(ggnHint(line({ ggn: '4063061000005', ggnTouched: true }), one), 'own');
        assert.equal(ggnHint(line(), none), 'none');
        assert.equal(ggnHint(line({ productVariantId: '' }), one), null);
        assert.equal(ggnHint(line(), undefined), null);
    });

    test('13 digits are required (spaces allowed); a wrong check digit is only a warning', () => {
        assert.equal(ggnProblem(''), null);
        assert.equal(ggnProblem('404 9928 415684'), null);
        assert.equal(ggnProblem('40499284'), 'ggn_13_digits');
        assert.equal(ggnProblem('404992841568A'), 'ggn_13_digits');
        assert.equal(ggnProblem('4049928415685'), 'ggn_check_digit');
    });
});
