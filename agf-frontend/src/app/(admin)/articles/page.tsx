'use client';

import Link from 'next/link';
import { useEffect, useMemo, useRef, useState } from 'react';
import { ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import ArticleForm from '../../../components/articles/ArticleForm';
import { inputClass } from '../../../components/supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../../../components/supply/SuccessPanel';
import { useTranslation } from '../../../i18n/LanguageContext';
import { formatGoodsCode, searchArticles } from '../../../lib/articleRules';
import { isMacPlatform, matchShortcut, shortcutLabel } from '../../../lib/shortcuts';
import { unitLabelKey } from '../../../lib/units';
import { toApiError } from '../../../services/apiClient';
import { getArticleReference, listArticles } from '../../../services/articleService';
import type { Article, ArticleReference } from '../../../types/article';

interface Loaded { articles: Article[]; reference: ArticleReference }

// Artikelen: compacte beheerwerkplek. Zoeken (⌥F), kiezen met pijltjes/Enter of klik, toevoegen (⌥N) en bewerken in één
// formulier boven de lijst. Een artikel wordt één keer vastgelegd; bij inkoop en verkoop kiest de gebruiker de zoekcode.
export default function ArticlesPage() {
    const { t, locale } = useTranslation();
    const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
    const [state, setState] = useState<LoadState<Loaded>>({ status: 'loading' });
    const [reloadToken, setReloadToken] = useState(0);
    const [query, setQuery] = useState('');
    const [editing, setEditing] = useState<{ article: Article | null; key: number } | null>(null);
    const searchInput = useRef<HTMLInputElement | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        Promise.all([listArticles(controller.signal), getArticleReference(controller.signal)])
            .then(([articles, reference]) => setState({ status: 'ready', data: { articles, reference } }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [reloadToken]);

    // Link vanuit een order- of aanvoerregel ("Geen kg per colli bij dit artikel"): /articles?article=<id> opent dat artikel direct.
    const openedFromLink = useRef(false);
    useEffect(() => {
        if (state.status !== 'ready' || openedFromLink.current) return;
        openedFromLink.current = true;
        const id = Number(new URLSearchParams(window.location.search).get('article'));
        const article = state.data.articles.find((candidate) => candidate.id === id);
        if (article) setEditing({ article, key: article.id });
    }, [state]);

    const shown = useMemo(() => (state.status === 'ready' ? searchArticles(state.data.articles, query) : []), [state, query]);

    const openNew = () => setEditing({ article: null, key: Date.now() });
    const openArticle = (article: Article) => setEditing({ article, key: article.id });

    // Sneltoetsen van de werkplek: ⌥F zoeken, ⌥N nieuw artikel; ⌘S en ⌥E regelt het formulier zelf.
    const handler = useRef<(event: KeyboardEvent) => void>(() => {});
    handler.current = (event: KeyboardEvent) => {
        const action = matchShortcut(event, isMac);
        if (action === 'focusSearch') { event.preventDefault(); searchInput.current?.focus(); searchInput.current?.select(); }
        if (action === 'newDocument') { event.preventDefault(); openNew(); }
    };
    useEffect(() => {
        const listener = (event: KeyboardEvent) => handler.current(event);
        document.addEventListener('keydown', listener, true);
        return () => document.removeEventListener('keydown', listener, true);
    }, []);

    // Na opslaan: lijst ter plekke bijwerken (geen herladen nodig) en nieuwe productsoorten in de keuzelijst opnemen.
    const onSaved = (saved: Article, created: boolean) => {
        setState((current) => {
            if (current.status !== 'ready') return current;
            const articles = created ? [...current.data.articles, saved] : current.data.articles.map((a) => (a.id === saved.id ? saved : a));
            const productNames = current.data.reference.productNames.includes(saved.productName) ? current.data.reference.productNames : [...current.data.reference.productNames, saved.productName].sort();
            return { status: 'ready', data: { articles, reference: { ...current.data.reference, productNames } } };
        });
        // Dezelfde key: het formulier blijft gemonteerd (melding en focus blijven), maar werkt voortaan op het opgeslagen artikel.
        setEditing((current) => ({ article: saved, key: current?.key ?? saved.id }));
    };

    const number = (value: number | null) => (value === null ? '–' : new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { maximumFractionDigits: 3 }).format(value));

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('articles_title')}</h1>
                    <p className="text-sm text-gray-600">{t('articles_subtitle')}</p>
                </div>
                <div className="flex items-center gap-2 text-sm">
                    <Link href="/products" className="font-medium text-blue-800 underline underline-offset-2">{t('articles_legacy_link')}</Link>
                    <button type="button" onClick={() => setReloadToken((n) => n + 1)} className={secondaryButtonClass}>↻ {t('availability_refresh')}</button>
                    <button type="button" onClick={openNew} className={primaryButtonClass}>+ {t('articles_new')} <kbd className="ml-1 text-xs opacity-80">{shortcutLabel('newDocument', isMac)}</kbd></button>
                </div>
            </div>

            {state.status === 'loading' && <LoadingRows label={t('articles_loading')} />}
            {state.status === 'error' && <ErrorState error={state.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {state.status === 'ready' && (
                <>
                    {editing !== null && (
                        <ArticleForm key={editing.key} article={editing.article} reference={state.data.reference} onSaved={onSaved} onClose={() => setEditing(null)} />
                    )}
                    <div className="flex flex-wrap items-center gap-2">
                        <label htmlFor="article-search" className="sr-only">{t('articles_search_label')}</label>
                        <input id="article-search" ref={searchInput} type="search" value={query} onChange={(event) => setQuery(event.target.value)}
                            placeholder={t('articles_search_placeholder')} className={`${inputClass} mt-0 max-w-xl`}
                            onKeyDown={(event) => { if (event.key === 'Enter' && shown.length === 1) { event.preventDefault(); openArticle(shown[0]); } }} />
                        <span className="text-xs text-gray-600">{t('articles_count', { count: String(shown.length) })} · {t('articles_shortcut_hint', { search: shortcutLabel('focusSearch', isMac), new: shortcutLabel('newDocument', isMac), save: shortcutLabel('save', isMac) })}</span>
                    </div>
                    <div className="overflow-x-auto rounded-md border border-gray-200 bg-white">
                        <table className="min-w-full divide-y divide-gray-200 text-sm">
                            <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                                <tr>
                                    <th scope="col" className="px-3 py-2">{t('articles_col_code')}</th>
                                    <th scope="col" className="px-3 py-2">{t('articles_col_description')}</th>
                                    <th scope="col" className="px-3 py-2">{t('articles_col_unit')}</th>
                                    <th scope="col" className="px-3 py-2 text-right">{t('articles_col_net_kg')}</th>
                                    <th scope="col" className="px-3 py-2 text-right">{t('articles_col_cpp')}</th>
                                    <th scope="col" className="px-3 py-2">{t('articles_col_trade_class')}</th>
                                    <th scope="col" className="px-3 py-2">{t('articles_col_goods_code')}</th>
                                    <th scope="col" className="px-3 py-2"><span className="sr-only">{t('articles_edit_action')}</span></th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {shown.length === 0 && <tr><td colSpan={8} className="px-3 py-6 text-center text-gray-600">{t('articles_none')}</td></tr>}
                                {shown.map((article) => (
                                    <tr key={article.id} className={`cursor-pointer hover:bg-blue-50 ${editing?.article?.id === article.id ? 'bg-blue-50' : ''}`} onClick={() => openArticle(article)} data-testid={`article-row-${article.id}`}>
                                        <td className="px-3 py-2 font-mono">{article.shortcutCode ?? <span className="text-gray-400">–</span>}</td>
                                        <td className="px-3 py-2 text-gray-900">{article.description}{article.brand ? <span className="ml-1 text-xs text-gray-500">· {article.brand}</span> : null}</td>
                                        <td className="px-3 py-2">{article.packagingUnit ? t(unitLabelKey(article.packagingUnit)) : '–'}</td>
                                        <td className="px-3 py-2 text-right font-mono">{number(article.defaultNetKgPerPackage)}{article.weightMethod === 'Calculated' ? '' : article.defaultNetKgPerPackage === null ? '' : ' ⚖'}</td>
                                        <td className="px-3 py-2 text-right font-mono">{number(article.defaultPackagesPerPallet)}</td>
                                        <td className="px-3 py-2">{article.tradeClass ?? '–'}</td>
                                        <td className="px-3 py-2 font-mono">{article.goodsCode ? `${formatGoodsCode(article.goodsCode)} ${article.goodsCodeSystem ?? ''}` : '–'}</td>
                                        <td className="px-3 py-2 text-right">
                                            <button type="button" className={`${secondaryButtonClass} px-2 py-0.5 text-xs`} onClick={(event) => { event.stopPropagation(); openArticle(article); }} aria-label={`${t('articles_edit_action')} #${article.id}`}>✎</button>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                </>
            )}
        </div>
    );
}
