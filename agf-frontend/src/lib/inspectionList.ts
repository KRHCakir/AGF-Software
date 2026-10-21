import type { QualityStatus } from '../types/availability';
import type { PalletAuditEvent, PalletDetailLine } from '../types/inspection';
import { isInspectable, qualityFromEvent } from './qualityTransitions.ts';

// Pure regels van de keuringslijst, los van React: eerst zoeken en kwaliteitsfilters, dan pagineren; compacte
// keuringshistorie per inhoudsregel. Alles alleen-lezen; auditgegevens worden nooit weggelaten, alleen gegroepeerd.

export const INSPECTION_PAGE_SIZE = 10;

export interface FilterablePallet {
    id: number;
    reference: string;
    contentLines: PalletDetailLine[];
}

function normalize(value: string): string {
    return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

// Binnen een pallet uitsluitend de regels die te beoordelen zijn én in het filter passen.
export function visibleLines(pallet: FilterablePallet, statusFilter: Record<QualityStatus, boolean>, search: string): PalletDetailLine[] {
    const term = normalize(search);
    const order: Record<QualityStatus, number> = { Quarantine: 0, Blocked: 1, Approved: 2, Rejected: 3 };
    return pallet.contentLines
        .filter((line) => line.route === 'Warehouse' && isInspectable(line.qualityStatus) && statusFilter[line.qualityStatus])
        .filter((line) => term === '' || normalize(`${pallet.reference} ${line.productName ?? ''} ${line.productVariantName ?? ''} ${line.lotNumber}`).includes(term))
        .sort((a, b) => order[a.qualityStatus] - order[b.qualityStatus] || a.id - b.id);
}

// Pallets zonder passende regels verdwijnen volledig; nieuwste pallet eerst, zodat recente (demo)records bovenaan staan.
export function filterPallets<T extends { pallet: FilterablePallet }>(contexts: readonly T[], statusFilter: Record<QualityStatus, boolean>, search: string): T[] {
    return contexts
        .filter((context) => visibleLines(context.pallet, statusFilter, search).length > 0)
        .sort((a, b) => b.pallet.id - a.pallet.id);
}

export interface Page<T> {
    items: T[];
    page: number;
    pageCount: number;
    total: number;
}

// De pagina wordt altijd geldig gehouden: na een actie waardoor een pallet verdwijnt, springt een te hoge pagina terug.
export function paginate<T>(items: readonly T[], page: number, pageSize: number = INSPECTION_PAGE_SIZE): Page<T> {
    const total = items.length;
    const pageCount = Math.max(1, Math.ceil(total / pageSize));
    const current = Math.min(Math.max(1, Math.floor(page) || 1), pageCount);
    return { items: items.slice((current - 1) * pageSize, current * pageSize), page: current, pageCount, total };
}

// ---- Historie ----

export const HISTORY_STEP = 5;

// Technische eventnamen → vertaalsleutels; onbekende typen blijven zichtbaar onder hun eigen naam.
const EVENT_LABEL_KEYS: Readonly<Record<string, string>> = {
    PalletCreated: 'event_pallet_created',
    ContentLineAdded: 'event_content_line_added',
    LogisticsStatusChanged: 'event_logistics_status_changed',
    LocationChanged: 'event_location_changed',
    QualityStatusChanged: 'event_quality_status_changed',
    ReservationCreated: 'event_reservation_created',
    ReservationConsumed: 'event_reservation_consumed',
    ReservationCorrected: 'event_reservation_corrected',
    ReservationReleased: 'event_reservation_released',
    DirectTradeAllocated: 'event_direct_trade_allocated',
    DirectTradeReleased: 'event_direct_trade_released',
    DirectTradeShipped: 'event_direct_trade_shipped',
    DirectTradeShipmentCorrected: 'event_direct_trade_shipment_corrected',
    ShipmentDeliveryConfirmed: 'event_shipment_delivery_confirmed',
    ShipmentDeliveryRetracted: 'event_shipment_delivery_retracted',
    SalesOrderCancelled: 'event_sales_order_cancelled',
    SalesOrderDispatched: 'event_sales_order_dispatched',
};

export function eventLabelKey(eventType: string): string | null {
    return EVENT_LABEL_KEYS[eventType] ?? null;
}

export const EVENT_LABEL_KEY_VALUES: readonly string[] = Object.values(EVENT_LABEL_KEYS);

export function newestFirst(events: readonly PalletAuditEvent[]): PalletAuditEvent[] {
    return [...events].sort((a, b) => b.occurredAt.localeCompare(a.occurredAt) || b.id - a.id);
}

// Alleen keuringsbeslissingen (oude kwaliteit → nieuwe kwaliteit), nieuwste eerst.
export function qualityDecisions(events: readonly PalletAuditEvent[]): PalletAuditEvent[] {
    return newestFirst(events.filter((event) => event.eventType === 'QualityStatusChanged'));
}

export interface LastInspection {
    actor: string | null;
    occurredAt: string;
    toStatus: string | null;
}

export function lastInspection(events: readonly PalletAuditEvent[]): LastInspection | null {
    const latest = qualityDecisions(events)[0];
    return latest ? { actor: latest.actorReference, occurredAt: latest.occurredAt, toStatus: qualityFromEvent(latest.newValue) } : null;
}
