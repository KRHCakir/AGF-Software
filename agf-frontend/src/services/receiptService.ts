import { ApiError, getJson, postJson } from './apiClient';
import { ReceiptOrder, ReceiveLineRequest, ReceiveLineResult, isReceiptOrder, isReceiveLineResult } from '../types/receipt';

// Ontvangen in één scherm: de lijst "Dit wordt verwacht" van een inkooporder, en één klik Ontvangen per regel (idempotent via requestId).

export async function getReceiptOrder(orderId: number, signal?: AbortSignal): Promise<ReceiptOrder> {
    const json = await getJson(`/receipts/orders/${orderId}`, signal);
    if (!isReceiptOrder(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de ontvangst.');
    return json;
}

export async function receiveLine(orderId: number, lineId: number, request: ReceiveLineRequest, signal?: AbortSignal): Promise<ReceiveLineResult> {
    const { json } = await postJson(`/receipts/orders/${orderId}/lines/${lineId}`, request, signal);
    if (!isReceiveLineResult(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het ontvangen.');
    return json;
}
