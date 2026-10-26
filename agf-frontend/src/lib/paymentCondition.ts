// Betalingscondities, spiegel van AgfAdmin/Services/PaymentConditionRules.cs (node:test). Pure regels zonder React of i18n.
//
// Vervaldatum: InvoiceDate = factuurdatum + dagen; EndOfMonth = laatste dag van de factuurmaand + dagen
// (15-02-2028, 30 dagen -> 29-02-2028 + 30 = 30-03-2028). Kortingsdatum: altijd factuurdatum + kortingsdagen.
// Kalenderdagen op YYYY-MM-DD in UTC, zodat zomertijd nooit een dag verschuift.

export const PAYMENT_CONDITION_BASES = ['InvoiceDate', 'EndOfMonth'] as const;
export type PaymentConditionBasis = (typeof PAYMENT_CONDITION_BASES)[number];

export const MAX_CONDITION_CODE_LENGTH = 16;
export const MAX_CONDITION_DESCRIPTION_LENGTH = 200;
export const MIN_CONDITION_DAYS = 0;
export const MAX_CONDITION_DAYS = 365;
export const MAX_DISCOUNT_PERCENT = 99.99;
const CODE = /^[A-Za-z0-9][A-Za-z0-9._-]{0,15}$/;

export const isPaymentConditionBasis = (value: unknown): value is PaymentConditionBasis =>
    typeof value === 'string' && (PAYMENT_CONDITION_BASES as readonly string[]).includes(value);

function addDays(date: string, days: number): string | null {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
    if (!match) return null;
    const time = Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]) + days);
    return Number.isNaN(time) ? null : new Date(time).toISOString().slice(0, 10);
}

export function isConditionCode(value: string): boolean {
    return CODE.test(value);
}

function endOfMonth(date: string): string | null {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
    if (!match) return null;
    // Dag 0 van de volgende maand = laatste dag van deze maand.
    const time = Date.UTC(Number(match[1]), Number(match[2]), 0);
    return Number.isNaN(time) ? null : new Date(time).toISOString().slice(0, 10);
}

export function conditionDueDate(invoiceDate: string, days: number, basis: PaymentConditionBasis): string | null {
    const start = basis === 'EndOfMonth' ? endOfMonth(invoiceDate) : invoiceDate;
    return start === null ? null : addDays(start, days);
}

export function conditionDiscountDate(invoiceDate: string, condition: { discountPercent: number | null; discountDays: number | null }): string | null {
    return condition.discountPercent === null || condition.discountDays === null ? null : addDays(invoiceDate, condition.discountDays);
}

// ---- beheerformulier ----

export interface PaymentConditionDraft {
    code: string;
    descriptionNl: string;
    descriptionEn: string;
    days: string;
    basis: PaymentConditionBasis;
    discountPercent: string;
    discountDays: string;
    sortOrder: string;
    isActive: boolean;
}

export type ConditionRuleCode = 'required' | 'too_long' | 'code' | 'days' | 'basis' | 'discount_pair' | 'discount_percent' | 'discount_days' | 'sort_order';
export interface ConditionRuleError { field: string; code: ConditionRuleCode }

export interface PaymentConditionValues {
    code: string;
    descriptionNl: string;
    descriptionEn: string;
    days: number;
    basis: PaymentConditionBasis;
    discountPercent: number | null;
    discountDays: number | null;
    sortOrder: number | null;
    isActive: boolean;
}

export function emptyConditionDraft(): PaymentConditionDraft {
    return { code: '', descriptionNl: '', descriptionEn: '', days: '30', basis: 'InvoiceDate', discountPercent: '', discountDays: '', sortOrder: '', isActive: true };
}

const integer = (value: string): number | null => (/^\d{1,4}$/.test(value.trim()) ? Number(value.trim()) : null);

// Percentage met komma of punt, hooguit 2 decimalen (meer decimalen = ongeldig, nooit afronden).
function percent(value: string): number | null {
    const match = /^(\d{1,2})(?:[.,](\d{1,2}))?$/.exec(value.trim());
    return match ? Number(match[2] === undefined ? match[1] : `${match[1]}.${match[2]}`) : null;
}

export function validateConditionDraft(draft: PaymentConditionDraft): { ok: true; value: PaymentConditionValues } | { ok: false; errors: ConditionRuleError[] } {
    const errors: ConditionRuleError[] = [];
    const code = draft.code.trim();
    if (code === '') errors.push({ field: 'code', code: 'required' });
    else if (!isConditionCode(code)) errors.push({ field: 'code', code: 'code' });
    const descriptionNl = draft.descriptionNl.trim();
    const descriptionEn = draft.descriptionEn.trim();
    for (const [field, value] of [['descriptionNl', descriptionNl], ['descriptionEn', descriptionEn]] as const) {
        if (value === '') errors.push({ field, code: 'required' });
        else if (value.length > MAX_CONDITION_DESCRIPTION_LENGTH) errors.push({ field, code: 'too_long' });
    }
    const days = integer(draft.days);
    if (days === null || days < MIN_CONDITION_DAYS || days > MAX_CONDITION_DAYS) errors.push({ field: 'days', code: 'days' });
    if (!isPaymentConditionBasis(draft.basis)) errors.push({ field: 'basis', code: 'basis' });
    const percentText = draft.discountPercent.trim();
    const discountDaysText = draft.discountDays.trim();
    let discountPercent: number | null = null;
    let discountDays: number | null = null;
    if ((percentText === '') !== (discountDaysText === '')) errors.push({ field: 'discountPercent', code: 'discount_pair' });
    if (percentText !== '') {
        discountPercent = percent(percentText);
        if (discountPercent === null || discountPercent <= 0 || discountPercent > MAX_DISCOUNT_PERCENT) errors.push({ field: 'discountPercent', code: 'discount_percent' });
    }
    if (discountDaysText !== '') {
        discountDays = integer(discountDaysText);
        if (discountDays === null || (days !== null && discountDays > days)) errors.push({ field: 'discountDays', code: 'discount_days' });
    }
    let sortOrder: number | null = null;
    if (draft.sortOrder.trim() !== '') {
        sortOrder = integer(draft.sortOrder);
        if (sortOrder === null) errors.push({ field: 'sortOrder', code: 'sort_order' });
    }
    if (errors.length > 0) return { ok: false, errors };
    return { ok: true, value: { code, descriptionNl, descriptionEn, days: days!, basis: draft.basis, discountPercent, discountDays, sortOrder, isActive: draft.isActive } };
}
