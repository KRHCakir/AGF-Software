import { ApiError, getJson } from './apiClient';
import { DeviationPreview, isDeviationPreview } from '../types/receiptDeviation';

// Alleen lezen: wat "Ja, klopt" zou doen voor de ontvangen variant, hoeveelheid en netto kg. Boeken gaat via addPalletContentLine.
export async function getDeviationPreview(lineId: number, receivedProductVariantId: number, quantity: number, netWeightKg: number | null, signal?: AbortSignal): Promise<DeviationPreview> {
    const kg = netWeightKg === null ? '' : `&netWeightKg=${netWeightKg}`;
    const json = await getJson(`/supply-commitment-lines/${lineId}/deviation-preview?receivedProductVariantId=${receivedProductVariantId}&quantity=${quantity}${kg}`, signal);
    if (!isDeviationPreview(json)) {
        throw new ApiError('shape', 'Onverwacht antwoord bij de controle van de afwijking.');
    }
    return json;
}
