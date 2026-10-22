import { ApiError, getJson, postJson, putJson } from './apiClient';
import { Article, ArticleReference, GoodsCodeSearch, GoodsCodeSystem, SaveArticleRequest, isArticle, isArticleList, isArticleReference, isGoodsCodeSearch } from '../types/article';

// Artikelwerkplek via api/articles: beheer van productsoorten en artikelen (ProductVariants). Raakt nooit documenten of voorraad.

export async function listArticles(signal?: AbortSignal): Promise<Article[]> {
    const json = await getJson('/articles', signal);
    if (!isArticleList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de artikelen.');
    return json;
}

export async function getArticleReference(signal?: AbortSignal): Promise<ArticleReference> {
    const json = await getJson('/articles/reference', signal);
    if (!isArticleReference(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de keuzelijsten.');
    return json;
}

export async function createArticle(request: SaveArticleRequest, signal?: AbortSignal): Promise<Article> {
    const { json } = await postJson('/articles', request, signal);
    if (!isArticle(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van het artikel.');
    return json;
}

export async function updateArticle(id: number, request: SaveArticleRequest, signal?: AbortSignal): Promise<Article> {
    const { json } = await putJson(`/articles/${id}`, request, signal);
    if (!isArticle(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het opslaan van het artikel.');
    return json;
}

// Lokale referentielijst: herkomst en versie komen mee; nooit volledig, de gebruiker kiest en bevestigt zelf.
// Het gekozen stelsel gaat mee en het antwoord bevat uitsluitend codes van dat stelsel (HS 6, GN/CN 8, TARIC 10).
export async function searchGoodsCodes(query: string, system: GoodsCodeSystem, signal?: AbortSignal): Promise<GoodsCodeSearch> {
    const json = await getJson(`/articles/goods-codes?query=${encodeURIComponent(query)}&system=${encodeURIComponent(system)}`, signal);
    if (!isGoodsCodeSearch(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de goederencodes.');
    return json;
}
