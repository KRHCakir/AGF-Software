import type { ReceiptLine, ReceiptOrder, ReceiveLineRequest } from '../types/receipt';
import { formatDecimalInput, parsePositiveDecimal, type DecimalError } from './decimal.ts';

// Ontvangen in één scherm (feedback 22 november): de invoer per regel (aantal, zo nodig netto kg, optioneel palletnummers) en wat er naar
// de server gaat. Geen imports buiten types en lib: met node:test te toetsen.

export const MAX_PALLETS = 99;

export interface ReceiveDraft {
    quantity: string;
    netKg: string;
    // Knop Palletnummers: open of dicht, het aantal pallets en per pallet een nummer (leeg = de server maakt er een).
    palletsOpen: boolean;
    palletCount: string;
    references: string[];
}

export type DraftError = { field: 'quantity' | 'netKg' | 'palletCount'; error: DecimalError | 'pallet_range' | 'kg_required' };

// Startwaarden: het openstaande aantal (een afwijkend aantal mag de gebruiker typen), geen kg, palletnummers dicht.
export function draftFor(line: ReceiptLine): ReceiveDraft {
    return {
        quantity: line.openQuantity > 0 ? formatDecimalInput(line.openQuantity) : '',
        netKg: '',
        palletsOpen: false,
        palletCount: String(line.suggestedPalletCount ?? 1),
        references: [],
    };
}

// Het aantal invoervelden voor palletnummers volgt het aantal pallets (1 tot en met 99); getypte nummers blijven staan.
export function referenceInputs(draft: ReceiveDraft): string[] {
    const count = palletCountOf(draft) ?? 1;
    return Array.from({ length: count }, (_, i) => draft.references[i] ?? '');
}

export function palletCountOf(draft: ReceiveDraft): number | null {
    if (!draft.palletsOpen) return 1;
    const count = Number(draft.palletCount.trim());
    return Number.isInteger(count) && count >= 1 && count <= MAX_PALLETS ? count : null;
}

// De aanvraag voor de server, of wat er niet klopt. Het netto kg alleen als de order geen kg per verpakking kent.
export function requestFor(line: ReceiptLine, draft: ReceiveDraft): { ok: true; request: Omit<ReceiveLineRequest, 'requestId'> } | { ok: false; errors: DraftError[] } {
    const errors: DraftError[] = [];
    const quantity = parsePositiveDecimal(draft.quantity);
    if (!quantity.ok) errors.push({ field: 'quantity', error: quantity.error });
    let netWeightKg: number | null = null;
    if (line.netKgPerPackage === null) {
        const kg = parsePositiveDecimal(draft.netKg);
        if (!kg.ok) errors.push({ field: 'netKg', error: kg.error === 'empty' ? 'kg_required' : kg.error });
        else netWeightKg = kg.value;
    }
    const palletCount = palletCountOf(draft);
    if (palletCount === null) errors.push({ field: 'palletCount', error: 'pallet_range' });
    if (errors.length > 0 || !quantity.ok) return { ok: false, errors };
    const references = draft.palletsOpen ? referenceInputs(draft).map((reference) => reference.trim()) : [];
    return {
        ok: true,
        request: {
            quantity: quantity.value,
            netWeightKg,
            palletCount: draft.palletsOpen ? palletCount : null,
            palletReferences: references.some((reference) => reference !== '') ? references : null,
        },
    };
}

// Dezelfde invoer gebruikt dezelfde requestId (een herhaling na een fout boekt niets dubbel); een andere invoer is een nieuw verzoek.
export function requestKey(lineId: number, request: Omit<ReceiveLineRequest, 'requestId'>): string {
    return JSON.stringify([lineId, request.quantity, request.netWeightKg ?? null, request.palletCount ?? null, request.palletReferences ?? null]);
}

// "Alles ontvangen": elke regel die ontvangen kan worden en nog iets open heeft, in de volgorde van de order.
export function openLines(order: ReceiptOrder): ReceiptLine[] {
    return order.receivable ? order.lines.filter((line) => line.receivable && line.openQuantity > 0) : [];
}

// Tekst bij een ontvangen regel: hoeveel van de bestelling binnen is ("40 van 100").
export function progressOf(line: ReceiptLine): 'none' | 'partial' | 'complete' {
    if (line.receivedQuantity <= 0) return 'none';
    return line.openQuantity > 0 ? 'partial' : 'complete';
}
