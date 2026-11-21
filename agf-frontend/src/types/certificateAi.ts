import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';
import { isCertificateStatus, type CertificateStatus } from './certificate.ts';

// Certificaten deel 2: een certificaat door AI laten voorinvullen (api/certificates/ai; backend: AgfAdmin/DTOs/CertificateAiDtos.cs).
// De AI doet een voorstel; Goedkeuren slaat op via het gewone opslaan van de certificaten (aiApproval).
export type AiProductMatch = 'Exact' | 'Translated' | 'Partial' | 'None';
export interface AiCertificateProduct { text: string; page: number | null; productId: number | null; productName: string | null; match: AiProductMatch }
export interface AiCertificateSite { name: string; country: string | null }
export interface AiCertificateProposal {
    typeCode: string | null;
    customTypeName: string | null;
    documentTypeName: string | null;
    number: string | null;
    holderName: string | null;
    holderCountry: string | null;
    certificationBody: string | null;
    validFrom: string | null;
    validUntil: string | null;
    status: CertificateStatus;
    daysLeft: number | null;
    products: AiCertificateProduct[];
    sites: AiCertificateSite[];
    holderMismatch: string | null;
    sameNumberCertificateId: number | null;
    sameNumberTypeName: string | null;
}
export interface AiCertificateExtraction {
    id: number;
    status: string;
    relationId: number | null;
    fileName: string;
    mediaType: string;
    displayMediaType: string;
    sizeBytes: number;
    provider: string;
    model: string | null;
    modelName: string | null;
    modelVersion: string | null;
    usedFallback: boolean;
    costEur: number;
    proposal: AiCertificateProposal | null;
    problems: { field: string; code: string }[];
}

const isMatch = (value: unknown): value is AiProductMatch => value === 'Exact' || value === 'Translated' || value === 'Partial' || value === 'None';
const isProduct = (value: unknown): value is AiCertificateProduct => isObject(value) && isString(value.text) && isNullableNumber(value.page)
    && isNullableNumber(value.productId) && isNullableString(value.productName) && isMatch(value.match);
const isSite = (value: unknown): value is AiCertificateSite => isObject(value) && isString(value.name) && isNullableString(value.country);
const isProposal = (value: unknown): value is AiCertificateProposal => isObject(value) && isNullableString(value.typeCode) && isNullableString(value.customTypeName)
    && isNullableString(value.documentTypeName) && isNullableString(value.number) && isNullableString(value.holderName) && isNullableString(value.holderCountry)
    && isNullableString(value.certificationBody) && isNullableString(value.validFrom) && isNullableString(value.validUntil) && isCertificateStatus(value.status)
    && isNullableNumber(value.daysLeft) && isArrayOf(value.products, isProduct) && isArrayOf(value.sites, isSite) && isNullableString(value.holderMismatch)
    && isNullableNumber(value.sameNumberCertificateId) && isNullableString(value.sameNumberTypeName);

export function isAiCertificateExtraction(value: unknown): value is AiCertificateExtraction {
    return isObject(value) && isNumber(value.id) && isString(value.status) && isNullableNumber(value.relationId) && isString(value.fileName) && isString(value.mediaType)
        && isString(value.displayMediaType) && isNumber(value.sizeBytes) && isString(value.provider) && isNullableString(value.model) && isNullableString(value.modelName)
        && isNullableString(value.modelVersion) && typeof value.usedFallback === 'boolean' && isNumber(value.costEur) && (value.proposal === null || isProposal(value.proposal))
        && isArrayOf(value.problems, (p): p is { field: string; code: string } => isObject(p) && isString(p.field) && isString(p.code));
}
