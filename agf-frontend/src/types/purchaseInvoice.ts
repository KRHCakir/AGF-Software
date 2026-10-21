import { PACKAGE_UNITS, PackageUnit } from './availability';
import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Contract van api/purchase-invoices (backend: AgfAdmin/DTOs/PurchaseInvoiceDtos.cs).

export const PURCHASE_OPTIONAL_FIELDS = [
    'receivedDate', 'paymentReference', 'remarks', 'grossWeightKg', 'netWeightKg', 'containerNumber', 'transporter',
    'driverName', 'licensePlate', 'incoterms', 'customsValue', 'originCountry', 'receiptReference',
] as const;
export type PurchaseOptionalField = (typeof PURCHASE_OPTIONAL_FIELDS)[number];

export function isOptionalField(value: unknown): value is PurchaseOptionalField {
    return typeof value === 'string' && (PURCHASE_OPTIONAL_FIELDS as readonly string[]).includes(value);
}

export interface PurchaseVendor {
    id: number;
    name: string;
    vatNumber: string | null;
    paymentTermsDays: number;
    defaultCurrency: string | null;
}

export interface PurchaseInvoiceSettings {
    allOptionalFields: PurchaseOptionalField[];
    optionalFields: PurchaseOptionalField[];
    defaultVatPercentage: number;
    defaultPaymentTermsDays: number;
    defaultCurrency: string;
}

export interface UpdatePurchaseInvoiceSettingsRequest {
    optionalFields: PurchaseOptionalField[];
    defaultVatPercentage: number;
    defaultPaymentTermsDays: number;
    defaultCurrency: string;
}

export interface CreatePurchaseInvoiceLineRequest {
    productVariantId: number | null;
    description: string | null;
    quantity: number;
    unit: PackageUnit | null;
    unitPrice: number;
    discountPercentage: number;
    vatPercentage: number;
}

export type OptionalFieldValues = Partial<Record<PurchaseOptionalField, string | number | null>>;

export interface CreatePurchaseInvoiceRequest extends OptionalFieldValues {
    requestId: string;
    vendorId: number;
    invoiceNumber: string;
    invoiceDate: string;
    paymentTermsDays: number;
    dueDate: string;
    currency: string;
    lines: CreatePurchaseInvoiceLineRequest[];
}

export interface PurchaseInvoiceLine {
    id: number;
    productVariantId: number | null;
    description: string | null;
    quantity: number;
    unit: PackageUnit | null;
    unitPrice: number;
    discountPercentage: number;
    lineTotal: number;
    vatPercentage: number;
    vatAmount: number;
}

export interface PurchaseInvoice extends OptionalFieldValues {
    id: number;
    requestId: string | null;
    vendorId: number | null;
    vendorName: string | null;
    invoiceNumber: string | null;
    invoiceDate: string | null;
    paymentTermsDays: number | null;
    dueDate: string | null;
    currency: string | null;
    status: string;
    subTotal: number;
    vatAmount: number;
    totalAmount: number;
    createdAt: string;
    lines: PurchaseInvoiceLine[];
}

export function isPurchaseVendor(value: unknown): value is PurchaseVendor {
    return isObject(value) && isNumber(value.id) && isString(value.name) && isNullableString(value.vatNumber)
        && isNumber(value.paymentTermsDays) && isNullableString(value.defaultCurrency);
}

export function isPurchaseVendorList(value: unknown): value is PurchaseVendor[] {
    return isArrayOf(value, isPurchaseVendor);
}

export function isPurchaseInvoiceSettings(value: unknown): value is PurchaseInvoiceSettings {
    return isObject(value) && isArrayOf(value.allOptionalFields, isOptionalField) && isArrayOf(value.optionalFields, isOptionalField)
        && isNumber(value.defaultVatPercentage) && isNumber(value.defaultPaymentTermsDays) && isString(value.defaultCurrency);
}

function isLine(value: unknown): value is PurchaseInvoiceLine {
    return isObject(value) && isNumber(value.id) && isNullableNumber(value.productVariantId) && isNullableString(value.description)
        && isNumber(value.quantity) && (value.unit === null || (isString(value.unit) && (PACKAGE_UNITS as readonly string[]).includes(value.unit)))
        && isNumber(value.unitPrice) && isNumber(value.discountPercentage) && isNumber(value.lineTotal) && isNumber(value.vatPercentage) && isNumber(value.vatAmount);
}

export function isPurchaseInvoice(value: unknown): value is PurchaseInvoice {
    return isObject(value) && isNumber(value.id) && isNullableString(value.requestId) && isNullableNumber(value.vendorId)
        && isNullableString(value.vendorName) && isNullableString(value.invoiceNumber) && isNullableString(value.invoiceDate)
        && isNullableNumber(value.paymentTermsDays) && isNullableString(value.dueDate) && isNullableString(value.currency)
        && isString(value.status) && isNumber(value.subTotal) && isNumber(value.vatAmount) && isNumber(value.totalAmount)
        && isString(value.createdAt) && isArrayOf(value.lines, isLine);
}

export function isPurchaseInvoiceList(value: unknown): value is PurchaseInvoice[] {
    return isArrayOf(value, isPurchaseInvoice);
}
