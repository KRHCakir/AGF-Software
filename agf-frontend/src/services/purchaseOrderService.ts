import { ApiError, getJson, postJson, putJson } from './apiClient';
import type { SaveResult } from './supplyService';
import {
    AddInvoiceRefRequest,
    CorrectPurchaseOrderRequest,
    FinalizePurchaseOrderRequest,
    UpdatePurchaseOrderRequest,
    PurchaseOrderDetail,
    PurchaseOrderInvoiceMatch,
    PurchaseOrderList,
    PurchaseOrderSupplierReferenceMatch,
    RejectRequest,
    isInvoiceMatchList,
    isPurchaseOrderDetail,
    isPurchaseOrderList,
    isSupplierReferenceMatchList,
} from '../types/purchaseOrder';

// /api/purchase-orders: lijst (server zoekt, filtert en pagineert), detail, opzoeken op factuurnummer,
// factuurnummer koppelen en afkeuren.
// POST is idempotent via requestId: 201 = nieuw, 200 = exact hetzelfde verzoek was al opgeslagen.

export interface PurchaseOrderQuery {
    query: string;
    status: string;
    vendorId: number | null;
    page: number;
    pageSize: number;
    // Filter "Ordernummer leverancier" (deel van het nummer, zonder spaties en streepjes vergeleken).
    supplierReference?: string;
    // Documentstatus: leeg = alle, anders Open of Definitive.
    document?: string;
}

export async function listPurchaseOrders(query: PurchaseOrderQuery, signal?: AbortSignal): Promise<PurchaseOrderList> {
    const params = new URLSearchParams({ status: query.status, page: String(query.page), pageSize: String(query.pageSize) });
    if (query.query.trim() !== '') params.set('query', query.query.trim());
    if (query.vendorId !== null) params.set('vendorId', String(query.vendorId));
    if ((query.supplierReference ?? '').trim() !== '') params.set('supplierReference', query.supplierReference!.trim());
    if ((query.document ?? '') !== '') params.set('document', query.document!);
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

// Orders van deze leverancier met hetzelfde ordernummer van de leverancier (op de sleutel). Alleen een melding, nooit een blokkade.
export async function findOrdersBySupplierReference(supplierReference: string, vendorId: number, signal?: AbortSignal): Promise<PurchaseOrderSupplierReferenceMatch[]> {
    const params = new URLSearchParams({ supplierReference, vendorId: String(vendorId) });
    const json = await getJson(`/purchase-orders/by-supplier-reference?${params.toString()}`, signal);
    if (!isSupplierReferenceMatchList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opzoeken van het ordernummer van de leverancier.');
    return json;
}

export async function addInvoiceRef(orderId: number, request: AddInvoiceRefRequest, signal?: AbortSignal): Promise<SaveResult<PurchaseOrderDetail>> {
    const { status, json } = await postJson(`/purchase-orders/${orderId}/invoice-refs`, request, signal);
    if (!isPurchaseOrderDetail(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het vastleggen van het factuurnummer.');
    return { value: json, replayed: status === 200 };
}

export async function rejectReceipt(orderId: number, request: RejectRequest, signal?: AbortSignal): Promise<SaveResult<PurchaseOrderDetail>> {
    const { status, json } = await postJson(`/purchase-orders/${orderId}/rejections`, request, signal);
    if (!isPurchaseOrderDetail(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het afkeuren.');
    return { value: json, replayed: status === 200 };
}

// Aanpassen zolang de order Open is (stap 4a). 200 met de order; 409 met code (stale, definitive, notEditable) en 400 met veldfouten.
export async function updatePurchaseOrder(orderId: number, request: UpdatePurchaseOrderRequest, signal?: AbortSignal): Promise<PurchaseOrderDetail> {
    const { json } = await putJson(`/purchase-orders/${orderId}`, request, signal);
    if (!isPurchaseOrderDetail(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het aanpassen van de inkooporder.');
    return json;
}

export async function finalizePurchaseOrder(orderId: number, request: FinalizePurchaseOrderRequest, signal?: AbortSignal): Promise<PurchaseOrderDetail> {
    const { json } = await postJson(`/purchase-orders/${orderId}/finalize`, request, signal);
    if (!isPurchaseOrderDetail(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het definitief maken van de inkooporder.');
    return json;
}

export async function correctPurchaseOrder(orderId: number, request: CorrectPurchaseOrderRequest, signal?: AbortSignal): Promise<PurchaseOrderDetail> {
    const { json } = await postJson(`/purchase-orders/${orderId}/corrections`, request, signal);
    if (!isPurchaseOrderDetail(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de correctie van de inkooporder.');
    return json;
}
