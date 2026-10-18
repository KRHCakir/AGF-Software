import { QualityStatus } from '../types/availability';
import { PalletAuditEvent } from '../types/inspection';

// Exacte spiegel van de backend-transitietabel (AgfAdmin/Services/PalletRules.cs). De server beslist altijd;
// deze tabel bepaalt alleen welke knoppen de UI aanbiedt.
const TRANSITIONS: Record<QualityStatus, readonly QualityStatus[]> = {
    Quarantine: ['Approved', 'Blocked', 'Rejected'],
    Approved: ['Blocked'],
    Blocked: ['Approved', 'Rejected'],
    Rejected: [],
};

export const MAX_REASON_LENGTH = 500;

export function allowedQualityTransitions(from: QualityStatus): readonly QualityStatus[] {
    return TRANSITIONS[from];
}

export function isAllowedQualityTransition(from: QualityStatus, to: QualityStatus): boolean {
    return TRANSITIONS[from].includes(to);
}

// Gelijk aan PalletRules.QualityTransitionRequiresReason, dus inclusief het opheffen van een blokkade.
export function qualityReasonRequired(from: QualityStatus, to: QualityStatus): boolean {
    return to === 'Blocked' || to === 'Rejected' || (from === 'Blocked' && to === 'Approved');
}

// Alleen Approved telt ooit mee in "Nu beschikbaar (vóór reserveringen)".
export function countsAsAvailableNow(status: QualityStatus): boolean {
    return status === 'Approved';
}

export function isInspectable(status: QualityStatus): boolean {
    return TRANSITIONS[status].length > 0;
}

export function actionLabelKey(from: QualityStatus, to: QualityStatus): string {
    if (to === 'Approved') return from === 'Blocked' ? 'inspection_action_release' : 'inspection_action_approve';
    if (to === 'Blocked') return 'inspection_action_block';
    return 'inspection_action_reject';
}

export type ReasonResult = { ok: true; value: string | null } | { ok: false; error: 'required' | 'too_long' };

// Trimmen; leeg wordt null; maximaal 500 tekens; verplicht waar de backend dat vereist.
export function normalizeReason(text: string, required: boolean): ReasonResult {
    const value = text.trim();
    if (value.length > MAX_REASON_LENGTH) return { ok: false, error: 'too_long' };
    if (value === '') return required ? { ok: false, error: 'required' } : { ok: true, value: null };
    return { ok: true, value };
}

function readJsonField(json: string | null, field: string): unknown {
    if (json === null) return undefined;
    try {
        const parsed: unknown = JSON.parse(json);
        return typeof parsed === 'object' && parsed !== null ? (parsed as Record<string, unknown>)[field] : undefined;
    } catch {
        return undefined;
    }
}

// "Ontvangst geregistreerd": alleen pallet-events (zonder inhoudsregel). PalletCreated met status Received,
// anders de laatste LogisticsStatusChanged naar Received. Niet af te leiden: null (nooit een verzonnen datum).
export function receiptRegisteredAt(events: PalletAuditEvent[]): string | null {
    const palletEvents = events.filter((event) => event.palletContentLineId === null);
    const created = palletEvents.find((event) =>
        event.eventType === 'PalletCreated' && readJsonField(event.newValue, 'logisticsStatus') === 'Received');
    if (created) return created.occurredAt;
    const changed = palletEvents
        .filter((event) => event.eventType === 'LogisticsStatusChanged' && readJsonField(event.newValue, 'logisticsStatus') === 'Received')
        .sort((a, b) => b.occurredAt.localeCompare(a.occurredAt) || b.id - a.id);
    return changed.length > 0 ? changed[0].occurredAt : null;
}

// Regelhistorie: uitsluitend events van deze inhoudsregel (ContentLineAdded, QualityStatusChanged, ...).
export function lineHistory(events: PalletAuditEvent[], palletContentLineId: number): PalletAuditEvent[] {
    return events
        .filter((event) => event.palletContentLineId === palletContentLineId)
        .sort((a, b) => a.occurredAt.localeCompare(b.occurredAt) || a.id - b.id);
}

export function qualityFromEvent(json: string | null): string | null {
    const value = readJsonField(json, 'qualityStatus');
    return typeof value === 'string' ? value : null;
}
