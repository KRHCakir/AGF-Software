import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Exact het contract van /api/purchase-orders (backend: AgfAdmin/DTOs/PurchaseOrderDtos.cs en PurchaseOrderRejectionDtos.cs).
// Datums zonder tijd (orderedOn, invoiceDate) als yyyy-MM-dd; tijdstippen als ISO in UTC.

export interface PurchaseOrderInvoiceNumber {
    kind: string;
    invoiceNumber: string;
}

export interface PurchaseOrderListItem {
    id: number;
    orderNumber: string;
    vendorId: number;
    vendorName: string | null;
    supplierReference: string | null;
    orderedOn: string;
    expectedAt: string | null;
    route: string;
    customerName: string | null;
    status: string;
    storedStatus: string;
    expectedQuantity: number;
    expectedNetWeightKg: number | null;
    receivedQuantity: number;
    rejectedQuantity: number;
    invoiceNumbers: PurchaseOrderInvoiceNumber[];
    invoiceIndicator: string;
}

export interface PurchaseOrderList {
    items: PurchaseOrderListItem[];
    total: number;
    page: number;
    pageSize: number;
}

export interface PurchaseOrderLine {
    id: number;
    productVariantId: number;
    productName: string | null;
    productVariantName: string | null;
    lotNumber: string | null;
    expectedQuantity: number;
    unit: string | null;
    expectedNetWeightKg: number | null;
    receivedQuantity: number;
    receivedNetWeightKg: number;
    rejectedQuantity: number;
    rejectedNetWeightKg: number;
}

export interface PurchaseOrderReceiptLine {
    palletContentLineId: number;
    palletId: number;
    palletReference: string;
    palletStatus: string;
    supplyCommitmentLineId: number;
    productName: string | null;
    productVariantName: string | null;
    lotNumber: string;
    quantity: number;
    unit: string;
    netWeightKg: number;
    weightMethod: string;
    netKgPerPackage: number | null;
    onHandQuantity: number;
    onHandNetWeightKg: number;
    reservedQuantity: number;
    reservedNetWeightKg: number;
    qualityStatus: string;
    qualityReason: string | null;
    origin: string;
    receivedAt: string;
    rejectable: boolean;
    rejectableQuantity: number;
    rejectableNetWeightKg: number;
    version: number;
}

export interface PurchaseOrderInvoiceRef {
    id: number;
    kind: string;
    invoiceNumber: string;
    invoiceDate: string | null;
    amountExclVat: number | null;
    incomingInvoiceId: number | null;
    actorReference: string | null;
    createdAt: string;
}

export interface PurchaseOrderRejectionLine {
    palletContentLineId: number;
    palletReference: string | null;
    lotNumber: string | null;
    quantity: number;
    unit: string;
    netWeightKg: number;
    splitOff: boolean;
}

export interface PurchaseOrderRejection {
    id: number;
    reason: string;
    explanation: string | null;
    decision: string;
    actorReference: string;
    rejectedAt: string;
    lines: PurchaseOrderRejectionLine[];
}

export interface PurchaseOrderHistoryEvent {
    occurredAt: string;
    type: string;
    actorReference: string | null;
    reference: string | null;
    channel: string | null;
    quantity: number | null;
    unit: string | null;
    fromStatus: string | null;
    toStatus: string | null;
    reason: string | null;
    decision: string | null;
}

export interface PurchaseOrderDetail {
    id: number;
    orderNumber: string;
    vendorId: number;
    vendorName: string | null;
    supplierReference: string | null;
    orderedOn: string;
    orderChannel: string | null;
    note: string | null;
    route: string;
    customerName: string | null;
    origin: string;
    destination: string | null;
    expectedAt: string | null;
    status: string;
    storedStatus: string;
    invoiceIndicator: string;
    expectedQuantity: number;
    expectedNetWeightKg: number | null;
    receivedQuantity: number;
    receivedNetWeightKg: number;
    rejectedQuantity: number;
    rejectedNetWeightKg: number;
    lines: PurchaseOrderLine[];
    receipts: PurchaseOrderReceiptLine[];
    invoiceRefs: PurchaseOrderInvoiceRef[];
    rejections: PurchaseOrderRejection[];
    history: PurchaseOrderHistoryEvent[];
}

export interface PurchaseOrderInvoiceMatch {
    id: number;
    orderNumber: string;
    kind: string;
    vendorName: string | null;
}

export interface RejectRequest {
    requestId: string;
    reason: string;
    explanation: string | null;
    decision: string;
    actorReference: string;
    lines: { palletContentLineId: number; expectedContentLineVersion: number; quantity: number; netWeightKg: number }[];
}

function isListItem(value: unknown): value is PurchaseOrderListItem {
    return isObject(value) && isNumber(value.id) && isString(value.orderNumber) && isNumber(value.vendorId) && isNullableString(value.vendorName)
        && isNullableString(value.supplierReference) && isString(value.orderedOn) && isNullableString(value.expectedAt) && isString(value.status)
        && isNumber(value.expectedQuantity) && isNullableNumber(value.expectedNetWeightKg) && isNumber(value.receivedQuantity) && isNumber(value.rejectedQuantity)
        && isArrayOf(value.invoiceNumbers, (item): item is PurchaseOrderInvoiceNumber => isObject(item) && isString(item.kind) && isString(item.invoiceNumber))
        && isString(value.invoiceIndicator);
}

export function isPurchaseOrderList(value: unknown): value is PurchaseOrderList {
    return isObject(value) && isArrayOf(value.items, isListItem) && isNumber(value.total) && isNumber(value.page) && isNumber(value.pageSize);
}

function isReceipt(value: unknown): value is PurchaseOrderReceiptLine {
    return isObject(value) && isNumber(value.palletContentLineId) && isString(value.palletReference) && isString(value.lotNumber) && isNumber(value.quantity)
        && isString(value.unit) && isNumber(value.netWeightKg) && isNumber(value.onHandQuantity) && isNumber(value.onHandNetWeightKg)
        && isString(value.qualityStatus) && isString(value.origin) && typeof value.rejectable === 'boolean' && isNumber(value.rejectableQuantity)
        && isNumber(value.rejectableNetWeightKg) && isNumber(value.version) && isNullableNumber(value.netKgPerPackage) && isString(value.weightMethod);
}

export function isPurchaseOrderDetail(value: unknown): value is PurchaseOrderDetail {
    return isObject(value) && isNumber(value.id) && isString(value.orderNumber) && isNullableString(value.vendorName) && isString(value.orderedOn)
        && isString(value.status) && isString(value.invoiceIndicator) && isNumber(value.expectedQuantity) && isNumber(value.receivedQuantity)
        && isNumber(value.rejectedQuantity) && isNumber(value.rejectedNetWeightKg) && isString(value.origin) && isNullableString(value.orderChannel)
        && isNullableString(value.note) && isNullableString(value.supplierReference) && isNullableString(value.expectedAt)
        && isArrayOf(value.lines, (line): line is PurchaseOrderLine => isObject(line) && isNumber(line.id) && isNumber(line.expectedQuantity) && isNumber(line.receivedQuantity) && isNumber(line.rejectedQuantity))
        && isArrayOf(value.receipts, isReceipt)
        && isArrayOf(value.invoiceRefs, (ref): ref is PurchaseOrderInvoiceRef => isObject(ref) && isNumber(ref.id) && isString(ref.kind) && isString(ref.invoiceNumber) && isNullableString(ref.invoiceDate) && isNullableNumber(ref.amountExclVat))
        && isArrayOf(value.rejections, (rejection): rejection is PurchaseOrderRejection => isObject(rejection) && isString(rejection.reason) && isString(rejection.decision) && Array.isArray(rejection.lines))
        && isArrayOf(value.history, (event): event is PurchaseOrderHistoryEvent => isObject(event) && isString(event.occurredAt) && isString(event.type));
}

export function isInvoiceMatchList(value: unknown): value is PurchaseOrderInvoiceMatch[] {
    return isArrayOf(value, (item): item is PurchaseOrderInvoiceMatch => isObject(item) && isNumber(item.id) && isString(item.orderNumber) && isString(item.kind));
}
