import type { PackageUnit } from '../types/availability';
import type { CreateSalesOrderRequest } from '../types/salesOrder';
import { toLocalMidnightIso } from './calendarDate.ts';
import { parsePositiveDecimal, type DecimalError } from './decimal.ts';
import { resolvePalletInput, type QuantityInputMode } from './palletInput.ts';

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
    | 'remainder_mismatch'
    | 'exceeds_remaining'
    | 'future_date'
    | 'before_shipping'
    | 'not_whole';

export interface RuleError {
    field: string;
    code: RuleCode;
}

export interface OrderLineDraft {
    key: string;
    productVariantId: string;
    quantity: string;
    unit: PackageUnit | '';
    // Invoer per pallet: 'pallets' rekent quantity uit palletCount × packagesPerPallet; 'packages' is de gewone invoer.
    mode: QuantityInputMode;
    palletCount: string;
    packagesPerPallet: string;
    loosePackages?: string;
}

export interface OrderDraft {
    customerId: string;
    customerReference: string;
    // Interne opmerking bij deze order (alleen intern; nooit overgenomen naar een volgende order).
    internalNote: string;
    requestedDeliveryDate: string;
    lines: OrderLineDraft[];
}

export const MAX_INTERNAL_NOTE_LENGTH = 1000;

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
    const internalNote = draft.internalNote.trim();
    if (internalNote.length > MAX_INTERNAL_NOTE_LENGTH) errors.push({ field: 'internalNote', code: 'too_long' });

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
        // Palletinvoer: het totaal volgt uit pallets × verpakkingen per pallet; de server controleert dezelfde rekensom.
        const amounts = resolvePalletInput(line);
        if (!amounts.ok) for (const error of amounts.errors) errors.push({ field: `${prefix}.${error.field}`, code: error.code as RuleCode });
        if (line.unit === '') errors.push({ field: `${prefix}.unit`, code: 'required' });
        // Eén regel per variant + eenheid, net als de databank-index: anders worden reserveringen dubbelzinnig.
        if (errors.length === before) {
            const key = `${line.productVariantId}|${line.unit}`;
            if (seen.has(key)) errors.push({ field: prefix, code: 'duplicate_line' });
            seen.add(key);
        }
        return {
            productVariantId: Number(line.productVariantId),
            quantity: amounts.ok ? amounts.value.quantity : 0,
            unit: (line.unit === '' ? 'box' : line.unit) as PackageUnit,
            palletCount: amounts.ok ? amounts.value.palletCount : null,
            packagesPerPallet: amounts.ok ? amounts.value.packagesPerPallet : null,
            // Losse verpakkingen alleen als ze zijn ingevuld: een regel zonder blijft dezelfde opdracht als vroeger.
            ...(amounts.ok && amounts.value.loosePackages !== null ? { loosePackages: amounts.value.loosePackages } : {}),
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
            internalNote: internalNote === '' ? null : internalNote,
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

// Resterende klantvraag na een voorgenomen reservering of toewijzing; nooit negatief en alleen voor weergave.
export function remainingAfter(openQuantity: number, quantity: number): number {
    return Math.max(0, openQuantity - quantity);
}

// ---- Toewijzen van verwachte, klantgebonden DirectTrade-aanvoer: alleen hoeveelheid ----

// Spiegel van de backendcontrole: niet boven wat op de bronregel nog toewijsbaar is en niet boven de open klantvraag. Geen kg.
export function validateAllocationInput(quantityText: string, remainingOnSource: number, openQuantity: number): BuildResult<{ quantity: number }> {
    const quantity = parsePositiveDecimal(quantityText);
    if (!quantity.ok) return { ok: false, errors: [{ field: 'quantity', code: quantity.error }] };
    const errors: RuleError[] = [];
    if (quantity.value > remainingOnSource) errors.push({ field: 'quantity', code: 'exceeds_remaining' });
    if (quantity.value > openQuantity) errors.push({ field: 'quantity', code: 'exceeds_demand' });
    return errors.length > 0 ? { ok: false, errors } : { ok: true, value: { quantity: quantity.value } };
}

// "Volledige rest toewijzen": het kleinste van resterend op de bron en open klantvraag; 0 als een van beide 0 is.
export function fullAllocationRemainder(remainingOnSource: number, openQuantity: number): number {
    return Math.max(0, Math.min(remainingOnSource, openQuantity));
}

// Verwachte leverdatum na de gevraagde leverdatum van de order (kalenderdagen, lokale tijd): alleen een waarschuwing, nooit een blokkade.
export function expectedAfterRequested(expectedAt: string | null, requestedDeliveryDate: string): boolean {
    if (expectedAt === null) return false;
    const expected = new Date(expectedAt);
    const requested = new Date(requestedDeliveryDate);
    if (Number.isNaN(expected.getTime()) || Number.isNaN(requested.getTime())) return false;
    const day = (date: Date) => new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
    return day(expected) > day(requested);
}

export interface LineProgress {
    reserved: number;
    // Toegewezen verwachte, klantgebonden aanvoer: telt mee als vastgelegd, maar is nooit voorraad en maakt een regel nooit gereed.
    allocated: number;
    dispatched: number;
    total: number;
    open: number;
    // Volledig vastgelegd volgens de servertotalen (open = 0): gereserveerd + verzonden = gevraagd.
    complete: boolean;
    onHold: boolean;
    // Groen zonder verzending betekent: volledig gereserveerd op magazijnvoorraad, geen enkele reservering on hold én niets toegewezen op verwachte aanvoer.
    ready: boolean;
    // Volledig vastgelegd, maar (deels) op verwachte aanvoer: amber, nooit groen.
    completeWithExpected: boolean;
    // Volledig verzonden (serverflag); toont als geleverd, nooit als "volledig gereserveerd" met 0 gereserveerd.
    fulfilled: boolean;
    // Deels verzonden, rest nog open of gereserveerd.
    partiallyDispatched: boolean;
}

// Structurele types: de helpers lezen alleen de servertotalen en de serverflags; er wordt niets afgeleid.
export interface LineTotals {
    quantity: number;
    reservedQuantity: number;
    allocatedQuantity: number;
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
        allocated: line.allocatedQuantity,
        dispatched: line.dispatchedQuantity,
        total: line.quantity,
        open: line.openQuantity,
        complete,
        onHold,
        ready: complete && !onHold && line.allocatedQuantity === 0,
        completeWithExpected: complete && line.allocatedQuantity > 0 && !line.fulfilled,
        fulfilled: line.fulfilled,
        partiallyDispatched: line.dispatchedQuantity > 0 && !line.fulfilled,
    };
}

export interface OrderProgress {
    linesComplete: number;
    linesFulfilled: number;
    linesTotal: number;
    onHoldCount: number;
    // Regels met toewijzingen van verwachte, klantgebonden aanvoer.
    allocatedLines: number;
    // Alle regels volledig vastgelegd volgens de totalen.
    complete: boolean;
    // Alle regels volledig vastgelegd, nergens een reservering on hold en nergens verwachte aanvoer toegewezen.
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
    const allocatedLines = order.lines.filter((line) => line.allocatedQuantity > 0).length;
    const complete = order.lines.length > 0 && linesComplete === order.lines.length;
    return {
        linesComplete,
        linesFulfilled,
        linesTotal: order.lines.length,
        onHoldCount,
        allocatedLines,
        complete,
        ready: complete && onHoldCount === 0 && allocatedLines === 0,
        fulfilled: order.fulfilled,
        partiallyDispatched: !order.fulfilled && progress.some((line) => line.dispatched > 0),
    };
}

// ---- Verzenden: keuze van hele reserveringen ----

export interface DispatchCandidate {
    id: number;
    status: string;
    source: string;
    onHold: boolean;
    quantity: number;
    unit: string;
    netWeightKg: number | null;
}

// Alleen Active fysieke reserveringen die niet on hold zijn kunnen worden verzonden; een DirectTrade-toewijzing (verwachte,
// klantgebonden aanvoer) nooit vanuit het magazijn. De server controleert dit opnieuw.
export function isDispatchable(reservation: DispatchCandidate): boolean {
    return reservation.status === 'Active' && !reservation.onHold && reservation.source !== 'DirectTrade';
}

export function isDirectTradeAllocation(reservation: { source: string }): boolean {
    return reservation.source === 'DirectTrade';
}

// Totalen van de gekozen reserveringen per eenheid; eenheden worden nooit samengevoegd of omgerekend.
export function dispatchTotals(selected: DispatchCandidate[]): { unit: string; quantity: number; netWeightKg: number; count: number }[] {
    const byUnit = new Map<string, { unit: string; quantity: number; netWeightKg: number; count: number }>();
    for (const reservation of selected) {
        const total = byUnit.get(reservation.unit) ?? { unit: reservation.unit, quantity: 0, netWeightKg: 0, count: 0 };
        total.quantity += reservation.quantity;
        total.netWeightKg += reservation.netWeightKg ?? 0;
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

// ---- DirectTrade-verzending ----

// Alleen Active DirectTrade-toewijzingen zijn te verzenden (hele toewijzingen); de server controleert dit opnieuw.
export function isShippableAllocation(reservation: { source: string; status: string }): boolean {
    return reservation.source === 'DirectTrade' && reservation.status === 'Active';
}

export function isShippedAllocation(reservation: { source: string; status: string }): boolean {
    return reservation.source === 'DirectTrade' && reservation.status === 'Consumed';
}

export interface ShipmentLineInput {
    netKg: string;
    lotNumber: string;
    lotDeviationReason: string;
}

// Werkelijk verzonden kg (ingevoerd, nooit afgeleid) en partij; een afwijking van een ingevulde bronpartij vraagt een toelichting.
// Veldsleutels gelijk aan de backend: lines[i].netWeightKg, lines[i].lotNumber, lines[i].lotDeviationReason.
export function validateShipmentLine(index: number, input: ShipmentLineInput, sourceLot: string | null): BuildResult<{ netWeightKg: number; lotNumber: string; lotDeviationReason: string | null }> {
    const errors: RuleError[] = [];
    const key = `lines[${index}]`;
    const kg = parsePositiveDecimal(input.netKg);
    if (!kg.ok) errors.push({ field: `${key}.netWeightKg`, code: kg.error });
    const lot = input.lotNumber.trim();
    if (lot === '') errors.push({ field: `${key}.lotNumber`, code: 'required' });
    else if (lot.length > 100) errors.push({ field: `${key}.lotNumber`, code: 'too_long' });
    const reason = input.lotDeviationReason.trim();
    const deviates = sourceLot !== null && lot !== '' && lot !== sourceLot;
    if (deviates && reason === '') errors.push({ field: `${key}.lotDeviationReason`, code: 'required' });
    if (reason.length > MAX_REASON_LENGTH) errors.push({ field: `${key}.lotDeviationReason`, code: 'too_long' });
    if (errors.length > 0 || !kg.ok) return { ok: false, errors };
    return { ok: true, value: { netWeightKg: kg.value, lotNumber: lot, lotDeviationReason: deviates ? reason : null } };
}

export function lotDeviates(sourceLot: string | null, lotText: string): boolean {
    const lot = lotText.trim();
    return sourceLot !== null && lot !== '' && lot !== sourceLot;
}

// De verzenddatum: verplicht, geldige kalenderdatum, niet na vandaag in Europe/Amsterdam.
export function validateShippedOn(value: string, today: string): BuildResult<string> {
    if (value.trim() === '') return { ok: false, errors: [{ field: 'shippedOn', code: 'required' }] };
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || Number.isNaN(Date.parse(`${value}T00:00:00Z`))) return { ok: false, errors: [{ field: 'shippedOn', code: 'invalid_date' }] };
    if (value > today) return { ok: false, errors: [{ field: 'shippedOn', code: 'future_date' }] };
    return { ok: true, value };
}

// Teruggenomen DirectTrade-verzending: historie, telt niet meer als verzonden of als bezette capaciteit.
export function isCorrectedAllocation(reservation: { source: string; status: string }): boolean {
    return reservation.source === 'DirectTrade' && reservation.status === 'Corrected';
}

// ---- Afleverbevestiging ----

// Afleverdatum: verplicht, geldige kalenderdatum, niet vóór de verzenddatum en niet na vandaag (beide Europe/Amsterdam).
export function validateDeliveredOn(value: string, shippedOn: string | null, today: string): BuildResult<string> {
    if (value.trim() === '') return { ok: false, errors: [{ field: 'deliveredOn', code: 'required' }] };
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || Number.isNaN(Date.parse(`${value}T00:00:00Z`))) return { ok: false, errors: [{ field: 'deliveredOn', code: 'invalid_date' }] };
    if (value > today) return { ok: false, errors: [{ field: 'deliveredOn', code: 'future_date' }] };
    if (shippedOn !== null && value < shippedOn) return { ok: false, errors: [{ field: 'deliveredOn', code: 'before_shipping' }] };
    return { ok: true, value };
}

