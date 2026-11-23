import { ApiError, getJson, postJson, putJson } from './apiClient';
import { SaveStockLocation, StockLocation, isStockLocation, isStockLocationList } from '../types/stockLocation';

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
