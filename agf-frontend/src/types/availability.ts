// Types die exact overeenkomen met de alleen-lezen Availability API (backend: AgfAdmin/DTOs/AvailabilityDtos.cs).
// Getallen zijn decimals uit de backend; de frontend rekent er niet mee, behalve optellen voor weergave.

export type SupplyRoute = 'Warehouse' | 'DirectTrade';
export type PackageUnit = 'box' | 'colli' | 'tray' | 'piece' | 'dozen';
export type QualityStatus = 'Quarantine' | 'Approved' | 'Blocked' | 'Rejected';
export type LogisticsStatus = 'Expected' | 'InTransit' | 'Received' | 'Dispatched' | 'Delivered' | 'Cancelled';
export type LocationContext = 'Supplier' | 'Warehouse' | 'Crossdock' | 'InTransit' | 'Customer' | 'External';
export type AvailabilityCategory = 'warehouse' | 'receivedOutsideWarehouse' | 'inTransit' | 'expectedOnPallets' | 'closed';
export type AvailabilityWarningCode =
    | 'OVER_REGISTERED'
    | 'SOURCE_UNIT_UNKNOWN'
    | 'DIRECT_TRADE_IN_WAREHOUSE'
    | 'RESERVATION_ON_NON_APPROVED_CONTENT'
    | 'RESERVATION_EXCEEDS_CONTENT'
    | 'RESERVATION_OUTSIDE_WAREHOUSE';

export const PACKAGE_UNITS: readonly PackageUnit[] = ['box', 'colli', 'tray', 'piece', 'dozen'];

export interface AvailabilityQuantity {
    quantity: number;
    netWeightKg: number;
}

export interface QualityBreakdown {
    approved: AvailabilityQuantity;
    quarantine: AvailabilityQuantity;
    blocked: AvailabilityQuantity;
    rejected: AvailabilityQuantity;
}

// Verwacht maar nog niet op een pallet: nooit een gewicht (zou een schatting zijn).
export interface ExpectedUnregistered {
    quantity: number;
    netWeightKg: null;
}

// Active Physical verkoopreserveringen op inhoud die nu Approved is en Received + Warehouse.
export interface Reserved {
    quantity: number;
    netWeightKg: number;
    applied: true;
}

export interface AvailabilityWarning {
    code: AvailabilityWarningCode;
    supplyCommitmentLineId: number | null;
    palletContentLineId: number | null;
    quantity: number | null;
}

export interface AvailabilityBasis {
    source: string;
    reservationsApplied: boolean;
    legacyInventoryIncluded: boolean;
    stockLedgerIncluded: boolean;
}

export interface WarehouseSupply {
    unit: PackageUnit | null;
    availableBeforeReservations: AvailabilityQuantity;
    reserved: Reserved;
    // Gereserveerd op inhoud die niet meer Approved is; wordt nooit nogmaals afgetrokken.
    reservedOnHold: AvailabilityQuantity;
    // availableBeforeReservations - reserved, per eenheid; nooit negatief.
    availableAfterReservations: AvailabilityQuantity;
    warehouse: QualityBreakdown;
    receivedOutsideWarehouse: QualityBreakdown;
    inTransit: QualityBreakdown;
    expectedOnPallets: QualityBreakdown;
    expectedUnregistered: ExpectedUnregistered;
    warnings: AvailabilityWarning[];
}

// Klantgebonden aanvoer: bewust zonder availableBeforeReservations, warehouse of reserved.
export interface DirectTradeSupply {
    customerId: number | null;
    customerName: string | null;
    unit: PackageUnit | null;
    customerBound: true;
    receivedOutsideWarehouse: QualityBreakdown;
    inTransit: QualityBreakdown;
    expectedOnPallets: QualityBreakdown;
    expectedUnregistered: ExpectedUnregistered;
    warnings: AvailabilityWarning[];
}

export interface VariantAvailability {
    productVariantId: number;
    productName: string;
    variantName: string;
    warehouseSupply: WarehouseSupply[];
    directTradeSupply: DirectTradeSupply[];
}

export interface AvailabilityOverview {
    generatedAt: string;
    basis: AvailabilityBasis;
    variants: VariantAvailability[];
}

export interface SourceLineAvailability {
    supplyCommitmentLineId: number;
    supplyCommitmentId: number;
    route: SupplyRoute;
    customerId: number | null;
    customerName: string | null;
    commitmentStatus: string;
    expectedAt: string | null;
    unit: PackageUnit | null;
    lotNumber: string | null;
    expectedQuantity: number;
    declaredExpectedNetWeightKg: number | null;
    registeredQuantity: number | null;
    unregisteredQuantity: number;
    warnings: AvailabilityWarning[];
}

export interface ContentLineAvailability {
    palletContentLineId: number;
    palletId: number;
    palletReference: string;
    category: AvailabilityCategory;
    logisticsStatus: LogisticsStatus;
    locationContext: LocationContext;
    warehouseLocation: string | null;
    route: SupplyRoute;
    customerId: number | null;
    customerName: string | null;
    supplyCommitmentLineId: number;
    lotNumber: string;
    unit: PackageUnit;
    // Historische ontvangst; voorraad = ontvangst - afgeboekt door verzendingen.
    quantity: number;
    netWeightKg: number;
    dispatchedQuantity: number;
    dispatchedNetWeightKg: number;
    onHandQuantity: number;
    onHandNetWeightKg: number;
    // Voorraad én kg zijn 0; zegt niets over de fysieke pallet.
    depleted: boolean;
    qualityStatus: QualityStatus;
    reservedQuantity: number;
    reservedNetWeightKg: number;
    onHold: boolean;
    availableQuantity: number;
    availableNetWeightKg: number;
    sellableNow: boolean;
    closed: boolean;
}

export interface AvailabilityDrillDown {
    generatedAt: string;
    basis: AvailabilityBasis;
    productVariantId: number;
    productName: string;
    variantName: string;
    sourceLines: SourceLineAvailability[];
    contentLines: ContentLineAvailability[];
}

// ---- Type-guards: controleren de vorm van het antwoord, zodat een onverwacht antwoord nooit stil verkeerde getallen toont ----

type JsonObject = Record<string, unknown>;

function isObject(value: unknown): value is JsonObject {
    return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNumber(value: unknown): value is number {
    return typeof value === 'number' && Number.isFinite(value);
}

function isNullableNumber(value: unknown): boolean {
    return value === null || isNumber(value);
}

function isNullableString(value: unknown): boolean {
    return value === null || typeof value === 'string';
}

function isUnit(value: unknown): value is PackageUnit {
    return typeof value === 'string' && (PACKAGE_UNITS as readonly string[]).includes(value);
}

function isNullableUnit(value: unknown): boolean {
    return value === null || isUnit(value);
}

function isQuantity(value: unknown): value is AvailabilityQuantity {
    return isObject(value) && isNumber(value.quantity) && isNumber(value.netWeightKg);
}

function isBreakdown(value: unknown): value is QualityBreakdown {
    return isObject(value)
        && isQuantity(value.approved)
        && isQuantity(value.quarantine)
        && isQuantity(value.blocked)
        && isQuantity(value.rejected);
}

function isExpectedUnregistered(value: unknown): value is ExpectedUnregistered {
    return isObject(value) && isNumber(value.quantity) && value.netWeightKg === null;
}

function isWarning(value: unknown): value is AvailabilityWarning {
    return isObject(value)
        && typeof value.code === 'string'
        && isNullableNumber(value.supplyCommitmentLineId)
        && isNullableNumber(value.palletContentLineId)
        && isNullableNumber(value.quantity);
}

function isArrayOf<T>(value: unknown, guard: (item: unknown) => item is T): value is T[] {
    return Array.isArray(value) && value.every(guard);
}

function isBasis(value: unknown): value is AvailabilityBasis {
    return isObject(value)
        && typeof value.source === 'string'
        && typeof value.reservationsApplied === 'boolean'
        && typeof value.legacyInventoryIncluded === 'boolean'
        && typeof value.stockLedgerIncluded === 'boolean';
}

function isWarehouseSupply(value: unknown): value is WarehouseSupply {
    return isObject(value)
        && isNullableUnit(value.unit)
        && isQuantity(value.availableBeforeReservations)
        && isObject(value.reserved)
        && isNumber(value.reserved.quantity)
        && isNumber(value.reserved.netWeightKg)
        && value.reserved.applied === true
        && isQuantity(value.reservedOnHold)
        && isQuantity(value.availableAfterReservations)
        && isBreakdown(value.warehouse)
        && isBreakdown(value.receivedOutsideWarehouse)
        && isBreakdown(value.inTransit)
        && isBreakdown(value.expectedOnPallets)
        && isExpectedUnregistered(value.expectedUnregistered)
        && isArrayOf(value.warnings, isWarning);
}

function isDirectTradeSupply(value: unknown): value is DirectTradeSupply {
    return isObject(value)
        && isNullableNumber(value.customerId)
        && isNullableString(value.customerName)
        && isNullableUnit(value.unit)
        && value.customerBound === true
        && !('availableBeforeReservations' in value)
        && isBreakdown(value.receivedOutsideWarehouse)
        && isBreakdown(value.inTransit)
        && isBreakdown(value.expectedOnPallets)
        && isExpectedUnregistered(value.expectedUnregistered)
        && isArrayOf(value.warnings, isWarning);
}

function isVariant(value: unknown): value is VariantAvailability {
    return isObject(value)
        && isNumber(value.productVariantId)
        && typeof value.productName === 'string'
        && typeof value.variantName === 'string'
        && isArrayOf(value.warehouseSupply, isWarehouseSupply)
        && isArrayOf(value.directTradeSupply, isDirectTradeSupply);
}

export function isAvailabilityOverview(value: unknown): value is AvailabilityOverview {
    return isObject(value)
        && typeof value.generatedAt === 'string'
        && isBasis(value.basis)
        && isArrayOf(value.variants, isVariant);
}

function isSourceLine(value: unknown): value is SourceLineAvailability {
    return isObject(value)
        && isNumber(value.supplyCommitmentLineId)
        && isNumber(value.supplyCommitmentId)
        && typeof value.route === 'string'
        && isNullableNumber(value.customerId)
        && isNullableString(value.customerName)
        && typeof value.commitmentStatus === 'string'
        && isNullableString(value.expectedAt)
        && isNullableUnit(value.unit)
        && isNullableString(value.lotNumber)
        && isNumber(value.expectedQuantity)
        && isNullableNumber(value.declaredExpectedNetWeightKg)
        && isNullableNumber(value.registeredQuantity)
        && isNumber(value.unregisteredQuantity)
        && isArrayOf(value.warnings, isWarning);
}

function isContentLine(value: unknown): value is ContentLineAvailability {
    return isObject(value)
        && isNumber(value.palletContentLineId)
        && isNumber(value.palletId)
        && typeof value.palletReference === 'string'
        && typeof value.category === 'string'
        && typeof value.logisticsStatus === 'string'
        && typeof value.locationContext === 'string'
        && isNullableString(value.warehouseLocation)
        && typeof value.route === 'string'
        && isNullableNumber(value.customerId)
        && isNullableString(value.customerName)
        && isNumber(value.supplyCommitmentLineId)
        && typeof value.lotNumber === 'string'
        && isUnit(value.unit)
        && isNumber(value.quantity)
        && isNumber(value.netWeightKg)
        && isNumber(value.dispatchedQuantity)
        && isNumber(value.dispatchedNetWeightKg)
        && isNumber(value.onHandQuantity)
        && isNumber(value.onHandNetWeightKg)
        && typeof value.depleted === 'boolean'
        && typeof value.qualityStatus === 'string'
        && isNumber(value.reservedQuantity)
        && isNumber(value.reservedNetWeightKg)
        && typeof value.onHold === 'boolean'
        && isNumber(value.availableQuantity)
        && isNumber(value.availableNetWeightKg)
        && typeof value.sellableNow === 'boolean'
        && typeof value.closed === 'boolean';
}

export function isAvailabilityDrillDown(value: unknown): value is AvailabilityDrillDown {
    return isObject(value)
        && typeof value.generatedAt === 'string'
        && isBasis(value.basis)
        && isNumber(value.productVariantId)
        && typeof value.productName === 'string'
        && typeof value.variantName === 'string'
        && isArrayOf(value.sourceLines, isSourceLine)
        && isArrayOf(value.contentLines, isContentLine);
}
