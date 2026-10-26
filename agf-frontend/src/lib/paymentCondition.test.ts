import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { conditionDiscountDate, conditionDueDate, emptyConditionDraft, freeTermsPreview, isConditionCode, validateConditionDraft, validateFreeTerms } from './paymentCondition.ts';
import type { FreeTermsInput } from './paymentCondition.ts';

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
    test('codes are letters, digits, dot, dash, underscore or percent without spaces, at most 16', () => {
        assert.deepEqual(['30D', 'EOM-30', 'a.b_c', '30 D', '-30', 'X'.repeat(17), 'X'.repeat(16), '30D-2%8', '365EM-99.99%365', '%30'].map(isConditionCode), [true, true, true, false, false, false, true, true, true, false]);
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
        assert.deepEqual(errors({ discountPercent: '2.5', discountDays: '5' }), ['discountPercent:discount_percent'], 'a dot is never a decimal separator in nl');
        assert.deepEqual(errors({ discountPercent: '100', discountDays: '5' }), ['discountPercent:discount_percent']);
        assert.deepEqual(errors({ discountPercent: '99,99', discountDays: '31' }), ['discountDays:discount_days']);
        assert.deepEqual(errors({ code: '', descriptionNl: ' ' }), ['code:required', 'descriptionNl:required']);
    });
});

// Spiegel van RelationFinanceRulesTests.Free_Terms_Validation: dagen leeg (= standaard) of geheel 0..365, korting beide of geen van
// beide (fout bij het lege veld), kortingsdagen niet langer dan de effectieve termijn.
describe('free payment terms on a role', () => {
    const input = (over: Partial<FreeTermsInput> = {}): FreeTermsInput => ({ paymentTermsDays: '', paymentTermsEndOfMonth: false, discountPercent: '', discountDays: '', ...over });
    const codes = (over: Partial<FreeTermsInput>, defaultDays = 30) => validateFreeTerms(input(over), defaultDays).errors.map((e) => `${e.field}:${e.code}`);

    test('days are empty (default) or whole numbers 0..365; never decimals, signs or exponents', () => {
        assert.deepEqual(validateFreeTerms(input(), 30), { values: { paymentTermsDays: null, paymentTermsEndOfMonth: false, discountPercent: null, discountDays: null }, effectiveDays: 30, errors: [] });
        for (const ok of ['0', ' 30 ', '365', '045']) assert.deepEqual(codes({ paymentTermsDays: ok }), [], ok);
        for (const bad of ['366', '-1', '30.5', '30,5', '1e2', 'x', '1000']) assert.deepEqual(codes({ paymentTermsDays: bad }), ['paymentTermsDays:terms_days'], bad);
    });

    test('a discount needs both fields; the error sits on the empty one; both empty = no discount', () => {
        assert.deepEqual(codes({ discountPercent: '2' }), ['discountDays:discount_days_required']);
        assert.deepEqual(codes({ discountDays: '8' }), ['discountPercent:discount_percent_required']);
        assert.deepEqual(validateFreeTerms(input({ paymentTermsDays: '30', discountPercent: '2,5', discountDays: '8' }), 30).values, { paymentTermsDays: 30, paymentTermsEndOfMonth: false, discountPercent: 2.5, discountDays: 8 });
        for (const bad of ['0', '100', '2,555', '-2']) assert.deepEqual(codes({ discountPercent: bad, discountDays: '8' }), ['discountPercent:discount_percent'], bad);
        assert.deepEqual(codes({ paymentTermsDays: '30', discountPercent: '2', discountDays: '31' }), ['discountDays:discount_days']);
        assert.deepEqual(codes({ discountPercent: '2', discountDays: '15' }, 14), ['discountDays:discount_days'], 'empty days: checked against the default');
    });

    test('the preview follows every keystroke: due date by basis, discount only when both fields are valid', () => {
        assert.deepEqual(freeTermsPreview('2026-10-26', input(), 30), { due: '2026-11-25', discountPercent: null, discountDate: null });
        assert.deepEqual(freeTermsPreview('2026-10-26', input({ paymentTermsDays: '30', paymentTermsEndOfMonth: true }), 30), { due: '2026-11-30', discountPercent: null, discountDate: null });
        assert.deepEqual(freeTermsPreview('2026-10-26', input({ paymentTermsDays: '30', discountPercent: '2' }), 30), { due: '2026-11-25', discountPercent: null, discountDate: null });
        assert.deepEqual(freeTermsPreview('2026-10-26', input({ paymentTermsDays: '30', discountPercent: '2', discountDays: '8' }), 30), { due: '2026-11-25', discountPercent: 2, discountDate: '2026-11-03' });
        assert.equal(freeTermsPreview('2026-10-26', input({ paymentTermsDays: '3x' }), 30), null);
    });
});
