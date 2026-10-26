import { ApiError, deleteJson, getJson, postJson, putJson } from './apiClient';
import { PaymentCondition, PaymentConditionInUse, SavePaymentConditionRequest, isPaymentCondition, isPaymentConditionInUse, isPaymentConditionList } from '../types/paymentCondition';

// Betalingscondities (api/payment-conditions). POST is idempotent op requestId (201 nieuw, 200 identieke herhaling), PUT op
// expectedVersion (409 = intussen gewijzigd). Verwijderen van een gebruikte conditie is een gewone uitkomst (409 met tellingen):
// het scherm stelt dan inactief zetten voor.

export async function listPaymentConditions(includeInactive: boolean, signal?: AbortSignal): Promise<PaymentCondition[]> {
    const json = await getJson(`/payment-conditions?includeInactive=${String(includeInactive)}`, signal);
    if (!isPaymentConditionList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de betalingscondities.');
    return json;
}

export async function createPaymentCondition(request: SavePaymentConditionRequest, signal?: AbortSignal): Promise<{ condition: PaymentCondition; replayed: boolean }> {
    const { status, json } = await postJson('/payment-conditions', request, signal);
    if (!isPaymentCondition(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het aanmaken van de betalingsconditie.');
    return { condition: json, replayed: status === 200 };
}

export async function updatePaymentCondition(id: number, request: SavePaymentConditionRequest, signal?: AbortSignal): Promise<PaymentCondition> {
    const { json } = await putJson(`/payment-conditions/${id}`, request, signal);
    if (!isPaymentCondition(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de betalingsconditie.');
    return json;
}

export type DeletePaymentConditionOutcome = { outcome: 'deleted' } | { outcome: 'in-use'; inUse: PaymentConditionInUse };

export async function deletePaymentCondition(id: number, signal?: AbortSignal): Promise<DeletePaymentConditionOutcome> {
    try {
        await deleteJson(`/payment-conditions/${id}`, signal);
        return { outcome: 'deleted' };
    } catch (caught) {
        if (caught instanceof ApiError && caught.kind === 'http' && caught.status === 409 && isPaymentConditionInUse(caught.body)) return { outcome: 'in-use', inUse: caught.body };
        throw caught;
    }
}
