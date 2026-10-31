import { ApiError, getJson, postJson, putJson } from './apiClient';
import { Article, ArticleReference, ArticleUsage, EmptyProduct, GoodsCodeSearch, GoodsCodeSystem, SaveArticleRequest, isArticle, isArticleList, isArticleReference, isArticleUsage, isEmptyProductList, isGoodsCodeSearch } from '../types/article';

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

// Waar een artikel aan hangt (verwijderen kan alleen zonder gebruik).
export async function getArticleUsage(id: number, signal?: AbortSignal): Promise<ArticleUsage> {
    const json = await getJson(`/articles/${id}/usage`, signal);
    if (!isArticleUsage(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het gebruik van het artikel.');
    return json;
}

// Verwijderen (204) of 409 "article-in-use" met de reden; de actor gaat mee voor de historie.
export async function deleteArticle(id: number, actorReference: string | null, signal?: AbortSignal): Promise<void> {
    await postJson(`/articles/${id}/delete`, { actorReference }, signal);
}

export async function setArticleArchived(id: number, archived: boolean, actorReference: string | null, signal?: AbortSignal): Promise<Article> {
    const { json } = await postJson(`/articles/${id}/${archived ? 'archive' : 'restore'}`, { actorReference }, signal);
    if (!isArticle(json)) throw new ApiError('shape', 'Onverwacht antwoord bij het archiveren van het artikel.');
    return json;
}

export async function listEmptyProducts(signal?: AbortSignal): Promise<EmptyProduct[]> {
    const json = await getJson('/articles/products/empty', signal);
    if (!isEmptyProductList(json)) throw new ApiError('shape', 'Onverwacht antwoord bij de productsoorten.');
    return json;
}

export async function deleteProduct(id: number, actorReference: string | null, signal?: AbortSignal): Promise<void> {
    await postJson(`/articles/products/${id}/delete`, { actorReference }, signal);
}
