import { ApiError, getJson, postJson } from './apiClient';
import { getAvailabilityOverview } from './availabilityService';
import { SaveResult } from './supplyService';
import { AvailabilityQuantity, PackageUnit } from '../types/availability';
import {
    CancelSalesOrderRequest,
    CorrectSalesDispatchRequest,
    CreateSalesDispatchRequest,
    CreateDirectTradeAllocationRequest,
    CreateDirectTradeShipmentRequest,
    DirectTradeShipment,
    CreateSalesOrderRequest,
    CreateStockReservationRequest,
    DirectTradeSupplyLine,
    ReleaseStockReservationRequest,
    ReservableStockLine,
    SalesDispatch,
    SalesDispatchCorrection,
    SalesOrder,
    SalesOrderAuditEvent,
    StockReservation,
    isDirectTradeShipment,
    isDirectTradeShipmentList,
    isDirectTradeSupplyList,
    isReservableStockList,
    isSalesDispatch,
    isSalesDispatchCorrection,
    isSalesDispatchList,
    isSalesOrder,
    isSalesOrderAuditEventList,
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

// Klantgebonden DirectTrade-aanvoer die aan deze regel kan worden toegewezen (zelfde klant, variant en eenheid; verwachte aanvoer).
export async function getDirectTradeSupply(orderId: number, lineId: number, signal?: AbortSignal): Promise<DirectTradeSupplyLine[]> {
    const json = await getJson(`/sales-orders/${orderId}/lines/${lineId}/direct-trade-supply`, signal);
    if (!isDirectTradeSupplyList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de klantgebonden aanvoer.');
    }
    return json;
}

// Toewijzen: 201 = nieuw, 200 = exact dezelfde opdracht was al verwerkt; 409 = herladen en opnieuw beslissen. Geen voorraadmutatie.
export async function allocateDirectTrade(orderId: number, lineId: number, request: CreateDirectTradeAllocationRequest, signal?: AbortSignal): Promise<SaveResult<StockReservation>> {
    const { status, json } = await postJson(`/sales-orders/${orderId}/lines/${lineId}/direct-trade-allocations`, request, signal);
    if (!isStockReservation(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het toewijzen.');
    }
    return { value: json, replayed: status === 200 };
}

// DirectTrade-verzending: 201 = nieuw, 200 = exact dezelfde opdracht was al verwerkt (nooit dubbel verzonden); 409 = herladen.
export async function createDirectTradeShipment(orderId: number, request: CreateDirectTradeShipmentRequest, signal?: AbortSignal): Promise<SaveResult<DirectTradeShipment>> {
    const { status, json } = await postJson(`/sales-orders/${orderId}/direct-trade-shipments`, request, signal);
    if (!isDirectTradeShipment(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de DirectTrade-verzending.');
    }
    return { value: json, replayed: status === 200 };
}

export async function listDirectTradeShipments(orderId: number, signal?: AbortSignal): Promise<DirectTradeShipment[]> {
    const json = await getJson(`/sales-orders/${orderId}/direct-trade-shipments`, signal);
    if (!isDirectTradeShipmentList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de DirectTrade-verzendingen.');
    }
    return json;
}

// Vrijgeven geeft 200 bij succes én bij een exacte herhaling; de teruggegeven toestand is leidend.
export async function releaseReservation(reservationId: number, request: ReleaseStockReservationRequest, signal?: AbortSignal): Promise<StockReservation> {
    const { json } = await postJson(`/stock-reservations/${reservationId}/release`, request, signal);
    if (!isStockReservation(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het vrijgeven.');
    }
    return json;
}

// Annuleren geeft 200 bij succes én bij een exacte herhaling; 409 bij een verouderde versie, een niet-Open order,
// afgeboekte reserveringen of een hergebruikte requestId met een andere payload.
export async function cancelSalesOrder(orderId: number, request: CancelSalesOrderRequest, signal?: AbortSignal): Promise<SalesOrder> {
    const { json } = await postJson(`/sales-orders/${orderId}/cancel`, request, signal);
    if (!isSalesOrder(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het annuleren van de verkooporder.');
    }
    return json;
}

export async function getSalesOrderAuditEvents(orderId: number, signal?: AbortSignal): Promise<SalesOrderAuditEvent[]> {
    const json = await getJson(`/sales-orders/${orderId}/audit-events`, signal);
    if (!isSalesOrderAuditEventList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de orderhistorie.');
    }
    return json;
}

// Verzenden: 201 = nieuw, 200 = exact dezelfde opdracht was al verwerkt; 409 = herladen en opnieuw beslissen.
export async function createSalesDispatch(orderId: number, request: CreateSalesDispatchRequest, signal?: AbortSignal): Promise<SaveResult<SalesDispatch>> {
    const { status, json } = await postJson(`/sales-orders/${orderId}/dispatches`, request, signal);
    if (!isSalesDispatch(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het verzenden.');
    }
    return { value: json, replayed: status === 200 };
}

// Administratieve correctie: 201 = nieuw, 200 = exact dezelfde opdracht was al verwerkt (nooit tweemaal teruggeboekt); 409 = herladen.
export async function correctSalesDispatch(orderId: number, dispatchId: number, request: CorrectSalesDispatchRequest, signal?: AbortSignal): Promise<SaveResult<SalesDispatchCorrection>> {
    const { status, json } = await postJson(`/sales-orders/${orderId}/dispatches/${dispatchId}/corrections`, request, signal);
    if (!isSalesDispatchCorrection(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij het corrigeren van de verzending.');
    }
    return { value: json, replayed: status === 200 };
}

export async function listSalesDispatches(orderId: number, signal?: AbortSignal): Promise<SalesDispatch[]> {
    const json = await getJson(`/sales-orders/${orderId}/dispatches`, signal);
    if (!isSalesDispatchList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de verzendingen.');
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
