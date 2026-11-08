import { isNullableString, isNumber, isString } from './guards';
import type { PartijKind, PartijNumberMode, PartijSequenceReset, PartijStatus } from '../lib/partijRules';

// Partijen (api/partijen): zie AgfAdmin/DTOs/PartijDtos.cs.

// Modus en opnieuw tellen gelden voor beide reeksen; formaat en aantal cijfers per reeks (inkoop, verkoop).
export interface PartijSettings {
    mode: PartijNumberMode;
    format: string;
    sequenceReset: PartijSequenceReset;
    sequenceDigits: number;
    salesFormat: string;
    salesSequenceDigits: number;
}

export interface PartijNextNumber {
    kind: PartijKind;
    mode: PartijNumberMode;
    format: string;
    sequenceReset: PartijSequenceReset;
    sequenceDigits: number;
    // Null in modus Own (zelf typen).
    suggestion: string | null;
}

export interface PartijSummary {
    id: number;
    number: string;
    kind: PartijKind;
    salesOrderId: number | null;
    status: string;
    note: string | null;
    createdAt: string;
    createdBy: string;
    linked: boolean;
}

export interface PartijTotals {
    id: number;
    number: string;
    kind: PartijKind;
    // Alleen bij een verkoop-partij: de order (1-op-1), de klant, wat er besteld is en uit welke inkoop-partijen de colli komen.
    salesOrderId: number | null;
    salesOrderNumber: string | null;
    salesOrderStatus: string | null;
    customerName: string | null;
    orderedQuantity: number;
    sourcePartijen: string[];
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
    // Netto kg (ontvangen bij inkoop, van de order bij verkoop): basis voor "verdeel naar kg"; ontbreekt bij een oudere server.
    netKg?: number;
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
    salesPartijId: number | null;
    salesPartijNumber: string | null;
    customerName: string | null;
    status: string;
    unit: string;
    reservedQuantity: number;
    shippedQuantity: number;
}

export interface PartijHistory {
    occurredAt: string;
    type: 'Created' | 'NumberCorrected' | 'NumberChanged' | 'OrderLinked' | 'Deleted';
    oldValue: string | null;
    newValue: string | null;
    reason: string | null;
    supplyCommitmentId: number | null;
    actorReference: string;
}

// Een inkoop-partij waaruit een verkoop-partij colli pakt (via de reserveringen).
export interface PartijSource {
    partijId: number;
    number: string | null;
    vendorName: string | null;
    product: string | null;
    unit: string;
    reservedQuantity: number;
    shippedQuantity: number;
}

// Een kostenregel op de partij (P2): uit een inkoopfactuurregel, excl. btw.
export interface PartijCostLine {
    kind: string;
    invoiceId: number;
    invoiceNumber: string | null;
    vendorName: string | null;
    invoiceDate: string | null;
    description: string | null;
    // In euro.
    amount: number;
    // Bij een factuur in een andere valuta: die valuta, het bedrag daarin en de ECB-koers (ontbreekt bij EUR of een oudere server).
    currency?: string | null;
    amountInCurrency?: number | null;
    exchangeRate?: number | null;
}

export interface PartijLinkedCost {
    partijId: number;
    number: string;
    kind: string;
    costTotal: number;
}

export interface PartijDetail extends PartijTotals {
    note: string | null;
    createdBy: string;
    linked: boolean;
    // Het nummer verandert alleen met reden (inkoop-partij met koppelingen; verkoop-partij na reserveren of leveren).
    numberFixed: boolean;
    sources: PartijSource[];
    // Kosten van deze partij en apart (niet opgeteld) die van gekoppelde partijen van de andere soort; ontbreken bij een oudere server.
    costs?: PartijCostLine[];
    costTotal?: number;
    linkedCosts?: PartijLinkedCost[];
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

// De velden van P2 (aantal cijfers, verkoopformaat, soort) ontbreken bij een oudere server; partijService vult dan de standaard in.
export function isPartijSettings(value: unknown): value is PartijSettings {
    return isRecord(value) && isString(value.mode) && isString(value.format) && isString(value.sequenceReset)
        && (value.sequenceDigits === undefined || isNumber(value.sequenceDigits)) && (value.salesFormat === undefined || isString(value.salesFormat))
        && (value.salesSequenceDigits === undefined || isNumber(value.salesSequenceDigits));
}

export function withSettingsDefaults(settings: PartijSettings): PartijSettings {
    return { ...settings, sequenceDigits: settings.sequenceDigits ?? 3, salesFormat: settings.salesFormat ?? '{JJ}{MM}{volgnr}', salesSequenceDigits: settings.salesSequenceDigits ?? 3 };
}

export function isPartijNextNumber(value: unknown): value is PartijNextNumber {
    return isRecord(value) && isString(value.mode) && isString(value.format) && isNullableString(value.suggestion);
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
