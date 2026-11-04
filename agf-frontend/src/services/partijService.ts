import { ApiError, deleteJson, getJson, postJson, putJson } from './apiClient';
import type { SaveResult } from './supplyService';
import {
    PartijDetail,
    PartijList,
    PartijNextNumber,
    PartijSettings,
    PartijSummary,
    isPartijDetail,
    isPartijList,
    isPartijNextNumber,
    isPartijSettings,
    isPartijSummary,
} from '../types/partij';

// /api/partijen (lezen, aanmaken, dubbelcontrole, nummer corrigeren, verwijderen, een order koppelen) en de nummering bij de
// instellingen (/api/settings/partij-numbering). POST aanmaken is idempotent via requestId: 201 = nieuw, 200 = al opgeslagen.

export interface PartijQuery {
    query: string;
    status: 'all' | 'Open' | 'SoldOut';
    page: number;
    pageSize: number;
}

export async function listPartijen(query: PartijQuery, signal?: AbortSignal): Promise<PartijList> {
    const params = new URLSearchParams({ status: query.status, page: String(query.page), pageSize: String(query.pageSize) });
    if (query.query.trim() !== '') params.set('query', query.query.trim());
    const json = await getJson(`/partijen?${params.toString()}`, signal);
    if (!isPartijList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de partijen.');
    return json;
}

export async function getPartij(id: number, signal?: AbortSignal): Promise<PartijDetail> {
    const json = await getJson(`/partijen/${id}`, signal);
    if (!isPartijDetail(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de partij.');
    return json;
}

// Het volgende voorgestelde nummer; met format/reset een voorbeeld voor nog niet opgeslagen instellingen.
export async function getNextPartijNumber(preview?: { format: string; reset: string }, signal?: AbortSignal): Promise<PartijNextNumber> {
    const params = new URLSearchParams(preview ? { format: preview.format, reset: preview.reset } : {});
    const json = await getJson(`/partijen/next-number${preview ? `?${params.toString()}` : ''}`, signal);
    if (!isPartijNextNumber(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het volgende partijnummer.');
    return json;
}

// Dubbelcontrole op de sleutel (hoofdletters, spaties en streepjes tellen niet); null als die partij er niet is.
export async function findPartijByNumber(number: string, signal?: AbortSignal): Promise<PartijSummary | null> {
    const json = await getJson(`/partijen/by-number?${new URLSearchParams({ number }).toString()}`, signal);
    if (json === null) return null;
    if (!isPartijSummary(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het zoeken van de partij.');
    return json;
}

export async function createPartij(request: { requestId: string; number: string | null; note?: string | null }, signal?: AbortSignal): Promise<SaveResult<PartijSummary>> {
    const { status, json } = await postJson('/partijen', request, signal);
    if (!isPartijSummary(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het aanmaken van de partij.');
    return { value: json, replayed: status === 200 };
}

export async function correctPartijNumber(id: number, request: { number: string; reason: string }, signal?: AbortSignal): Promise<PartijSummary> {
    const { json } = await postJson(`/partijen/${id}/correct-number`, request, signal);
    if (!isPartijSummary(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het corrigeren van het nummer.');
    return json;
}

export async function deletePartij(id: number, signal?: AbortSignal): Promise<void> {
    await deleteJson(`/partijen/${id}`, signal);
}

// Een inkooporder (kop en regels) aan de partij koppelen; na ontvangst alleen een order zonder partij, met reden.
export async function linkPartijToOrder(partijId: number, request: { supplyCommitmentId: number; reason?: string | null }, signal?: AbortSignal): Promise<PartijSummary> {
    const { json } = await postJson(`/partijen/${partijId}/orders`, request, signal);
    if (!isPartijSummary(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het koppelen van de partij.');
    return json;
}

export async function getPartijNumbering(signal?: AbortSignal): Promise<PartijSettings> {
    const json = await getJson('/settings/partij-numbering', signal);
    if (!isPartijSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de partijnummering.');
    return json;
}

export async function updatePartijNumbering(settings: PartijSettings, signal?: AbortSignal): Promise<PartijSettings> {
    const { json } = await putJson('/settings/partij-numbering', settings, signal);
    if (!isPartijSettings(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de partijnummering.');
    return json;
}
