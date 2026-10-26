// Rolafspraken van klant en leverancier, spiegel van AgfAdmin/Services/RoleTermsRules.cs (node:test):
//   - betalingstermijn vrij: dagen (leeg = standaard), einde maand, optionele korting (validateFreeTerms in paymentCondition.ts);
//   - betaalwijze leeg -> BankTransfer; DirectDebit vraagt mandaatkenmerk (max 35) en ondertekeningsdatum (niet in de toekomst);
//     bij elke andere betaalwijze worden de mandaatvelden leeggemaakt;
//   - leveringsconditie: geen | Incoterm (Incoterms 2020 + plaats verplicht) | Franco / AfKwekerij (toelichting optioneel) |
//     Other (omschrijving verplicht); velden die niet bij de gekozen soort horen worden leeggemaakt;
//   - handelsvoorwaarden: geen | Own | Frugiventa | Cofreurop | Other (+ omschrijving verplicht);
//   - krediet (alleen klant, EUR): limiet leeg of >= 0 met hooguit 2 decimalen; verzekerd -> verzekeraar (Other + naam) en
//     verzekerde limiet verplicht; niet verzekerd -> alle verzekeringsvelden leeg. Verlopen verzekering = waarschuwing, geen fout.

import { validateFreeTerms } from './paymentCondition.ts';
import type { FreeTermsRuleCode } from './paymentCondition.ts';

export const PAYMENT_METHODS = ['BankTransfer', 'DirectDebit', 'Cash', 'Offset'] as const;
export type PaymentMethod = (typeof PAYMENT_METHODS)[number];
export const DELIVERY_TERM_KINDS = ['Incoterm', 'Franco', 'AfKwekerij', 'Other'] as const;
export type DeliveryTermKind = (typeof DELIVERY_TERM_KINDS)[number];
export const INCOTERMS_2020 = ['EXW', 'FCA', 'CPT', 'CIP', 'DAP', 'DPU', 'DDP', 'FAS', 'FOB', 'CFR', 'CIF'] as const;
export const TRADE_CONDITIONS = ['Own', 'Frugiventa', 'Cofreurop', 'Other'] as const;
export type TradeConditions = (typeof TRADE_CONDITIONS)[number];
export const CREDIT_INSURERS = ['Atradius', 'AllianzTrade', 'Coface', 'Other'] as const;
export type CreditInsurer = (typeof CREDIT_INSURERS)[number];

export const MAX_MANDATE_REFERENCE_LENGTH = 35;
export const MAX_INCOTERM_PLACE_LENGTH = 100;
export const MAX_DELIVERY_TERM_TEXT_LENGTH = 200;
export const MAX_TRADE_CONDITIONS_TEXT_LENGTH = 200;
export const MAX_INSURER_TEXT_LENGTH = 100;
export const MAX_INSURER_REFERENCE_LENGTH = 64;
export const INSURANCE_EXPIRY_WARNING_DAYS = 30;
// Terugval als de referentie (bedrijfsinstelling) geen standaardtermijn meegeeft; gelijk aan PurchaseInvoiceRules.DefaultPaymentTermsDays.
export const DEFAULT_PAYMENT_TERMS_DAYS = 30;

export type TermsRuleCode = 'required' | 'too_long' | 'invalid' | 'currency' | 'mandate_required' | 'mandate_date_required' | 'mandate_future' | 'incoterm' | 'incoterm_place'
    | 'delivery_text' | 'trade_text' | 'amount' | 'insurer' | 'insurer_text' | 'insured_limit' | FreeTermsRuleCode;
export interface TermsRuleError { field: string; code: TermsRuleCode }
// Foutenlijst van de aanroeper (ook een bredere lijst, zoals die van de relatieregels).
export interface TermsErrorSink { push(error: TermsRuleError): number; readonly length: number }

// Invoer zoals het formulier haar bijhoudt (tekst); '' = niet gekozen.
export interface RoleTermsDraft {
    // Vrije betalingstermijn: '' = standaard uit de bedrijfsinstellingen.
    paymentTermsDays: string;
    paymentTermsEndOfMonth: boolean;
    discountPercent: string;
    discountDays: string;
    paymentMethod: string;
    mandateReference: string;
    mandateSignedOn: string;
    currency: string;
    deliveryTermKind: string;
    incoterm: string;
    incotermPlace: string;
    deliveryTermText: string;
    tradeConditions: string;
    tradeConditionsText: string;
}

export interface CreditDraft {
    creditLimit: string;
    isInsured: boolean;
    insurer: string;
    insurerText: string;
    insuredLimit: string;
    insurerReference: string;
    insuredUntil: string;
}

export interface RoleTermsValues {
    paymentTermsDays: number | null;
    paymentTermsEndOfMonth: boolean;
    discountPercent: number | null;
    discountDays: number | null;
    paymentMethod: PaymentMethod;
    mandateReference: string | null;
    mandateSignedOn: string | null;
    deliveryTermKind: DeliveryTermKind | null;
    incoterm: string | null;
    incotermPlace: string | null;
    deliveryTermText: string | null;
    tradeConditions: TradeConditions | null;
    tradeConditionsText: string | null;
}

export interface CreditValues {
    creditLimit: number | null;
    isInsured: boolean;
    insurer: CreditInsurer | null;
    insurerText: string | null;
    insuredLimit: number | null;
    insurerReference: string | null;
    insuredUntil: string | null;
}

export function emptyTermsDraft(currency = 'EUR'): RoleTermsDraft {
    return { paymentTermsDays: '', paymentTermsEndOfMonth: false, discountPercent: '', discountDays: '', paymentMethod: 'BankTransfer', mandateReference: '', mandateSignedOn: '', currency, deliveryTermKind: '', incoterm: '', incotermPlace: '', deliveryTermText: '', tradeConditions: '', tradeConditionsText: '' };
}

export function emptyCreditDraft(): CreditDraft {
    return { creditLimit: '', isInsured: false, insurer: '', insurerText: '', insuredLimit: '', insurerReference: '', insuredUntil: '' };
}

const blank = (value: string): string | null => (value.trim() === '' ? null : value.trim());
const oneOf = <T extends string>(list: readonly T[], value: string | null): value is T => value !== null && (list as readonly string[]).includes(value);

function optionalText(value: string, max: number, field: string, errors: TermsErrorSink): string | null {
    const text = blank(value);
    if (text !== null && text.length > max) errors.push({ field, code: 'too_long' });
    return text;
}

// Bedrag in EUR: 0 of meer, hooguit 2 decimalen, komma of punt; geen duizendtalscheiding. Leeg -> null, ongeldig -> undefined.
export function parseAmount(value: string): number | null | undefined {
    const text = value.trim();
    if (text === '') return null;
    const match = /^(\d{1,16})(?:[.,](\d{1,2}))?$/.exec(text);
    return match ? Number(match[2] === undefined ? match[1] : `${match[1]}.${match[2]}`) : undefined;
}

// defaultTermsDays: standaard betalingstermijn (bedrijfsinstelling) voor een leeg dagenveld; nodig voor de grens van de kortingsdagen.
export function normalizeTerms(draft: RoleTermsDraft, prefix: string, today: string, errors: TermsErrorSink, defaultTermsDays = DEFAULT_PAYMENT_TERMS_DAYS): RoleTermsValues {
    // Oudere concepten (sessionStorage van vóór de vrije termijn) missen de velden: dan leeg.
    const free = validateFreeTerms({ paymentTermsDays: draft.paymentTermsDays ?? '', paymentTermsEndOfMonth: draft.paymentTermsEndOfMonth ?? false, discountPercent: draft.discountPercent ?? '', discountDays: draft.discountDays ?? '' }, defaultTermsDays);
    for (const error of free.errors) errors.push({ field: `${prefix}.${error.field}`, code: error.code });
    const methodText = blank(draft.paymentMethod) ?? 'BankTransfer';
    if (!oneOf(PAYMENT_METHODS, methodText)) errors.push({ field: `${prefix}.paymentMethod`, code: 'invalid' });
    const paymentMethod = (oneOf(PAYMENT_METHODS, methodText) ? methodText : 'BankTransfer') as PaymentMethod;
    let mandateReference: string | null = null;
    let mandateSignedOn: string | null = null;
    if (paymentMethod === 'DirectDebit') {
        const before = errors.length;
        mandateReference = optionalText(draft.mandateReference, MAX_MANDATE_REFERENCE_LENGTH, `${prefix}.mandateReference`, errors);
        if (mandateReference === null && errors.length === before) errors.push({ field: `${prefix}.mandateReference`, code: 'mandate_required' });
        mandateSignedOn = blank(draft.mandateSignedOn);
        if (mandateSignedOn === null) errors.push({ field: `${prefix}.mandateSignedOn`, code: 'mandate_date_required' });
        else if (mandateSignedOn > today) errors.push({ field: `${prefix}.mandateSignedOn`, code: 'mandate_future' });
    }

    const kindText = blank(draft.deliveryTermKind);
    let deliveryTermKind: DeliveryTermKind | null = null;
    let incoterm: string | null = null;
    let incotermPlace: string | null = null;
    let deliveryTermText: string | null = null;
    if (kindText !== null && !oneOf(DELIVERY_TERM_KINDS, kindText)) {
        errors.push({ field: `${prefix}.deliveryTermKind`, code: 'invalid' });
    } else if (kindText !== null) {
        deliveryTermKind = kindText as DeliveryTermKind;
        if (deliveryTermKind === 'Incoterm') {
            incoterm = blank(draft.incoterm)?.toUpperCase() ?? null;
            if (!oneOf(INCOTERMS_2020, incoterm)) errors.push({ field: `${prefix}.incoterm`, code: 'incoterm' });
            const before = errors.length;
            incotermPlace = optionalText(draft.incotermPlace, MAX_INCOTERM_PLACE_LENGTH, `${prefix}.incotermPlace`, errors);
            if (incotermPlace === null && errors.length === before) errors.push({ field: `${prefix}.incotermPlace`, code: 'incoterm_place' });
        } else {
            const before = errors.length;
            deliveryTermText = optionalText(draft.deliveryTermText, MAX_DELIVERY_TERM_TEXT_LENGTH, `${prefix}.deliveryTermText`, errors);
            if (deliveryTermKind === 'Other' && deliveryTermText === null && errors.length === before) errors.push({ field: `${prefix}.deliveryTermText`, code: 'delivery_text' });
        }
    }

    const tradeText = blank(draft.tradeConditions);
    let tradeConditions: TradeConditions | null = null;
    let tradeConditionsText: string | null = null;
    if (tradeText !== null && !oneOf(TRADE_CONDITIONS, tradeText)) {
        errors.push({ field: `${prefix}.tradeConditions`, code: 'invalid' });
    } else if (tradeText !== null) {
        tradeConditions = tradeText as TradeConditions;
        if (tradeConditions === 'Other') {
            const before = errors.length;
            tradeConditionsText = optionalText(draft.tradeConditionsText, MAX_TRADE_CONDITIONS_TEXT_LENGTH, `${prefix}.tradeConditionsText`, errors);
            if (tradeConditionsText === null && errors.length === before) errors.push({ field: `${prefix}.tradeConditionsText`, code: 'trade_text' });
        }
    }

    return {
        ...free.values,
        paymentMethod, mandateReference, mandateSignedOn, deliveryTermKind, incoterm, incotermPlace, deliveryTermText, tradeConditions, tradeConditionsText,
    };
}

export function normalizeCredit(draft: CreditDraft, prefix: string, errors: TermsErrorSink): CreditValues {
    const creditLimit = parseAmount(draft.creditLimit);
    if (creditLimit === undefined) errors.push({ field: `${prefix}.creditLimit`, code: 'amount' });
    if (!draft.isInsured) {
        return { creditLimit: creditLimit ?? null, isInsured: false, insurer: null, insurerText: null, insuredLimit: null, insurerReference: null, insuredUntil: null };
    }
    const insurerValue = blank(draft.insurer);
    let insurerText: string | null = null;
    if (!oneOf(CREDIT_INSURERS, insurerValue)) {
        errors.push({ field: `${prefix}.insurer`, code: 'insurer' });
    } else if (insurerValue === 'Other') {
        const before = errors.length;
        insurerText = optionalText(draft.insurerText, MAX_INSURER_TEXT_LENGTH, `${prefix}.insurerText`, errors);
        if (insurerText === null && errors.length === before) errors.push({ field: `${prefix}.insurerText`, code: 'insurer_text' });
    }
    const insuredLimit = parseAmount(draft.insuredLimit);
    if (insuredLimit === null || insuredLimit === undefined) errors.push({ field: `${prefix}.insuredLimit`, code: 'insured_limit' });
    const insurerReference = optionalText(draft.insurerReference, MAX_INSURER_REFERENCE_LENGTH, `${prefix}.insurerReference`, errors);
    return {
        creditLimit: creditLimit ?? null, isInsured: true, insurer: oneOf(CREDIT_INSURERS, insurerValue) ? insurerValue : null, insurerText,
        insuredLimit: insuredLimit ?? null, insurerReference, insuredUntil: blank(draft.insuredUntil),
    };
}

function plusDays(date: string, days: number): string {
    const [year, month, day] = date.split('-').map(Number);
    return new Date(Date.UTC(year, month - 1, day + days)).toISOString().slice(0, 10);
}

// Verzekering verlopen (einddatum vóór vandaag) of verloopt binnen 30 dagen (vandaag t/m vandaag + 30); datums als YYYY-MM-DD.
export const insuranceExpired = (insuredUntil: string | null, today: string): boolean => insuredUntil !== null && insuredUntil < today;
export const insuranceExpiresSoon = (insuredUntil: string | null, today: string): boolean =>
    insuredUntil !== null && insuredUntil >= today && insuredUntil <= plusDays(today, INSURANCE_EXPIRY_WARNING_DAYS);

// Gat tussen interne limiet en verzekerde limiet (alleen als de verzekerde limiet lager is), anders null.
export function insuranceGap(creditLimit: number | null, isInsured: boolean, insuredLimit: number | null): number | null {
    return isInsured && creditLimit !== null && insuredLimit !== null && insuredLimit < creditLimit ? Math.round((creditLimit - insuredLimit) * 100) / 100 : null;
}
