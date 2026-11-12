import { ApiError, getBlob, getJson, postJson, putJson } from './apiClient';
import { type LoadingDetails, type LoadingSlipSettings, type SalesOrderDocuments, isLoadingSlipSettings, isSalesOrderDocuments } from '../types/salesOrderDocument';

// /api/sales-orders/{id}/confirm, /loading, /documents (documenten deel 1: bevestigen en de laadbon) en /api/settings/loading-slip.
const documents = (json: unknown): SalesOrderDocuments => {
    if (!isSalesOrderDocuments(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de documenten van de verkooporder.');
    return json;
};

export async function getSalesOrderDocuments(orderId: number, signal?: AbortSignal): Promise<SalesOrderDocuments> {
    return documents(await getJson(`/sales-orders/${orderId}/documents`, signal));
}

export async function confirmSalesOrder(orderId: number, details: LoadingDetails, signal?: AbortSignal): Promise<SalesOrderDocuments> {
    return documents((await postJson(`/sales-orders/${orderId}/confirm`, details, signal)).json);
}

export async function updateSalesOrderLoading(orderId: number, details: LoadingDetails, signal?: AbortSignal): Promise<SalesOrderDocuments> {
    return documents((await putJson(`/sales-orders/${orderId}/loading`, details, signal)).json);
}

export async function markDocumentSent(orderId: number, documentId: number, signal?: AbortSignal): Promise<SalesOrderDocuments> {
    return documents((await postJson(`/sales-orders/${orderId}/documents/${documentId}/sent`, {}, signal)).json);
}

// De PDF van één versie (voor Bekijken, Downloaden en Afdrukken).
export function getDocumentPdf(orderId: number, documentId: number, signal?: AbortSignal): Promise<Blob> {
    return getBlob(`/sales-orders/${orderId}/documents/${documentId}/pdf`, signal);
}

export async function getLoadingSlipSettings(signal?: AbortSignal): Promise<LoadingSlipSettings> {
    const json = await getJson('/settings/loading-slip', signal);
    if (!isLoadingSlipSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instellingen van de laadbon.');
    return json;
}

export async function updateLoadingSlipSettings(settings: LoadingSlipSettings, signal?: AbortSignal): Promise<LoadingSlipSettings> {
    const { json } = await putJson('/settings/loading-slip', settings, signal);
    if (!isLoadingSlipSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instellingen van de laadbon.');
    return json;
}
