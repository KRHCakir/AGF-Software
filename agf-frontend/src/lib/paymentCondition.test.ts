import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { conditionDiscountDate, conditionDueDate, emptyConditionDraft, isConditionCode, validateConditionDraft } from './paymentCondition.ts';

// Spiegel van PaymentConditionRulesTests: vervaldatum op factuurdatum of einde maand, kortingsdatum altijd vanaf de factuurdatum,
// code zonder spaties (max 16), dagen 0..365, korting beide of geen van beide met hooguit 2 decimalen.

describe('payment condition dates', () => {
    test('InvoiceDate counts from the invoice date, EndOfMonth from the last day of the invoice month (leap year included)', () => {
        assert.equal(conditionDueDate('2026-10-21', 30, 'InvoiceDate'), '2026-11-20');
        assert.equal(conditionDueDate('2028-02-15', 30, 'EndOfMonth'), '2028-03-30');
        assert.equal(conditionDueDate('2027-02-15', 30, 'EndOfMonth'), '2027-03-30');
        assert.equal(conditionDueDate('2026-12-31', 0, 'EndOfMonth'), '2026-12-31');
        assert.equal(conditionDueDate('2026-01-31', 30, 'EndOfMonth'), '2026-03-02');
        assert.equal(conditionDueDate('geen datum', 30, 'InvoiceDate'), null);
    });

    test('the discount date is always invoice date + discount days, and absent without a discount', () => {
        assert.equal(conditionDiscountDate('2026-10-21', { discountPercent: 2, discountDays: 8 }), '2026-10-29');
        assert.equal(conditionDiscountDate('2026-10-21', { discountPercent: null, discountDays: null }), null);
    });
});

describe('payment condition form', () => {
    test('codes are letters, digits, dot, dash or underscore without spaces, at most 16', () => {
        assert.deepEqual(['30D', 'EOM-30', 'a.b_c', '30 D', '-30', 'X'.repeat(17), 'X'.repeat(16)].map(isConditionCode), [true, true, true, false, false, false, true]);
    });

    test('valid input becomes values; empty sort order stays null (server: = days)', () => {
        const result = validateConditionDraft({ ...emptyConditionDraft(), code: ' EOM30 ', descriptionNl: '30 dagen einde maand', descriptionEn: '30 days end of month', days: '30', basis: 'EndOfMonth', discountPercent: '2,5', discountDays: '8' });
        assert.equal(result.ok, true);
        if (result.ok) assert.deepEqual(result.value, { code: 'EOM30', descriptionNl: '30 dagen einde maand', descriptionEn: '30 days end of month', days: 30, basis: 'EndOfMonth', discountPercent: 2.5, discountDays: 8, sortOrder: null, isActive: true });
    });

    test('days 0..365, discount both or neither, percent above 0 and at most 99.99 with 2 decimals, discount days up to the days', () => {
        const errors = (over: Partial<ReturnType<typeof emptyConditionDraft>>) => {
            const result = validateConditionDraft({ ...emptyConditionDraft(), code: 'X', descriptionNl: 'x', descriptionEn: 'x', ...over });
            return result.ok ? [] : result.errors.map((e) => `${e.field}:${e.code}`);
        };
        assert.deepEqual(errors({ days: '366' }), ['days:days']);
        assert.deepEqual(errors({ discountPercent: '2' }), ['discountPercent:discount_pair']);
        assert.deepEqual(errors({ discountPercent: '0', discountDays: '5' }), ['discountPercent:discount_percent']);
        assert.deepEqual(errors({ discountPercent: '1.234', discountDays: '5' }), ['discountPercent:discount_percent']);
        assert.deepEqual(errors({ discountPercent: '100', discountDays: '5' }), ['discountPercent:discount_percent']);
        assert.deepEqual(errors({ discountPercent: '99.99', discountDays: '31' }), ['discountDays:discount_days']);
        assert.deepEqual(errors({ code: '', descriptionNl: ' ' }), ['code:required', 'descriptionNl:required']);
    });
});
