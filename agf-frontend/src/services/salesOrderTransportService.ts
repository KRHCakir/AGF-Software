import { ApiError, deleteJson, getJson, putJson } from './apiClient';
import { type SalesOrderTransport, type SaveSalesOrderTransport, type TransportOrderSettings, isSalesOrderTransport, isTransportOrderSettings } from '../types/salesOrderTransport';

// /api/sales-orders/{id}/transport en /api/settings/transport-order (documenten deel 2: de transportopdracht).
const transport = (json: unknown): SalesOrderTransport => {
    if (!isSalesOrderTransport(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het transport van de verkooporder.');
    return json;
};

export async function getSalesOrderTransport(orderId: number, signal?: AbortSignal): Promise<SalesOrderTransport> {
    return transport(await getJson(`/sales-orders/${orderId}/transport`, signal));
}

export async function saveSalesOrderTransport(orderId: number, request: SaveSalesOrderTransport, signal?: AbortSignal): Promise<SalesOrderTransport> {
    return transport((await putJson(`/sales-orders/${orderId}/transport`, request, signal)).json);
}

export async function deleteSalesOrderTransport(orderId: number, signal?: AbortSignal): Promise<SalesOrderTransport> {
    return transport((await deleteJson(`/sales-orders/${orderId}/transport`, signal)).json);
}

export async function getTransportOrderSettings(signal?: AbortSignal): Promise<TransportOrderSettings> {
    const json = await getJson('/settings/transport-order', signal);
    if (!isTransportOrderSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instellingen van de transportopdracht.');
    return json;
}

export async function updateTransportOrderSettings(settings: TransportOrderSettings, signal?: AbortSignal): Promise<TransportOrderSettings> {
    const { json } = await putJson('/settings/transport-order', settings, signal);
    if (!isTransportOrderSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instellingen van de transportopdracht.');
    return json;
}
