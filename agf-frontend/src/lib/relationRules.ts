import type { AddressPurpose, ChannelKind, ContactChannel, CustomerRole, Relation, RelationAddress, RelationBankAccount, RelationContact, RoleTerms, SaveBankAccount, SaveRelationRequest, SaveRoleTerms, VendorGgn } from '../types/relation';
import { todayInAmsterdam } from './calendarDate.ts';
import { formatDecimalInput } from './decimal.ts';
import { emailTypoSuggestion, isStrictEmail } from './emailRules.ts';
import { resolveGgn } from './ggn.ts';
import { MAX_ACCOUNT_HOLDER_LENGTH, MAX_BANK_ACCOUNTS, MAX_OTHER_ACCOUNT_NUMBER_LENGTH, checkIban, isBic, normalizeBic, normalizeIban } from './iban.ts';
import { CreditDraft, RoleTermsDraft, TermsRuleCode, emptyCreditDraft, emptyTermsDraft, normalizeCredit, normalizeTerms } from './roleTermsRules.ts';
import { VAT_EXAMPLES, checkVat, greekVatSuggestion, isVatShape } from './vatRules.ts';

// Pure regels van de relatiewerkplek (node:test), spiegel van RelationService/RelationRules: alleen de bedrijfsnaam is verplicht,
// leeg = niet vastgelegd, zoekcode zonder spaties, land verplicht voor nieuwe/gewijzigde adressen, één standaard per adresdoel,
// één primair contact, GGN's van 13 cijfers. Formaatcontroles bewijzen nooit geldigheid (btw) of certificering (GGN).
// Strikte e-mail en btw-vorm per land gelden alleen voor nieuwe of gewijzigde waarden (een ongewijzigd opgeslagen adres of
// nummer blijft opslaanbaar). Bankrekeningen worden nooit verwijderd; wie ze toevoegt of wijzigt, vult "Wie" in.

export { isVatShape };

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

export interface GgnDraft {
    key: string;
    id: number | null;
    ggn: string;
    producerName: string;
    isActive: boolean;
    checkedOn: string;
    checkedBy: string;
    certificateValidUntil: string;
    hasGgnLabel: boolean;
}

export interface BankAccountDraft {
    key: string;
    id: number | null;
    // IBAN of (bij een land zonder IBAN) een ander rekeningnummer; precies één van beide.
    useOther: boolean;
    iban: string;
    otherAccountNumber: string;
    bic: string;
    accountHolder: string;
    currency: string;
    isDefault: boolean;
    isActive: boolean;
    // De opgeslagen rekening (status, bevestiging en de vergelijking voor "Wie").
    stored: RelationBankAccount | null;
}

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
    // Valuta zit in de rolafspraken (klant: currency, leverancier: defaultCurrency).
    customerTerms: RoleTermsDraft;
    credit: CreditDraft;
    vendorRole: boolean;
    vendorTerms: RoleTermsDraft;
    ggns: GgnDraft[];
    addresses: AddressDraft[];
    contacts: ContactDraft[];
    bankAccounts: BankAccountDraft[];
    actorReference: string;
}

export type RelationRuleCode = TermsRuleCode | 'invalid_email' | 'vat_shape' | 'vat_country' | 'vat_greece' | 'country_required' | 'address_empty' | 'two_defaults' | 'two_primary'
    | 'ggn_13_digits' | 'ggn_duplicate' | 'ggn_check_future' | 'ggn_check_pair' | 'iban_shape' | 'iban_country' | 'iban_length' | 'iban_checksum' | 'bic' | 'account_one_of'
    | 'account_duplicate' | 'two_default_accounts' | 'inactive_default' | 'too_many_accounts' | 'actor_required';
export interface RelationRuleError { field: string; code: RelationRuleCode; params?: Record<string, string> }
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
    return { key: newKey(), id: null, ggn: '', producerName: '', isActive: true, checkedOn: '', checkedBy: '', certificateValidUntil: '', hasGgnLabel: false };
}

export function emptyBankAccountDraft(): BankAccountDraft {
    return { key: newKey(), id: null, useOther: false, iban: '', otherAccountNumber: '', bic: '', accountHolder: '', currency: 'EUR', isDefault: false, isActive: true, stored: null };
}

export function emptyRelationDraft(): RelationDraft {
    return {
        searchCode: '', name: '', tradeName: '', preferredLanguage: '', website: '', email: '', phone: '', vatNumber: '', registrationNumber: '', registrationCountry: '', registrationType: '', legalForm: '',
        internalNote: '', isActive: true, customerRole: false, customerTerms: emptyTermsDraft(), credit: emptyCreditDraft(), vendorRole: false, vendorTerms: emptyTermsDraft(),
        ggns: [], addresses: [], contacts: [], bankAccounts: [], actorReference: '',
    };
}

const text = (value: string | null) => value ?? '';
// Bedragen en percentages als invoertekst in de schermtaal (4,25 en niet 4.25, dat in het Nederlands 425 zou betekenen).
const amountText = (value: number | null) => (value === null ? '' : formatDecimalInput(value, 2));

// Rolafspraken van de server als invoer (ook na "rol toevoegen": de standaarden van de server komen in het concept).
export function termsDraftFromRole(role: RoleTerms, currency: string | null): RoleTermsDraft {
    return {
        // Een bestaande rol toont haar huidige termijn (ook als die gelijk is aan de standaard); korting alleen als de conditie die heeft.
        paymentTermsDays: String(role.paymentTermsDays), paymentTermsEndOfMonth: role.paymentTermsEndOfMonth, discountPercent: amountText(role.discountPercent),
        discountDays: role.discountDays === null ? '' : String(role.discountDays), paymentMethod: role.paymentMethod ?? 'BankTransfer',
        mandateReference: text(role.mandateReference), mandateSignedOn: text(role.mandateSignedOn), currency: currency ?? 'EUR', deliveryTermKind: text(role.deliveryTermKind),
        incoterm: text(role.incoterm), incotermPlace: text(role.incotermPlace), deliveryTermText: text(role.deliveryTermText), tradeConditions: text(role.tradeConditions),
        tradeConditionsText: text(role.tradeConditionsText),
    };
}

export function creditDraftFromRole(role: CustomerRole): CreditDraft {
    return {
        creditLimit: amountText(role.creditLimit), isInsured: role.isInsured, insurer: text(role.insurer), insurerText: text(role.insurerText), insuredLimit: amountText(role.insuredLimit),
        insurerReference: text(role.insurerReference), insuredUntil: text(role.insuredUntil),
    };
}

export function bankAccountDraftFrom(account: RelationBankAccount): BankAccountDraft {
    return {
        key: newKey(), id: account.id, useOther: account.iban === null && account.otherAccountNumber !== null, iban: text(account.iban), otherAccountNumber: text(account.otherAccountNumber),
        bic: text(account.bic), accountHolder: account.accountHolder, currency: account.currency, isDefault: account.isDefault, isActive: account.isActive, stored: account,
    };
}

export function draftFromRelation(relation: Relation): RelationDraft {
    return {
        searchCode: text(relation.searchCode), name: relation.name, tradeName: text(relation.tradeName), preferredLanguage: text(relation.preferredLanguage), website: text(relation.website),
        email: text(relation.email), phone: text(relation.phone), vatNumber: text(relation.vatNumber), registrationNumber: text(relation.registrationNumber), registrationCountry: text(relation.registrationCountry),
        registrationType: text(relation.registrationType), legalForm: text(relation.legalForm), internalNote: text(relation.internalNote), isActive: relation.isActive,
        customerRole: relation.customer !== null, customerTerms: relation.customer ? termsDraftFromRole(relation.customer, relation.customer.currency) : emptyTermsDraft(),
        credit: relation.customer ? creditDraftFromRole(relation.customer) : emptyCreditDraft(),
        vendorRole: relation.vendor !== null, vendorTerms: relation.vendor ? termsDraftFromRole(relation.vendor, relation.vendor.defaultCurrency) : emptyTermsDraft(),
        ggns: (relation.vendor?.ggns ?? []).map((g) => ({
            key: newKey(), id: g.id, ggn: g.ggn, producerName: text(g.producerName), isActive: g.isActive, checkedOn: text(g.checkedOn), checkedBy: text(g.checkedBy),
            certificateValidUntil: text(g.certificateValidUntil), hasGgnLabel: g.hasGgnLabel,
        })),
        addresses: relation.addresses.map((a) => ({ key: newKey(), id: a.id, purpose: a.purpose, label: text(a.label), line1: text(a.line1), line2: text(a.line2), postalCode: text(a.postalCode), city: text(a.city), region: text(a.region), countryCode: text(a.countryCode), isDefault: a.isDefault, isActive: a.isActive, stored: a })),
        contacts: relation.contacts.map((c) => ({ key: newKey(), id: c.id, name: c.name, department: text(c.department), jobTitle: text(c.jobTitle), isPrimary: c.isPrimary, isActive: c.isActive, note: text(c.note), channels: c.channels.map((ch) => ({ key: newKey(), id: ch.id, kind: ch.kind, label: text(ch.label), value: ch.value })) })),
        bankAccounts: relation.bankAccounts.map(bankAccountDraftFrom),
        // "Wie" geldt per opslag en wordt nooit uit een vorige opslag overgenomen.
        actorReference: '',
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

// Btw per land alleen voor een nieuw of gewijzigd nummer (VatRules); GR krijgt de suggestie EL.
function vatErrors(normalized: string, errors: RelationRuleError[]) {
    const problem = checkVat(normalized);
    if (problem === 'shape') errors.push({ field: 'vatNumber', code: 'vat_shape' });
    else if (problem === 'country_pattern') errors.push({ field: 'vatNumber', code: 'vat_country', params: { country: normalized.slice(0, 2), example: VAT_EXAMPLES[normalized.slice(0, 2)] } });
    else if (problem === 'greece_uses_el') errors.push({ field: 'vatNumber', code: 'vat_greece', params: { suggestion: greekVatSuggestion(normalized) ?? 'EL123456789' } });
}

// Nieuw of gewijzigd e-mailadres: strikte vorm; een ongewijzigd opgeslagen adres alleen de lichte controle.
function emailOk(value: string, stored: string | null | undefined): boolean {
    return value === stored ? looksLikeEmail(value) : isStrictEmail(value);
}

// Tikfoutsuggestie voor een nieuw of gewijzigd adres (geen fout; de gebruiker kan haar met één klik overnemen).
export function emailSuggestion(value: string, stored: string | null | undefined): string | null {
    const trimmed = value.trim();
    return trimmed === '' || trimmed === stored ? null : emailTypoSuggestion(trimmed);
}

// "Wie" is verplicht als een rekening nieuw is, haar IBAN, houder of ander nummer wijzigt of zij (in)actief wordt.
// BIC, valuta, standaard en volgorde vragen geen naam (zelfde regel als de server, die er dan ook geen auditregel voor schrijft).
export function bankAccountsNeedActor(accounts: readonly BankAccountDraft[]): boolean {
    return accounts.some((a) => {
        const s = a.stored;
        if (s === null || a.id === null) return true;
        const iban = a.useOther ? null : normalizeIban(a.iban);
        const other = a.useOther ? blankToNull(a.otherAccountNumber) : null;
        return iban !== s.iban || other !== s.otherAccountNumber || a.accountHolder.trim() !== s.accountHolder || a.isActive !== s.isActive;
    });
}

function saveTerms(values: ReturnType<typeof normalizeTerms>): SaveRoleTerms {
    return {
        paymentTermsDays: values.paymentTermsDays, paymentTermsEndOfMonth: values.paymentTermsEndOfMonth, discountPercent: values.discountPercent, discountDays: values.discountDays,
        paymentMethod: values.paymentMethod, mandateReference: values.mandateReference, mandateSignedOn: values.mandateSignedOn,
        deliveryTermKind: values.deliveryTermKind, incoterm: values.incoterm, incotermPlace: values.incotermPlace, deliveryTermText: values.deliveryTermText,
        tradeConditions: values.tradeConditions, tradeConditionsText: values.tradeConditionsText,
    };
}

function currencyOf(value: string, field: string, errors: RelationRuleError[]): string {
    const currency = value.trim().toUpperCase();
    if (!/^[A-Z]{3}$/.test(currency)) errors.push({ field, code: 'currency' });
    return currency;
}

function optional(value: string, max: number, field: string, errors: RelationRuleError[]): string | null {
    const trimmed = blankToNull(value);
    if (trimmed !== null && trimmed.length > max) errors.push({ field, code: 'too_long' });
    return trimmed;
}

function addressUnchanged(draft: AddressDraft): boolean {
    const s = draft.stored;
    return s !== null && s.countryCode === null && text(s.line1) === draft.line1.trim() && text(s.line2) === draft.line2.trim() && text(s.postalCode) === draft.postalCode.trim() && text(s.city) === draft.city.trim() && text(s.region) === draft.region.trim();
}

export function buildRelationPayload(draft: RelationDraft, options: { requestId?: string; expectedVersion?: number; acknowledgeSimilarNames?: boolean; stored?: Relation | null; today?: string; defaultPaymentTermsDays?: number } = {}): RelationBuildResult {
    const stored = options.stored ?? null;
    const today = options.today ?? todayInAmsterdam();
    const errors: RelationRuleError[] = [];
    const name = draft.name.trim();
    if (name === '') errors.push({ field: 'name', code: 'required' });
    else if (name.length > MAX_RELATION_NAME_LENGTH) errors.push({ field: 'name', code: 'too_long' });
    const searchCode = blankToNull(draft.searchCode);
    if (searchCode !== null && !SEARCH_CODE.test(searchCode)) errors.push({ field: 'searchCode', code: 'invalid' });
    const tradeName = optional(draft.tradeName, MAX_RELATION_NAME_LENGTH, 'tradeName', errors);
    const website = optional(draft.website, 200, 'website', errors);
    const email = optional(draft.email, 200, 'email', errors);
    if (email !== null && !emailOk(email, stored?.email)) errors.push({ field: 'email', code: 'invalid_email' });
    const phone = optional(draft.phone, 40, 'phone', errors);
    const vatRaw = blankToNull(draft.vatNumber);
    const vatNumber = vatRaw === null ? null : normalizeVat(vatRaw);
    if (vatNumber !== null && vatNumber !== stored?.vatNumber) vatErrors(vatNumber, errors);
    const registrationNumber = optional(draft.registrationNumber, 64, 'registrationNumber', errors);
    const registrationCountry = blankToNull(draft.registrationCountry)?.toUpperCase() ?? null;
    if (registrationCountry !== null && !COUNTRY.test(registrationCountry)) errors.push({ field: 'registrationCountry', code: 'invalid' });
    if (registrationNumber !== null && registrationCountry === null) errors.push({ field: 'registrationCountry', code: 'required' });
    const registrationType = optional(draft.registrationType, 32, 'registrationType', errors);
    const legalForm = optional(draft.legalForm, 64, 'legalForm', errors);
    const internalNote = optional(draft.internalNote, MAX_RELATION_NOTE_LENGTH, 'internalNote', errors);

    let customer: SaveRelationRequest['customer'] = null;
    if (draft.customerRole) {
        const termsValues = normalizeTerms(draft.customerTerms, 'customer', today, errors, options.defaultPaymentTermsDays);
        const currency = currencyOf(draft.customerTerms.currency, 'customer.currency', errors);
        const credit = normalizeCredit(draft.credit, 'customer', errors);
        customer = { ...saveTerms(termsValues), currency, ...credit };
    }
    let vendor: SaveRelationRequest['vendor'] = null;
    if (draft.vendorRole) {
        const termsValues = normalizeTerms(draft.vendorTerms, 'vendor', today, errors, options.defaultPaymentTermsDays);
        const currency = currencyOf(draft.vendorTerms.currency, 'vendor.defaultCurrency', errors);
        const seen = new Set<string>();
        const ggns: VendorGgn[] = [];
        draft.ggns.forEach((g, index) => {
            const resolved = resolveGgn(g.ggn);
            if (!resolved.ok || resolved.value === null) { errors.push({ field: `ggns.${g.key}.ggn`, code: 'ggn_13_digits' }); return; }
            if (seen.has(resolved.value)) { errors.push({ field: `ggns.${g.key}.ggn`, code: 'ggn_duplicate' }); return; }
            seen.add(resolved.value);
            // Certificaatcontrole: datum niet in de toekomst; datum en naam van wie controleerde beide of geen van beide.
            const checkedOn = blankToNull(g.checkedOn);
            const checkedBy = optional(g.checkedBy, 100, `ggns.${g.key}.checkedBy`, errors);
            if (checkedOn !== null && checkedOn > today) errors.push({ field: `ggns.${g.key}.checkedOn`, code: 'ggn_check_future' });
            if ((checkedOn === null) !== (checkedBy === null)) errors.push({ field: `ggns.${g.key}.${checkedOn === null ? 'checkedOn' : 'checkedBy'}`, code: 'ggn_check_pair' });
            ggns.push({
                id: g.id, ggn: resolved.value, producerName: optional(g.producerName, 200, `ggns.${g.key}.producerName`, errors), isActive: g.isActive, checkedOn, checkedBy,
                certificateValidUntil: blankToNull(g.certificateValidUntil), hasGgnLabel: g.hasGgnLabel,
            });
            void index;
        });
        vendor = { ...saveTerms(termsValues), defaultCurrency: currency, ggns };
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
            const storedValue = ch.id === null ? undefined : stored?.contacts.flatMap((sc) => sc.channels).find((sc) => sc.id === ch.id)?.value;
            if (ch.kind === 'Email' && !emailOk(value, storedValue)) errors.push({ field: `contacts.${c.key}.channels.${ch.key}`, code: 'invalid_email' });
            if (value.length > (ch.kind === 'Phone' ? 40 : 200)) errors.push({ field: `contacts.${c.key}.channels.${ch.key}`, code: 'too_long' });
            channels.push({ id: ch.id, kind: ch.kind, label: optional(ch.label, 50, `contacts.${c.key}.channels.${ch.key}.label`, errors), value });
        }
        return { id: c.id, name: contactName, department: optional(c.department, 50, `contacts.${c.key}.department`, errors), jobTitle: optional(c.jobTitle, 100, `contacts.${c.key}.jobTitle`, errors), isPrimary: c.isPrimary, isActive: c.isActive, note: optional(c.note, 500, `contacts.${c.key}.note`, errors), channels };
    });
    if (draft.contacts.filter((c) => c.isPrimary && c.isActive).length > 1) errors.push({ field: 'contacts', code: 'two_primary' });

    // Bankrekeningen: precies één van IBAN / ander nummer, IBAN-controle (vorm, land, lengte, mod-97), BIC-vorm, houder verplicht,
    // inactief kan geen standaard zijn, hooguit één actieve standaard, dezelfde IBAN niet twee keer actief, hooguit 20 rekeningen.
    if (draft.bankAccounts.length > MAX_BANK_ACCOUNTS) errors.push({ field: 'bankAccounts', code: 'too_many_accounts' });
    const activeIbans = new Set<string>();
    const bankAccounts: SaveBankAccount[] = draft.bankAccounts.map((a) => {
        const k = (field: string) => `bankAccounts.${a.key}.${field}`;
        let iban: string | null = null;
        let otherAccountNumber: string | null = null;
        if (a.useOther) {
            otherAccountNumber = optional(a.otherAccountNumber, MAX_OTHER_ACCOUNT_NUMBER_LENGTH, k('otherAccountNumber'), errors);
            if (otherAccountNumber === null) errors.push({ field: k('otherAccountNumber'), code: 'account_one_of' });
        } else {
            iban = normalizeIban(a.iban);
            if (iban === null) errors.push({ field: k('iban'), code: 'account_one_of' });
            else {
                const problem = checkIban(iban);
                if (problem === 'shape') errors.push({ field: k('iban'), code: 'iban_shape' });
                else if (problem === 'unknown_country') errors.push({ field: k('iban'), code: 'iban_country', params: { country: iban.slice(0, 2) } });
                else if (problem === 'length') errors.push({ field: k('iban'), code: 'iban_length', params: { country: iban.slice(0, 2), actual: String(iban.length) } });
                else if (problem === 'checksum') errors.push({ field: k('iban'), code: 'iban_checksum' });
                else if (a.isActive && activeIbans.has(iban)) errors.push({ field: k('iban'), code: 'account_duplicate' });
                if (a.isActive) activeIbans.add(iban);
            }
        }
        const bic = normalizeBic(a.bic);
        if (bic !== null && !isBic(bic)) errors.push({ field: k('bic'), code: 'bic' });
        const accountHolder = a.accountHolder.trim();
        if (accountHolder === '') errors.push({ field: k('accountHolder'), code: 'required' });
        else if (accountHolder.length > MAX_ACCOUNT_HOLDER_LENGTH) errors.push({ field: k('accountHolder'), code: 'too_long' });
        const currency = currencyOf(a.currency === '' ? 'EUR' : a.currency, k('currency'), errors);
        if (a.isDefault && !a.isActive) errors.push({ field: k('isDefault'), code: 'inactive_default' });
        return { id: a.id, iban, otherAccountNumber, bic, accountHolder, currency, isDefault: a.isDefault, isActive: a.isActive };
    });
    if (draft.bankAccounts.filter((a) => a.isDefault && a.isActive).length > 1) errors.push({ field: 'bankAccounts', code: 'two_default_accounts' });
    const actorReference = blankToNull(draft.actorReference);
    if (actorReference !== null && actorReference.length > 100) errors.push({ field: 'actorReference', code: 'too_long' });
    else if (actorReference === null && bankAccountsNeedActor(draft.bankAccounts)) errors.push({ field: 'actorReference', code: 'actor_required' });

    if (errors.length > 0) return { ok: false, errors };
    return {
        ok: true,
        value: {
            ...(options.requestId ? { requestId: options.requestId } : {}),
            ...(options.expectedVersion !== undefined ? { expectedVersion: options.expectedVersion } : {}),
            searchCode, name, tradeName, preferredLanguage: blankToNull(draft.preferredLanguage), website, email, phone, vatNumber, registrationNumber, registrationCountry, registrationType, legalForm, internalNote,
            isActive: draft.isActive, acknowledgeSimilarNames: options.acknowledgeSimilarNames ?? false, actorReference, customer, vendor, addresses, contacts, bankAccounts,
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

// Bedrijfsinstelling: absolute https-URL (zelfde regel als RelationRules.IsHttpsUrl), hooguit 500 tekens.
export const MAX_PORTAL_URL_LENGTH = 500;
export function isHttpsUrl(value: string): boolean {
    try {
        const url = new URL(value.trim());
        return url.protocol === 'https:' && url.hostname !== '' && value.trim().length <= MAX_PORTAL_URL_LENGTH;
    } catch {
        return false;
    }
}
