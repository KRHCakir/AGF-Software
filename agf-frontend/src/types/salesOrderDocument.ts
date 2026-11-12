import { isArrayOf, isNullableString, isNumber, isObject, isString } from './guards';

// Bevestigen en documenten van een verkooporder (documenten deel 1: de laadbon). Backend: AgfAdmin/DTOs/SalesOrderDocumentDtos.cs.
export interface SalesOrderDocument {
    id: number;
    kind: string;
    number: string;
    version: number;
    reason: string;
    createdAt: string;
    createdBy: string;
    sentAt: string | null;
    sentBy: string | null;
    latest: boolean;
}

export interface SalesOrderDocuments {
    salesOrderId: number;
    confirmedAt: string | null;
    confirmedBy: string | null;
    loadAt: string | null;
    licensePlate: string | null;
    // Er is een nieuwere versie van de laadbon dan de eerste en die is nog niet doorgegeven of verstuurd.
    needsResend: boolean;
    documents: SalesOrderDocument[];
}

export interface LoadingDetails { loadAt: string | null; licensePlate: string | null }

export interface LoadingSlipSettings { language: 'en' | 'nl'; showCustomer: boolean; showSupplier: boolean }

const isDocument = (value: unknown): value is SalesOrderDocument => isObject(value) && isNumber(value.id) && isString(value.kind) && isString(value.number)
    && isNumber(value.version) && isString(value.createdAt) && isString(value.createdBy) && isNullableString(value.sentAt) && typeof value.latest === 'boolean';

export function isSalesOrderDocuments(value: unknown): value is SalesOrderDocuments {
    return isObject(value) && isNumber(value.salesOrderId) && isNullableString(value.confirmedAt) && isNullableString(value.licensePlate)
        && typeof value.needsResend === 'boolean' && isArrayOf(value.documents, isDocument);
}

export function isLoadingSlipSettings(value: unknown): value is LoadingSlipSettings {
    return isObject(value) && (value.language === 'en' || value.language === 'nl') && typeof value.showCustomer === 'boolean' && typeof value.showSupplier === 'boolean';
}
