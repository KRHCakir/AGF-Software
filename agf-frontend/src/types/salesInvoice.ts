import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Verkoopfactuur 1: de verkoopfactuur (concept, definitief, vervallen). Backend: AgfAdmin/DTOs/SalesInvoiceDtos.cs.
export type SalesInvoiceKind = 'Delivery' | 'Direct';
export type SalesInvoiceStatus = 'Concept' | 'Final' | 'Discarded';
export type SalesInvoiceLineKind = 'Product' | 'Service' | 'Packaging';

export interface SalesInvoiceVatRate { percentage: number; net: number; vat: number }
export interface SalesInvoiceTotals { net: number | null; vatRates: SalesInvoiceVatRate[]; vat: number | null; gross: number | null; linesWithoutPrice: number }
export interface SalesInvoiceDelivery { salesDeliveryId: number; salesOrderId: number; orderNumber: string; sequence: number; partijNumber: string | null; deliveredAt: string | null; customerReference: string | null }

export interface SalesInvoiceLine {
    id: number;
    lineKind: SalesInvoiceLineKind;
    sortOrder: number;
    salesDeliveryId: number | null;
    salesOrderLineId: number | null;
    salesOrderServiceLineId: number | null;
    salesOrderPackagingLineId: number | null;
    productVariantId: number | null;
    partijId: number | null;
    partijNumber: string | null;
    costArticleId: number | null;
    revenueTypeId: number | null;
    revenueTypeName: string | null;
    description: string | null;
    quantity: number;
    unit: string;
    unitPrice: number | null;
    discountPercentage: number;
    vatPercentage: number;
    amount: number | null;
    vatAmount: number | null;
    netWeightKg: number | null;
    origin: string | null;
    lotNumber: string | null;
    ggn: string | null;
    // In een concept met regels uit een levering: het hoogste aantal (nooit meer dan geleverd).
    maxQuantity: number | null;
}

export interface SalesInvoice {
    id: number;
    kind: SalesInvoiceKind;
    status: SalesInvoiceStatus;
    invoiceNumber: string | null;
    invoiceDate: string;
    deliveryDate: string | null;
    dueDate: string | null;
    paymentTermDays: number | null;
    customerId: number | null;
    customerName: string | null;
    customerAddress: string | null;
    customerVatNumber: string | null;
    referenceNumber: string | null;
    salesLotNumber: string | null;
    deliveries: SalesInvoiceDelivery[];
    lines: SalesInvoiceLine[];
    totals: SalesInvoiceTotals;
    createdAt: string;
    createdBy: string | null;
    finalizedAt: string | null;
    finalizedBy: string | null;
    discardedAt: string | null;
    discardedBy: string | null;
    discardReason: string | null;
    stale: boolean;
    version: number;
}

export interface SalesInvoiceListItem {
    id: number;
    kind: SalesInvoiceKind;
    status: SalesInvoiceStatus;
    invoiceNumber: string | null;
    invoiceDate: string;
    deliveryDate: string | null;
    dueDate: string | null;
    customerId: number | null;
    customerName: string | null;
    salesLotNumbers: string[];
    purchaseLotNumbers: string[];
    orderNumbers: string[];
    referenceNumber: string | null;
    net: number;
    vat: number;
    gross: number;
    linesWithoutPrice: number;
    stale: boolean;
    createdAt: string;
    version: number;
}

export interface SalesInvoiceList { items: SalesInvoiceListItem[]; total: number; page: number; pageSize: number }

export interface FinalizeResult { id: number; final: boolean; invoiceNumber: string | null; version: number | null; message: string | null; errors: Record<string, string[]> | null }

export interface RevenueType { id: number; name: string; externalCode: string | null; isActive: boolean; isDefault: boolean; version: number }

const isStringList = (value: unknown): value is string[] => isArrayOf(value, isString);
const isKind = (value: unknown): value is SalesInvoiceKind => value === 'Delivery' || value === 'Direct';
const isStatus = (value: unknown): value is SalesInvoiceStatus => value === 'Concept' || value === 'Final' || value === 'Discarded';

const isLine = (value: unknown): value is SalesInvoiceLine => isObject(value) && isNumber(value.id) && isString(value.lineKind) && isNumber(value.quantity)
    && isString(value.unit) && isNullableNumber(value.unitPrice) && isNumber(value.discountPercentage) && isNumber(value.vatPercentage) && isNullableNumber(value.amount)
    && isNullableString(value.description) && isNullableString(value.lotNumber) && isNullableNumber(value.maxQuantity);

const isTotals = (value: unknown): value is SalesInvoiceTotals => isObject(value) && isNullableNumber(value.net) && isNullableNumber(value.gross)
    && isArrayOf(value.vatRates, (rate): rate is SalesInvoiceVatRate => isObject(rate) && isNumber(rate.percentage) && isNumber(rate.net) && isNumber(rate.vat));

const isDelivery = (value: unknown): value is SalesInvoiceDelivery => isObject(value) && isNumber(value.salesDeliveryId) && isNumber(value.salesOrderId)
    && isString(value.orderNumber) && isNumber(value.sequence);

export function isSalesInvoice(value: unknown): value is SalesInvoice {
    return isObject(value) && isNumber(value.id) && isKind(value.kind) && isStatus(value.status) && isNullableString(value.invoiceNumber) && isString(value.invoiceDate)
        && isArrayOf(value.lines, isLine) && isArrayOf(value.deliveries, isDelivery) && isTotals(value.totals) && typeof value.stale === 'boolean' && isNumber(value.version);
}

const isListItem = (value: unknown): value is SalesInvoiceListItem => isObject(value) && isNumber(value.id) && isKind(value.kind) && isStatus(value.status)
    && isNullableString(value.invoiceNumber) && isStringList(value.salesLotNumbers) && isStringList(value.purchaseLotNumbers) && isStringList(value.orderNumbers)
    && isNumber(value.gross) && isNumber(value.version) && typeof value.stale === 'boolean';

export function isSalesInvoiceList(value: unknown): value is SalesInvoiceList {
    return isObject(value) && isArrayOf(value.items, isListItem) && isNumber(value.total) && isNumber(value.page) && isNumber(value.pageSize);
}

export function isFinalizeResults(value: unknown): value is { results: FinalizeResult[] } {
    return isObject(value) && isArrayOf(value.results, (r): r is FinalizeResult => isObject(r) && isNumber(r.id) && typeof r.final === 'boolean' && isNullableString(r.invoiceNumber));
}

export function isRevenueTypes(value: unknown): value is RevenueType[] {
    return isArrayOf(value, (r): r is RevenueType => isObject(r) && isNumber(r.id) && isString(r.name) && typeof r.isActive === 'boolean' && typeof r.isDefault === 'boolean' && isNumber(r.version));
}
