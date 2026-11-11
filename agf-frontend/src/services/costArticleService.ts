import { ApiError, getJson, postJson, putJson } from './apiClient';
import type { SaveResult } from './supplyService';
import { type CostArticle, isCostArticle } from '../types/costArticle';

// /api/cost-articles: lijst (alleen actieve, of alle), zelf aanmaken (idempotent via requestId) en actief of inactief maken.
export async function listCostArticles(includeInactive: boolean, signal?: AbortSignal): Promise<CostArticle[]> {
    const json = await getJson(`/cost-articles${includeInactive ? '?includeInactive=true' : ''}`, signal);
    if (!Array.isArray(json) || !json.every(isCostArticle)) throw new ApiError('shape', 'Onverwacht antwoord bij de kostenartikelen.');
    return json;
}

export async function createCostArticle(request: { requestId: string; name: string }, signal?: AbortSignal): Promise<SaveResult<CostArticle>> {
    const { status, json } = await postJson('/cost-articles', request, signal);
    if (!isCostArticle(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het aanmaken van het kostenartikel.');
    return { value: json, replayed: status === 200 };
}

export async function setCostArticleActive(id: number, isActive: boolean, signal?: AbortSignal): Promise<CostArticle> {
    const { json } = await putJson(`/cost-articles/${id}/active`, { isActive }, signal);
    if (!isCostArticle(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het wijzigen van het kostenartikel.');
    return json;
}

// /api/packaging-articles: emballage-artikelen (stamgegevens). Naam alleen bij aanmaken; prijs (null = geen standaardprijs) en btw ook daarna.
export interface PackagingArticleInput {
    requestId?: string;
    name?: string;
    defaultUnitPrice: number | null;
    vatPercentage: number;
}

export async function listPackagingArticles(includeInactive: boolean, signal?: AbortSignal): Promise<CostArticle[]> {
    const json = await getJson(`/packaging-articles${includeInactive ? '?includeInactive=true' : ''}`, signal);
    if (!Array.isArray(json) || !json.every(isCostArticle)) throw new ApiError('shape', 'Onverwacht antwoord bij de emballage.');
    return json;
}

export async function createPackagingArticle(request: PackagingArticleInput, signal?: AbortSignal): Promise<SaveResult<CostArticle>> {
    const { status, json } = await postJson('/packaging-articles', request, signal);
    if (!isCostArticle(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het aanmaken van de emballage.');
    return { value: json, replayed: status === 200 };
}

export async function updatePackagingArticle(id: number, request: PackagingArticleInput, signal?: AbortSignal): Promise<CostArticle> {
    const { json } = await putJson(`/packaging-articles/${id}`, request, signal);
    if (!isCostArticle(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het wijzigen van de emballage.');
    return json;
}

export async function setPackagingArticleActive(id: number, isActive: boolean, signal?: AbortSignal): Promise<CostArticle> {
    const { json } = await putJson(`/packaging-articles/${id}/active`, { isActive }, signal);
    if (!isCostArticle(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het wijzigen van de emballage.');
    return json;
}
