import { ApiError, getJson, postJson } from './apiClient';
import type { SaveResult } from './supplyService';
import {
    PurchaseOrderDetail,
    PurchaseOrderInvoiceMatch,
    PurchaseOrderList,
    RejectRequest,
    isInvoiceMatchList,
    isPurchaseOrderDetail,
    isPurchaseOrderList,
} from '../types/purchaseOrder';

// /api/purchase-orders: lijst (server zoekt, filtert en pagineert), detail, opzoeken op factuurnummer en afkeuren.
// POST is idempotent via requestId: 201 = nieuw, 200 = exact hetzelfde verzoek was al opgeslagen.

export interface PurchaseOrderQuery {
    query: string;
    status: string;
    vendorId: number | null;
    page: number;
    pageSize: number;
}

export async function listPurchaseOrders(query: PurchaseOrderQuery, signal?: AbortSignal): Promise<PurchaseOrderList> {
    const params = new URLSearchParams({ status: query.status, page: String(query.page), pageSize: String(query.pageSize) });
    if (query.query.trim() !== '') params.set('query', query.query.trim());
    if (query.vendorId !== null) params.set('vendorId', String(query.vendorId));
    const json = await getJson(`/purchase-orders?${params.toString()}`, signal);
    if (!isPurchaseOrderList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de inkooporders.');
    return json;
}

export async function getPurchaseOrder(id: number, signal?: AbortSignal): Promise<PurchaseOrderDetail> {
    const json = await getJson(`/purchase-orders/${id}`, signal);
    if (!isPurchaseOrderDetail(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de inkooporder.');
    return json;
}

export async function findOrdersByInvoiceNumber(invoiceNumber: string, vendorId: number | null, signal?: AbortSignal): Promise<PurchaseOrderInvoiceMatch[]> {
    const params = new URLSearchParams({ invoiceNumber });
    if (vendorId !== null) params.set('vendorId', String(vendorId));
    const json = await getJson(`/purchase-orders/by-invoice-number?${params.toString()}`, signal);
    if (!isInvoiceMatchList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opzoeken van het factuurnummer.');
    return json;
}

export async function rejectReceipt(orderId: number, request: RejectRequest, signal?: AbortSignal): Promise<SaveResult<PurchaseOrderDetail>> {
    const { status, json } = await postJson(`/purchase-orders/${orderId}/rejections`, request, signal);
    if (!isPurchaseOrderDetail(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het afkeuren.');
    return { value: json, replayed: status === 200 };
}
