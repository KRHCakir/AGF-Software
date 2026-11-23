import type { PurchaseOrderLine } from '../types/purchaseOrder';

// Feedback 22 november, orderoverzicht per regel: per regel besteld, ontvangen, open en de status, ook bij 30 regels overzichtelijk.
// Besteld = verwacht + vervallen restant (na splitsen verwacht de oude regel alleen nog wat ontvangen is). Geen imports buiten types: met
// node:test te toetsen.

export type LineProgressStatus = 'Ordered' | 'Partial' | 'Received' | 'Closed';

export interface LineProgress {
    id: number;
    ordered: number;
    received: number;
    open: number;
    cancelled: number;
    status: LineProgressStatus;
}

// Actieve regels volgen de server (PurchaseOrderService.ActiveLines): een regel die iets verwacht of iets ontving. Een geheel vervangen regel
// zonder ontvangst hoort niet in het overzicht.
export function activeLines(lines: PurchaseOrderLine[]): PurchaseOrderLine[] {
    return lines.filter((line) => line.expectedQuantity > 0 || line.receivedQuantity > 0);
}

export function lineProgress(line: PurchaseOrderLine): LineProgress {
    const cancelled = line.cancelledQuantity ?? 0;
    const open = Math.max(0, line.expectedQuantity - line.receivedQuantity);
    const status: LineProgressStatus = open > 0
        ? (line.receivedQuantity > 0 ? 'Partial' : 'Ordered')
        : cancelled > 0 ? 'Closed' : 'Received';
    return { id: line.id, ordered: line.expectedQuantity + cancelled, received: line.receivedQuantity, open, cancelled, status };
}

// In de lijst: "2 van 5 regels" (helemaal ontvangen van de actieve regels) en het aantal open verpakkingen; alleen als er iets open staat
// en al iets binnen is (deels ontvangen).
export function partialSummary(item: { lineCount?: number; receivedLineCount?: number; openQuantity?: number; status: string }): { received: number; lines: number; open: number } | null {
    if (item.status !== 'PartiallyReceived' || item.lineCount === undefined || item.receivedLineCount === undefined || item.openQuantity === undefined) return null;
    return { received: item.receivedLineCount, lines: item.lineCount, open: item.openQuantity };
}
