import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Exact het contract van de afwijkende ontvangst (backend: AgfAdmin/DTOs/ReceiptDeviationDtos.cs).

export interface DeviationVariant {
    productVariantId: number;
    productId: number;
    productName: string | null;
    variantName: string | null;
    origin: string | null;
    size: string | null;
    packaging: string | null;
    packagingUnit: string | null;
    tradeClass: string | null;
    brand: string | null;
}

// Andere variant van hetzelfde product; otherArticle = ander ras of naam (nooit een afwijking, wel een nieuwe aanvoerregel).
export interface DeviationCandidate {
    variant: DeviationVariant;
    shortcutCode: string | null;
    otherArticle: boolean;
    kinds: string[];
}

export type DeviationStatus = 'Exact' | 'Deviation' | 'OtherArticle' | 'BlockedAssigned';

export interface DeviationPreview {
    status: DeviationStatus;
    supplyCommitmentId: number;
    orderNumber: string;
    originalLineId: number;
    expected: DeviationVariant;
    received: DeviationVariant;
    kinds: string[];
    quantity: number;
    unit: string | null;
    originalExpectedQuantity: number;
    originalReceivedQuantity: number;
    reducedQuantity: number;
    originalExpectedQuantityAfter: number;
    excessQuantity: number;
    assignedQuantity: number | null;
    // Netto gewicht (nooit bruto): per verpakking en voor deze hoeveelheid, en het verwachte kg van de regel vóór en na Ja.
    expectedKgPerPackage?: number | null;
    receivedKgPerPackage?: number | null;
    expectedNetKg?: number | null;
    receivedNetKg?: number | null;
    originalExpectedNetKg?: number | null;
    originalExpectedNetKgAfter?: number | null;
}

export interface PurchaseOrderDeviation {
    id: number;
    originalLineId: number;
    // Null bij een afwijking in alleen het gewicht (geen vervangende regel).
    newLineId: number | null;
    palletContentLineId: number;
    palletReference: string | null;
    kinds: string[];
    expected: DeviationVariant | null;
    received: DeviationVariant | null;
    quantity: number;
    unit: string;
    reducedQuantity: number;
    expectedKgPerPackage?: number | null;
    receivedKgPerPackage?: number | null;
    reason: string | null;
    reasonNote: string | null;
    actorReference: string | null;
    createdAt: string;
}

const isStringArray = (value: unknown): value is string[] => isArrayOf(value, isString);

export function isDeviationVariant(value: unknown): value is DeviationVariant {
    return isObject(value) && isNumber(value.productVariantId) && isNumber(value.productId) && isNullableString(value.productName)
        && isNullableString(value.variantName) && isNullableString(value.origin) && isNullableString(value.size) && isNullableString(value.packaging)
        && isNullableString(value.packagingUnit) && isNullableString(value.tradeClass) && isNullableString(value.brand);
}

export function isDeviationCandidateList(value: unknown): value is DeviationCandidate[] {
    return isArrayOf(value, (item): item is DeviationCandidate => isObject(item) && isDeviationVariant(item.variant)
        && isNullableString(item.shortcutCode) && typeof item.otherArticle === 'boolean' && isStringArray(item.kinds));
}

export function isDeviationPreview(value: unknown): value is DeviationPreview {
    return isObject(value) && isString(value.status) && ['Exact', 'Deviation', 'OtherArticle', 'BlockedAssigned'].includes(value.status)
        && isNumber(value.supplyCommitmentId) && isNumber(value.originalLineId) && isString(value.orderNumber) && isDeviationVariant(value.expected) && isDeviationVariant(value.received)
        && isStringArray(value.kinds) && isNumber(value.quantity) && isNullableString(value.unit) && isNumber(value.originalExpectedQuantity)
        && isNumber(value.originalReceivedQuantity) && isNumber(value.reducedQuantity) && isNumber(value.originalExpectedQuantityAfter)
        && isNumber(value.excessQuantity) && isNullableNumber(value.assignedQuantity);
}

export function isPurchaseOrderDeviation(value: unknown): value is PurchaseOrderDeviation {
    return isObject(value) && isNumber(value.id) && isNumber(value.originalLineId) && isNullableNumber(value.newLineId) && isStringArray(value.kinds)
        && (value.expected === null || isDeviationVariant(value.expected)) && (value.received === null || isDeviationVariant(value.received))
        && isNumber(value.quantity) && isString(value.unit) && isNumber(value.reducedQuantity) && isNullableString(value.reason)
        && isNullableString(value.reasonNote) && isNullableString(value.actorReference) && isString(value.createdAt) && isNullableString(value.palletReference);
}
