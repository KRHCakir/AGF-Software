import type { PackageUnit } from '../types/availability';
import type { CreateSalesOrderRequest } from '../types/salesOrder';
import { toLocalMidnightIso } from './calendarDate.ts';
import { parsePositiveDecimal, type DecimalError } from './decimal.ts';

// Pure regels van de verkoopwerkplek, los van React en i18n, zodat ze met node:test te toetsen zijn.
// Ze spiegelen de backendregels (SalesOrderService, StockReservationService); de server blijft altijd leidend.
// Er wordt nooit een eenheid omgerekend en nooit kg afgeleid uit hoeveelheid, dozen of maat.
// Let op: imports met .ts-extensie zijn nodig voor node:test; dit bestand importeert alleen pure modules zonder verdere imports.

export const MAX_ORDER_LINES = 200;
export const MAX_CUSTOMER_REFERENCE_LENGTH = 100;
export const MAX_ACTOR_LENGTH = 100;
export const MAX_REASON_LENGTH = 500;

export type RuleCode =
    | DecimalError
    | 'required'
    | 'invalid_date'
    | 'too_long'
    | 'too_many_lines'
    | 'duplicate_line'
    | 'exceeds_available'
    | 'exceeds_demand'
    | 'remainder_mismatch';

export interface RuleError {
    field: string;
    code: RuleCode;
}

export interface OrderLineDraft {
    key: string;
    productVariantId: string;
    quantity: string;
    unit: PackageUnit | '';
}

export interface OrderDraft {
    customerId: string;
    customerReference: string;
    requestedDeliveryDate: string;
    lines: OrderLineDraft[];
}

export type OrderPayload = Omit<CreateSalesOrderRequest, 'requestId'>;

export type BuildResult<T> = { ok: true; value: T } | { ok: false; errors: RuleError[] };

export { toLocalMidnightIso };

export function normalizeActor(actor: string): BuildResult<string> {
    const value = actor.trim();
    if (value === '') return { ok: false, errors: [{ field: 'actorReference', code: 'required' }] };
    if (value.length > MAX_ACTOR_LENGTH) return { ok: false, errors: [{ field: 'actorReference', code: 'too_long' }] };
    return { ok: true, value };
}

// Getrimd; leeg wordt null; verplicht alleen als `required` waar is.
export function normalizeReason(text: string, required: boolean): BuildResult<string | null> {
    const value = text.trim();
    if (value.length > MAX_REASON_LENGTH) return { ok: false, errors: [{ field: 'reason', code: 'too_long' }] };
    if (value === '') return required ? { ok: false, errors: [{ field: 'reason', code: 'required' }] } : { ok: true, value: null };
    return { ok: true, value };
}

// Volledige validatie van een nieuwe order; alle fouten tegelijk, met dezelfde veldsleutels als de backend.
export function buildOrderPayload(draft: OrderDraft, actor: string): BuildResult<OrderPayload> {
    const errors: RuleError[] = [];
    const actorResult = normalizeActor(actor);
    if (!actorResult.ok) errors.push(...actorResult.errors);

    const customerId = Number(draft.customerId);
    if (!/^[1-9]\d*$/.test(draft.customerId)) errors.push({ field: 'customerId', code: 'required' });

    const reference = draft.customerReference.trim();
    if (reference.length > MAX_CUSTOMER_REFERENCE_LENGTH) errors.push({ field: 'customerReference', code: 'too_long' });

    const requestedDeliveryDate = toLocalMidnightIso(draft.requestedDeliveryDate);
    if (requestedDeliveryDate === null) {
        errors.push({ field: 'requestedDeliveryDate', code: draft.requestedDeliveryDate.trim() === '' ? 'required' : 'invalid_date' });
    }

    if (draft.lines.length === 0) errors.push({ field: 'lines', code: 'required' });
    else if (draft.lines.length > MAX_ORDER_LINES) errors.push({ field: 'lines', code: 'too_many_lines' });

    const seen = new Set<string>();
    const lines = draft.lines.map((line, index) => {
        const prefix = `lines[${index}]`;
        const before = errors.length;
        if (!/^[1-9]\d*$/.test(line.productVariantId)) errors.push({ field: `${prefix}.productVariantId`, code: 'required' });
        const quantity = parsePositiveDecimal(line.quantity);
        if (!quantity.ok) errors.push({ field: `${prefix}.quantity`, code: quantity.error });
        if (line.unit === '') errors.push({ field: `${prefix}.unit`, code: 'required' });
        // Eén regel per variant + eenheid, net als de databank-index: anders worden reserveringen dubbelzinnig.
        if (errors.length === before) {
            const key = `${line.productVariantId}|${line.unit}`;
            if (seen.has(key)) errors.push({ field: prefix, code: 'duplicate_line' });
            seen.add(key);
        }
        return {
            productVariantId: Number(line.productVariantId),
            quantity: quantity.ok ? quantity.value : 0,
            unit: (line.unit === '' ? 'box' : line.unit) as PackageUnit,
        };
    });

    if (errors.length > 0 || !actorResult.ok || requestedDeliveryDate === null) {
        return { ok: false, errors };
    }
    return {
        ok: true,
        value: {
            customerId,
            customerReference: reference === '' ? null : reference,
            requestedDeliveryDate,
            actorReference: actorResult.value,
            lines,
        },
    };
}

export interface StockBalance {
    availableQuantity: number;
    availableNetWeightKg: number;
}

export interface ReservationInput {
    quantity: string;
    netKg: string;
}

export interface ReservationAmounts {
    quantity: number;
    netWeightKg: number;
}

// "Volledige rest reserveren" mag alleen als de hele resterende inhoud in de resterende klantvraag past.
export function fullRemainderFits(stock: StockBalance, openQuantity: number): boolean {
    return stock.availableQuantity > 0 && stock.availableNetWeightKg > 0 && stock.availableQuantity <= openQuantity;
}

// Spiegel van de backendcontrole: niet boven de inhoud, niet boven de open vraag, en geen gestrande rest
// (de volledige resterende hoeveelheid hoort bij het volledige resterende kg, en omgekeerd).
export function validateReservationInput(input: ReservationInput, stock: StockBalance, openQuantity: number): BuildResult<ReservationAmounts> {
    const errors: RuleError[] = [];
    const quantity = parsePositiveDecimal(input.quantity);
    const netKg = parsePositiveDecimal(input.netKg);
    if (!quantity.ok) errors.push({ field: 'quantity', code: quantity.error });
    if (!netKg.ok) errors.push({ field: 'netWeightKg', code: netKg.error });
    if (!quantity.ok || !netKg.ok) return { ok: false, errors };

    if (quantity.value > stock.availableQuantity) errors.push({ field: 'quantity', code: 'exceeds_available' });
    if (netKg.value > stock.availableNetWeightKg) errors.push({ field: 'netWeightKg', code: 'exceeds_available' });
    if (errors.length === 0 && (quantity.value === stock.availableQuantity) !== (netKg.value === stock.availableNetWeightKg)) {
        errors.push({ field: 'netWeightKg', code: 'remainder_mismatch' });
    }
    if (quantity.value > openQuantity) errors.push({ field: 'quantity', code: 'exceeds_demand' });
    return errors.length > 0 ? { ok: false, errors } : { ok: true, value: { quantity: quantity.value, netWeightKg: netKg.value } };
}

// Resterende klantvraag na een voorgenomen reservering; nooit negatief en alleen voor weergave.
export function remainingAfter(openQuantity: number, quantity: number): number {
    return Math.max(0, openQuantity - quantity);
}

export interface LineProgress {
    reserved: number;
    dispatched: number;
    total: number;
    open: number;
    // Volledig vastgelegd volgens de servertotalen (open = 0): gereserveerd + verzonden = gevraagd.
    complete: boolean;
    onHold: boolean;
    // Groen zonder verzending betekent: volledig gereserveerd én geen enkele reservering on hold.
    ready: boolean;
    // Volledig verzonden (serverflag); toont als geleverd, nooit als "volledig gereserveerd" met 0 gereserveerd.
    fulfilled: boolean;
    // Deels verzonden, rest nog open of gereserveerd.
    partiallyDispatched: boolean;
}

// Structurele types: de helpers lezen alleen de servertotalen en de serverflags; er wordt niets afgeleid.
export interface LineTotals {
    quantity: number;
    reservedQuantity: number;
    dispatchedQuantity: number;
    openQuantity: number;
    fulfilled: boolean;
    reservations: { onHold: boolean }[];
}

export function lineProgress(line: LineTotals): LineProgress {
    const complete = line.openQuantity === 0;
    const onHold = line.reservations.some((reservation) => reservation.onHold);
    return {
        reserved: line.reservedQuantity,
        dispatched: line.dispatchedQuantity,
        total: line.quantity,
        open: line.openQuantity,
        complete,
        onHold,
        ready: complete && !onHold,
        fulfilled: line.fulfilled,
        partiallyDispatched: line.dispatchedQuantity > 0 && !line.fulfilled,
    };
}

export interface OrderProgress {
    linesComplete: number;
    linesFulfilled: number;
    linesTotal: number;
    onHoldCount: number;
    // Alle regels volledig vastgelegd volgens de totalen.
    complete: boolean;
    // Alle regels volledig vastgelegd en nergens een reservering on hold.
    ready: boolean;
    // Alle regels volledig verzonden (serverflag).
    fulfilled: boolean;
    // Minstens één regel (deels) verzonden, maar niet alles.
    partiallyDispatched: boolean;
}

export function orderProgress(order: { fulfilled: boolean; lines: LineTotals[] }): OrderProgress {
    const progress = order.lines.map(lineProgress);
    const onHoldCount = order.lines.reduce((sum, line) => sum + line.reservations.filter((reservation) => reservation.onHold).length, 0);
    const linesComplete = progress.filter((line) => line.complete).length;
    const linesFulfilled = progress.filter((line) => line.fulfilled).length;
    const complete = order.lines.length > 0 && linesComplete === order.lines.length;
    return {
        linesComplete,
        linesFulfilled,
        linesTotal: order.lines.length,
        onHoldCount,
        complete,
        ready: complete && onHoldCount === 0,
        fulfilled: order.fulfilled,
        partiallyDispatched: !order.fulfilled && progress.some((line) => line.dispatched > 0),
    };
}

// ---- Verzenden: keuze van hele reserveringen ----

export interface DispatchCandidate {
    id: number;
    status: string;
    onHold: boolean;
    quantity: number;
    unit: string;
    netWeightKg: number;
}

// Alleen Active reserveringen die niet on hold zijn kunnen worden verzonden; de server controleert dit opnieuw.
export function isDispatchable(reservation: DispatchCandidate): boolean {
    return reservation.status === 'Active' && !reservation.onHold;
}

// Totalen van de gekozen reserveringen per eenheid; eenheden worden nooit samengevoegd of omgerekend.
export function dispatchTotals(selected: DispatchCandidate[]): { unit: string; quantity: number; netWeightKg: number; count: number }[] {
    const byUnit = new Map<string, { unit: string; quantity: number; netWeightKg: number; count: number }>();
    for (const reservation of selected) {
        const total = byUnit.get(reservation.unit) ?? { unit: reservation.unit, quantity: 0, netWeightKg: 0, count: 0 };
        total.quantity += reservation.quantity;
        total.netWeightKg += reservation.netWeightKg;
        total.count += 1;
        byUnit.set(reservation.unit, total);
    }
    return Array.from(byUnit.values()).sort((a, b) => a.unit.localeCompare(b.unit));
}

export function normalizeDispatchReference(text: string): BuildResult<string | null> {
    const value = text.trim();
    if (value.length > 100) return { ok: false, errors: [{ field: 'reference', code: 'too_long' }] };
    return { ok: true, value: value === '' ? null : value };
}

function normalizeSearch(value: string): string {
    return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim();
}

// Zoeken op klant, referentie, ordernummer en productnamen; lege zoekterm laat alles door.
export interface SearchableOrder {
    id: number;
    customerName: string | null;
    customerReference: string | null;
    lines: { productName: string | null; productVariantName: string | null }[];
}

export function matchesOrderSearch(order: SearchableOrder, search: string): boolean {
    const term = normalizeSearch(search);
    if (term === '') return true;
    const haystack = [
        `#${order.id}`,
        String(order.id),
        order.customerName ?? '',
        order.customerReference ?? '',
        ...order.lines.map((line) => `${line.productName ?? ''} ${line.productVariantName ?? ''}`),
    ].join(' ');
    return normalizeSearch(haystack).includes(term);
}

export function groupErrors(errors: RuleError[]): Record<string, RuleCode[]> {
    const grouped: Record<string, RuleCode[]> = {};
    for (const error of errors) {
        (grouped[error.field] ??= []).push(error.code);
    }
    return grouped;
}
