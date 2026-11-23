import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';
import { type CertificateCheck, isCertificateCheck } from './certificate';

// Ontvangen in één scherm (feedback 22 november): per inkooporder "Dit wordt verwacht", met per regel één knop Ontvangen.

export interface ReceiptPallet {
    palletId: number;
    reference: string;
    contentLineId: number;
    quantity: number;
    netWeightKg: number;
    qualityStatus: string;
}

export interface ReceiptLine {
    id: number;
    productId: number;
    productName: string | null;
    productVariantName: string | null;
    origin: string | null;
    size: string | null;
    partijNumber: string | null;
    unit: string | null;
    orderedQuantity: number;
    receivedQuantity: number;
    openQuantity: number;
    // Null = de order kent geen kg per verpakking: de ontvanger vult het netto kg in.
    netKgPerPackage: number | null;
    suggestedPalletCount: number | null;
    receivable: boolean;
    pallets: ReceiptPallet[];
}

export interface ReceiptOrder {
    orderId: number;
    orderNumber: string | null;
    supplierReference: string | null;
    vendorId: number;
    vendorName: string | null;
    expectedAt: string | null;
    partijNumber: string | null;
    // Feedback 22 november: waar ontvangen wordt (null = de eigen loods); op een locatie van de soort Onderweg heet de knop Afgeleverd.
    locationId?: number | null;
    locationName?: string | null;
    locationKind?: string | null;
    status: string;
    receivable: boolean;
    notReceivableReason: string | null;
    lines: ReceiptLine[];
    certificateWarnings: CertificateCheck[];
}

export interface ReceiveLineRequest {
    requestId: string;
    quantity: number;
    netWeightKg?: number | null;
    palletCount?: number | null;
    palletReferences?: string[] | null;
}

export interface ReceiveLineResult {
    lineId: number;
    quantity: number;
    netWeightKg: number;
    pallets: ReceiptPallet[];
    order: ReceiptOrder;
}

const isPallet = (value: unknown): value is ReceiptPallet => isObject(value) && isNumber(value.palletId) && isString(value.reference) && isNumber(value.contentLineId)
    && isNumber(value.quantity) && isNumber(value.netWeightKg) && isString(value.qualityStatus);

const isLine = (value: unknown): value is ReceiptLine => isObject(value) && isNumber(value.id) && isNumber(value.productId) && isNullableString(value.productName)
    && isNullableString(value.productVariantName) && isNullableString(value.origin) && isNullableString(value.size) && isNullableString(value.partijNumber)
    && isNullableString(value.unit) && isNumber(value.orderedQuantity) && isNumber(value.receivedQuantity) && isNumber(value.openQuantity)
    && isNullableNumber(value.netKgPerPackage) && isNullableNumber(value.suggestedPalletCount) && typeof value.receivable === 'boolean' && isArrayOf(value.pallets, isPallet);

export const isReceiptOrder = (value: unknown): value is ReceiptOrder => isObject(value) && isNumber(value.orderId) && isNullableString(value.orderNumber)
    && isNullableString(value.supplierReference) && isNumber(value.vendorId) && isNullableString(value.vendorName) && isNullableString(value.expectedAt)
    && isNullableString(value.partijNumber) && isString(value.status) && typeof value.receivable === 'boolean' && isNullableString(value.notReceivableReason)
    && isArrayOf(value.lines, isLine) && isArrayOf(value.certificateWarnings, isCertificateCheck);

export const isReceiveLineResult = (value: unknown): value is ReceiveLineResult => isObject(value) && isNumber(value.lineId) && isNumber(value.quantity)
    && isNumber(value.netWeightKg) && isArrayOf(value.pallets, isPallet) && isReceiptOrder(value.order);
