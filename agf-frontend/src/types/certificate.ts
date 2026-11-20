import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Certificaten deel 1. Backend: AgfAdmin/DTOs/CertificateDtos.cs (api/certificates).
export type CertificateStatus = 'Valid' | 'ExpiringSoon' | 'Expired' | 'NoEndDate';
export const CERTIFICATE_STATUSES: readonly CertificateStatus[] = ['Valid', 'ExpiringSoon', 'Expired', 'NoEndDate'];
export const GGN_TYPE = 'GlobalGapIfa';
export const CUSTOM_TYPE = 'Custom';

export interface CertificateProduct { id: number; name: string | null }
export interface CertificateFile { id: number; version: number; fileName: string; mediaType: string; sizeBytes: number; sha256: string; createdAt: string; createdBy: string }
export interface CertificateWarning { code: string; field: string; message: string }

export interface Certificate {
    id: number;
    typeCode: string;
    customTypeName: string | null;
    typeName: string;
    number: string;
    certificationBody: string | null;
    validFrom: string | null;
    validUntil: string | null;
    products: CertificateProduct[];
    productsPending: boolean;
    producerName: string | null;
    hasGgnLabel: boolean;
    checkedOn: string | null;
    checkedBy: string | null;
    isActive: boolean;
    status: CertificateStatus;
    daysLeft: number | null;
    files: CertificateFile[];
}

export interface CertificateList { version: string; warningDays: number; certificates: Certificate[]; warnings: CertificateWarning[] }

export interface SaveCertificate {
    id: number | null;
    typeCode: string;
    customTypeName: string | null;
    number: string;
    certificationBody: string | null;
    validFrom: string | null;
    validUntil: string | null;
    productIds: number[];
    producerName: string | null;
    hasGgnLabel: boolean;
    checkedOn: string | null;
    isActive: boolean;
}
export interface SaveCertificates { expectedVersion: string; certificates: SaveCertificate[] }

export interface CertificateType { code: string; name: string; description: string; typicalHolder: string; productsRequired: boolean; ggnNumber: boolean }
export interface CertificateReference {
    types: CertificateType[];
    customTypes: string[];
    warningDays: number;
    globalGapPortalUrl: string;
    globalGapDatabaseUrl: string;
    maxFileBytes: number;
}
export interface CertificateSettings { warningDays: number; customTypes: string[] }

export interface CertificateOverviewItem {
    certificateId: number;
    relationId: number | null;
    relationName: string | null;
    isCustomer: boolean;
    isVendor: boolean;
    isCarrier: boolean;
    typeCode: string;
    typeName: string;
    number: string;
    validUntil: string | null;
    daysLeft: number | null;
    status: CertificateStatus;
    productsPending: boolean;
    isActive: boolean;
    productNames: string[];
}
export interface CertificateOverview { warningDays: number; total: number; items: CertificateOverviewItem[] }

export interface RelationCertificateSummary { typeCode: string; typeName: string; status: CertificateStatus; productsPending: boolean }

export const isCertificateStatus = (value: unknown): value is CertificateStatus => isString(value) && (CERTIFICATE_STATUSES as readonly string[]).includes(value);
const isProduct = (value: unknown): value is CertificateProduct => isObject(value) && isNumber(value.id) && isNullableString(value.name);
export const isCertificateFile = (value: unknown): value is CertificateFile => isObject(value) && isNumber(value.id) && isNumber(value.version) && isString(value.fileName)
    && isString(value.mediaType) && isNumber(value.sizeBytes) && isString(value.sha256) && isString(value.createdAt) && isString(value.createdBy);
const isWarning = (value: unknown): value is CertificateWarning => isObject(value) && isString(value.code) && isString(value.field) && isString(value.message);

const isCertificate = (value: unknown): value is Certificate => isObject(value) && isNumber(value.id) && isString(value.typeCode) && isNullableString(value.customTypeName)
    && isString(value.typeName) && isString(value.number) && isNullableString(value.certificationBody) && isNullableString(value.validFrom) && isNullableString(value.validUntil)
    && isArrayOf(value.products, isProduct) && typeof value.productsPending === 'boolean' && isNullableString(value.producerName) && typeof value.hasGgnLabel === 'boolean'
    && isNullableString(value.checkedOn) && isNullableString(value.checkedBy) && typeof value.isActive === 'boolean' && isCertificateStatus(value.status)
    && isNullableNumber(value.daysLeft) && isArrayOf(value.files, isCertificateFile);

export function isCertificateList(value: unknown): value is CertificateList {
    return isObject(value) && isString(value.version) && isNumber(value.warningDays) && isArrayOf(value.certificates, isCertificate) && isArrayOf(value.warnings, isWarning);
}

export function isCertificateReference(value: unknown): value is CertificateReference {
    return isObject(value) && isArrayOf(value.types, (t): t is CertificateType => isObject(t) && isString(t.code) && isString(t.name) && isString(t.description)
        && isString(t.typicalHolder) && typeof t.productsRequired === 'boolean' && typeof t.ggnNumber === 'boolean')
        && isArrayOf(value.customTypes, isString) && isNumber(value.warningDays) && isString(value.globalGapPortalUrl) && isString(value.globalGapDatabaseUrl) && isNumber(value.maxFileBytes);
}

export function isCertificateSettings(value: unknown): value is CertificateSettings {
    return isObject(value) && isNumber(value.warningDays) && isArrayOf(value.customTypes, isString);
}

const isOverviewItem = (value: unknown): value is CertificateOverviewItem => isObject(value) && isNumber(value.certificateId) && isNullableNumber(value.relationId)
    && isNullableString(value.relationName) && typeof value.isCustomer === 'boolean' && typeof value.isVendor === 'boolean' && typeof value.isCarrier === 'boolean'
    && isString(value.typeCode) && isString(value.typeName) && isString(value.number) && isNullableString(value.validUntil) && isNullableNumber(value.daysLeft)
    && isCertificateStatus(value.status) && typeof value.productsPending === 'boolean' && typeof value.isActive === 'boolean' && isArrayOf(value.productNames, isString);

export function isCertificateOverview(value: unknown): value is CertificateOverview {
    return isObject(value) && isNumber(value.warningDays) && isNumber(value.total) && isArrayOf(value.items, isOverviewItem);
}

export const isRelationCertificateSummary = (value: unknown): value is RelationCertificateSummary => isObject(value) && isString(value.typeCode)
    && isString(value.typeName) && isCertificateStatus(value.status) && typeof value.productsPending === 'boolean';
