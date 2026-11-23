'use client';

import Link from 'next/link';
import { useEffect, useMemo, useRef, useState } from 'react';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import CreatedNotice, { useCreated } from '../../../components/shared/CreatedNotice';
import ProductColors from '../../../components/articles/ProductColors';
import ArticleForm from '../../../components/articles/ArticleForm';
import ArticleLifecycle from '../../../components/articles/ArticleLifecycle';
import EmptyProducts from '../../../components/articles/EmptyProducts';
import { inputClass } from '../../../components/supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../../../components/supply/SuccessPanel';
import { useTranslation } from '../../../i18n/LanguageContext';
import { visibleArticles } from '../../../lib/articleLifecycle';
import { formatGoodsCode, searchArticles } from '../../../lib/articleRules';
import { isMacPlatform, matchShortcut, shortcutLabel } from '../../../lib/shortcuts';
import { unitLabelKey } from '../../../lib/units';
import { toApiError } from '../../../services/apiClient';
import { getArticleReference, listArticles } from '../../../services/articleService';
import { getCountries } from '../../../services/countryService';
import type { Article, ArticleReference } from '../../../types/article';
import type { CountryReference } from '../../../types/country';
import OriginSettings from '../../../components/articles/OriginSettings';
import SizeClassCleanup from '../../../components/articles/SizeClassCleanup';
import OrderOriginCheck from '../../../components/articles/OrderOriginCheck';
import VatCheck from '../../../components/articles/VatCheck';
import PackagingArticles from '../../../components/articles/PackagingArticles';
import { listRowClass, rowClick } from '../../../lib/listRows';
import { type SortColumns, sortRows } from '../../../lib/listSort';
import { SortHeader } from '../../../components/shared/SortHeader';
import { useListSort } from '../../../components/shared/useListSort';

// Sorteren op de kolomkoppen (feedback 22 november). Standaard blijft de volgorde van de lijst (bij zoeken de beste treffer eerst); een net
// aangemaakt artikel staat zolang de melding staat altijd bovenaan.
const ARTICLE_COLUMNS: SortColumns<Article> = {
    code: { kind: 'natural', value: (article) => article.shortcutCode },
    description: { kind: 'text', value: (article) => article.description },
    unit: { kind: 'text', value: (article) => article.packagingUnit },
    netKg: { kind: 'number', value: (article) => article.defaultNetKgPerPackage },
    cpp: { kind: 'number', value: (article) => article.defaultPackagesPerPallet },
    tradeClass: { kind: 'text', value: (article) => article.tradeClass },
    goodsCode: { kind: 'natural', value: (article) => article.goodsCode },
};
const ARTICLE_SORT_KEYS = Object.keys(ARTICLE_COLUMNS);

interface Loaded { articles: Article[]; reference: ArticleReference; countries: CountryReference }

// Artikelen: compacte beheerwerkplek. Zoeken (⌥F), kiezen met pijltjes/Enter of klik, toevoegen (⌥N) en bewerken in één
// formulier boven de lijst. Een artikel wordt één keer vastgelegd; bij inkoop en verkoop kiest de gebruiker de zoekcode.
export default function ArticlesPage() {
    const { t, locale } = useTranslation();
    const isMac = typeof navigator !== 'undefined' && isMacPlatform(navigator.userAgent);
    const [state, setState] = useState<LoadState<Loaded>>({ status: 'loading' });
    const [reloadToken, setReloadToken] = useState(0);
    const [query, setQuery] = useState('');
    const [showArchived, setShowArchived] = useState(false);
    const [emptyProductsToken, setEmptyProductsToken] = useState(0);
    const [editing, setEditing] = useState<{ article: Article | null; key: number } | null>(null);
    // Tab Goederen (artikelen met voorraad) of Emballage (pallets, kratten: geen voorraad); /articles?soort=emballage opent Emballage.
    const [tab, setTab] = useState<'goods' | 'packaging'>('goods');
    useEffect(() => { if (new URLSearchParams(window.location.search).get('soort') === 'emballage') setTab('packaging'); }, []);
    const searchInput = useRef<HTMLInputElement | null>(null);

    useEffect(() => {
        const controller = new AbortController();
        Promise.all([listArticles(controller.signal), getArticleReference(controller.signal), getCountries(controller.signal)])
            .then(([articles, reference, countries]) => setState({ status: 'ready', data: { articles, reference, countries } }))
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

    // Gearchiveerde artikelen staan standaard niet in de lijst; "Toon gearchiveerde" laat ze zien (met label).
    const { sort, toggle: toggleSort } = useListSort('articles', ARTICLE_SORT_KEYS);
    const shown = useMemo(() => (state.status === 'ready' ? sortRows(searchArticles(visibleArticles(state.data.articles, showArchived), query), ARTICLE_COLUMNS, sort) : []), [state, query, showArchived, sort]);
    const archivedCount = state.status === 'ready' ? state.data.articles.filter((article) => article.isArchived).length : 0;

    const created = useCreated();
    // Het nieuwe artikel staat zolang de melding staat bovenaan de lijst, direct onder de melding.
    const rows = useMemo(() => {
        const id = created.created?.id ?? null;
        const fresh = id === null ? undefined : shown.find((article) => article.id === id);
        return fresh === undefined ? shown : [fresh, ...shown.filter((article) => article.id !== id)];
    }, [shown, created.created]);
    const openNew = () => { created.clear(); setTab('goods'); setEditing({ article: null, key: Date.now() }); };
    // Nog geen enkel artikel (ook geen gearchiveerd): schone start, de eerste stap is een nieuw artikel.
    const nothingYet = state.status === 'ready' && state.data.articles.length === 0;

    // Link vanaf de Beginnen-kaart of een formulier dat een artikel mist: /articles?new=1 opent meteen een nieuw artikel.
    useEffect(() => {
        if (new URLSearchParams(window.location.search).get('new') === '1') setEditing({ article: null, key: Date.now() });
    }, []);
    const openArticle = (article: Article) => { created.clear(); setEditing({ article, key: article.id }); };

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
    const onSaved = (saved: Article, isNew: boolean) => {
        setState((current) => {
            if (current.status !== 'ready') return current;
            const articles = isNew ? [...current.data.articles, saved] : current.data.articles.map((a) => (a.id === saved.id ? saved : a));
            const productNames = current.data.reference.productNames.includes(saved.productName) ? current.data.reference.productNames : [...current.data.reference.productNames, saved.productName].sort();
            return { status: 'ready', data: { ...current.data, articles, reference: { ...current.data.reference, productNames } } };
        });
        // Nieuw (fixes 5b): het formulier sluit, de melding blijft boven de lijst en de nieuwe regel is kort gemarkeerd. Bewerken: dezelfde
        // key, het formulier blijft gemonteerd (melding en focus blijven) en werkt voortaan op het opgeslagen artikel.
        if (isNew) {
            setEditing(null);
            created.announce({ id: saved.id, text: t('articles_created', { description: saved.description }) });
        } else {
            setEditing((current) => ({ article: saved, key: current?.key ?? saved.id }));
        }
    };

    // Verwijderd: uit de lijst en het formulier dicht; een productsoort zonder artikelen verdwijnt uit de keuzelijst.
    const onDeleted = (id: number) => {
        setState((current) => {
            if (current.status !== 'ready') return current;
            const removed = current.data.articles.find((a) => a.id === id);
            const articles = current.data.articles.filter((a) => a.id !== id);
            const productNames = removed && !articles.some((a) => a.productId === removed.productId)
                ? current.data.reference.productNames.filter((name) => name !== removed.productName) : current.data.reference.productNames;
            return { status: 'ready', data: { ...current.data, articles, reference: { ...current.data.reference, productNames } } };
        });
        setEditing(null);
        setEmptyProductsToken((n) => n + 1);
    };

    const number = (value: number | null) => (value === null ? '–' : new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { maximumFractionDigits: 3 }).format(value));

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 data-tour="page-title" className="text-2xl font-bold text-gray-900">{t('articles_title')}</h1>
                    <p className="text-sm text-gray-600">{t('articles_subtitle')}</p>
                </div>
                <div className="flex items-center gap-2 text-sm">
                    <Link href="/products" className="font-medium text-blue-800 underline underline-offset-2">{t('articles_legacy_link')}</Link>
                    <button type="button" onClick={() => setReloadToken((n) => n + 1)} className={secondaryButtonClass}>↻ {t('availability_refresh')}</button>
                    <button type="button" onClick={openNew} className={primaryButtonClass} data-tour="articles-new">+ {t('articles_new')} <kbd className="ml-1 text-xs opacity-80">{shortcutLabel('newDocument', isMac)}</kbd></button>
                </div>
            </div>

            <div role="tablist" aria-label={t('articles_kind_tabs')} className="flex gap-1 border-b border-gray-200 text-sm">
                {(['goods', 'packaging'] as const).map((kind) => (
                    <button key={kind} type="button" role="tab" aria-selected={tab === kind} data-tour={`articles-tab-${kind}`} onClick={() => setTab(kind)}
                        className={`-mb-px border-b-2 px-3 py-1.5 font-medium ${tab === kind ? 'border-blue-700 text-blue-800' : 'border-transparent text-gray-600 hover:text-gray-900'}`}>
                        {t(kind === 'goods' ? 'articles_tab_goods' : 'articles_tab_packaging')}
                    </button>
                ))}
            </div>
            {tab === 'packaging' && <PackagingArticles />}
            {tab === 'goods' && state.status === 'loading' && <LoadingRows label={t('articles_loading')} />}
            {tab === 'goods' && state.status === 'error' && <ErrorState error={state.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {tab === 'goods' && state.status === 'ready' && (
                <>
                    <CreatedNotice created={created.created} tour="article-notice" onClose={created.clear} />
                    {editing !== null && (
                        <ArticleForm key={editing.key} article={editing.article} reference={state.data.reference} countries={state.data.countries} onSaved={onSaved} onClose={() => setEditing(null)} />
                    )}
                    {editing?.article && <ArticleLifecycle article={editing.article} onChanged={(saved) => onSaved(saved, false)} onDeleted={onDeleted} />}
                    {/* Feedback 22 november: de kleurwaarden van het product (voor de kleur van een partij). */}
                    {editing?.article && editing.article.productId > 0 && <ProductColors key={editing.article.productId} productId={editing.article.productId} />}
                    <div className="flex flex-wrap items-center gap-2">
                        <label htmlFor="article-search" className="sr-only">{t('articles_search_label')}</label>
                        <input id="article-search" ref={searchInput} type="search" value={query} onChange={(event) => setQuery(event.target.value)}
                            placeholder={t('articles_search_placeholder')} className={`${inputClass} mt-0 max-w-xl`}
                            onKeyDown={(event) => { if (event.key === 'Enter' && shown.length === 1) { event.preventDefault(); openArticle(shown[0]); } }} />
                        {archivedCount > 0 && (
                            <label className="flex items-center gap-1 text-xs text-gray-700">
                                <input type="checkbox" checked={showArchived} onChange={(event) => setShowArchived(event.target.checked)} data-testid="articles-show-archived" />
                                {t('articles_show_archived', { count: String(archivedCount) })}
                            </label>
                        )}
                        <span className="text-xs text-gray-600">{t('articles_count', { count: String(shown.length) })} · {t('articles_shortcut_hint', { search: shortcutLabel('focusSearch', isMac), new: shortcutLabel('newDocument', isMac), save: shortcutLabel('save', isMac) })}</span>
                    </div>
                    <div className="overflow-x-auto rounded-md border border-gray-200 bg-white">
                        <table className="min-w-full divide-y divide-gray-200 text-sm">
                            <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                                <tr>
                                    <SortHeader className="px-3 py-2" label={t('articles_col_code')} sortKey="code" sort={sort} onSort={toggleSort} />
                                    <SortHeader className="px-3 py-2" label={t('articles_col_description')} sortKey="description" sort={sort} onSort={toggleSort} />
                                    <SortHeader className="px-3 py-2" label={t('articles_col_unit')} sortKey="unit" sort={sort} onSort={toggleSort} />
                                    <SortHeader className="px-3 py-2 text-right" label={t('articles_col_net_kg')} sortKey="netKg" sort={sort} onSort={toggleSort} />
                                    <SortHeader className="px-3 py-2 text-right" label={t('articles_col_cpp')} sortKey="cpp" sort={sort} onSort={toggleSort} />
                                    <SortHeader className="px-3 py-2" label={t('articles_col_trade_class')} sortKey="tradeClass" sort={sort} onSort={toggleSort} />
                                    <SortHeader className="px-3 py-2" label={t('articles_col_goods_code')} sortKey="goodsCode" sort={sort} onSort={toggleSort} />
                                    <th scope="col" className="px-3 py-2"><span className="sr-only">{t('articles_edit_action')}</span></th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-gray-100">
                                {shown.length === 0 && (
                                    <tr><td colSpan={8} className={nothingYet ? 'p-2' : 'px-3 py-6 text-center text-gray-600'}>
                                        {nothingYet ? (
                                            <EmptyState testId="articles-empty" title={t('articles_empty_title')} description={t('articles_empty_text')}
                                                action={editing === null ? <button type="button" onClick={openNew} className={primaryButtonClass}>{t('articles_empty_action')}</button> : undefined} />
                                        ) : t('articles_none')}
                                    </td></tr>
                                )}
                                {rows.map((article, index) => (
                                    <tr key={article.id} data-fresh={created.fresh === article.id ? 'true' : undefined} className={listRowClass(index, { fresh: created.fresh === article.id, selected: editing?.article?.id === article.id, clickable: true })} onClick={rowClick(() => openArticle(article))} data-testid={`article-row-${article.id}`}>
                                        <td className="px-3 py-2 font-mono">{article.shortcutCode ?? <span className="text-gray-400">–</span>}</td>
                                        <td className="px-3 py-2 text-gray-900">{article.description}{article.brand ? <span className="ml-1 text-xs text-gray-500">· {article.brand}</span> : null}{article.isArchived && <span className="ml-2 rounded bg-gray-200 px-1.5 py-0.5 text-xs font-medium text-gray-700">{t('articles_archived_badge')}</span>}</td>
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
                    {!nothingYet && <EmptyProducts reloadToken={reloadToken + emptyProductsToken} onDeleted={(name) => setState((current) => (current.status === 'ready'
                        ? { status: 'ready', data: { ...current.data, reference: { ...current.data.reference, productNames: current.data.reference.productNames.filter((n) => n !== name) } } } : current))} />}
                    {!nothingYet && <VatCheck reloadToken={reloadToken} onOpenArticle={(id) => { const found = state.data.articles.find((candidate) => candidate.id === id); if (found) openArticle(found); }} />}
                    {!nothingYet && <SizeClassCleanup reloadToken={reloadToken} onArticleChanged={(saved) => setState((current) => (current.status === 'ready'
                        ? { status: 'ready', data: { ...current.data, articles: current.data.articles.map((a) => (a.id === saved.id ? saved : a)) } } : current))} />}
                    <OrderOriginCheck countries={state.data.countries} reloadToken={reloadToken} />
                    <OriginSettings countries={state.data.countries} cleanupToken={reloadToken}
                        onCountriesChanged={(countries) => setState((current) => (current.status === 'ready' ? { status: 'ready', data: { ...current.data, countries } } : current))}
                        onArticleChanged={(saved) => setState((current) => (current.status === 'ready'
                            ? { status: 'ready', data: { ...current.data, articles: current.data.articles.map((a) => (a.id === saved.id ? saved : a)) } } : current))} />
                </>
            )}
        </div>
    );
}
