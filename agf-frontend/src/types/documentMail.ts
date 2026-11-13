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
