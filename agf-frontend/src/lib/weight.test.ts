import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { WEIGHT_BASIS_LABEL_KEYS, WEIGHT_METHOD_LABEL_KEYS, calculatedReservationKg, calculatedTotalKg, isWeightBasis, isWeightMethod } from './weight.ts';
import nl from '../i18n/nl.json';
import en from '../i18n/en.json';

describe('calculatedTotalKg', () => {
    test('quantity × kg per package, rounded to 3 decimals like the server', () => {
        assert.equal(calculatedTotalKg(80, 4), 320);
        assert.equal(calculatedTotalKg(120, 4), 480);
        assert.equal(calculatedTotalKg(80, 4.125), 330);
        assert.equal(calculatedTotalKg(3, 0.3333), 1);
        assert.equal(calculatedTotalKg(7, 1.4285), 10);
        assert.equal(calculatedTotalKg(1, 0.0005), 0.001);
    });
});

describe('calculatedReservationKg', () => {
    test('a partial reservation rounds; the full remainder takes exactly the remaining kg', () => {
        // 3 × 1,001 = 3,003 kg: 1,5 box → 1,502 kg; daarna rest 1,5 box met 1,501 kg.
        assert.equal(calculatedReservationKg(1.5, 1.001, 3, 3.003), 1.502);
        assert.equal(calculatedReservationKg(1.5, 1.001, 1.5, 1.501), 1.501);
        assert.equal(calculatedReservationKg(30, 10, 80, 800), 300);
        assert.equal(calculatedReservationKg(80, 10, 80, 800), 800);
    });
});

describe('weight method and basis', () => {
    test('only the two known methods and three bases are accepted', () => {
        assert.ok(isWeightMethod('Weighed') && isWeightMethod('Calculated'));
        assert.equal(isWeightMethod('Estimated'), false);
        assert.ok(isWeightBasis('mixed'));
        assert.equal(isWeightBasis('Weighed'), false);
    });

    test('labels exist in both languages and never call a calculated weight weighed', () => {
        for (const key of [...Object.values(WEIGHT_METHOD_LABEL_KEYS), ...Object.values(WEIGHT_BASIS_LABEL_KEYS)]) {
            assert.ok(key in nl, `${key} in nl`);
            assert.ok(key in en, `${key} in en`);
        }
        assert.equal((nl as Record<string, string>).weight_calculated.includes('gewogen'), false);
        assert.equal((en as Record<string, string>).weight_calculated.includes('weighed'), false);
    });
});
