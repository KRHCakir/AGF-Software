import { putJson } from './apiClient';
import type { WeightMethod } from '../lib/weight';

export interface UpdateWeightSettingsRequest {
    weightMethod: WeightMethod;
    defaultNetKgPerPackage: number | null;
}

// PUT /api/products/variants/{id}/weight-settings: alleen de instellingen van de variant; raakt geen bestaande inhoudsregels.
export async function updateWeightSettings(variantId: number, request: UpdateWeightSettingsRequest, signal?: AbortSignal): Promise<void> {
    await putJson(`/products/variants/${variantId}/weight-settings`, request, signal);
}
