'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import { FirstStepLink } from '../../../components/shared/FirstStep';
import PartijDetail from '../../../components/partij/PartijDetail';
import PartijList, { PURCHASE_PARTIJ_SORT_KEYS } from '../../../components/partij/PartijList';
import SalesPartijList, { SALES_PARTIJ_SORT_KEYS } from '../../../components/partij/SalesPartijList';
import { useListSort } from '../../../components/shared/useListSort';
import { sortParams } from '../../../lib/listSort';
import { inputClass } from '../../../components/supply/FieldError';
import { secondaryButtonClass } from '../../../components/supply/SuccessPanel';
import { useTranslation } from '../../../i18n/LanguageContext';
import { toApiError } from '../../../services/apiClient';
import { type PartijQuery, getPartij, listPartijen } from '../../../services/partijService';
import type { PartijDetail as Detail, PartijList as List } from '../../../types/partij';

const PAGE_SIZE = 50;

// Voorraad › Partijen: de server zoekt (partijnummer zonder spaties en streepjes, ook zonder I- of V-, partij van de leverancier, product, leverancier; bij
// verkoop-partijen ook klant en VO-nummer) en pagineert; nieuwste eerst; klikken opent het detail (?id=). Twee tabbladen: inkoop-partijen
// (één leverancier) en verkoop-partijen (één verkooporder), ?soort=verkoop.
export default function PartijenPage() {
    const { t } = useTranslation();
    return (
        <Suspense fallback={<LoadingRows label={t('partij_loading')} />}>
            <PartijenWorkspace />
        </Suspense>
    );
}

function parseId(value: string | null): number | null {
    return value !== null && /^[1-9][0-9]{0,9}$/.test(value) ? Number(value) : null;
}

function PartijenWorkspace() {
    const { t } = useTranslation();
    const router = useRouter();
    const pathname = usePathname();
    const searchParams = useSearchParams();
    const selectedId = parseId(searchParams.get('id'));
    const kind: 'Purchase' | 'Sales' = searchParams.get('soort') === 'verkoop' ? 'Sales' : 'Purchase';
    const [query, setQuery] = useState(searchParams.get('query') ?? '');
    const [debounced, setDebounced] = useState(query.trim());
    const [status, setStatus] = useState<PartijQuery['status']>('all');
    const [page, setPage] = useState(1);
    const [reloadToken, setReloadToken] = useState(0);
    const [list, setList] = useState<LoadState<List>>({ status: 'loading' });
    const [detail, setDetail] = useState<LoadState<Detail> | null>(null);
    // Feedback 22 november: zoeken zonder letter vindt beide soorten (261140 vindt I-261140 en V-261140). De lijst toont één soort; hoeveel
    // partijen van de andere soort ook passen, staat eronder met een knop naar dat tabblad.
    const [otherKindCount, setOtherKindCount] = useState(0);
    const detailRef = useRef<HTMLDivElement | null>(null);
    const pendingScrollId = useRef<number | null>(null);

    useEffect(() => {
        const handle = setTimeout(() => setDebounced(query.trim()), 250);
        return () => clearTimeout(handle);
    }, [query]);
    // Sorteren op een kolom (feedback 22 november): op de server, over alle partijen; per soort onthouden in deze browser.
    const { sort, ready: sortReady, toggle: toggleSort, setSort } = useListSort(kind === 'Sales' ? 'partijen-sales' : 'partijen-purchase', kind === 'Sales' ? SALES_PARTIJ_SORT_KEYS : PURCHASE_PARTIJ_SORT_KEYS);
    useEffect(() => { setPage(1); }, [debounced, status, kind, sort]);

    useEffect(() => {
        if (!sortReady) return;
        const controller = new AbortController();
        setList((current) => (current.status === 'ready' ? current : { status: 'loading' }));
        listPartijen({ query: debounced, status: kind === 'Sales' ? 'all' : status, kind, page, pageSize: PAGE_SIZE, ...sortParams(sort) }, controller.signal)
            .then((data) => {
                // Lege pagina na verwijderen (of een kleiner totaal): spring naar de laatste pagina die nog bestaat.
                if (data.items.length === 0 && data.total > 0 && page > 1) { setPage(Math.max(1, Math.ceil(data.total / PAGE_SIZE))); return; }
                setList({ status: 'ready', data });
            })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setList({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [debounced, status, kind, page, reloadToken, sort, sortReady]);

    useEffect(() => {
        if (debounced === '') { setOtherKindCount(0); return; }
        const controller = new AbortController();
        listPartijen({ query: debounced, status: 'all', kind: kind === 'Sales' ? 'Purchase' : 'Sales', page: 1, pageSize: 1 }, controller.signal)
            .then((data) => setOtherKindCount(data.total))
            .catch((caught: unknown) => { if (toApiError(caught).kind !== 'aborted') setOtherKindCount(0); });
        return () => controller.abort();
    }, [debounced, kind, reloadToken]);

    useEffect(() => {
        if (selectedId === null) { setDetail(null); return; }
        const controller = new AbortController();
        setDetail((current) => (current?.status === 'ready' && current.data.id === selectedId ? current : { status: 'loading' }));
        getPartij(selectedId, controller.signal)
            .then((data) => setDetail({ status: 'ready', data }))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setDetail({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [selectedId, reloadToken]);

    // Scroll pas nadat het geselecteerde detail daadwerkelijk is gerenderd (niet naar de nog lege container).
    useEffect(() => {
        if (detail?.status === 'ready' && pendingScrollId.current === detail.data.id) {
            pendingScrollId.current = null;
            detailRef.current?.scrollIntoView?.({ block: 'start' });
        } else if (detail?.status === 'error') {
            pendingScrollId.current = null;
        }
    }, [detail]);

    const open = useCallback((id: number | null) => {
        const params = new URLSearchParams(searchParams.toString());
        if (id === null) params.delete('id'); else params.set('id', String(id));
        router.replace(params.size === 0 ? pathname : `${pathname}?${params.toString()}`, { scroll: false });
        pendingScrollId.current = id;
    }, [router, pathname, searchParams]);

    const chooseKind = (next: 'Purchase' | 'Sales') => {
        const params = new URLSearchParams(searchParams.toString());
        params.delete('id');
        if (next === 'Sales') params.set('soort', 'verkoop'); else params.delete('soort');
        router.replace(params.size === 0 ? pathname : `${pathname}?${params.toString()}`, { scroll: false });
    };

    const total = list.status === 'ready' ? list.data.total : 0;
    const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 data-tour="page-title" className="text-2xl font-bold text-gray-900">{t('partij_title')}</h1>
                    <p className="text-sm text-gray-600" data-testid="page-intro">{t('intro_partijen')}</p>
                </div>
                <button type="button" onClick={() => setReloadToken((n) => n + 1)} className={secondaryButtonClass}>↻ {t('availability_refresh')}</button>
            </div>

            <div className="flex flex-wrap gap-1" role="tablist" aria-label={t('partij_kind_tabs')}>
                {(['Purchase', 'Sales'] as const).map((option) => (
                    <button key={option} type="button" role="tab" aria-selected={kind === option} data-tour="partij-kind-tab" data-tour-key={option}
                        onClick={() => chooseKind(option)}
                        className={`rounded border px-3 py-1 text-sm font-medium ${kind === option ? 'border-blue-600 bg-blue-600 text-white' : 'border-gray-300 bg-white text-gray-800 hover:bg-gray-100'}`}>
                        {t(option === 'Sales' ? 'partij_tab_sales' : 'partij_tab_purchase')}
                    </button>
                ))}
            </div>

            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                <div className={kind === 'Sales' ? 'sm:col-span-3' : 'sm:col-span-2'}>
                    <label htmlFor="partij-list-search" className="text-xs font-medium text-gray-700">{t('partij_list_search')}</label>
                    <input id="partij-list-search" data-tour="partij-search" type="search" value={query} onChange={(event) => setQuery(event.target.value)}
                        placeholder={t(kind === 'Sales' ? 'partij_list_search_sales_placeholder' : 'partij_list_search_placeholder')} className={inputClass} />
                </div>
                <div className={kind === 'Sales' ? 'hidden' : ''}>
                    <label htmlFor="partij-list-status" className="text-xs font-medium text-gray-700">{t('partij_col_status')}</label>
                    <select id="partij-list-status" value={status} onChange={(event) => setStatus(event.target.value as PartijQuery['status'])} className={inputClass}>
                        <option value="all">{t('po_filter_all')}</option>
                        <option value="Open">{t('partij_status_open')}</option>
                        <option value="SoldOut">{t('partij_status_sold_out')}</option>
                    </select>
                </div>
            </div>

            {otherKindCount > 0 && (
                <p className="flex flex-wrap items-center gap-2 text-sm text-gray-700" data-testid="partij-other-kind">
                    {t(kind === 'Sales' ? 'partij_other_kind_purchase' : 'partij_other_kind_sales', { count: String(otherKindCount), query: debounced })}
                    <button type="button" className={secondaryButtonClass} onClick={() => chooseKind(kind === 'Sales' ? 'Purchase' : 'Sales')}>
                        {t(kind === 'Sales' ? 'partij_show_purchase' : 'partij_show_sales')}
                    </button>
                </p>
            )}

            {list.status === 'loading' && <LoadingRows label={t('partij_loading')} />}
            {list.status === 'error' && <ErrorState error={list.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {list.status === 'ready' && list.data.items.length === 0 && kind === 'Sales' && (debounced === ''
                // Nog geen verkoop-partij: die ontstaat vanzelf met elke verkooporder.
                ? <EmptyState testId="partij-sales-empty" title={t('partij_sales_empty_title')} description={t('partij_sales_empty_text')}
                    action={<FirstStepLink href="/sales-orders" label={t('nav_sales_orders')} />} />
                : <EmptyState title={t('partij_list_empty')} />)}
            {list.status === 'ready' && list.data.items.length === 0 && kind === 'Purchase' && (debounced === '' && status === 'all'
                // Nog geen enkele partij: die ontstaat met de eerste inkooporder.
                ? <EmptyState testId="partij-empty" title={t('partij_empty_title')} description={t('partij_empty_text')}
                    action={<FirstStepLink href="/purchase-orders/new" label={t('nav_purchase_order_new')} />} />
                : <EmptyState title={t('partij_list_empty')} />)}
            {list.status === 'ready' && list.data.items.length > 0 && (
                <>
                    {kind === 'Sales'
                        ? <SalesPartijList items={list.data.items} selectedId={selectedId} onOpen={open} sort={sort} onSort={toggleSort} onSortChange={setSort} />
                        : <PartijList items={list.data.items} selectedId={selectedId} onOpen={open} sort={sort} onSort={toggleSort} onSortChange={setSort} />}
                    {pages > 1 && (
                        <nav className="flex items-center gap-2 text-sm" aria-label={t('po_paging')}>
                            <button type="button" className={secondaryButtonClass} disabled={page <= 1} onClick={() => setPage((n) => n - 1)}>‹</button>
                            <span>{t('po_page_of', { page: String(page), pages: String(pages), total: String(total) })}</span>
                            <button type="button" className={secondaryButtonClass} disabled={page >= pages} onClick={() => setPage((n) => n + 1)}>›</button>
                        </nav>
                    )}
                </>
            )}

            <div ref={detailRef}>
                {detail?.status === 'loading' && <LoadingRows rows={4} label={t('partij_loading')} />}
                {detail?.status === 'error' && <ErrorState error={detail.error} onRetry={() => setReloadToken((n) => n + 1)} />}
                {detail?.status === 'ready' && <PartijDetail key={detail.data.id} partij={detail.data}
                    onChanged={() => setReloadToken((n) => n + 1)} onDeleted={() => { open(null); setReloadToken((n) => n + 1); }} />}
            </div>
        </div>
    );
}
