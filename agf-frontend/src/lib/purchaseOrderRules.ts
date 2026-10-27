// Inkooporder (spiegel van AgfAdmin/Services/PurchaseOrderRules*.cs): ordernummer, weergavestatus en factuurindicator.
// Afkeuren: purchaseOrderRejection.ts. Pure module zonder React of i18n, zodat ze met node:test te toetsen is.
export type OrderChannel = 'Phone' | 'Email' | 'Portal' | 'Other';
export const ORDER_CHANNELS: readonly OrderChannel[] = ['Phone', 'Email', 'Portal', 'Other'];

export type PurchaseOrderStatus = 'Ordered' | 'PartiallyReceived' | 'Received' | 'Rejected' | 'Cancelled';
export const PURCHASE_ORDER_STATUSES: readonly PurchaseOrderStatus[] = ['Ordered', 'PartiallyReceived', 'Received', 'Rejected', 'Cancelled'];
export type PurchaseOrderStatusFilter = 'open' | 'all' | PurchaseOrderStatus;

export type InvoiceKind = 'Invoice' | 'CreditNote';
export type InvoiceIndicator = 'None' | 'Received' | 'CreditNoteExpected';

export type RejectionReason = 'Quality' | 'Ripeness' | 'Temperature' | 'Damaged' | 'WrongArticle' | 'Other';
export type RejectionDecision = 'ReturnToSupplier' | 'ClaimDiscount' | 'Dispose';

export const MAX_SUPPLIER_REFERENCE_LENGTH = 100;
export const MAX_INVOICE_NUMBER_LENGTH = 100;

// Delivered (DirectTrade afgeleverd) telt voor de inkoop als ontvangen; ontvangen in de eenheid van de regel.
export function orderStatus(storedStatus: string, lines: readonly { expected: number; received: number }[]): PurchaseOrderStatus {
    if (storedStatus === 'Cancelled') return 'Cancelled';
    if (storedStatus === 'Rejected') return 'Rejected';
    if (storedStatus === 'Delivered' || storedStatus === 'Received') return 'Received';
    if (lines.length === 0 || lines.every((line) => line.received <= 0)) return 'Ordered';
    return lines.every((line) => line.received >= line.expected) ? 'Received' : 'PartiallyReceived';
}

export function isFullyRejected(receivedQuantity: number, rejectedQuantity: number): boolean {
    return receivedQuantity > 0 && rejectedQuantity >= receivedQuantity;
}

// Orderregel: elk aantal met zijn eigen kg, zodat het bestelde gewicht nooit naast het ontvangen aantal staat.
// Ontvangen kg alleen als er iets ontvangen is; afgekeurd alleen als er iets afgekeurd is.
export type LineAmountPart = { key: 'po_line_ordered' | 'po_line_received' | 'po_line_rejected'; quantity: number; kg: number | null };

export function lineAmountParts(line: {
    expectedQuantity: number; expectedNetWeightKg: number | null; receivedQuantity: number; receivedNetWeightKg: number; rejectedQuantity: number; rejectedNetWeightKg: number;
}): LineAmountPart[] {
    const parts: LineAmountPart[] = [
        { key: 'po_line_ordered', quantity: line.expectedQuantity, kg: line.expectedNetWeightKg },
        { key: 'po_line_received', quantity: line.receivedQuantity, kg: line.receivedQuantity > 0 ? line.receivedNetWeightKg : null },
    ];
    if (line.rejectedQuantity > 0) parts.push({ key: 'po_line_rejected', quantity: line.rejectedQuantity, kg: line.rejectedNetWeightKg });
    return parts;
}

export function expectsCreditNote(decision: string): boolean {
    return decision === 'ReturnToSupplier' || decision === 'ClaimDiscount';
}

// Creditnota verwacht zolang er na de laatst vastgelegde creditnota nog een afkeuring met terugsturen of korting is.
export function invoiceIndicator(refs: readonly { kind: string; createdAt: string }[], rejections: readonly { decision: string; rejectedAt: string }[]): InvoiceIndicator {
    const creditNotes = refs.filter((ref) => ref.kind === 'CreditNote').map((ref) => Date.parse(ref.createdAt));
    const lastCreditNote = creditNotes.length === 0 ? null : Math.max(...creditNotes);
    if (rejections.some((rejection) => expectsCreditNote(rejection.decision) && (lastCreditNote === null || Date.parse(rejection.rejectedAt) > lastCreditNote))) {
        return 'CreditNoteExpected';
    }
    return refs.some((ref) => ref.kind === 'Invoice') ? 'Received' : 'None';
}

// Getrimd; leeg = geen nummer. Vergelijken gebeurt zonder hoofdletters (zoals de server).
export function normalizeInvoiceNumber(value: string): string | null {
    const trimmed = value.trim();
    return trimmed === '' ? null : trimmed;
}

export const ORDER_STATUS_LABEL_KEYS: Readonly<Record<PurchaseOrderStatus, string>> = {
    Ordered: 'po_status_ordered',
    PartiallyReceived: 'po_status_partially_received',
    Received: 'po_status_received',
    Rejected: 'po_status_rejected',
    Cancelled: 'po_status_cancelled',
};

export const INVOICE_INDICATOR_LABEL_KEYS: Readonly<Record<InvoiceIndicator, string>> = {
    None: 'po_invoice_none',
    Received: 'po_invoice_received',
    CreditNoteExpected: 'po_invoice_credit_note_expected',
};

export const CHANNEL_LABEL_KEYS: Readonly<Record<OrderChannel, string>> = {
    Phone: 'po_channel_phone',
    Email: 'po_channel_email',
    Portal: 'po_channel_portal',
    Other: 'po_channel_other',
};

export const REASON_LABEL_KEYS: Readonly<Record<RejectionReason, string>> = {
    Quality: 'po_reason_quality',
    Ripeness: 'po_reason_ripeness',
    Temperature: 'po_reason_temperature',
    Damaged: 'po_reason_damaged',
    WrongArticle: 'po_reason_wrong_article',
    Other: 'po_reason_other',
};

export const DECISION_LABEL_KEYS: Readonly<Record<RejectionDecision, string>> = {
    ReturnToSupplier: 'po_decision_return',
    ClaimDiscount: 'po_decision_discount',
    Dispose: 'po_decision_dispose',
};

// Onbekende waarden (een nieuwe backendwaarde) tonen we ongewijzigd: geen sleutel.
export function labelKey<T extends string>(keys: Readonly<Record<T, string>>, value: string | null): string | null {
    return value !== null && Object.prototype.hasOwnProperty.call(keys, value) ? keys[value as T] : null;
}
