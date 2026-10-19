import { ApiError, getJson, postJson } from './apiClient';
import { getAvailabilityOverview } from './availabilityService';
import { SaveResult } from './supplyService';
import { AvailabilityQuantity, PackageUnit } from '../types/availability';
import {
    CreateSalesOrderRequest,
    CreateStockReservationRequest,
    ReleaseStockReservationRequest,
    ReservableStockLine,
    SalesOrder,
    StockReservation,
    isReservableStockList,
    isSalesOrder,
    isSalesOrderList,
    isStockReservation,
} from '../types/salesOrder';

// Getypeerde client voor de verkoopflow. Elke POST is idempotent via requestId: 201 = nieuw, 200 = exact dezelfde
// opdracht was al verwerkt. Een 409 betekent altijd: herladen en opnieuw beslissen, nooit stil opnieuw versturen.

export interface SalesOrderListFilter {
    customerId?: number;
    status?: string;
}

export async function listSalesOrders(filter: SalesOrderListFilter = {}, signal?: AbortSignal): Promise<SalesOrder[]> {
    const params = new URLSearchParams();
    if (filter.customerId !== undefined) params.set('customerId', String(filter.customerId));
    if (filter.status !== undefined) params.set('status', filter.status);
    const suffix = params.toString() === '' ? '' : `?${params.toString()}`;
    const json = await getJson(`/sales-orders${suffix}`, signal);
    if (!isSalesOrderList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de verkooporders.');
    }
    return json;
}

export async function getSalesOrder(id: number, signal?: AbortSignal): Promise<SalesOrder> {
    const json = await getJson(`/sales-orders/${id}`, signal);
    if (!isSalesOrder(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de verkooporder.');
    }
    return json;
}

export async function createSalesOrder(request: CreateSalesOrderRequest, signal?: AbortSignal): Promise<SaveResult<SalesOrder>> {
    const { status, json } = await postJson('/sales-orders', request, signal);
    if (!isSalesOrder(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het vastleggen van de verkooporder.');
    }
    return { value: json, replayed: status === 200 };
}

export async function getReservableStock(orderId: number, lineId: number, signal?: AbortSignal): Promise<ReservableStockLine[]> {
    const json = await getJson(`/sales-orders/${orderId}/lines/${lineId}/reservable-stock`, signal);
    if (!isReservableStockList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de reserveerbare voorraad.');
    }
    return json;
}

export async function reserveStock(orderId: number, lineId: number, request: CreateStockReservationRequest, signal?: AbortSignal): Promise<SaveResult<StockReservation>> {
    const { status, json } = await postJson(`/sales-orders/${orderId}/lines/${lineId}/reservations`, request, signal);
    if (!isStockReservation(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het reserveren.');
    }
    return { value: json, replayed: status === 200 };
}

// Vrijgeven geeft 200 bij succes én bij een exacte herhaling; de teruggegeven toestand is leidend.
export async function releaseReservation(reservationId: number, request: ReleaseStockReservationRequest, signal?: AbortSignal): Promise<StockReservation> {
    const { json } = await postJson(`/stock-reservations/${reservationId}/release`, request, signal);
    if (!isStockReservation(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het vrijgeven.');
    }
    return json;
}

export interface AvailabilitySnapshot {
    availableBeforeReservations: AvailabilityQuantity;
    reserved: AvailabilityQuantity;
    availableAfterReservations: AvailabilityQuantity;
    reservedOnHold: AvailabilityQuantity;
}

// Na een geslaagde schrijfactie: de actuele magazijnbeschikbaarheid voor variant + eenheid, rechtstreeks uit de API.
// null als er (nog) geen magazijnaanbod in die eenheid is.
export async function getAvailabilitySnapshot(productVariantId: number, unit: PackageUnit, signal?: AbortSignal): Promise<AvailabilitySnapshot | null> {
    const overview = await getAvailabilityOverview({ productVariantId, route: 'Warehouse', unit }, signal);
    const supply = overview.variants
        .filter((variant) => variant.productVariantId === productVariantId)
        .flatMap((variant) => variant.warehouseSupply)
        .find((entry) => entry.unit === unit);
    if (!supply) {
        return null;
    }
    return {
        availableBeforeReservations: supply.availableBeforeReservations,
        reserved: { quantity: supply.reserved.quantity, netWeightKg: supply.reserved.netWeightKg },
        availableAfterReservations: supply.availableAfterReservations,
        reservedOnHold: supply.reservedOnHold,
    };
}
