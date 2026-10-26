import { isArrayOf, isNullableNumber, isNumber, isObject, isString } from './guards';
import { PaymentConditionBasis, isPaymentConditionBasis } from '../lib/paymentCondition';

// Contract van api/payment-conditions (backend: AgfAdmin/DTOs/PaymentConditionDtos.cs). Een conditie wordt door klant- en
// leveranciersrollen gedeeld; verwijderen kan alleen als niemand haar gebruikt, anders inactief zetten.

export interface PaymentConditionSummary {
    id: number;
    code: string;
    descriptionNl: string;
    descriptionEn: string;
    days: number;
    basis: PaymentConditionBasis;
    discountPercent: number | null;
    discountDays: number | null;
    isActive: boolean;
}

export interface PaymentCondition extends PaymentConditionSummary {
    sortOrder: number;
    version: number;
    customerCount: number;
    vendorCount: number;
}

// POST (requestId) en PUT (expectedVersion). isActive/sortOrder null = ongewijzigd laten (nieuw: actief, volgorde = dagen).
export interface SavePaymentConditionRequest {
    requestId?: string;
    expectedVersion?: number;
    code: string;
    descriptionNl: string;
    descriptionEn: string;
    days: number;
    basis: PaymentConditionBasis;
    discountPercent: number | null;
    discountDays: number | null;
    isActive: boolean | null;
    sortOrder: number | null;
}

export const PAYMENT_CONDITION_IN_USE_CODE = 'payment-condition-in-use';

export interface PaymentConditionInUse { message: string; code: typeof PAYMENT_CONDITION_IN_USE_CODE; customerCount: number; vendorCount: number }

export function isPaymentConditionSummary(value: unknown): value is PaymentConditionSummary {
    return isObject(value) && isNumber(value.id) && isString(value.code) && isString(value.descriptionNl) && isString(value.descriptionEn) && isNumber(value.days)
        && isPaymentConditionBasis(value.basis) && isNullableNumber(value.discountPercent) && isNullableNumber(value.discountDays) && typeof value.isActive === 'boolean';
}

export function isPaymentCondition(value: unknown): value is PaymentCondition {
    return isPaymentConditionSummary(value) && isObject(value) && isNumber(value.sortOrder) && isNumber(value.version) && isNumber(value.customerCount) && isNumber(value.vendorCount);
}

export function isPaymentConditionList(value: unknown): value is PaymentCondition[] {
    return isArrayOf(value, isPaymentCondition);
}

export function isPaymentConditionInUse(value: unknown): value is PaymentConditionInUse {
    return isObject(value) && value.code === PAYMENT_CONDITION_IN_USE_CODE && isString(value.message) && isNumber(value.customerCount) && isNumber(value.vendorCount);
}
