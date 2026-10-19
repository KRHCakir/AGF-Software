import { PACKAGE_UNITS, PackageUnit, QualityStatus } from './availability';
import { isArrayOf, isNullableString, isNumber, isObject, isString } from './guards';
import { isQualityStatus } from './inspection';

// Exact het contract van /api/sales-orders en /api/stock-reservations (backend: AgfAdmin/DTOs/SalesOrderDtos.cs).
// Getallen zijn decimals uit de backend; de frontend rekent er niet mee, behalve vergelijken en aftrekken voor weergave.

export type ReservationStatus = 'Active' | 'Released' | 'Consumed';

export const RESERVATION_STATUSES: readonly ReservationStatus[] = ['Active', 'Released', 'Consumed'];

export interface CreateSalesOrderLineRequest {
    productVariantId: number;
    quantity: number;
    unit: PackageUnit;
}

export interface CreateSalesOrderRequest {
    requestId: string;
    customerId: number;
    customerReference: string | null;
    requestedDeliveryDate: string;
    actorReference: string;
    lines: CreateSalesOrderLineRequest[];
}

// Hoeveelheid, eenheid en netto-kg zijn altijd expliciet; de UI leidt nooit kg af uit dozen of maat.
export interface CreateStockReservationRequest {
    requestId: string;
    palletContentLineId: number;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    actorReference: string;
    reason: string | null;
}

export interface ReleaseStockReservationRequest {
    requestId: string;
    expectedReservationVersion: number;
    actorReference: string;
    reason: string;
}

export interface StockReservation {
    id: number;
    requestId: string | null;
    salesOrderId: number;
    salesOrderLineId: number;
    palletContentLineId: number;
    palletId: number;
    palletReference: string | null;
    warehouseLocation: string | null;
    lotNumber: string | null;
    quantity: number;
    unit: PackageUnit;
    netWeightKg: number;
    source: string;
    status: ReservationStatus;
    createdAt: string;
    releasedAt: string | null;
    releaseReason: string | null;
    version: number;
    // Actuele kwaliteit van de inhoudsregel, rechtstreeks uit de server; nooit afgeleid uit de reserveringsstatus.
    contentQualityStatus: QualityStatus | null;
    // Active op inhoud die niet (meer) Approved is: wacht op vrijgave of herkeuring.
    onHold: boolean;
}

export interface SalesOrderLine {
    id: number;
    productVariantId: number;
    productName: string | null;
    productVariantName: string | null;
    quantity: number;
    unit: PackageUnit;
    reservedQuantity: number;
    reservedNetWeightKg: number;
    openQuantity: number;
    version: number;
    reservations: StockReservation[];
}

export interface SalesOrder {
    id: number;
    requestId: string;
    customerId: number;
    customerName: string | null;
    status: string;
    customerReference: string | null;
    requestedDeliveryDate: string;
    actorReference: string;
    createdAt: string;
    version: number;
    lines: SalesOrderLine[];
}

// Inhoudsregel die voor een orderregel te reserveren is: zelfde variant + eenheid, Approved, Received + Warehouse, route Warehouse.
export interface ReservableStockLine {
    palletContentLineId: number;
    palletId: number;
    palletReference: string;
    warehouseLocation: string | null;
    lotNumber: string;
    unit: PackageUnit;
    quantity: number;
    netWeightKg: number;
    reservedQuantity: number;
    reservedNetWeightKg: number;
    availableQuantity: number;
    availableNetWeightKg: number;
    qualityStatus: QualityStatus;
    contentLineVersion: number;
}

function isUnit(value: unknown): value is PackageUnit {
    return isString(value) && (PACKAGE_UNITS as readonly string[]).includes(value);
}

function isReservationStatus(value: unknown): value is ReservationStatus {
    return isString(value) && (RESERVATION_STATUSES as readonly string[]).includes(value);
}

export function isStockReservation(value: unknown): value is StockReservation {
    return isObject(value)
        && isNumber(value.id)
        && isNullableString(value.requestId)
        && isNumber(value.salesOrderId)
        && isNumber(value.salesOrderLineId)
        && isNumber(value.palletContentLineId)
        && isNumber(value.palletId)
        && isNullableString(value.palletReference)
        && isNullableString(value.warehouseLocation)
        && isNullableString(value.lotNumber)
        && isNumber(value.quantity)
        && isUnit(value.unit)
        && isNumber(value.netWeightKg)
        && isString(value.source)
        && isReservationStatus(value.status)
        && isString(value.createdAt)
        && isNullableString(value.releasedAt)
        && isNullableString(value.releaseReason)
        && isNumber(value.version)
        && (value.contentQualityStatus === null || isQualityStatus(value.contentQualityStatus))
        && typeof value.onHold === 'boolean';
}

function isSalesOrderLine(value: unknown): value is SalesOrderLine {
    return isObject(value)
        && isNumber(value.id)
        && isNumber(value.productVariantId)
        && isNullableString(value.productName)
        && isNullableString(value.productVariantName)
        && isNumber(value.quantity)
        && isUnit(value.unit)
        && isNumber(value.reservedQuantity)
        && isNumber(value.reservedNetWeightKg)
        && isNumber(value.openQuantity)
        && isNumber(value.version)
        && isArrayOf(value.reservations, isStockReservation);
}

export function isSalesOrder(value: unknown): value is SalesOrder {
    return isObject(value)
        && isNumber(value.id)
        && isString(value.requestId)
        && isNumber(value.customerId)
        && isNullableString(value.customerName)
        && isString(value.status)
        && isNullableString(value.customerReference)
        && isString(value.requestedDeliveryDate)
        && isString(value.actorReference)
        && isString(value.createdAt)
        && isNumber(value.version)
        && isArrayOf(value.lines, isSalesOrderLine);
}

export function isSalesOrderList(value: unknown): value is SalesOrder[] {
    return isArrayOf(value, isSalesOrder);
}

export function isReservableStockLine(value: unknown): value is ReservableStockLine {
    return isObject(value)
        && isNumber(value.palletContentLineId)
        && isNumber(value.palletId)
        && isString(value.palletReference)
        && isNullableString(value.warehouseLocation)
        && isString(value.lotNumber)
        && isUnit(value.unit)
        && isNumber(value.quantity)
        && isNumber(value.netWeightKg)
        && isNumber(value.reservedQuantity)
        && isNumber(value.reservedNetWeightKg)
        && isNumber(value.availableQuantity)
        && isNumber(value.availableNetWeightKg)
        && isQualityStatus(value.qualityStatus)
        && isNumber(value.contentLineVersion);
}

export function isReservableStockList(value: unknown): value is ReservableStockLine[] {
    return isArrayOf(value, isReservableStockLine);
}
