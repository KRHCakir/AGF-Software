import { PACKAGE_UNITS, type PackageUnit } from '../types/availability.ts';
import type { CreateSalesOrderRequest, CreateStockReservationRequest, ReleaseStockReservationRequest } from '../types/salesOrder';

// Onafgeronde commando's: een bevroren payload met zijn requestId waarvan de uitkomst onbekend is (netwerk, ongeldige
// JSON, 5xx). Ze blijven boven de formulieren bewaard (per tabblad), zodat weg- en terugnavigeren exact dezelfde
// opdracht teruggeeft en nooit een nieuw commando dezelfde plaats inneemt voordat de uitkomst bekend is.
// Alleen pure imports (met .ts-extensie), zodat node:test dit bestand kan laden.

export interface CommandMeta {
    requestId: string;
    attempts: number;
    lastError: string | null;
    frozenAt: string;
}

export interface CreateOrderCommand extends CommandMeta {
    kind: 'create-order';
    key: 'create-order';
    payload: CreateSalesOrderRequest;
    display: { customerName: string };
}

export interface ReserveCommand extends CommandMeta {
    kind: 'reserve';
    key: string;
    orderId: number;
    lineId: number;
    payload: CreateStockReservationRequest;
    display: { palletReference: string; lotNumber: string; productLabel: string };
}

export interface ReleaseCommand extends CommandMeta {
    kind: 'release';
    key: string;
    orderId: number;
    lineId: number;
    reservationId: number;
    payload: ReleaseStockReservationRequest;
    display: { palletReference: string; lotNumber: string; productLabel: string };
}

export type PendingCommand = CreateOrderCommand | ReserveCommand | ReleaseCommand;

export type PendingCommands = Record<string, PendingCommand>;

export const CREATE_ORDER_KEY = 'create-order';

export function reserveKey(orderId: number, lineId: number): string {
    return `reserve:${orderId}:${lineId}`;
}

export function releaseKey(orderId: number, lineId: number, reservationId: number): string {
    return `release:${orderId}:${lineId}:${reservationId}`;
}

export function commandsForLine(commands: PendingCommands, orderId: number, lineId: number): PendingCommand[] {
    return Object.values(commands).filter((command) => command.kind !== 'create-order' && command.orderId === orderId && command.lineId === lineId);
}

export function commandsForOrder(commands: PendingCommands, orderId: number): PendingCommand[] {
    return Object.values(commands).filter((command) => command.kind !== 'create-order' && command.orderId === orderId);
}

// Een nieuw commando mag een onafgerond commando op dezelfde plaats nooit vervangen: eerst de uitkomst afwachten.
// Lezen van andere orders blijft altijd mogelijk; dit blokkeert alleen schrijven op dezelfde orderregel of een nieuwe order.
export function blockedBy(commands: PendingCommands, scope: { kind: 'create-order' } | { kind: 'line'; orderId: number; lineId: number }): PendingCommand | null {
    if (scope.kind === 'create-order') {
        return commands[CREATE_ORDER_KEY] ?? null;
    }
    return commandsForLine(commands, scope.orderId, scope.lineId)[0] ?? null;
}

export function sortedCommands(commands: PendingCommands): PendingCommand[] {
    return Object.values(commands).sort((a, b) => a.frozenAt.localeCompare(b.frozenAt));
}

export function withAttempt(command: PendingCommand, lastError: string | null): PendingCommand {
    return { ...command, attempts: command.attempts + 1, lastError };
}

// ---- Opslag per tabblad: alleen wat volledig de verwachte vorm heeft, wordt teruggelezen ----
// Elke payload wordt veld voor veld gecontroleerd (ids, hoeveelheden, kg, eenheid, actor, reden, versie, regels),
// zodat een verminkte of vreemde opslag nooit een onvolledig of fout commando kan herstellen.

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isPositiveInt(value: unknown): value is number {
    return typeof value === 'number' && Number.isInteger(value) && value > 0;
}

function isNonNegativeInt(value: unknown): value is number {
    return typeof value === 'number' && Number.isInteger(value) && value >= 0;
}

function isPositiveAmount(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value) && value > 0;
}

function isText(value: unknown): value is string {
    return typeof value === 'string' && value.trim() !== '';
}

function isNullableText(value: unknown): value is string | null {
    return value === null || typeof value === 'string';
}

function isUnit(value: unknown): value is PackageUnit {
    return typeof value === 'string' && (PACKAGE_UNITS as readonly string[]).includes(value);
}

function isTimestamp(value: unknown): value is string {
    return typeof value === 'string' && !Number.isNaN(new Date(value).getTime());
}

function isCreateOrderPayload(value: unknown, requestId: string): value is CreateSalesOrderRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isPositiveInt(value.customerId)
        && isNullableText(value.customerReference)
        && isTimestamp(value.requestedDeliveryDate)
        && isText(value.actorReference)
        && Array.isArray(value.lines)
        && value.lines.length > 0
        && value.lines.every((line: unknown) => isRecord(line) && isPositiveInt(line.productVariantId) && isPositiveAmount(line.quantity) && isUnit(line.unit));
}

function isReservePayload(value: unknown, requestId: string): value is CreateStockReservationRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isPositiveInt(value.palletContentLineId)
        && isPositiveAmount(value.quantity)
        && isUnit(value.unit)
        && isPositiveAmount(value.netWeightKg)
        && isText(value.actorReference)
        && isNullableText(value.reason);
}

function isReleasePayload(value: unknown, requestId: string): value is ReleaseStockReservationRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isNonNegativeInt(value.expectedReservationVersion)
        && isText(value.actorReference)
        && isText(value.reason);
}

function hasMeta(value: Record<string, unknown>): value is Record<string, unknown> & CommandMeta {
    return isText(value.requestId)
        && isNonNegativeInt(value.attempts)
        && isNullableText(value.lastError)
        && isTimestamp(value.frozenAt);
}

function isLineDisplay(value: unknown): value is ReserveCommand['display'] {
    return isRecord(value) && typeof value.palletReference === 'string' && typeof value.lotNumber === 'string' && typeof value.productLabel === 'string';
}

export function isPendingCommand(value: unknown): value is PendingCommand {
    if (!isRecord(value) || !hasMeta(value) || typeof value.key !== 'string') return false;
    switch (value.kind) {
        case 'create-order':
            return value.key === CREATE_ORDER_KEY
                && isCreateOrderPayload(value.payload, value.requestId)
                && isRecord(value.display) && typeof value.display.customerName === 'string';
        case 'reserve':
            return isPositiveInt(value.orderId) && isPositiveInt(value.lineId)
                && value.key === reserveKey(value.orderId, value.lineId)
                && isReservePayload(value.payload, value.requestId)
                && isLineDisplay(value.display);
        case 'release':
            return isPositiveInt(value.orderId) && isPositiveInt(value.lineId) && isPositiveInt(value.reservationId)
                && value.key === releaseKey(value.orderId, value.lineId, value.reservationId)
                && isReleasePayload(value.payload, value.requestId)
                && isLineDisplay(value.display);
        default:
            return false;
    }
}

export function parseStoredCommands(raw: string | null): PendingCommands {
    if (raw === null || raw === '') return {};
    let parsed: unknown;
    try {
        parsed = JSON.parse(raw);
    } catch {
        return {};
    }
    if (!isRecord(parsed)) return {};
    const commands: PendingCommands = {};
    for (const [key, value] of Object.entries(parsed)) {
        if (isPendingCommand(value) && value.key === key) {
            commands[key] = value;
        }
    }
    return commands;
}

export function serializeCommands(commands: PendingCommands): string {
    return JSON.stringify(commands);
}
