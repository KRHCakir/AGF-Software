import type { PackageUnit } from '../types/availability';
import type { CreatePurchaseInvoiceLineRequest, CreatePurchaseInvoiceRequest, OptionalFieldValues, PurchaseInvoiceSettings, PurchaseOptionalField, PurchaseVendor } from '../types/purchaseInvoice';
import { parsePositiveDecimal } from './decimal.ts';
import { PaymentConditionBasis, conditionDueDate } from './paymentCondition.ts';
import { fitsAmount, lineNet, lineVat, totals } from './invoiceMath.ts';

// Pure regels van de inkoopfactuurwerkplek, los van React en i18n (node:test). Spiegelt PurchaseInvoiceService:
// leveranciersdefaults zijn voorstellen die op het document worden bewaard; handmatige invoer wordt nooit overschreven;
// bedragen worden berekend, nooit vrij ingevoerd; alleen ingeschakelde optionele velden gaan mee in de payload.

export interface LineDraft {
    key: string;
    productVariantId: string; // '' = vrije omschrijving
    description: string;
    quantity: string;
    unit: PackageUnit | '';
    unitPrice: string;
    discount: string;
    vat: string;
}

export interface HeaderDraft {
    vendorId: string;
    invoiceNumber: string;
    invoiceDate: string; // YYYY-MM-DD
    paymentTermsDays: string;
    dueDate: string;     // YYYY-MM-DD
    currency: string;
    // Interne opmerking bij dit document; blijft in het concept en de bevroren opdracht, nooit overgenomen naar een volgend document.
    internalNote: string;
    extra: Partial<Record<PurchaseOptionalField, string>>;
}

export const MAX_INTERNAL_NOTE_LENGTH = 1000;

// Welke voorgestelde velden de gebruiker zelf heeft aangepast: die blijven staan bij leverancierswissel en verversing.
export interface Touched {
    paymentTermsDays: boolean;
    currency: boolean;
    dueDate: boolean;
}

// Betalingsconditie van de gekozen leverancier (dagen + basis). Ontbreekt in oudere bewaarde concepten: dan factuurdatum + dagen.
export interface VendorTerms {
    days: number;
    basis: PaymentConditionBasis;
}

export interface InvoiceDraft {
    header: HeaderDraft;
    lines: LineDraft[];
    touched: Touched;
    vendorTerms?: VendorTerms | null;
}

export type RuleCode = 'required' | 'empty' | 'invalid' | 'not_positive' | 'too_many_decimals' | 'too_large' | 'negative' | 'out_of_range' | 'before_invoice_date' | 'too_many_lines';
export interface RuleError { field: string; code: RuleCode }
export type BuildResult<T> = { ok: true; value: T } | { ok: false; errors: RuleError[] };

export const MAX_LINES = 200;
export const MAX_PAYMENT_TERMS_DAYS = 365;
let lineCounter = 0;
export const newLineKey = () => `line-${Date.now().toString(36)}-${(lineCounter++).toString(36)}`;

export function emptyLine(defaultVat: number): LineDraft {
    return { key: newLineKey(), productVariantId: '', description: '', quantity: '', unit: '', unitPrice: '', discount: '0', vat: String(defaultVat) };
}

export function emptyDraft(today: string, settings: PurchaseInvoiceSettings): InvoiceDraft {
    const terms = settings.defaultPaymentTermsDays;
    return {
        header: { vendorId: '', invoiceNumber: '', invoiceDate: today, paymentTermsDays: String(terms), dueDate: addDays(today, terms) ?? '', currency: settings.defaultCurrency, internalNote: '', extra: {} },
        lines: [emptyLine(settings.defaultVatPercentage)],
        touched: { paymentTermsDays: false, currency: false, dueDate: false },
    };
}

// Kalenderrekenen op YYYY-MM-DD in UTC, zodat zomertijd nooit een dag verschuift.
export function addDays(date: string, days: number): string | null {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
    if (!match) return null;
    const time = Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]) + days);
    if (Number.isNaN(time)) return null;
    return new Date(time).toISOString().slice(0, 10);
}

export function daysBetween(from: string, to: string): number | null {
    const a = addDays(from, 0); const b = addDays(to, 0);
    if (a === null || b === null) return null;
    return Math.round((Date.parse(b) - Date.parse(a)) / 86_400_000);
}

// Zelfde regel als de server: de basis van de leveranciersconditie (bijv. einde maand) geldt alleen zolang de termijn gelijk is
// aan de dagen van die conditie; een afwijkende termijn telt vanaf de factuurdatum.
export function dueDateBasis(draft: InvoiceDraft): PaymentConditionBasis {
    const terms = draft.vendorTerms;
    return terms != null && /^\d+$/.test(draft.header.paymentTermsDays) && Number(draft.header.paymentTermsDays) === terms.days ? terms.basis : 'InvoiceDate';
}

function expectedDueDate(draft: InvoiceDraft): string | null {
    if (!/^\d+$/.test(draft.header.paymentTermsDays)) return null;
    return conditionDueDate(draft.header.invoiceDate, Number(draft.header.paymentTermsDays), dueDateBasis(draft));
}

function recomputeDueDate(draft: InvoiceDraft): InvoiceDraft {
    if (draft.touched.dueDate) return draft;
    const due = expectedDueDate(draft);
    return { ...draft, header: { ...draft.header, dueDate: due ?? draft.header.dueDate } };
}

// Leverancier gekozen of gewisseld: alleen niet-aangepaste voorstellen volgen de leverancier (of het bedrijf).
export function selectVendor(draft: InvoiceDraft, vendor: PurchaseVendor | null, settings: PurchaseInvoiceSettings): InvoiceDraft {
    const header = { ...draft.header, vendorId: vendor === null ? '' : String(vendor.id) };
    const vendorTerms: VendorTerms | null = vendor === null ? null : { days: vendor.paymentTermsDays, basis: vendor.paymentConditionBasis };
    if (!draft.touched.paymentTermsDays) header.paymentTermsDays = String(vendor?.paymentTermsDays ?? settings.defaultPaymentTermsDays);
    if (!draft.touched.currency) header.currency = (vendor?.defaultCurrency ?? '').trim() === '' ? settings.defaultCurrency : vendor!.defaultCurrency!.trim().toUpperCase();
    return recomputeDueDate({ ...draft, header, vendorTerms });
}

export function changePaymentTerms(draft: InvoiceDraft, value: string): InvoiceDraft {
    return recomputeDueDate({ ...draft, header: { ...draft.header, paymentTermsDays: value }, touched: { ...draft.touched, paymentTermsDays: true } });
}

export function changeInvoiceDate(draft: InvoiceDraft, value: string): InvoiceDraft {
    return recomputeDueDate({ ...draft, header: { ...draft.header, invoiceDate: value } });
}

export function changeDueDate(draft: InvoiceDraft, value: string): InvoiceDraft {
    return { ...draft, header: { ...draft.header, dueDate: value }, touched: { ...draft.touched, dueDate: true } };
}

export function changeCurrency(draft: InvoiceDraft, value: string): InvoiceDraft {
    return { ...draft, header: { ...draft.header, currency: value.toUpperCase() }, touched: { ...draft.touched, currency: true } };
}

// Een handmatige vervaldatum terug laten volgen uit de termijn.
export function followPaymentTerms(draft: InvoiceDraft): InvoiceDraft {
    return recomputeDueDate({ ...draft, touched: { ...draft.touched, dueDate: false } });
}

// Zichtbaar houden hoe een handmatige vervaldatum zich verhoudt tot de termijn: het verschil in dagen ten opzichte van
// factuurdatum + termijn (null als de datum gewoon de termijn volgt of niet te berekenen is).
export function dueDateDeviation(draft: InvoiceDraft): number | null {
    if (!draft.touched.dueDate) return null;
    const expected = expectedDueDate(draft);
    if (expected === null) return null;
    const diff = daysBetween(expected, draft.header.dueDate);
    return diff === null || diff === 0 ? null : diff;
}

function normalize(value: string): string {
    return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

// Zoeken op naam, btw-nummer en #nummer ("#12" of "12" zoekt eerst het exacte nummer).
export function searchVendors(vendors: readonly PurchaseVendor[], query: string): PurchaseVendor[] {
    const term = normalize(query);
    if (term === '') return [];
    const number = /^#?(\d{1,10})$/.exec(term);
    if (number) {
        const exact = vendors.filter((vendor) => vendor.id === Number(number[1]));
        if (exact.length > 0 || term.startsWith('#')) return exact;
    }
    const exactCode = vendors.filter((vendor) => vendor.searchCode !== null && normalize(vendor.searchCode) === term);
    const rest = vendors.filter((vendor) => !exactCode.includes(vendor) && normalize(`${vendor.searchCode ?? ''} ${vendor.name} ${vendor.vatNumber ?? ''}`).includes(term));
    return [...exactCode, ...rest];
}

export function describeVendor(vendor: PurchaseVendor): string {
    return [vendor.searchCode, vendor.name, vendor.vatNumber, `#${vendor.id}`].filter((part): part is string => typeof part === 'string' && part !== '').join(' · ');
}

// Niet-negatief bedrag of percentage met hooguit `maxDecimals` decimalen (0 toegestaan).
export function parseNonNegative(input: string, maxDecimals: number): { ok: true; value: number } | { ok: false; code: RuleCode } {
    const text = input.trim();
    if (text === '') return { ok: false, code: 'required' };
    const match = /^(\d+)(?:[.,](\d+))?$/.exec(text);
    if (match === null) return { ok: false, code: 'invalid' };
    if ((match[2] ?? '').length > maxDecimals) return { ok: false, code: 'too_many_decimals' };
    if (match[1].replace(/^0+(?=\d)/, '').length > 12) return { ok: false, code: 'too_large' };
    return { ok: true, value: Number(`${match[1]}.${match[2] ?? '0'}`) };
}

export interface LineAmounts { net: number; vat: number }

// Live regelbedrag; null zolang de regel niet volledig is.
export function lineAmounts(line: LineDraft): LineAmounts | null {
    const quantity = parsePositiveDecimal(line.quantity);
    const price = parseNonNegative(line.unitPrice, 2);
    const discount = parseNonNegative(line.discount === '' ? '0' : line.discount, 2);
    const vat = parseNonNegative(line.vat, 2);
    if (!quantity.ok || !price.ok || !discount.ok || !vat.ok || discount.value > 100 || vat.value > 100) return null;
    const net = lineNet(quantity.value, price.value, discount.value);
    return { net, vat: lineVat(net, vat.value) };
}

export function draftTotals(lines: readonly LineDraft[]) {
    return totals(lines.map(lineAmounts).filter((amounts): amounts is LineAmounts => amounts !== null));
}

// Een lege regel (nog niets ingevuld) telt niet mee en geeft geen fouten: de gebruiker mag een regel afmaken.
export function isBlankLine(line: LineDraft): boolean {
    return line.productVariantId === '' && line.description.trim() === '' && line.quantity.trim() === '' && line.unitPrice.trim() === '';
}

const OPTIONAL_NUMBER_FIELDS: readonly PurchaseOptionalField[] = ['grossWeightKg', 'netWeightKg', 'customsValue'];
const OPTIONAL_DATE_FIELDS: readonly PurchaseOptionalField[] = ['receivedDate'];

export function buildPurchaseInvoicePayload(draft: InvoiceDraft, settings: PurchaseInvoiceSettings): BuildResult<Omit<CreatePurchaseInvoiceRequest, 'requestId'>> {
    const errors: RuleError[] = [];
    const { header } = draft;
    if (!/^[1-9]\d*$/.test(header.vendorId)) errors.push({ field: 'vendorId', code: 'required' });
    const invoiceNumber = header.invoiceNumber.trim();
    if (invoiceNumber === '') errors.push({ field: 'invoiceNumber', code: 'required' });
    if (addDays(header.invoiceDate, 0) === null) errors.push({ field: 'invoiceDate', code: header.invoiceDate.trim() === '' ? 'required' : 'invalid' });
    const terms = Number(header.paymentTermsDays);
    if (!/^\d+$/.test(header.paymentTermsDays)) errors.push({ field: 'paymentTermsDays', code: header.paymentTermsDays.trim() === '' ? 'required' : 'invalid' });
    else if (terms > MAX_PAYMENT_TERMS_DAYS) errors.push({ field: 'paymentTermsDays', code: 'out_of_range' });
    if (addDays(header.dueDate, 0) === null) errors.push({ field: 'dueDate', code: header.dueDate.trim() === '' ? 'required' : 'invalid' });
    else if (addDays(header.invoiceDate, 0) !== null && header.dueDate < header.invoiceDate) errors.push({ field: 'dueDate', code: 'before_invoice_date' });
    const currency = header.currency.trim().toUpperCase();
    if (!/^[A-Z]{3}$/.test(currency)) errors.push({ field: 'currency', code: currency === '' ? 'required' : 'invalid' });
    const internalNote = header.internalNote.trim();
    if (internalNote.length > MAX_INTERNAL_NOTE_LENGTH) errors.push({ field: 'internalNote', code: 'too_large' });

    const extra: OptionalFieldValues = {};
    for (const field of settings.optionalFields) {
        const raw = (header.extra[field] ?? '').trim();
        if (raw === '') continue;
        if (OPTIONAL_NUMBER_FIELDS.includes(field)) {
            const parsed = parseNonNegative(raw, 3);
            if (!parsed.ok) { errors.push({ field, code: parsed.code }); continue; }
            extra[field] = parsed.value;
        } else if (OPTIONAL_DATE_FIELDS.includes(field)) {
            if (addDays(raw, 0) === null) { errors.push({ field, code: 'invalid' }); continue; }
            extra[field] = raw;
        } else {
            extra[field] = raw;
        }
    }

    const filled = draft.lines.filter((line) => !isBlankLine(line));
    if (filled.length === 0) errors.push({ field: 'lines', code: 'required' });
    else if (filled.length > MAX_LINES) errors.push({ field: 'lines', code: 'too_many_lines' });
    const lines: CreatePurchaseInvoiceLineRequest[] = [];
    const amounts: { net: number; vat: number }[] = [];
    for (const line of draft.lines) {
        if (isBlankLine(line)) continue;
        const index = draft.lines.indexOf(line);
        const prefix = `lines[${index}]`;
        const hasProduct = /^[1-9]\d*$/.test(line.productVariantId);
        const description = line.description.trim();
        if (!hasProduct && description === '') errors.push({ field: `${prefix}.description`, code: 'required' });
        const quantity = parsePositiveDecimal(line.quantity);
        if (!quantity.ok) errors.push({ field: `${prefix}.quantity`, code: quantity.error });
        const price = parseNonNegative(line.unitPrice, 2);
        if (!price.ok) errors.push({ field: `${prefix}.unitPrice`, code: price.code });
        const discount = parseNonNegative(line.discount === '' ? '0' : line.discount, 2);
        if (!discount.ok) errors.push({ field: `${prefix}.discountPercentage`, code: discount.code });
        else if (discount.value > 100) errors.push({ field: `${prefix}.discountPercentage`, code: 'out_of_range' });
        const vat = parseNonNegative(line.vat, 2);
        if (!vat.ok) errors.push({ field: `${prefix}.vatPercentage`, code: vat.code });
        else if (vat.value > 100) errors.push({ field: `${prefix}.vatPercentage`, code: 'out_of_range' });
        // Twee op zich geldige waarden kunnen samen buiten de geldkolommen vallen (zelfde grens als de server).
        if (quantity.ok && price.ok && discount.ok && vat.ok) {
            const net = lineNet(quantity.value, price.value, discount.value);
            if (!fitsAmount(net) || !fitsAmount(lineVat(net, vat.value))) errors.push({ field: `${prefix}.unitPrice`, code: 'too_large' });
            else amounts.push({ net, vat: lineVat(net, vat.value) });
        }
        lines.push({
            productVariantId: hasProduct ? Number(line.productVariantId) : null,
            description: description === '' ? null : description,
            quantity: quantity.ok ? quantity.value : 0,
            unit: line.unit === '' ? null : line.unit,
            unitPrice: price.ok ? price.value : 0,
            discountPercentage: discount.ok ? discount.value : 0,
            vatPercentage: vat.ok ? vat.value : 0,
        });
    }

    if (errors.length === 0) {
        const sum = totals(amounts);
        if (!fitsAmount(sum.net) || !fitsAmount(sum.vat) || !fitsAmount(sum.gross)) errors.push({ field: 'lines', code: 'too_large' });
    }

    if (errors.length > 0) return { ok: false, errors };
    return {
        ok: true,
        value: { vendorId: Number(header.vendorId), invoiceNumber, invoiceDate: header.invoiceDate, paymentTermsDays: terms, dueDate: header.dueDate, currency, internalNote: internalNote === '' ? null : internalNote, lines, ...extra },
    };
}

export function groupErrors(errors: RuleError[]): Record<string, RuleCode[]> {
    const grouped: Record<string, RuleCode[]> = {};
    for (const error of errors) (grouped[error.field] ??= []).push(error.code);
    return grouped;
}
