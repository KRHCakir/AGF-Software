import type { CreatePurchaseInvoiceRequest } from '../types/purchaseInvoice';
import type { InvoiceDraft, LineDraft } from './purchaseInvoiceRules.ts';

// Conceptbehoud en bevroren opslagopdracht van de inkoopfactuurwerkplek, per tabblad (sessionStorage), naar het voorbeeld van
// pendingCommands.ts: een validatiefout, verversing van referentiedata of weg- en terugnavigeren wist het concept nooit, en een
// opslag met onbekende uitkomst blijft als exact dezelfde opdracht (zelfde requestId) herstelbaar. Alleen pure imports.

export const DRAFT_KEY = 'agf_purchase_invoice_draft';
export const PENDING_KEY = 'agf_purchase_invoice_pending';
export const EXTRA_OPEN_KEY = 'agf_purchase_invoice_extra_open';

export interface PendingSave {
    requestId: string;
    payload: CreatePurchaseInvoiceRequest;
    // "save" toont het resultaat; "saveAndNew" opent pas een leeg document als de opslag bevestigd is.
    mode: 'save' | 'saveAndNew';
    attempts: number;
    lastError: string | null;
    frozenAt: string;
}

type Storage = { getItem(key: string): string | null; setItem(key: string, value: string): void; removeItem(key: string): void };

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null;
}
const isString = (value: unknown): value is string => typeof value === 'string';

function isLineDraft(value: unknown): value is LineDraft {
    return isRecord(value) && isString(value.key) && isString(value.productVariantId) && isString(value.description) && isString(value.quantity)
        && isString(value.unit) && isString(value.unitPrice) && isString(value.discount) && isString(value.vat);
}

export function parseDraft(raw: string | null): InvoiceDraft | null {
    if (raw === null) return null;
    try {
        const value: unknown = JSON.parse(raw);
        if (!isRecord(value) || !isRecord(value.header) || !Array.isArray(value.lines) || !isRecord(value.touched)) return null;
        const h = value.header; const t = value.touched;
        if (!isString(h.vendorId) || !isString(h.invoiceNumber) || !isString(h.invoiceDate) || !isString(h.paymentTermsDays) || !isString(h.dueDate) || !isString(h.currency) || !isRecord(h.extra)) return null;
        if (!value.lines.every(isLineDraft) || typeof t.paymentTermsDays !== 'boolean' || typeof t.currency !== 'boolean' || typeof t.dueDate !== 'boolean') return null;
        if (!Object.values(h.extra).every(isString)) return null;
        // Concepten van vóór de interne opmerking hebben het veld niet: dan leeg, nooit het hele concept weggooien.
        if (h.internalNote === undefined) h.internalNote = '';
        else if (!isString(h.internalNote)) return null;
        return value as unknown as InvoiceDraft;
    } catch {
        return null;
    }
}

export function parsePending(raw: string | null): PendingSave | null {
    if (raw === null) return null;
    try {
        const value: unknown = JSON.parse(raw);
        if (!isRecord(value) || !isString(value.requestId) || !isRecord(value.payload) || (value.mode !== 'save' && value.mode !== 'saveAndNew')) return null;
        if (typeof value.attempts !== 'number' || !(value.lastError === null || isString(value.lastError)) || !isString(value.frozenAt)) return null;
        const p = value.payload;
        if (typeof p.vendorId !== 'number' || !isString(p.invoiceNumber) || !isString(p.invoiceDate) || !isString(p.currency) || !Array.isArray(p.lines) || p.requestId !== value.requestId) return null;
        return value as unknown as PendingSave;
    } catch {
        return null;
    }
}

function storage(): Storage | null {
    try {
        return typeof window === 'undefined' ? null : window.sessionStorage;
    } catch {
        return null;
    }
}

export function loadDraft(): InvoiceDraft | null { return parseDraft(storage()?.getItem(DRAFT_KEY) ?? null); }
export function saveDraft(draft: InvoiceDraft): void { try { storage()?.setItem(DRAFT_KEY, JSON.stringify(draft)); } catch { /* niet onthouden is geen fout */ } }
export function clearDraft(): void { try { storage()?.removeItem(DRAFT_KEY); } catch { /* idem */ } }

export function loadPending(): PendingSave | null { return parsePending(storage()?.getItem(PENDING_KEY) ?? null); }
export function savePending(pending: PendingSave): void { try { storage()?.setItem(PENDING_KEY, JSON.stringify(pending)); } catch { /* idem */ } }
export function clearPending(): void { try { storage()?.removeItem(PENDING_KEY); } catch { /* idem */ } }

export function loadExtraOpen(): boolean { return storage()?.getItem(EXTRA_OPEN_KEY) === '1'; }
export function saveExtraOpen(open: boolean): void { try { storage()?.setItem(EXTRA_OPEN_KEY, open ? '1' : '0'); } catch { /* idem */ } }
