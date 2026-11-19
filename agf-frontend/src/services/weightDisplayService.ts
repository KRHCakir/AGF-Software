import { ApiError, getJson, putJson } from './apiClient';

// Gewichten tonen met 0 of 1 decimaal (instelling van het bedrijf; lezen mag iedereen, wijzigen met Beheer).
const read = (json: unknown): number => {
    const decimals = (json as { decimals?: unknown } | null)?.decimals;
    if (decimals !== 0 && decimals !== 1) throw new ApiError('shape', 'Onverwacht antwoord bij de gewichtsweergave.');
    return decimals;
};

export async function getWeightDecimals(signal?: AbortSignal): Promise<number> {
    return read(await getJson('/settings/weight-display', signal));
}

export async function updateWeightDecimals(decimals: number): Promise<number> {
    return read((await putJson('/settings/weight-display', { decimals })).json);
}
