import { ApiError, getJson, postJson, putJson } from './apiClient';
import type { SaveResult } from './supplyService';
import {
    CreatePurchaseInvoiceRequest, PurchaseInvoice, PurchaseInvoiceSettings, PurchaseVendor, UpdatePurchaseInvoiceSettingsRequest,
    isPurchaseInvoice, isPurchaseInvoiceList, isPurchaseInvoiceSettings, isPurchaseVendorList,
} from '../types/purchaseInvoice';
import { type InvoiceAllocations, type SaveLineAllocationRequest, isInvoiceAllocations } from '../types/partijCost';

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

export async function listPurchaseInvoices(signal?: AbortSignal): Promise<PurchaseInvoice[]> {
    const json = await getJson('/purchase-invoices', signal);
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

// De hele verdeling van één regel opslaan (idempotent via requestId; 409 bij een gelijktijdige wijziging).
export async function saveLineAllocation(invoiceId: number, lineId: number, request: SaveLineAllocationRequest, signal?: AbortSignal): Promise<InvoiceAllocations> {
    const { json } = await putJson(`/purchase-invoices/${invoiceId}/lines/${lineId}/allocations`, request, signal);
    if (!isInvoiceAllocations(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de kostenverdeling.');
    return json;
}
