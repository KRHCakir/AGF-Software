import { ApiError, deleteJson, getJson, postJson } from './apiClient';
import { isArticle, type Article } from '../types/article';
import { CountryAlias, CountryReference, OriginCleanupItem, isCountryAlias, isCountryReference, isOriginCleanupList } from '../types/country';

// Landenlijst, eigen aliassen en "Herkomst opschonen" (api/countries, api/articles/origin-cleanup).

export async function getCountries(signal?: AbortSignal): Promise<CountryReference> {
    const json = await getJson('/countries', signal);
    if (!isCountryReference(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de landenlijst.');
    return json;
}

export async function addCountryAlias(alias: string, countryCode: string, actorReference: string | null, signal?: AbortSignal): Promise<CountryAlias> {
    const { json } = await postJson('/countries/aliases', { alias, countryCode, actorReference }, signal);
    if (!isCountryAlias(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van de snelkoppeling.');
    return json;
}

export async function removeCountryAlias(id: number, signal?: AbortSignal): Promise<CountryReference> {
    const { json } = await deleteJson(`/countries/aliases/${id}`, signal);
    if (!isCountryReference(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het verwijderen van de snelkoppeling.');
    return json;
}

export async function getOriginCleanup(signal?: AbortSignal): Promise<OriginCleanupItem[]> {
    const json = await getJson('/articles/origin-cleanup', signal);
    if (!isOriginCleanupList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij Herkomst opschonen.');
    return json;
}

export async function setArticleOriginCountry(id: number, originCountryCode: string, signal?: AbortSignal): Promise<Article> {
    const { json } = await postJson(`/articles/${id}/origin-country`, { originCountryCode }, signal);
    if (!isArticle(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van het land.');
    return json;
}
