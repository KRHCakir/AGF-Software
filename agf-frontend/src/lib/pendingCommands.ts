import { PACKAGE_UNITS, type PackageUnit } from '../types/availability.ts';
import type { CreateCustomerRequest } from '../types/referenceData';
import type { CancelSalesOrderRequest, ConfirmDeliveryRequest, CorrectDirectTradeShipmentRequest, RetractDeliveryRequest, CorrectSalesDispatchRequest, CreateDirectTradeAllocationRequest, CreateDirectTradeShipmentRequest, CreateSalesDispatchRequest, CreateSalesOrderRequest, CreateStockReservationRequest, ReleaseStockReservationRequest } from '../types/salesOrder';

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

// Toewijzing van verwachte, klantgebonden DirectTrade-aanvoer aan een orderregel: regelcommando, net als reserveren.
export interface AllocateDirectTradeCommand extends CommandMeta {
    kind: 'allocate-direct-trade';
    key: string;
    orderId: number;
    lineId: number;
    payload: CreateDirectTradeAllocationRequest;
    // expectedAt: de verwachte leverdatum zoals getoond bij het bevriezen. Alleen weergave; nooit onderdeel van de payload,
    // zodat de herhaling byte voor byte gelijk blijft, ook als de bronregel daarna niet meer in de picker staat.
    display: { sourceLabel: string; lotNumber: string; productLabel: string; expectedAt: string | null };
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

export interface CancelOrderCommand extends CommandMeta {
    kind: 'cancel-order';
    key: string;
    orderId: number;
    payload: CancelSalesOrderRequest;
    display: { customerName: string; activeReservationCount: number };
}

export interface DispatchCommand extends CommandMeta {
    kind: 'dispatch';
    key: string;
    orderId: number;
    payload: CreateSalesDispatchRequest;
    display: { customerName: string; lineCount: number };
}

// DirectTrade-verzending: ordercommando (blokkeert regelcommando's, annuleren en magazijnverzending, en wordt erdoor geblokkeerd).
export interface ShipDirectTradeCommand extends CommandMeta {
    kind: 'ship-direct-trade';
    key: string;
    orderId: number;
    payload: CreateDirectTradeShipmentRequest;
    display: { customerName: string; lineCount: number };
}

// Terugnemen van een DirectTrade-verzending: ordercommando, net als de magazijncorrectie.
export interface CorrectDtShipmentCommand extends CommandMeta {
    kind: 'correct-dt-shipment';
    key: string;
    orderId: number;
    shipmentId: number;
    payload: CorrectDirectTradeShipmentRequest;
    display: { customerName: string; lineCount: number };
}

// Afleverbevestiging: ordercommando; de soort staat in de sleutel, zodat gelijke nummers in beide reeksen nooit botsen.
export interface ConfirmDeliveryCommand extends CommandMeta {
    kind: 'confirm-delivery';
    key: string;
    orderId: number;
    shipmentKind: 'Warehouse' | 'DirectTrade';
    shipmentId: number;
    payload: ConfirmDeliveryRequest;
    display: { customerName: string; lineCount: number };
}

// Intrekken van een afleverbevestiging: ordercommando per bevestiging; soort, verzending en bevestiging staan in de sleutel.
export interface RetractDeliveryCommand extends CommandMeta {
    kind: 'retract-delivery';
    key: string;
    orderId: number;
    shipmentKind: 'Warehouse' | 'DirectTrade';
    shipmentId: number;
    confirmationId: number;
    payload: RetractDeliveryRequest;
    display: { customerName: string; sequence: number };
}

export interface CorrectDispatchCommand extends CommandMeta {
    kind: 'correct-dispatch';
    key: string;
    orderId: number;
    dispatchId: number;
    payload: CorrectSalesDispatchRequest;
    display: { customerName: string; lineCount: number };
}

// Nieuwe klant vanuit het orderformulier. Eén vaste sleutel: een tweede klantopdracht kan een onafgeronde nooit vervangen.
export interface CreateCustomerCommand extends CommandMeta {
    kind: 'create-customer';
    key: 'create-customer';
    payload: CreateCustomerRequest;
    display: { name: string };
}

export type PendingCommand = CreateOrderCommand | ReserveCommand | AllocateDirectTradeCommand | ReleaseCommand | CancelOrderCommand | DispatchCommand | ShipDirectTradeCommand | CorrectDtShipmentCommand | ConfirmDeliveryCommand | RetractDeliveryCommand | CorrectDispatchCommand | CreateCustomerCommand;

// Regelcommando's: horen bij één orderregel en blokkeren elkaar daar.
const LINE_KINDS: readonly PendingCommand['kind'][] = ['reserve', 'allocate-direct-trade', 'release'];

function isLineCommand(command: PendingCommand): command is ReserveCommand | AllocateDirectTradeCommand | ReleaseCommand {
    return LINE_KINDS.includes(command.kind);
}

export type PendingCommands = Record<string, PendingCommand>;

export const CREATE_ORDER_KEY = 'create-order';
export const CREATE_CUSTOMER_KEY = 'create-customer';

export function reserveKey(orderId: number, lineId: number): string {
    return `reserve:${orderId}:${lineId}`;
}

export function allocateKey(orderId: number, lineId: number): string {
    return `allocate:${orderId}:${lineId}`;
}

export function releaseKey(orderId: number, lineId: number, reservationId: number): string {
    return `release:${orderId}:${lineId}:${reservationId}`;
}

export function cancelKey(orderId: number): string {
    return `cancel-order:${orderId}`;
}

export function dispatchKey(orderId: number): string {
    return `dispatch:${orderId}`;
}

export function shipDirectTradeKey(orderId: number): string {
    return `ship-direct-trade:${orderId}`;
}

export function correctDtShipmentKey(orderId: number, shipmentId: number): string {
    return `correct-dt-shipment:${orderId}:${shipmentId}`;
}

export function confirmDeliveryKey(orderId: number, shipmentKind: 'Warehouse' | 'DirectTrade', shipmentId: number): string {
    return `confirm-delivery:${orderId}:${shipmentKind === 'Warehouse' ? 'warehouse' : 'direct-trade'}:${shipmentId}`;
}

export function retractDeliveryKey(orderId: number, shipmentKind: 'Warehouse' | 'DirectTrade', shipmentId: number, confirmationId: number): string {
    return `retract-delivery:${orderId}:${shipmentKind === 'Warehouse' ? 'warehouse' : 'direct-trade'}:${shipmentId}:${confirmationId}`;
}

export function correctDispatchKey(orderId: number, dispatchId: number): string {
    return `correct-dispatch:${orderId}:${dispatchId}`;
}

export function commandsForLine(commands: PendingCommands, orderId: number, lineId: number): PendingCommand[] {
    return Object.values(commands).filter((command) => isLineCommand(command) && command.orderId === orderId && command.lineId === lineId);
}

// Alle commando's op een order: regelcommando's én een annulering van de order zelf.
export function commandsForOrder(commands: PendingCommands, orderId: number): PendingCommand[] {
    return Object.values(commands).filter((command) => command.kind !== 'create-order' && command.kind !== 'create-customer' && command.orderId === orderId);
}

export type CommandScope =
    | { kind: 'create-order' }
    | { kind: 'create-customer' }
    | { kind: 'line'; orderId: number; lineId: number }
    | { kind: 'cancel-order'; orderId: number }
    | { kind: 'dispatch'; orderId: number }
    | { kind: 'ship-direct-trade'; orderId: number }
    | { kind: 'correct-dt-shipment'; orderId: number }
    | { kind: 'confirm-delivery'; orderId: number }
    | { kind: 'retract-delivery'; orderId: number }
    | { kind: 'correct-dispatch'; orderId: number };

// Een nieuw commando mag een onafgerond commando op dezelfde plaats nooit vervangen: eerst de uitkomst afwachten.
// Lezen van andere orders blijft altijd mogelijk. Een onafgeronde annulering of verzending (ordercommando's)
// blokkeert elke regel van die order en elkaar; een onafgerond regelcommando blokkeert annuleren en verzenden.
export function blockedBy(commands: PendingCommands, scope: CommandScope): PendingCommand | null {
    if (scope.kind === 'create-order') {
        return commands[CREATE_ORDER_KEY] ?? null;
    }
    if (scope.kind === 'create-customer') {
        return commands[CREATE_CUSTOMER_KEY] ?? null;
    }
    if (scope.kind === 'cancel-order' || scope.kind === 'dispatch' || scope.kind === 'ship-direct-trade' || scope.kind === 'correct-dt-shipment' || scope.kind === 'confirm-delivery' || scope.kind === 'retract-delivery' || scope.kind === 'correct-dispatch') {
        return commandsForOrder(commands, scope.orderId)[0] ?? null;
    }
    // Regelcommando's worden ook geblokkeerd door elk ordercommando (annuleren, verzenden, verzending corrigeren).
    return commandsForLine(commands, scope.orderId, scope.lineId)[0]
        ?? commandsForOrder(commands, scope.orderId).find((command) => !isLineCommand(command))
        ?? null;
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
        && (value.internalNote === undefined || isNullableText(value.internalNote)) // bevroren opdrachten van vóór de interne opmerking
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

// De bevestiging moet letterlijk true zijn: zonder bevestiging bestaat er geen geldig toewijzingscommando.
function isAllocatePayload(value: unknown, requestId: string): value is CreateDirectTradeAllocationRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isPositiveInt(value.supplyCommitmentLineId)
        && isPositiveAmount(value.quantity)
        && isUnit(value.unit)
        && value.acknowledgeExpectedSupply === true
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

function isCancelPayload(value: unknown, requestId: string): value is CancelSalesOrderRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isNonNegativeInt(value.expectedOrderVersion)
        && isText(value.actorReference)
        && isText(value.reason);
}

function isDispatchPayload(value: unknown, requestId: string): value is CreateSalesDispatchRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isNonNegativeInt(value.expectedOrderVersion)
        && isText(value.actorReference)
        && (value.reference === null || (typeof value.reference === 'string' && value.reference.length <= 100))
        && Array.isArray(value.lines)
        && value.lines.length > 0
        && value.lines.every((line: unknown) => isRecord(line) && isPositiveInt(line.stockReservationId) && isNonNegativeInt(line.expectedReservationVersion))
        && new Set(value.lines.map((line: { stockReservationId: number }) => line.stockReservationId)).size === value.lines.length;
}

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

// Kg strikt positief; partij verplicht; toelichting optioneel (de server eist haar alleen bij afwijking van een ingevulde bronpartij).
function isShipDirectTradePayload(value: unknown, requestId: string): value is CreateDirectTradeShipmentRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isNonNegativeInt(value.expectedOrderVersion)
        && isText(value.actorReference)
        && typeof value.shippedOn === 'string' && DATE_ONLY.test(value.shippedOn)
        && (value.reference === null || (typeof value.reference === 'string' && value.reference.length <= 100))
        && Array.isArray(value.lines)
        && value.lines.length > 0
        && value.lines.every((line: unknown) => isRecord(line)
            && isPositiveInt(line.stockReservationId) && isNonNegativeInt(line.expectedReservationVersion)
            && isPositiveAmount(line.quantity) && isUnit(line.unit) && isPositiveAmount(line.netWeightKg)
            && isText(line.lotNumber) && (line.lotNumber as string).length <= 100
            && (line.lotDeviationReason === null || isText(line.lotDeviationReason)))
        && new Set(value.lines.map((line: { stockReservationId: number }) => line.stockReservationId)).size === value.lines.length;
}

// De verklaring moet letterlijk true zijn; reden verplicht; volledige, unieke regelset met versies.
function isCorrectDtShipmentPayload(value: unknown, requestId: string): value is CorrectDirectTradeShipmentRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isNonNegativeInt(value.expectedOrderVersion)
        && isText(value.actorReference)
        && isText(value.reason) && (value.reason as string).length <= 500
        && value.shipmentDidNotTakePlace === true
        && Array.isArray(value.lines)
        && value.lines.length > 0
        && value.lines.every((line: unknown) => isRecord(line) && isPositiveInt(line.stockReservationId) && isNonNegativeInt(line.expectedReservationVersion))
        && new Set(value.lines.map((line: { stockReservationId: number }) => line.stockReservationId)).size === value.lines.length;
}

// De verklaring moet letterlijk true zijn; datum yyyy-MM-dd; referentie optioneel en begrensd.
function isConfirmDeliveryPayload(value: unknown, requestId: string): value is ConfirmDeliveryRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isNonNegativeInt(value.expectedOrderVersion)
        && isText(value.actorReference)
        && typeof value.deliveredOn === 'string' && DATE_ONLY.test(value.deliveredOn)
        && isBoundedNullableText(value.receiptReference, 100)
        && value.deliveredInFull === true;
}

// De verklaring moet letterlijk true zijn; reden verplicht en begrensd.
function isRetractDeliveryPayload(value: unknown, requestId: string): value is RetractDeliveryRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isNonNegativeInt(value.expectedOrderVersion)
        && isText(value.actorReference)
        && typeof value.reason === 'string' && value.reason.trim() !== '' && value.reason.length <= 500
        && value.recordedInError === true;
}

function isCorrectDispatchPayload(value: unknown, requestId: string): value is CorrectSalesDispatchRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isNonNegativeInt(value.expectedOrderVersion)
        && isText(value.actorReference)
        && isText(value.reason)
        && value.goodsNeverLeftWarehouse === true
        && Array.isArray(value.lines)
        && value.lines.length > 0
        && value.lines.every((line: unknown) => isRecord(line) && isPositiveInt(line.stockReservationId) && isNonNegativeInt(line.expectedReservationVersion))
        && new Set(value.lines.map((line: { stockReservationId: number }) => line.stockReservationId)).size === value.lines.length;
}

function isBoundedNullableText(value: unknown, max: number): value is string | null {
    return value === null || (typeof value === 'string' && value.trim() !== '' && value.length <= max);
}

function isCreateCustomerPayload(value: unknown, requestId: string): value is CreateCustomerRequest {
    return isRecord(value)
        && value.requestId === requestId
        && isText(value.name) && (value.name as string).length <= 200
        && isBoundedNullableText(value.contactPerson, 100)
        && isBoundedNullableText(value.email, 200)
        && isBoundedNullableText(value.phone, 40)
        && isBoundedNullableText(value.shippingAddress, 300)
        && typeof value.acknowledgeSimilarNames === 'boolean';
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

function isAllocateDisplay(value: unknown): value is AllocateDirectTradeCommand['display'] {
    return isRecord(value) && typeof value.sourceLabel === 'string' && typeof value.lotNumber === 'string' && typeof value.productLabel === 'string'
        && (value.expectedAt === null || isTimestamp(value.expectedAt));
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
        case 'allocate-direct-trade':
            return isPositiveInt(value.orderId) && isPositiveInt(value.lineId)
                && value.key === allocateKey(value.orderId, value.lineId)
                && isAllocatePayload(value.payload, value.requestId)
                && isAllocateDisplay(value.display);
        case 'release':
            return isPositiveInt(value.orderId) && isPositiveInt(value.lineId) && isPositiveInt(value.reservationId)
                && value.key === releaseKey(value.orderId, value.lineId, value.reservationId)
                && isReleasePayload(value.payload, value.requestId)
                && isLineDisplay(value.display);
        case 'cancel-order':
            return isPositiveInt(value.orderId)
                && value.key === cancelKey(value.orderId)
                && isCancelPayload(value.payload, value.requestId)
                && isRecord(value.display) && typeof value.display.customerName === 'string' && isNonNegativeInt(value.display.activeReservationCount);
        case 'dispatch':
            return isPositiveInt(value.orderId)
                && value.key === dispatchKey(value.orderId)
                && isDispatchPayload(value.payload, value.requestId)
                && isRecord(value.display) && typeof value.display.customerName === 'string' && isPositiveInt(value.display.lineCount);
        case 'ship-direct-trade':
            return isPositiveInt(value.orderId)
                && value.key === shipDirectTradeKey(value.orderId)
                && isShipDirectTradePayload(value.payload, value.requestId)
                && isRecord(value.display) && typeof value.display.customerName === 'string' && isPositiveInt(value.display.lineCount);
        case 'correct-dt-shipment':
            return isPositiveInt(value.orderId) && isPositiveInt(value.shipmentId)
                && value.key === correctDtShipmentKey(value.orderId, value.shipmentId)
                && isCorrectDtShipmentPayload(value.payload, value.requestId)
                && isRecord(value.display) && typeof value.display.customerName === 'string' && isPositiveInt(value.display.lineCount);
        case 'confirm-delivery':
            return isPositiveInt(value.orderId) && isPositiveInt(value.shipmentId)
                && (value.shipmentKind === 'Warehouse' || value.shipmentKind === 'DirectTrade')
                && value.key === confirmDeliveryKey(value.orderId, value.shipmentKind, value.shipmentId)
                && isConfirmDeliveryPayload(value.payload, value.requestId)
                && isRecord(value.display) && typeof value.display.customerName === 'string' && isPositiveInt(value.display.lineCount);
        case 'retract-delivery':
            return isPositiveInt(value.orderId) && isPositiveInt(value.shipmentId) && isPositiveInt(value.confirmationId)
                && (value.shipmentKind === 'Warehouse' || value.shipmentKind === 'DirectTrade')
                && value.key === retractDeliveryKey(value.orderId, value.shipmentKind, value.shipmentId, value.confirmationId)
                && isRetractDeliveryPayload(value.payload, value.requestId)
                && isRecord(value.display) && typeof value.display.customerName === 'string' && isPositiveInt(value.display.sequence);
        case 'create-customer':
            return value.key === CREATE_CUSTOMER_KEY
                && isCreateCustomerPayload(value.payload, value.requestId)
                && isRecord(value.display) && typeof value.display.name === 'string';
        case 'correct-dispatch':
            return isPositiveInt(value.orderId) && isPositiveInt(value.dispatchId)
                && value.key === correctDispatchKey(value.orderId, value.dispatchId)
                && isCorrectDispatchPayload(value.payload, value.requestId)
                && isRecord(value.display) && typeof value.display.customerName === 'string' && isPositiveInt(value.display.lineCount);
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
