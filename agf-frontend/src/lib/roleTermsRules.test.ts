import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { TermsRuleError, emptyCreditDraft, emptyTermsDraft, insuranceExpired, insuranceExpiresSoon, insuranceGap, normalizeCredit, normalizeTerms, parseAmount } from './roleTermsRules.ts';

// Spiegel van RoleTermsRulesTests: incasso vraagt mandaat, Incoterm vraagt plaats, "anders" vraagt tekst, velden die niet bij de
// keuze horen worden leeggemaakt; krediet en verzekering met de vensters verlopen / verloopt binnen 30 dagen / gat.

const today = '2026-10-26';
const codes = (errors: TermsRuleError[]) => errors.map((e) => `${e.field}:${e.code}`);

describe('role terms', () => {
    test('empty method is bank transfer; other methods clear the mandate fields', () => {
        const errors: TermsRuleError[] = [];
        const values = normalizeTerms({ ...emptyTermsDraft(), paymentMethod: '', mandateReference: 'M-1', mandateSignedOn: '2026-01-01' }, 'customer', today, errors);
        assert.deepEqual(errors, []);
        assert.deepEqual([values.paymentMethod, values.mandateReference, values.mandateSignedOn], ['BankTransfer', null, null]);
    });

    test('direct debit needs a mandate reference and a signing date that is not in the future', () => {
        const errors: TermsRuleError[] = [];
        normalizeTerms({ ...emptyTermsDraft(), paymentMethod: 'DirectDebit' }, 'vendor', today, errors);
        assert.deepEqual(codes(errors), ['vendor.mandateReference:mandate_required', 'vendor.mandateSignedOn:mandate_date_required']);
        const future: TermsRuleError[] = [];
        normalizeTerms({ ...emptyTermsDraft(), paymentMethod: 'DirectDebit', mandateReference: 'X'.repeat(36), mandateSignedOn: '2026-10-27' }, 'vendor', today, future);
        assert.deepEqual(codes(future), ['vendor.mandateReference:too_long', 'vendor.mandateSignedOn:mandate_future']);
        const ok: TermsRuleError[] = [];
        const values = normalizeTerms({ ...emptyTermsDraft(), paymentMethod: 'DirectDebit', mandateReference: ' SEPA-1 ', mandateSignedOn: today }, 'vendor', today, ok);
        assert.deepEqual([ok, values.mandateReference, values.mandateSignedOn], [[], 'SEPA-1', today]);
    });

    test('an Incoterm needs a 2020 code and a place; text is cleared; Other needs its own text; Franco keeps an optional note', () => {
        const errors: TermsRuleError[] = [];
        normalizeTerms({ ...emptyTermsDraft(), deliveryTermKind: 'Incoterm', incoterm: 'FOT' }, 'customer', today, errors);
        assert.deepEqual(codes(errors), ['customer.incoterm:incoterm', 'customer.incotermPlace:incoterm_place']);
        const ok: TermsRuleError[] = [];
        const values = normalizeTerms({ ...emptyTermsDraft(), deliveryTermKind: 'Incoterm', incoterm: 'fca', incotermPlace: 'Venlo', deliveryTermText: 'weg' }, 'customer', today, ok);
        assert.deepEqual([ok, values.incoterm, values.incotermPlace, values.deliveryTermText], [[], 'FCA', 'Venlo', null]);
        const other: TermsRuleError[] = [];
        normalizeTerms({ ...emptyTermsDraft(), deliveryTermKind: 'Other' }, 'customer', today, other);
        assert.deepEqual(codes(other), ['customer.deliveryTermText:delivery_text']);
        const franco: TermsRuleError[] = [];
        const f = normalizeTerms({ ...emptyTermsDraft(), deliveryTermKind: 'Franco', incoterm: 'EXW', deliveryTermText: 'vanaf 10 pallets' }, 'customer', today, franco);
        assert.deepEqual([franco, f.incoterm, f.deliveryTermText], [[], null, 'vanaf 10 pallets']);
    });

    test('trade conditions Other need a text; others clear it', () => {
        const errors: TermsRuleError[] = [];
        normalizeTerms({ ...emptyTermsDraft(), tradeConditions: 'Other' }, 'vendor', today, errors);
        assert.deepEqual(codes(errors), ['vendor.tradeConditionsText:trade_text']);
        const ok: TermsRuleError[] = [];
        const values = normalizeTerms({ ...emptyTermsDraft(), tradeConditions: 'Frugiventa', tradeConditionsText: 'x', paymentConditionId: '4' }, 'vendor', today, ok);
        assert.deepEqual([ok, values.tradeConditionsText, values.paymentConditionId], [[], null, 4]);
    });
});

describe('credit and insurance', () => {
    test('amounts: empty, 0 or more with at most 2 decimals, comma or dot', () => {
        assert.deepEqual(['', '0', '1000', '1000,5', '12.34', '-1', '1.234', '1.000,00'].map(parseAmount), [null, 0, 1000, 1000.5, 12.34, undefined, undefined, undefined]);
    });

    test('not insured clears every insurance field; insured needs an insurer (Other + name) and an insured limit', () => {
        const errors: TermsRuleError[] = [];
        const plain = normalizeCredit({ ...emptyCreditDraft(), creditLimit: '5000', insurer: 'Coface', insuredLimit: '100' }, 'customer', errors);
        assert.deepEqual([errors, plain], [[], { creditLimit: 5000, isInsured: false, insurer: null, insurerText: null, insuredLimit: null, insurerReference: null, insuredUntil: null }]);
        const missing: TermsRuleError[] = [];
        normalizeCredit({ ...emptyCreditDraft(), isInsured: true, creditLimit: 'x' }, 'customer', missing);
        assert.deepEqual(codes(missing), ['customer.creditLimit:amount', 'customer.insurer:insurer', 'customer.insuredLimit:insured_limit']);
        const other: TermsRuleError[] = [];
        normalizeCredit({ ...emptyCreditDraft(), isInsured: true, insurer: 'Other', insuredLimit: '0' }, 'customer', other);
        assert.deepEqual(codes(other), ['customer.insurerText:insurer_text']);
    });

    test('insurance windows: expired before today, expires soon from today through today + 30; gap only when insured lower', () => {
        assert.deepEqual([insuranceExpired('2026-10-25', today), insuranceExpired(today, today), insuranceExpired(null, today)], [true, false, false]);
        assert.deepEqual([insuranceExpiresSoon(today, today), insuranceExpiresSoon('2026-11-25', today), insuranceExpiresSoon('2026-11-26', today), insuranceExpiresSoon('2026-10-25', today)], [true, true, false, false]);
        assert.deepEqual([insuranceGap(10000, true, 7500), insuranceGap(10000, true, 12000), insuranceGap(10000, false, 7500), insuranceGap(null, true, 1)], [2500, null, null, null]);
    });
});
