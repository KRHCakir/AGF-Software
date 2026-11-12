import { ApiError, deleteJson, getJson, putJson } from './apiClient';
import { type SalesOrderTransport, type SaveSalesOrderTransport, isSalesOrderTransport } from '../types/salesOrderTransport';

// /api/sales-orders/{id}/transport (documenten deel 2: de transportopdracht).
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

// Met de versie die de gebruiker zag: een gelijktijdige wijziging gaat niet verloren (409).
export async function deleteSalesOrderTransport(orderId: number, expectedVersion: number, signal?: AbortSignal): Promise<SalesOrderTransport> {
    return transport((await deleteJson(`/sales-orders/${orderId}/transport?expectedVersion=${expectedVersion}`, signal)).json);
}
