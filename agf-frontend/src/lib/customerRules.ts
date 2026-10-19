import type { CreateCustomerRequest, CustomerOption } from '../types/referenceData';

// Pure regels voor "Nieuwe klant" vanuit Verkoop. Lengtes zijn gelijk aan de backend (CustomerService).
export const MAX_CUSTOMER_NAME_LENGTH = 200;
export const MAX_CONTACT_PERSON_LENGTH = 100;
export const MAX_EMAIL_LENGTH = 200;
export const MAX_PHONE_LENGTH = 40;
export const MAX_SHIPPING_ADDRESS_LENGTH = 300;

export interface CustomerDraft {
    name: string;
    contactPerson: string;
    email: string;
    phone: string;
    shippingAddress: string;
}

export const emptyCustomerDraft = (): CustomerDraft => ({ name: '', contactPerson: '', email: '', phone: '', shippingAddress: '' });

export type CustomerRuleCode = 'required' | 'too_long' | 'invalid_email';

export interface CustomerRuleError {
    field: keyof CustomerDraft;
    code: CustomerRuleCode;
}

// Leeg of alleen spaties wordt null; anders getrimd. Zelfde normalisatie als de server, zodat een herhaling identiek is.
export function blankToNull(value: string): string | null {
    const trimmed = value.trim();
    return trimmed === '' ? null : trimmed;
}

export function looksLikeEmail(value: string): boolean {
    const at = value.indexOf('@');
    return at > 0 && at === value.lastIndexOf('@') && at < value.length - 1 && !/\s/.test(value);
}

export function validateCustomerDraft(draft: CustomerDraft): CustomerRuleError[] {
    const errors: CustomerRuleError[] = [];
    const name = draft.name.trim();
    if (name === '') errors.push({ field: 'name', code: 'required' });
    else if (name.length > MAX_CUSTOMER_NAME_LENGTH) errors.push({ field: 'name', code: 'too_long' });
    const optional: [keyof CustomerDraft, number][] = [['contactPerson', MAX_CONTACT_PERSON_LENGTH], ['email', MAX_EMAIL_LENGTH], ['phone', MAX_PHONE_LENGTH], ['shippingAddress', MAX_SHIPPING_ADDRESS_LENGTH]];
    for (const [field, max] of optional) {
        const value = blankToNull(draft[field]);
        if (value !== null && value.length > max) errors.push({ field, code: 'too_long' });
    }
    const email = blankToNull(draft.email);
    if (email !== null && email.length <= MAX_EMAIL_LENGTH && !looksLikeEmail(email)) errors.push({ field: 'email', code: 'invalid_email' });
    return errors;
}

export function buildCustomerPayload(draft: CustomerDraft, requestId: string, acknowledgeSimilarNames: boolean): { ok: true; value: CreateCustomerRequest } | { ok: false; errors: CustomerRuleError[] } {
    const errors = validateCustomerDraft(draft);
    if (errors.length > 0) return { ok: false, errors };
    return {
        ok: true,
        value: {
            requestId,
            name: draft.name.trim(),
            contactPerson: blankToNull(draft.contactPerson),
            email: blankToNull(draft.email),
            phone: blankToNull(draft.phone),
            shippingAddress: blankToNull(draft.shippingAddress),
            acknowledgeSimilarNames,
        },
    };
}

// Kleine letters, zonder accenten en leestekens, één spatie tussen woorden (gelijk aan de server).
export function normalizeCustomerName(value: string): string {
    return value
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '')
        .toLowerCase()
        .replace(/[^\p{L}\p{N}]+/gu, ' ')
        .trim();
}

// Gelijk, of de één bevat de ander (vanaf 3 tekens). Alleen een waarschuwing: namen zijn niet uniek.
export function isSimilarCustomerName(a: string, b: string): boolean {
    const x = normalizeCustomerName(a);
    const y = normalizeCustomerName(b);
    if (x === '' || y === '') return false;
    if (x === y) return true;
    const [shorter, longer] = x.length <= y.length ? [x, y] : [y, x];
    return shorter.length >= 3 && longer.includes(shorter);
}

export function similarCustomers(name: string, options: readonly CustomerOption[]): CustomerOption[] {
    return options.filter((option) => isSimilarCustomerName(name, option.name));
}
