import { ApiError, getJson } from './apiClient';
import { DeviationCandidate, DeviationPreview, isDeviationCandidateList, isDeviationPreview } from '../types/receiptDeviation';

// Alleen lezen: kiezen van het werkelijk ontvangen artikel en wat "Ja, klopt" zou doen. Boeken gaat via addPalletContentLine.
export async function listDeviationCandidates(lineId: number, signal?: AbortSignal): Promise<DeviationCandidate[]> {
    const json = await getJson(`/supply-commitment-lines/${lineId}/deviation-candidates`, signal);
    if (!isDeviationCandidateList(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de andere artikelen.');
    }
    return json;
}

// Met netto kg controleert de server ook het gewicht (kg per verpakking).
export async function getDeviationPreview(lineId: number, receivedProductVariantId: number, quantity: number, netWeightKg: number | null = null, signal?: AbortSignal): Promise<DeviationPreview> {
    const kg = netWeightKg === null ? '' : `&netWeightKg=${netWeightKg}`;
    const json = await getJson(`/supply-commitment-lines/${lineId}/deviation-preview?receivedProductVariantId=${receivedProductVariantId}&quantity=${quantity}${kg}`, signal);
    if (!isDeviationPreview(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de controle van de afwijking.');
    }
    return json;
}
