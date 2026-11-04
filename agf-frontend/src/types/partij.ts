import { isNullableString, isNumber, isString } from './guards';
import type { PartijNumberMode, PartijSequenceReset, PartijStatus } from '../lib/partijRules';

// Partijen (api/partijen): zie AgfAdmin/DTOs/PartijDtos.cs.

export interface PartijSettings {
    mode: PartijNumberMode;
    format: string;
    sequenceReset: PartijSequenceReset;
}

export interface PartijNextNumber extends PartijSettings {
    // Null in modus Own (zelf typen).
    suggestion: string | null;
}

export interface PartijSummary {
    id: number;
    number: string;
    status: string;
    note: string | null;
    createdAt: string;
    createdBy: string;
    linked: boolean;
}

export interface PartijTotals {
    id: number;
    number: string;
    status: PartijStatus;
    createdAt: string;
    vendors: string[];
    products: string[];
    supplierLotReferences: string[];
    // De eenheid als die voor de hele partij gelijk is, anders null (gemengd).
    unit: string | null;
    purchasedQuantity: number;
    receivedQuantity: number;
    onHandQuantity: number;
    inInspectionQuantity: number;
    openExpectedQuantity: number;
    soldQuantity: number;
}

export interface PartijList {
    items: PartijTotals[];
    total: number;
    page: number;
    pageSize: number;
}

export interface PartijOrder {
    id: number;
    orderNumber: string;
    vendorName: string | null;
    status: string;
    orderedOn: string;
    expectedAt: string | null;
    supplierLotReference: string | null;
    unit: string | null;
    expectedQuantity: number;
    receivedQuantity: number;
}

export interface PartijReceipt {
    palletContentLineId: number;
    palletId: number;
    palletReference: string;
    palletStatus: string;
    supplyCommitmentId: number;
    orderNumber: string;
    productName: string | null;
    productVariantName: string | null;
    lotNumber: string;
    kind: 'Receipt' | 'Correction' | 'SplitOff';
    quantity: number;
    unit: string;
    netWeightKg: number;
    onHandQuantity: number;
    qualityStatus: string;
    receivedAt: string;
}

export interface PartijSalesOrder {
    salesOrderId: number;
    orderNumber: string | null;
    customerName: string | null;
    status: string;
    unit: string;
    reservedQuantity: number;
    shippedQuantity: number;
}

export interface PartijHistory {
    occurredAt: string;
    type: 'Created' | 'NumberCorrected' | 'OrderLinked' | 'Deleted';
    oldValue: string | null;
    newValue: string | null;
    reason: string | null;
    supplyCommitmentId: number | null;
    actorReference: string;
}

export interface PartijDetail extends PartijTotals {
    note: string | null;
    createdBy: string;
    linked: boolean;
    reservedQuantity: number;
    shippedQuantity: number;
    orders: PartijOrder[];
    receipts: PartijReceipt[];
    salesOrders: PartijSalesOrder[];
    history: PartijHistory[];
}

// 409 bij een nummer dat al bestaat (zelfde sleutel): het scherm vraagt of de bestaande partij gebruikt moet worden.
export interface PartijExistsConflict {
    message: string;
    code: 'PARTIJ_EXISTS';
    partij: PartijSummary;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null;
}

export function isPartijSettings(value: unknown): value is PartijSettings {
    return isRecord(value) && isString(value.mode) && isString(value.format) && isString(value.sequenceReset);
}

export function isPartijNextNumber(value: unknown): value is PartijNextNumber {
    return isPartijSettings(value) && isNullableString((value as unknown as Record<string, unknown>).suggestion);
}

export function isPartijSummary(value: unknown): value is PartijSummary {
    return isRecord(value) && isNumber(value.id) && isString(value.number) && isString(value.status) && typeof value.linked === 'boolean';
}

function isPartijTotals(value: unknown): value is PartijTotals {
    return isRecord(value) && isNumber(value.id) && isString(value.number) && isString(value.status) && Array.isArray(value.vendors)
        && Array.isArray(value.products) && isNumber(value.purchasedQuantity) && isNumber(value.onHandQuantity) && isNumber(value.soldQuantity);
}

export function isPartijList(value: unknown): value is PartijList {
    return isRecord(value) && Array.isArray(value.items) && value.items.every(isPartijTotals) && isNumber(value.total);
}

export function isPartijDetail(value: unknown): value is PartijDetail {
    return isPartijTotals(value) && Array.isArray((value as unknown as Record<string, unknown>).orders)
        && Array.isArray((value as unknown as Record<string, unknown>).receipts) && Array.isArray((value as unknown as Record<string, unknown>).history);
}

export function isPartijExistsConflict(value: unknown): value is PartijExistsConflict {
    return isRecord(value) && value.code === 'PARTIJ_EXISTS' && isPartijSummary(value.partij);
}
