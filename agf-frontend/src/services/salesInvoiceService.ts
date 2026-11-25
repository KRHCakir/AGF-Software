import { ApiError, getBlob, getJson, postJson, putJson } from './apiClient';
import {
    isFinalizeResults, isRevenueTypes, isSalesInvoice, isSalesInvoiceList,
    type FinalizeResult, type RevenueType, type SalesInvoice, type SalesInvoiceList,
} from '../types/salesInvoice';

// Verkoopfactuur 1: /api/sales-invoices. Elke schrijfactie draagt de versie die de gebruiker zag (expectedVersion) of een requestId.
export interface SalesInvoiceQuery {
    query: string;
    status: string;
    page: number;
    pageSize: number;
    sort?: string;
    direction?: 'asc' | 'desc';
}

const shape = (what: string) => new ApiError('shape', `Onverwacht antwoord bij ${what}.`);

function invoiceOf(json: unknown): SalesInvoice {
    if (!isSalesInvoice(json)) throw shape('de verkoopfactuur');
    return json;
}

export async function listSalesInvoices(query: SalesInvoiceQuery, signal?: AbortSignal): Promise<SalesInvoiceList> {
    const params = new URLSearchParams({ status: query.status, page: String(query.page), pageSize: String(query.pageSize) });
    if (query.query.trim() !== '') params.set('query', query.query.trim());
    if (query.sort) { params.set('sort', query.sort); params.set('direction', query.direction ?? 'asc'); }
    const json = await getJson(`/sales-invoices?${params.toString()}`, signal);
    if (!isSalesInvoiceList(json)) throw shape('de verkoopfacturen');
    return json;
}

export async function getSalesInvoice(id: number, signal?: AbortSignal): Promise<SalesInvoice> {
    return invoiceOf(await getJson(`/sales-invoices/${id}`, signal));
}

export async function finalizeSalesInvoice(id: number, expectedVersion: number): Promise<SalesInvoice> {
    return invoiceOf((await postJson(`/sales-invoices/${id}/finalize`, { expectedVersion })).json);
}

export async function finalizeSalesInvoices(invoices: { id: number; expectedVersion: number }[]): Promise<FinalizeResult[]> {
    const { json } = await postJson('/sales-invoices/finalize', { invoices });
    if (!isFinalizeResults(json)) throw shape('Definitief maken');
    return json.results;
}

export async function discardSalesInvoice(id: number, expectedVersion: number, reason: string | null): Promise<SalesInvoice> {
    return invoiceOf((await postJson(`/sales-invoices/${id}/discard`, { expectedVersion, reason })).json);
}

export interface UpdateLine { id: number; quantity?: number; unitPrice?: number | null; discountPercentage?: number | null; vatPercentage?: number; remove?: boolean; description?: string; revenueTypeId?: number | null }
export interface NewLine { lineKind: 'Product' | 'Service'; productVariantId?: number | null; partijId?: number | null; costArticleId?: number | null; description?: string | null; quantity: number | null; unit?: string | null; unitPrice: number | null; discountPercentage: number | null; vatPercentage: number | null; revenueTypeId?: number | null }

export async function updateSalesInvoice(id: number, body: { expectedVersion: number; referenceNumber: string | null; deliveryDate?: string | null; lines: UpdateLine[]; newLines?: NewLine[] }): Promise<SalesInvoice> {
    return invoiceOf((await putJson(`/sales-invoices/${id}`, body)).json);
}

export async function createInvoiceFromDeliveries(requestId: string, deliveryIds: number[]): Promise<{ value: SalesInvoice; replayed: boolean }> {
    const { status, json } = await postJson('/sales-invoices/from-deliveries', { requestId, deliveryIds });
    return { value: invoiceOf(json), replayed: status === 200 };
}

export async function addInvoiceDeliveries(id: number, expectedVersion: number, deliveryIds: number[]): Promise<SalesInvoice> {
    return invoiceOf((await postJson(`/sales-invoices/${id}/deliveries`, { expectedVersion, deliveryIds })).json);
}

export async function createDirectInvoice(body: { requestId: string; customerId: number | null; deliveryDate: string | null; referenceNumber: string | null; lotNumber: string | null; lines: NewLine[] }): Promise<{ value: SalesInvoice; replayed: boolean }> {
    const { status, json } = await postJson('/sales-invoices', body);
    return { value: invoiceOf(json), replayed: status === 200 };
}

export async function getSalesInvoicePdf(id: number, download = false, signal?: AbortSignal): Promise<Blob> {
    return getBlob(`/sales-invoices/${id}/pdf${download ? '?download=true' : ''}`, signal);
}

export async function getRevenueTypes(includeInactive = false, signal?: AbortSignal): Promise<RevenueType[]> {
    const json = await getJson(`/revenue-types${includeInactive ? '?includeInactive=true' : ''}`, signal);
    if (!isRevenueTypes(json)) throw shape('de omzetsoorten');
    return json;
}
