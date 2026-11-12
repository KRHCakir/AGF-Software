import { ApiError, getJson, postJson, putJson } from './apiClient';
import type { SaveResult } from './supplyService';
import {
    CreatePurchaseInvoiceRequest, PurchaseInvoice, PurchaseInvoiceSettings, PurchaseVendor, UpdatePurchaseInvoiceSettingsRequest,
    isPurchaseInvoice, isPurchaseInvoiceList, isPurchaseInvoiceSettings, isPurchaseVendorList,
} from '../types/purchaseInvoice';
import { type ExchangeRate, type InvoiceAllocations, type SaveLineAllocationRequest, isExchangeRate, isInvoiceAllocations } from '../types/partijCost';

// Inkoopfacturen via api/purchase-invoices. Alleen administratief: nooit voorraad, pallets of kwaliteit.

export async function getPurchaseVendors(signal?: AbortSignal): Promise<PurchaseVendor[]> {
    const json = await getJson('/purchase-invoices/vendors', signal);
    if (!isPurchaseVendorList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij leveranciers.');
    return json;
}

export async function getPurchaseInvoiceSettings(signal?: AbortSignal): Promise<PurchaseInvoiceSettings> {
    const json = await getJson('/purchase-invoices/settings', signal);
    if (!isPurchaseInvoiceSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instellingen.');
    return json;
}

export async function updatePurchaseInvoiceSettings(request: UpdatePurchaseInvoiceSettingsRequest, signal?: AbortSignal): Promise<PurchaseInvoiceSettings> {
    const { json } = await putJson('/purchase-invoices/settings', request, signal);
    if (!isPurchaseInvoiceSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de instellingen.');
    return json;
}

// Met een zoekterm: factuurnummer, leverancier of het ordernummer van een gekoppelde order (stap 4b).
export async function listPurchaseInvoices(signal?: AbortSignal, query = ''): Promise<PurchaseInvoice[]> {
    const json = await getJson(query.trim() === '' ? '/purchase-invoices' : `/purchase-invoices?${new URLSearchParams({ query: query.trim() }).toString()}`, signal);
    if (!isPurchaseInvoiceList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de inkoopfacturen.');
    return json;
}

export async function getPurchaseInvoice(id: number, signal?: AbortSignal): Promise<PurchaseInvoice> {
    const json = await getJson(`/purchase-invoices/${id}`, signal);
    if (!isPurchaseInvoice(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de inkoopfactuur.');
    return json;
}

// 201 = nieuw, 200 = identieke herhaling (requestId); 409 bij een afwijkende herhaling.
export async function createPurchaseInvoice(request: CreatePurchaseInvoiceRequest, signal?: AbortSignal): Promise<SaveResult<PurchaseInvoice>> {
    const { status, json } = await postJson('/purchase-invoices', request, signal);
    if (!isPurchaseInvoice(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de inkoopfactuur.');
    return { value: json, replayed: status === 200 };
}

// Kosten verdelen over partijen: per regel de verdeling, de rest (algemene kosten) en de historie.
export async function getInvoiceAllocations(invoiceId: number, signal?: AbortSignal): Promise<InvoiceAllocations> {
    const json = await getJson(`/purchase-invoices/${invoiceId}/allocations`, signal);
    if (!isInvoiceAllocations(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de kostenverdeling.');
    return json;
}

// De ECB-koers van een factuur in een andere valuta opnieuw laten ophalen (de server doet dat, zolang er niets verdeeld is); geeft de
// verdeling terug. 503 = de ECB is nu niet bereikbaar; 400 = geen actuele koers voor deze valuta.
export async function refreshExchangeRate(invoiceId: number, signal?: AbortSignal): Promise<InvoiceAllocations> {
    const { json } = await postJson(`/purchase-invoices/${invoiceId}/exchange-rate`, {}, signal);
    if (!isInvoiceAllocations(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de koers.');
    return json;
}

// Alleen tonen in het invoerscherm: de ECB-koers voor een valuta en factuurdatum (slaat niets op).
export async function getExchangeRate(currency: string, invoiceDate: string | null, signal?: AbortSignal): Promise<ExchangeRate> {
    const query = new URLSearchParams({ currency });
    if (invoiceDate) query.set('date', invoiceDate);
    const json = await getJson(`/exchange-rates?${query.toString()}`, signal);
    if (!isExchangeRate(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de koers.');
    return json;
}

// De hele verdeling van één regel opslaan (idempotent via requestId; 409 bij een gelijktijdige wijziging).
export async function saveLineAllocation(invoiceId: number, lineId: number, request: SaveLineAllocationRequest, signal?: AbortSignal): Promise<InvoiceAllocations> {
    const { json } = await putJson(`/purchase-invoices/${invoiceId}/lines/${lineId}/allocations`, request, signal);
    if (!isInvoiceAllocations(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de kostenverdeling.');
    return json;
}
