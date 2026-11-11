import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

const isBoolean = (value: unknown): value is boolean => typeof value === 'boolean';

// AI-lezen van een inkoopfactuur (stap 7a), zoals de API het geeft. De AI doet een voorstel; de gebruiker keurt goed.

export interface AiModel { id: string; name: string; inputPerMillionUsd: number; outputPerMillionUsd: number; priceCheckedOn: string | null }

export interface AiReadingStatus {
    enabled: boolean;
    configured: boolean;
    notConfiguredReason: string | null;
    provider: string;
    primaryModel: AiModel;
    fallbackEnabled: boolean;
    fallbackModel: AiModel | null;
    monthSpentEur: number;
    monthlyLimitEur: number;
    limitReached: boolean;
    maxFileBytes: number;
}

export interface AiLineProposal {
    description: string | null;
    quantity: number | null;
    unitText: string | null;
    unit: string | null;
    unitPrice: number | null;
    amount: number | null;
    vatPercentage: number | null;
}

export interface AiInvoiceProposal {
    vendorName: string | null;
    vendorVatNumber: string | null;
    vendorRegistrationNumber: string | null;
    vendorIban: string | null;
    supplierOrderNumber: string | null;
    invoiceNumber: string | null;
    invoiceDate: string | null; // YYYY-MM-DD
    currency: string | null;
    lines: AiLineProposal[];
    totalNet: number | null;
    totalVat: number | null;
    totalGross: number | null;
}

export interface AiProblem { field: string; code: string }
export interface AiVendorCandidate { vendorId: number; name: string; matchedOn: string[] }
export interface AiOrderCandidate { id: number; orderNumber: string; supplierReference: string | null; vendorId: number; vendorName: string | null }

export interface AiMatches {
    vendorCandidates: AiVendorCandidate[];
    suggestedVendorId: number | null;
    orderCandidates: AiOrderCandidate[];
    suggestedOrderId: number | null;
    duplicateInvoice: { id: number; invoiceNumber: string } | null;
    lineCostArticles: { index: number; costArticleId: number; name: string }[];
}

export interface AiExtraction {
    id: number;
    status: string;
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
    proposal: AiInvoiceProposal | null;
    problems: AiProblem[];
    matches: AiMatches | null;
}

const isLine = (value: unknown): value is AiLineProposal => isObject(value) && isNullableString(value.description) && isNullableNumber(value.quantity)
    && isNullableNumber(value.unitPrice) && isNullableNumber(value.amount) && isNullableNumber(value.vatPercentage);

const isProposal = (value: unknown): value is AiInvoiceProposal => isObject(value) && isNullableString(value.vendorName) && isNullableString(value.invoiceNumber)
    && isNullableString(value.invoiceDate) && isNullableString(value.currency) && isArrayOf(value.lines, isLine)
    && isNullableNumber(value.totalNet) && isNullableNumber(value.totalVat) && isNullableNumber(value.totalGross);

const isMatches = (value: unknown): value is AiMatches => isObject(value)
    && isArrayOf(value.vendorCandidates, (c): c is AiVendorCandidate => isObject(c) && isNumber(c.vendorId) && isString(c.name) && isArrayOf(c.matchedOn, isString))
    && isNullableNumber(value.suggestedVendorId)
    && isArrayOf(value.orderCandidates, (o): o is AiOrderCandidate => isObject(o) && isNumber(o.id) && isString(o.orderNumber))
    && isNullableNumber(value.suggestedOrderId)
    && isArrayOf(value.lineCostArticles, (c): c is AiMatches['lineCostArticles'][number] => isObject(c) && isNumber(c.index) && isNumber(c.costArticleId) && isString(c.name));

export function isAiExtraction(value: unknown): value is AiExtraction {
    return isObject(value) && isNumber(value.id) && isString(value.status) && isString(value.fileName) && isString(value.displayMediaType)
        && isNullableString(value.modelName) && isBoolean(value.usedFallback) && isNumber(value.costEur)
        && (value.proposal === null || isProposal(value.proposal)) && isArrayOf(value.problems, (p): p is AiProblem => isObject(p) && isString(p.field) && isString(p.code))
        && (value.matches === null || value.matches === undefined || isMatches(value.matches));
}

export function isAiReadingStatus(value: unknown): value is AiReadingStatus {
    return isObject(value) && isBoolean(value.enabled) && isBoolean(value.configured) && isNullableString(value.notConfiguredReason) && isObject(value.primaryModel)
        && isString(value.primaryModel.name) && isNumber(value.monthSpentEur) && isNumber(value.monthlyLimitEur) && isBoolean(value.limitReached) && isNumber(value.maxFileBytes);
}
