'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import PartijDetail from '../../../components/partij/PartijDetail';
import PartijList from '../../../components/partij/PartijList';
import { inputClass } from '../../../components/supply/FieldError';
import { secondaryButtonClass } from '../../../components/supply/SuccessPanel';
import { useTranslation } from '../../../i18n/LanguageContext';
import { toApiError } from '../../../services/apiClient';
import { type PartijQuery, getPartij, listPartijen } from '../../../services/partijService';
import type { PartijDetail as Detail, PartijList as List } from '../../../types/partij';

const PAGE_SIZE = 50;

// Voorraad › Partijen: de server zoekt (partijnummer zonder spaties en streepjes, partij van de leverancier, product, leverancier) en
// pagineert; nieuwste eerst; klikken opent het detail (?id=), zoals bij de inkooporders.
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
    const [query, setQuery] = useState(searchParams.get('query') ?? '');
    const [debounced, setDebounced] = useState(query.trim());
    const [status, setStatus] = useState<PartijQuery['status']>('all');
    const [page, setPage] = useState(1);
    const [reloadToken, setReloadToken] = useState(0);
    const [list, setList] = useState<LoadState<List>>({ status: 'loading' });
    const [detail, setDetail] = useState<LoadState<Detail> | null>(null);
    const detailRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        const handle = setTimeout(() => setDebounced(query.trim()), 250);
        return () => clearTimeout(handle);
    }, [query]);
    useEffect(() => { setPage(1); }, [debounced, status]);

    useEffect(() => {
        const controller = new AbortController();
        setList((current) => (current.status === 'ready' ? current : { status: 'loading' }));
        listPartijen({ query: debounced, status, page, pageSize: PAGE_SIZE }, controller.signal)
            .then((data) => {
                // Lege pagina na verwijderen (of een kleiner totaal): spring naar de laatste pagina die nog bestaat.
                if (data.items.length === 0 && data.total > 0 && page > 1) { setPage(Math.max(1, Math.ceil(data.total / PAGE_SIZE))); return; }
                setList({ status: 'ready', data });
            })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setList({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [debounced, status, page, reloadToken]);

    useEffect(() => {
        if (selectedId === null) { setDetail(null); return; }
        const controller = new AbortController();
        setDetail((current) => (current?.status === 'ready' && current.data.id === selectedId ? current : { status: 'loading' }));
        getPartij(selectedId, controller.signal)
            .then((data) => setDetail({ status: 'ready', data }))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setDetail({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [selectedId, reloadToken]);

    const open = useCallback((id: number | null) => {
        const params = new URLSearchParams(searchParams.toString());
        if (id === null) params.delete('id'); else params.set('id', String(id));
        router.replace(params.size === 0 ? pathname : `${pathname}?${params.toString()}`, { scroll: false });
        if (id !== null) requestAnimationFrame(() => detailRef.current?.scrollIntoView?.({ block: 'start' }));
    }, [router, pathname, searchParams]);

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

            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                <div className="sm:col-span-2">
                    <label htmlFor="partij-list-search" className="text-xs font-medium text-gray-700">{t('partij_list_search')}</label>
                    <input id="partij-list-search" data-tour="partij-search" type="search" value={query} onChange={(event) => setQuery(event.target.value)}
                        placeholder={t('partij_list_search_placeholder')} className={inputClass} />
                </div>
                <div>
                    <label htmlFor="partij-list-status" className="text-xs font-medium text-gray-700">{t('partij_col_status')}</label>
                    <select id="partij-list-status" value={status} onChange={(event) => setStatus(event.target.value as PartijQuery['status'])} className={inputClass}>
                        <option value="all">{t('po_filter_all')}</option>
                        <option value="Open">{t('partij_status_open')}</option>
                        <option value="SoldOut">{t('partij_status_sold_out')}</option>
                    </select>
                </div>
            </div>

            {list.status === 'loading' && <LoadingRows label={t('partij_loading')} />}
            {list.status === 'error' && <ErrorState error={list.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {list.status === 'ready' && list.data.items.length === 0 && <EmptyState title={t('partij_list_empty')} />}
            {list.status === 'ready' && list.data.items.length > 0 && (
                <>
                    <PartijList items={list.data.items} selectedId={selectedId} onOpen={open} />
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
