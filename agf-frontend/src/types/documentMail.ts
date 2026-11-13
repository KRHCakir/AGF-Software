import { isArrayOf, isNullableNumber, isNullableString, isNumber, isObject, isString } from './guards';

// Documentontvangers en mailconcepten (documenten deel 3). Backend: AgfAdmin/DTOs/DocumentRecipientDtos.cs en MailDraftDtos.cs.
export const DOCUMENT_KINDS = ['OrderConfirmation', 'LoadingSlip', 'TransportOrder', 'Invoice', 'Other'] as const;
export type DocumentKind = (typeof DOCUMENT_KINDS)[number];
export type RecipientRole = 'To' | 'Cc';

export interface DocumentRecipient { id: number | null; name: string | null; email: string; role: RecipientRole; kinds: DocumentKind[]; relationAddressId: number | null }
export interface RecipientAddressOption { id: number; purpose: string; text: string | null }
export interface RecipientWarning { code: string; field: string; message: string; suggestion: string | null }
export interface DocumentRecipients { version: string; recipients: DocumentRecipient[]; addresses: RecipientAddressOption[]; warnings: RecipientWarning[] }
export interface SaveDocumentRecipients { expectedVersion: string; recipients: DocumentRecipient[] }

const isKind = (value: unknown): value is DocumentKind => typeof value === 'string' && (DOCUMENT_KINDS as readonly string[]).includes(value);
const isRecipient = (value: unknown): value is DocumentRecipient => isObject(value) && isNullableNumber(value.id) && isNullableString(value.name) && isString(value.email)
    && (value.role === 'To' || value.role === 'Cc') && isArrayOf(value.kinds, isKind) && isNullableNumber(value.relationAddressId);
const isAddress = (value: unknown): value is RecipientAddressOption => isObject(value) && isNumber(value.id) && isString(value.purpose) && isNullableString(value.text);

export function isDocumentRecipients(value: unknown): value is DocumentRecipients {
    return isObject(value) && isString(value.version) && isArrayOf(value.recipients, isRecipient) && isArrayOf(value.addresses, isAddress) && Array.isArray(value.warnings);
}

// De afzender (Beheer › Mail): het wachtwoord gaat er alleen in (password), terug komt alleen hasPassword. configured = versturen kan.
export interface MailSettings {
    host: string | null; port: number; security: 'None' | 'StartTls' | 'SslOnConnect'; userName: string | null; password: string | null; clearPassword: boolean;
    hasPassword: boolean; fromAddress: string | null; fromName: string | null; signature: string | null; sender: string; configured: boolean;
}
export interface MailTemplate { kind: string; subject: string; body: string; isDefault: boolean }
export interface MailTemplates { templates: MailTemplate[]; placeholders: string[] }

export function isMailSettings(value: unknown): value is MailSettings {
    return isObject(value) && isNullableString(value.host) && isNumber(value.port) && isString(value.security) && isNullableString(value.userName)
        && typeof value.hasPassword === 'boolean' && isNullableString(value.fromAddress) && isNullableString(value.fromName) && isNullableString(value.signature)
        && isString(value.sender) && typeof value.configured === 'boolean';
}

export function isMailTemplates(value: unknown): value is MailTemplates {
    return isObject(value) && isArrayOf(value.templates, (t): t is MailTemplate => isObject(t) && isString(t.kind) && isString(t.subject) && isString(t.body) && typeof t.isDefault === 'boolean')
        && isArrayOf(value.placeholders, isString);
}

// Een mailconcept bij een documentversie. to/cc: adressen gescheiden door komma's. missingRecipient: geen adres voor deze soort (dan niet
// versturen). previousSentAt: een eerdere versie is al verstuurd of doorgegeven.
export type MailDraftStatus = 'Draft' | 'Sending' | 'Sent' | 'Failed' | 'Discarded' | 'Superseded';
export interface MailDraft {
    id: number; salesOrderId: number; orderNumber: string; customerName: string | null; documentId: number; documentKind: string; documentNumber: string;
    documentVersion: number; status: MailDraftStatus; to: string | null; cc: string | null; subject: string; body: string; missingRecipient: string | null;
    missingRelationId: number | null; previousSentAt: string | null; createdAt: string; sentAt: string | null; sentBy: string | null; messageId: string | null;
    lastError: string | null; attempts: number; version: number;
}
export interface MailStatus { configured: boolean; sender: string }

export function isMailDraft(value: unknown): value is MailDraft {
    return isObject(value) && isNumber(value.id) && isNumber(value.salesOrderId) && isString(value.orderNumber) && isNumber(value.documentId) && isString(value.documentKind)
        && isString(value.documentNumber) && isNumber(value.documentVersion) && isString(value.status) && isNullableString(value.to) && isNullableString(value.cc)
        && isString(value.subject) && isString(value.body) && isNullableString(value.missingRecipient) && isNullableNumber(value.missingRelationId)
        && isNullableString(value.previousSentAt) && isNullableString(value.sentAt) && isNullableString(value.lastError) && isNumber(value.version);
}

export const isMailStatus = (value: unknown): value is MailStatus => isObject(value) && typeof value.configured === 'boolean' && isString(value.sender);
