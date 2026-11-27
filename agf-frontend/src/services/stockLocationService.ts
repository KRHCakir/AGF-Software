import { ApiError, getJson, postJson, putJson } from './apiClient';
import { ReceiveDefaults, SaveStockLocation, StockLocation, isReceiveDefaults, isStockLocation, isStockLocationList } from '../types/stockLocation';

// Voorraadlocaties (feedback 22 november): lijst, toevoegen en aanpassen (nooit verwijderen: inactief zetten).

export async function listStockLocations(signal?: AbortSignal): Promise<StockLocation[]> {
    const json = await getJson('/stock-locations', signal);
    if (!isStockLocationList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de voorraadlocaties.');
    return json;
}

export async function saveStockLocation(id: number | null, request: SaveStockLocation, signal?: AbortSignal): Promise<StockLocation> {
    const { json } = id === null ? await postJson('/stock-locations', request, signal) : await putJson(`/stock-locations/${id}`, request, signal);
    if (!isStockLocation(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de voorraadlocatie.');
    return json;
}

// De standaard ontvangstlocatie en het adres van de eigen loods (november 2026).
export async function getReceiveDefaults(signal?: AbortSignal): Promise<ReceiveDefaults> {
    const json = await getJson('/stock-locations/receive-defaults', signal);
    if (!isReceiveDefaults(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de standaard ontvangstlocatie.');
    return json;
}

export async function saveReceiveDefaults(defaultReceiveLocationId: number | null, signal?: AbortSignal): Promise<ReceiveDefaults> {
    const { json } = await putJson('/stock-locations/receive-defaults', { defaultReceiveLocationId }, signal);
    if (!isReceiveDefaults(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de standaard ontvangstlocatie.');
    return json;
}
