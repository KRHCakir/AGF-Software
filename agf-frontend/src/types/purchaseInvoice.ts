import { PACKAGE_UNITS, PackageUnit } from './availability';
import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';
import { PaymentConditionBasis, isPaymentConditionBasis } from '../lib/paymentCondition';

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
    // Zoekcode en actief-vlag van de relatie: zoeken op code; inactieve leveranciers niet kiesbaar voor nieuwe facturen.
    searchCode: string | null;
    isActive: boolean;
    // Betalingsconditie van de leveranciersrol (basis bepaalt de voorgestelde vervaldatum) en de status van de standaardrekening.
    paymentConditionId: number | null;
    paymentConditionCode: string | null;
    paymentConditionBasis: PaymentConditionBasis;
    hasDefaultBankAccount: boolean;
    defaultBankAccountUnconfirmed: boolean;
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
    // Kostenartikel (type Dienst) in plaats van een artikel; alleen meegestuurd als het gekozen is.
    costArticleId?: number;
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
    // Interne opmerking bij dit document: alleen intern zichtbaar, telt mee in de replayvergelijking, nooit overgenomen.
    internalNote: string | null;
    lines: CreatePurchaseInvoiceLineRequest[];
    // Goedkeuren van een AI-voorstel: de lezing en de door de gebruiker bevestigde inkooporder (alleen bij AI).
    aiExtractionId?: number;
    linkPurchaseOrderId?: number | null;
}

export interface PurchaseInvoiceLine {
    id: number;
    productVariantId: number | null;
    // Ontbreekt in oudere antwoorden.
    costArticleId?: number | null;
    costArticleName?: string | null;
    description: string | null;
    quantity: number;
    unit: PackageUnit | null;
    unitPrice: number;
    discountPercentage: number;
    lineTotal: number;
    vatPercentage: number;
    vatAmount: number;
}

// Op een factuur uit een AI-voorstel: wie het goedkeurde, met welk model (ontbreekt in oudere antwoorden en bij handmatige invoer).
export interface PurchaseInvoiceAiReading {
    extractionId: number;
    model: string | null;
    modelName: string | null;
    modelVersion: string | null;
    usedFallback: boolean;
    approvedBy: string;
    approvedAt: string;
    adjustedFields: number;
}

export interface PurchaseInvoiceAttachment { id: number; fileName: string; mediaType: string; sizeBytes: number; sha256: string; createdAt: string }

export interface PurchaseInvoice extends OptionalFieldValues {
    aiReading?: PurchaseInvoiceAiReading | null;
    attachments?: PurchaseInvoiceAttachment[];
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
    internalNote: string | null;
    lines: PurchaseInvoiceLine[];
}

export function isPurchaseVendor(value: unknown): value is PurchaseVendor {
    return isObject(value) && isNumber(value.id) && isString(value.name) && isNullableString(value.vatNumber)
        && isNumber(value.paymentTermsDays) && isNullableString(value.defaultCurrency) && isNullableString(value.searchCode) && typeof value.isActive === 'boolean'
        && isNullableNumber(value.paymentConditionId) && isNullableString(value.paymentConditionCode) && isPaymentConditionBasis(value.paymentConditionBasis)
        && typeof value.hasDefaultBankAccount === 'boolean' && typeof value.defaultBankAccountUnconfirmed === 'boolean';
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
        && isString(value.createdAt) && isNullableString(value.internalNote) && isArrayOf(value.lines, isLine);
}

export function isPurchaseInvoiceList(value: unknown): value is PurchaseInvoice[] {
    return isArrayOf(value, isPurchaseInvoice);
}
