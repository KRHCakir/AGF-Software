import type { PackageUnit } from './availability';

// Verkopen vóór aankomst (SalesBalanceDtos en SalesBeforeArrivalDtos in de backend). Vrij te verkopen = Voorraad + Verwacht - Verkocht
// en mag negatief zijn: dat is een tekort, een waarschuwing en geen blokkade. Alles per exacte variant en eenheid; nooit omgerekend.

// Stock = uit voorraad; Expected = uit verwachte aanvoer (verkocht vóór aankomst, nog geen reservering mogelijk).
export type SupplySource = 'Stock' | 'Expected';
export const SUPPLY_SOURCES: readonly SupplySource[] = ['Stock', 'Expected'];

export interface SalesBalance {
    productVariantId: number;
    unit: PackageUnit;
    // Goedgekeurd in het eigen magazijn.
    stock: number;
    // Open inkoop (eigen magazijn): nog niet ontvangen + ontvangen maar in keuring. Klantgebonden aanvoer telt niet.
    expected: number;
    expectedNotReceived: number;
    expectedInInspection: number;
    // Open verkooporderregels (besteld min verzonden), waarvan gereserveerd.
    sold: number;
    soldReserved: number;
    // Voorraad + Verwacht - Verkocht; negatief = tekort.
    free: number;
    // Wat er nog uit voorraad verkocht kan worden (grens voor een regel "Uit voorraad").
    freeStock: number;
    shortageQuantity: number;
    shortage: boolean;
}

export interface ExpectedCover {
    supplyCommitmentId: number;
    supplyOrderNumber: string | null;
    expectedAt: string | null;
    quantity: number;
    inInspection: boolean;
}

// Dekking van het nog niet gereserveerde deel van een regel, oudste verkoop eerst.
export interface SalesLineCoverage {
    fromStock: number;
    expected: ExpectedCover[];
    shortage: number;
}

export interface SalesShortageWarning {
    productVariantId: number;
    productName: string | null;
    variantName: string | null;
    unit: PackageUnit;
    shortageQuantity: number;
}

export interface AvailabilitySalesLine {
    salesOrderId: number;
    orderNumber: string;
    customerId: number;
    customerName: string | null;
    customerReference: string | null;
    requestedDeliveryDate: string;
    salesOrderLineId: number;
    unit: PackageUnit;
    supplySource: SupplySource;
    openQuantity: number;
    reservedQuantity: number;
    coverage: SalesLineCoverage;
}

export interface AvailabilityExpectedSupply {
    supplyCommitmentId: number;
    supplyCommitmentLineId: number;
    supplyOrderNumber: string | null;
    expectedAt: string | null;
    lotNumber: string | null;
    unit: PackageUnit;
    notReceived: number;
    inInspection: number;
}

export interface FifoReservationItem {
    palletContentLineId: number;
    quantity: number;
    netWeightKg: number;
}

export interface AwaitingStockLine {
    salesOrderId: number;
    orderNumber: string;
    customerId: number;
    customerName: string | null;
    customerReference: string | null;
    requestedDeliveryDate: string;
    salesOrderLineId: number;
    productVariantId: number;
    productName: string | null;
    variantName: string | null;
    unit: PackageUnit;
    unreservedQuantity: number;
    reservableNow: number;
    coverage: SalesLineCoverage;
}

// ---- Type-guards: een onverwacht antwoord toont nooit stil verkeerde getallen ----

type JsonObject = Record<string, unknown>;
const isObject = (value: unknown): value is JsonObject => typeof value === 'object' && value !== null && !Array.isArray(value);
const isNumber = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value);
const isNullableString = (value: unknown) => value === null || typeof value === 'string';
const isString = (value: unknown): value is string => typeof value === 'string';

export function isSupplySource(value: unknown): value is SupplySource {
    return value === 'Stock' || value === 'Expected';
}

export function isSalesBalance(value: unknown): value is SalesBalance {
    return isObject(value)
        && isNumber(value.productVariantId)
        && isString(value.unit)
        && ['stock', 'expected', 'expectedNotReceived', 'expectedInInspection', 'sold', 'soldReserved', 'free', 'freeStock', 'shortageQuantity'].every((key) => isNumber(value[key]))
        && typeof value.shortage === 'boolean';
}

function isExpectedCover(value: unknown): value is ExpectedCover {
    return isObject(value)
        && isNumber(value.supplyCommitmentId)
        && isNullableString(value.supplyOrderNumber)
        && isNullableString(value.expectedAt)
        && isNumber(value.quantity)
        && typeof value.inInspection === 'boolean';
}

export function isSalesLineCoverage(value: unknown): value is SalesLineCoverage {
    return isObject(value) && isNumber(value.fromStock) && isNumber(value.shortage) && Array.isArray(value.expected) && value.expected.every(isExpectedCover);
}

export function isSalesShortageWarning(value: unknown): value is SalesShortageWarning {
    return isObject(value) && isNumber(value.productVariantId) && isString(value.unit) && isNumber(value.shortageQuantity);
}

export function isAvailabilitySalesLine(value: unknown): value is AvailabilitySalesLine {
    return isObject(value)
        && isNumber(value.salesOrderId)
        && isString(value.orderNumber)
        && isNumber(value.salesOrderLineId)
        && isSupplySource(value.supplySource)
        && isNumber(value.openQuantity)
        && isNumber(value.reservedQuantity)
        && isSalesLineCoverage(value.coverage);
}

export function isAvailabilityExpectedSupply(value: unknown): value is AvailabilityExpectedSupply {
    return isObject(value)
        && isNumber(value.supplyCommitmentId)
        && isNullableString(value.supplyOrderNumber)
        && isNullableString(value.expectedAt)
        && isNumber(value.notReceived)
        && isNumber(value.inInspection);
}

export function isAwaitingStockLine(value: unknown): value is AwaitingStockLine {
    return isObject(value)
        && isNumber(value.salesOrderId)
        && isString(value.orderNumber)
        && isNumber(value.salesOrderLineId)
        && isNumber(value.productVariantId)
        && isString(value.unit)
        && isNumber(value.unreservedQuantity)
        && isNumber(value.reservableNow)
        && isSalesLineCoverage(value.coverage);
}

export function isAwaitingStockList(value: unknown): value is AwaitingStockLine[] {
    return Array.isArray(value) && value.every(isAwaitingStockLine);
}
