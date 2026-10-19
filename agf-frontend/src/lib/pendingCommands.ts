import type { CreateSalesOrderRequest, CreateStockReservationRequest, ReleaseStockReservationRequest } from '../types/salesOrder';

// Onafgeronde commando's: een bevroren payload met zijn requestId waarvan de uitkomst onbekend is (netwerk, ongeldige
// JSON, 5xx). Ze blijven boven de formulieren bewaard (per tabblad), zodat weg- en terugnavigeren exact dezelfde
// opdracht teruggeeft en nooit een nieuw commando dezelfde plaats inneemt voordat de uitkomst bekend is.
// Zonder imports met uitvoer, zodat node:test dit bestand kan laden.

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

// ---- Opslag per tabblad: alleen wat exact de verwachte vorm heeft, wordt teruggelezen ----

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function hasMeta(value: Record<string, unknown>): boolean {
    return typeof value.requestId === 'string' && value.requestId !== ''
        && typeof value.attempts === 'number'
        && (value.lastError === null || typeof value.lastError === 'string')
        && typeof value.frozenAt === 'string'
        && isRecord(value.payload)
        && value.payload.requestId === value.requestId
        && isRecord(value.display);
}

function isPendingCommand(value: unknown): value is PendingCommand {
    if (!isRecord(value) || !hasMeta(value) || typeof value.key !== 'string') return false;
    switch (value.kind) {
        case 'create-order':
            return value.key === CREATE_ORDER_KEY && Array.isArray((value.payload as Record<string, unknown>).lines);
        case 'reserve':
            return typeof value.orderId === 'number' && typeof value.lineId === 'number' && value.key === reserveKey(value.orderId, value.lineId);
        case 'release':
            return typeof value.orderId === 'number' && typeof value.lineId === 'number' && typeof value.reservationId === 'number'
                && value.key === releaseKey(value.orderId, value.lineId, value.reservationId);
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
