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

export async function getDeviationPreview(lineId: number, receivedProductVariantId: number, quantity: number, signal?: AbortSignal): Promise<DeviationPreview> {
    const json = await getJson(`/supply-commitment-lines/${lineId}/deviation-preview?receivedProductVariantId=${receivedProductVariantId}&quantity=${quantity}`, signal);
    if (!isDeviationPreview(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de controle van de afwijking.');
    }
    return json;
}
