import type { AddressPurpose, ChannelKind, ContactChannel, Relation, RelationAddress, RelationContact, SaveRelationRequest, VendorGgn } from '../types/relation';
import { resolveGgn } from './ggn.ts';

// Pure regels van de relatiewerkplek (node:test), spiegel van RelationService/RelationRules: alleen de bedrijfsnaam is verplicht,
// leeg = niet vastgelegd, zoekcode zonder spaties, land verplicht voor nieuwe/gewijzigde adressen, één standaard per adresdoel,
// één primair contact, GGN's van 13 cijfers. Formaatcontroles bewijzen nooit geldigheid (btw) of certificering (GGN).

export const MAX_RELATION_NAME_LENGTH = 200;
export const MAX_SEARCH_CODE_LENGTH = 32;
export const MAX_RELATION_NOTE_LENGTH = 1000;
const SEARCH_CODE = /^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$/;
const COUNTRY = /^[A-Za-z]{2}$/;

export interface AddressDraft {
    key: string;
    id: number | null;
    purpose: AddressPurpose;
    label: string;
    line1: string;
    line2: string;
    postalCode: string;
    city: string;
    region: string;
    countryCode: string;
    isDefault: boolean;
    isActive: boolean;
    // Het opgeslagen adres (voor de landregel: een onveranderd legacy-adres zonder land mag blijven staan).
    stored: RelationAddress | null;
}

export interface ChannelDraft { key: string; id: number | null; kind: ChannelKind; label: string; value: string }

export interface ContactDraft {
    key: string;
    id: number | null;
    name: string;
    department: string;
    jobTitle: string;
    isPrimary: boolean;
    isActive: boolean;
    note: string;
    channels: ChannelDraft[];
}

export interface GgnDraft { key: string; id: number | null; ggn: string; producerName: string; isActive: boolean }

export interface RelationDraft {
    searchCode: string;
    name: string;
    tradeName: string;
    preferredLanguage: string;
    website: string;
    email: string;
    phone: string;
    vatNumber: string;
    registrationNumber: string;
    registrationCountry: string;
    registrationType: string;
    legalForm: string;
    internalNote: string;
    isActive: boolean;
    customerRole: boolean;
    customerPaymentTermsDays: string;
    vendorRole: boolean;
    vendorPaymentTermsDays: string;
    vendorCurrency: string;
    ggns: GgnDraft[];
    addresses: AddressDraft[];
    contacts: ContactDraft[];
}

export type RelationRuleCode = 'required' | 'too_long' | 'invalid' | 'invalid_email' | 'vat_shape' | 'country_required' | 'address_empty' | 'two_defaults' | 'two_primary' | 'ggn_13_digits' | 'ggn_duplicate' | 'terms_range' | 'currency';
export interface RelationRuleError { field: string; code: RelationRuleCode }
export type RelationBuildResult = { ok: true; value: SaveRelationRequest } | { ok: false; errors: RelationRuleError[] };

let keyCounter = 0;
export const newKey = (): string => `k${++keyCounter}`;

export function emptyAddressDraft(purpose: AddressPurpose = 'Main'): AddressDraft {
    return { key: newKey(), id: null, purpose, label: '', line1: '', line2: '', postalCode: '', city: '', region: '', countryCode: '', isDefault: false, isActive: true, stored: null };
}

export function emptyChannelDraft(kind: ChannelKind = 'Phone'): ChannelDraft {
    return { key: newKey(), id: null, kind, label: '', value: '' };
}

export function emptyContactDraft(): ContactDraft {
    return { key: newKey(), id: null, name: '', department: '', jobTitle: '', isPrimary: false, isActive: true, note: '', channels: [emptyChannelDraft('Phone'), emptyChannelDraft('Email')] };
}

export function emptyGgnDraft(): GgnDraft {
    return { key: newKey(), id: null, ggn: '', producerName: '', isActive: true };
}

export function emptyRelationDraft(): RelationDraft {
    return {
        searchCode: '', name: '', tradeName: '', preferredLanguage: '', website: '', email: '', phone: '', vatNumber: '', registrationNumber: '', registrationCountry: '', registrationType: '', legalForm: '',
        internalNote: '', isActive: true, customerRole: false, customerPaymentTermsDays: '30', vendorRole: false, vendorPaymentTermsDays: '30', vendorCurrency: 'EUR', ggns: [], addresses: [], contacts: [],
    };
}

const text = (value: string | null) => value ?? '';

export function draftFromRelation(relation: Relation): RelationDraft {
    return {
        searchCode: text(relation.searchCode), name: relation.name, tradeName: text(relation.tradeName), preferredLanguage: text(relation.preferredLanguage), website: text(relation.website),
        email: text(relation.email), phone: text(relation.phone), vatNumber: text(relation.vatNumber), registrationNumber: text(relation.registrationNumber), registrationCountry: text(relation.registrationCountry),
        registrationType: text(relation.registrationType), legalForm: text(relation.legalForm), internalNote: text(relation.internalNote), isActive: relation.isActive,
        customerRole: relation.customer !== null, customerPaymentTermsDays: String(relation.customer?.paymentTermsDays ?? 30),
        vendorRole: relation.vendor !== null, vendorPaymentTermsDays: String(relation.vendor?.paymentTermsDays ?? 30), vendorCurrency: relation.vendor?.defaultCurrency ?? 'EUR',
        ggns: (relation.vendor?.ggns ?? []).map((g) => ({ key: newKey(), id: g.id, ggn: g.ggn, producerName: text(g.producerName), isActive: g.isActive })),
        addresses: relation.addresses.map((a) => ({ key: newKey(), id: a.id, purpose: a.purpose, label: text(a.label), line1: text(a.line1), line2: text(a.line2), postalCode: text(a.postalCode), city: text(a.city), region: text(a.region), countryCode: text(a.countryCode), isDefault: a.isDefault, isActive: a.isActive, stored: a })),
        contacts: relation.contacts.map((c) => ({ key: newKey(), id: c.id, name: c.name, department: text(c.department), jobTitle: text(c.jobTitle), isPrimary: c.isPrimary, isActive: c.isActive, note: text(c.note), channels: c.channels.map((ch) => ({ key: newKey(), id: ch.id, kind: ch.kind, label: text(ch.label), value: ch.value })) })),
    };
}

export function blankToNull(value: string): string | null {
    const trimmed = value.trim();
    return trimmed === '' ? null : trimmed;
}

export function looksLikeEmail(value: string): boolean {
    const at = value.indexOf('@');
    return at > 0 && at === value.lastIndexOf('@') && at < value.length - 1 && !/\s/.test(value);
}

// Btw-nummer: alleen spaties, punten en streepjes vallen weg; twee letters vooraan (XI voor Noord-Ierland), verder letters/cijfers.
export function normalizeVat(raw: string): string {
    return raw.trim().replace(/[ .-]/g, '').toUpperCase();
}

export function isVatShape(normalized: string): boolean {
    return /^[A-Z]{2}[A-Z0-9]{2,30}$/.test(normalized);
}

function optional(value: string, max: number, field: string, errors: RelationRuleError[]): string | null {
    const trimmed = blankToNull(value);
    if (trimmed !== null && trimmed.length > max) errors.push({ field, code: 'too_long' });
    return trimmed;
}

function terms(value: string, field: string, errors: RelationRuleError[]): number {
    const parsed = /^\d{1,3}$/.test(value.trim()) ? Number(value.trim()) : NaN;
    if (!Number.isInteger(parsed) || parsed < 0 || parsed > 365) { errors.push({ field, code: 'terms_range' }); return 0; }
    return parsed;
}

function addressUnchanged(draft: AddressDraft): boolean {
    const s = draft.stored;
    return s !== null && s.countryCode === null && text(s.line1) === draft.line1.trim() && text(s.line2) === draft.line2.trim() && text(s.postalCode) === draft.postalCode.trim() && text(s.city) === draft.city.trim() && text(s.region) === draft.region.trim();
}

export function buildRelationPayload(draft: RelationDraft, options: { requestId?: string; expectedVersion?: number; acknowledgeSimilarNames?: boolean } = {}): RelationBuildResult {
    const errors: RelationRuleError[] = [];
    const name = draft.name.trim();
    if (name === '') errors.push({ field: 'name', code: 'required' });
    else if (name.length > MAX_RELATION_NAME_LENGTH) errors.push({ field: 'name', code: 'too_long' });
    const searchCode = blankToNull(draft.searchCode);
    if (searchCode !== null && !SEARCH_CODE.test(searchCode)) errors.push({ field: 'searchCode', code: 'invalid' });
    const tradeName = optional(draft.tradeName, MAX_RELATION_NAME_LENGTH, 'tradeName', errors);
    const website = optional(draft.website, 200, 'website', errors);
    const email = optional(draft.email, 200, 'email', errors);
    if (email !== null && !looksLikeEmail(email)) errors.push({ field: 'email', code: 'invalid_email' });
    const phone = optional(draft.phone, 40, 'phone', errors);
    const vatRaw = blankToNull(draft.vatNumber);
    const vatNumber = vatRaw === null ? null : normalizeVat(vatRaw);
    if (vatNumber !== null && !isVatShape(vatNumber)) errors.push({ field: 'vatNumber', code: 'vat_shape' });
    const registrationNumber = optional(draft.registrationNumber, 64, 'registrationNumber', errors);
    const registrationCountry = blankToNull(draft.registrationCountry)?.toUpperCase() ?? null;
    if (registrationCountry !== null && !COUNTRY.test(registrationCountry)) errors.push({ field: 'registrationCountry', code: 'invalid' });
    if (registrationNumber !== null && registrationCountry === null) errors.push({ field: 'registrationCountry', code: 'required' });
    const registrationType = optional(draft.registrationType, 32, 'registrationType', errors);
    const legalForm = optional(draft.legalForm, 64, 'legalForm', errors);
    const internalNote = optional(draft.internalNote, MAX_RELATION_NOTE_LENGTH, 'internalNote', errors);

    const customer = draft.customerRole ? { paymentTermsDays: terms(draft.customerPaymentTermsDays, 'customerPaymentTermsDays', errors) } : null;
    let vendor: SaveRelationRequest['vendor'] = null;
    if (draft.vendorRole) {
        const currency = draft.vendorCurrency.trim().toUpperCase();
        if (!/^[A-Z]{3}$/.test(currency)) errors.push({ field: 'vendorCurrency', code: 'currency' });
        const seen = new Set<string>();
        const ggns: VendorGgn[] = [];
        draft.ggns.forEach((g, index) => {
            const resolved = resolveGgn(g.ggn);
            if (!resolved.ok || resolved.value === null) { errors.push({ field: `ggns.${g.key}.ggn`, code: 'ggn_13_digits' }); return; }
            if (seen.has(resolved.value)) { errors.push({ field: `ggns.${g.key}.ggn`, code: 'ggn_duplicate' }); return; }
            seen.add(resolved.value);
            ggns.push({ id: g.id, ggn: resolved.value, producerName: optional(g.producerName, 200, `ggns.${g.key}.producerName`, errors), isActive: g.isActive });
            void index;
        });
        vendor = { paymentTermsDays: terms(draft.vendorPaymentTermsDays, 'vendorPaymentTermsDays', errors), defaultCurrency: currency, ggns };
    }

    const addresses: RelationAddress[] = draft.addresses.map((a) => {
        const line1 = optional(a.line1, 300, `addresses.${a.key}.line1`, errors);
        const line2 = optional(a.line2, 200, `addresses.${a.key}.line2`, errors);
        const postalCode = optional(a.postalCode, 20, `addresses.${a.key}.postalCode`, errors);
        const city = optional(a.city, 100, `addresses.${a.key}.city`, errors);
        const region = optional(a.region, 100, `addresses.${a.key}.region`, errors);
        const countryCode = blankToNull(a.countryCode)?.toUpperCase() ?? null;
        if (line1 === null && postalCode === null && city === null) errors.push({ field: `addresses.${a.key}.line1`, code: 'address_empty' });
        if (countryCode !== null && !COUNTRY.test(countryCode)) errors.push({ field: `addresses.${a.key}.countryCode`, code: 'invalid' });
        else if (countryCode === null && !addressUnchanged(a)) errors.push({ field: `addresses.${a.key}.countryCode`, code: 'country_required' });
        return { id: a.id, purpose: a.purpose, label: optional(a.label, 100, `addresses.${a.key}.label`, errors), line1, line2, postalCode, city, region, countryCode, isDefault: a.isDefault, isActive: a.isActive };
    });
    for (const purpose of ['Main', 'Billing', 'Delivery'] as const) {
        const group = draft.addresses.filter((a) => a.purpose === purpose && a.isActive);
        if (group.filter((a) => a.isDefault).length > 1) errors.push({ field: 'addresses', code: 'two_defaults' });
    }

    const contacts: RelationContact[] = draft.contacts.map((c) => {
        const contactName = c.name.trim();
        if (contactName === '') errors.push({ field: `contacts.${c.key}.name`, code: 'required' });
        else if (contactName.length > 100) errors.push({ field: `contacts.${c.key}.name`, code: 'too_long' });
        const channels: ContactChannel[] = [];
        for (const ch of c.channels) {
            const value = blankToNull(ch.value);
            if (value === null) continue; // lege regel = niet vastgelegd
            if (ch.kind === 'Email' && !looksLikeEmail(value)) errors.push({ field: `contacts.${c.key}.channels.${ch.key}`, code: 'invalid_email' });
            if (value.length > (ch.kind === 'Phone' ? 40 : 200)) errors.push({ field: `contacts.${c.key}.channels.${ch.key}`, code: 'too_long' });
            channels.push({ id: ch.id, kind: ch.kind, label: optional(ch.label, 50, `contacts.${c.key}.channels.${ch.key}.label`, errors), value });
        }
        return { id: c.id, name: contactName, department: optional(c.department, 50, `contacts.${c.key}.department`, errors), jobTitle: optional(c.jobTitle, 100, `contacts.${c.key}.jobTitle`, errors), isPrimary: c.isPrimary, isActive: c.isActive, note: optional(c.note, 500, `contacts.${c.key}.note`, errors), channels };
    });
    if (draft.contacts.filter((c) => c.isPrimary && c.isActive).length > 1) errors.push({ field: 'contacts', code: 'two_primary' });

    if (errors.length > 0) return { ok: false, errors };
    return {
        ok: true,
        value: {
            ...(options.requestId ? { requestId: options.requestId } : {}),
            ...(options.expectedVersion !== undefined ? { expectedVersion: options.expectedVersion } : {}),
            searchCode, name, tradeName, preferredLanguage: blankToNull(draft.preferredLanguage), website, email, phone, vatNumber, registrationNumber, registrationCountry, registrationType, legalForm, internalNote,
            isActive: draft.isActive, acknowledgeSimilarNames: options.acknowledgeSimilarNames ?? false, customer, vendor, addresses, contacts,
        },
    };
}

export function groupRelationErrors(errors: RelationRuleError[]): Record<string, RelationRuleCode[]> {
    const grouped: Record<string, RelationRuleCode[]> = {};
    for (const error of errors) (grouped[error.field] ??= []).push(error.code);
    return grouped;
}

// Rol en samenvatting voor de compacte lijst.
export type RelationRole = 'customer' | 'vendor' | 'both' | 'none';
export function relationRole(item: { customerId: number | null; vendorId: number | null }): RelationRole {
    if (item.customerId !== null && item.vendorId !== null) return 'both';
    if (item.customerId !== null) return 'customer';
    if (item.vendorId !== null) return 'vendor';
    return 'none';
}
