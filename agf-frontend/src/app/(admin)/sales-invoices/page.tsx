'use client';

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { EmptyState, ErrorState, LoadingRows, type LoadState } from '../../../components/availability/StateViews';
import PageTitle from '../../../components/shared/PageTitle';
import { useListSort } from '../../../components/shared/useListSort';
import SalesInvoiceDetail from '../../../components/salesInvoice/SalesInvoiceDetail';
import SalesInvoiceList from '../../../components/salesInvoice/SalesInvoiceList';
import InvoiceConceptEditor from '../../../components/salesInvoice/InvoiceConceptEditor';
import ToInvoiceList from '../../../components/salesInvoice/ToInvoiceList';
import { inputClass } from '../../../components/supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../../../components/supply/SuccessPanel';
import { useTranslation } from '../../../i18n/LanguageContext';
import { sortParams } from '../../../lib/listSort';
import { SALES_INVOICE_SORT_KEYS, SALES_INVOICE_STATUS_FILTERS } from '../../../lib/salesInvoice';
import { toApiError } from '../../../services/apiClient';
import { finalizeSalesInvoices, getSalesInvoice, listSalesInvoices } from '../../../services/salesInvoiceService';
import type { FinalizeResult, SalesInvoice, SalesInvoiceList as InvoiceList } from '../../../types/salesInvoice';

const PAGE_SIZE = 50;

const FILTER_KEYS: Record<(typeof SALES_INVOICE_STATUS_FILTERS)[number], string> = {
    all: 'si_filter_all', open: 'si_filter_open', Final: 'si_filter_final', Discarded: 'si_filter_discarded',
};

// Verkoop › Verkoopfacturen (verkoopfactuur 1): de lijst (concepten eerst), zoeken op nummer, klant, V- of I-nummer (ook zonder letter),
// filter op status, sorteren op de kolomkoppen (op de server) en bladeren. Klikken opent het detail (?id=). Concepten zijn aan te vinken en
// samen definitief te maken, na een bevestiging.
export default function SalesInvoicesPage() {
    const { t } = useTranslation();
    return (
        <Suspense fallback={<LoadingRows label={t('si_loading')} />}>
            <SalesInvoicesWorkspace />
        </Suspense>
    );
}

function parseId(value: string | null): number | null {
    return value !== null && /^[1-9][0-9]{0,9}$/.test(value) ? Number(value) : null;
}

function SalesInvoicesWorkspace() {
    const { t } = useTranslation();
    const router = useRouter();
    const pathname = usePathname();
    const searchParams = useSearchParams();
    const selectedId = parseId(searchParams.get('id'));
    const [query, setQuery] = useState(searchParams.get('query') ?? '');
    const [debounced, setDebounced] = useState(query.trim());
    const [status, setStatus] = useState<string>('all');
    const [page, setPage] = useState(1);
    const [reloadToken, setReloadToken] = useState(0);
    const [list, setList] = useState<LoadState<InvoiceList>>({ status: 'loading' });
    const [detail, setDetail] = useState<LoadState<SalesInvoice> | null>(null);
    const [chosen, setChosen] = useState<Set<number>>(new Set());
    const [confirmMany, setConfirmMany] = useState(false);
    const [busy, setBusy] = useState(false);
    const [results, setResults] = useState<FinalizeResult[] | null>(null);
    const [manyError, setManyError] = useState<string | null>(null);
    const detailRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        const handle = setTimeout(() => setDebounced(query.trim()), 250);
        return () => clearTimeout(handle);
    }, [query]);
    const { sort, ready: sortReady, toggle: toggleSort, setSort } = useListSort('sales-invoices', SALES_INVOICE_SORT_KEYS);
    useEffect(() => { setPage(1); }, [debounced, status, sort]);

    useEffect(() => {
        if (!sortReady) return;
        const controller = new AbortController();
        setList((current) => (current.status === 'ready' ? current : { status: 'loading' }));
        listSalesInvoices({ query: debounced, status, page, pageSize: PAGE_SIZE, ...sortParams(sort) }, controller.signal)
            .then((data) => setList({ status: 'ready', data }))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setList({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [debounced, status, page, reloadToken, sort, sortReady]);

    useEffect(() => {
        if (selectedId === null) { setDetail(null); return; }
        const controller = new AbortController();
        setDetail((current) => (current?.status === 'ready' && current.data.id === selectedId ? current : { status: 'loading' }));
        getSalesInvoice(selectedId, controller.signal)
            .then((data) => setDetail({ status: 'ready', data }))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setDetail({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [selectedId, reloadToken]);

    const open = useCallback((id: number) => {
        const params = new URLSearchParams(searchParams.toString());
        params.set('id', String(id));
        router.replace(`${pathname}?${params.toString()}`, { scroll: false });
        requestAnimationFrame(() => detailRef.current?.scrollIntoView?.({ block: 'start' }));
    }, [router, pathname, searchParams]);

    const changed = (invoice: SalesInvoice) => {
        setDetail({ status: 'ready', data: invoice });
        setReloadToken((n) => n + 1);
    };
    const choose = (id: number, on: boolean) => setChosen((current) => {
        const next = new Set(current);
        if (on) next.add(id); else next.delete(id);
        return next;
    });
    const items = list.status === 'ready' ? list.data.items : [];
    const chosenItems = items.filter((item) => chosen.has(item.id));
    const finalizeMany = async () => {
        setBusy(true);
        setManyError(null);
        try {
            setResults(await finalizeSalesInvoices(chosenItems.map((item) => ({ id: item.id, expectedVersion: item.version }))));
            setChosen(new Set());
            setConfirmMany(false);
            setReloadToken((n) => n + 1);
        } catch (caught) {
            setManyError(toApiError(caught).message);
        } finally {
            setBusy(false);
        }
    };
    const total = list.status === 'ready' ? list.data.total : 0;
    const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <PageTitle data-tour="page-title">{t('si_title')}</PageTitle>
                    <p className="text-sm text-gray-600" data-testid="page-intro">{t('si_intro')}</p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                    <button type="button" onClick={() => setReloadToken((n) => n + 1)} className={secondaryButtonClass}>↻ {t('availability_refresh')}</button>
                </div>
            </div>

            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3">
                <div className="sm:col-span-2">
                    <label htmlFor="si-search" className="text-xs font-medium text-gray-700">{t('si_search')}</label>
                    <input id="si-search" data-tour="si-search" type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('si_search_placeholder')} className={inputClass} />
                </div>
                <div>
                    <label htmlFor="si-status" className="text-xs font-medium text-gray-700">{t('si_col_status')}</label>
                    <select id="si-status" data-tour="si-status-filter" value={status} onChange={(event) => setStatus(event.target.value)} className={inputClass}>
                        {SALES_INVOICE_STATUS_FILTERS.map((value) => <option key={value} value={value}>{t(FILTER_KEYS[value])}</option>)}
                    </select>
                </div>
            </div>

            {chosenItems.length > 0 && (
                <div className="flex flex-wrap items-center gap-2 rounded border border-blue-300 bg-blue-50 px-3 py-2 text-sm" data-testid="si-selection">
                    <span>{t('si_chosen', { count: String(chosenItems.length) })}</span>
                    {!confirmMany && <button type="button" data-tour="si-finalize-many" className={primaryButtonClass} onClick={() => setConfirmMany(true)}>{t('si_finalize_many', { count: String(chosenItems.length) })}</button>}
                    {confirmMany && (
                        <span role="alertdialog" aria-label={t('si_finalize_confirm_title')} className="flex flex-wrap items-center gap-2">
                            <span className="font-semibold">{t('si_finalize_many_confirm', { count: String(chosenItems.length) })}</span>
                            <button type="button" className={primaryButtonClass} disabled={busy} onClick={finalizeMany} data-tour="si-finalize-many-confirm">{t('si_finalize_confirm')}</button>
                            <button type="button" className={secondaryButtonClass} disabled={busy} onClick={() => setConfirmMany(false)}>{t('si_cancel')}</button>
                        </span>
                    )}
                    {manyError && <span role="alert" className="text-red-800">{manyError}</span>}
                </div>
            )}
            {results && (
                <ul className="rounded border border-gray-200 bg-white px-3 py-2 text-sm" data-testid="si-results" aria-live="polite">
                    {results.map((result) => (
                        <li key={result.id} className={result.final ? 'text-green-900' : 'text-red-800'}>
                            {result.final ? t('si_result_final', { number: result.invoiceNumber ?? '' }) : t('si_result_refused', { message: result.message ?? '' })}
                        </li>
                    ))}
                </ul>
            )}

            {/* Verkoopfactuur 1: de werklijst Nog te factureren (leveringen met een rest, per klant). */}
            <ToInvoiceList token={reloadToken} onCreated={(id) => { setReloadToken((n) => n + 1); open(id); }} />

            {list.status === 'loading' && <LoadingRows label={t('si_loading')} />}
            {list.status === 'error' && <ErrorState error={list.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {list.status === 'ready' && items.length === 0 && (
                <EmptyState title={debounced === '' ? t('si_empty') : t('si_none_found')} description={debounced === '' ? t('si_empty_hint') : undefined} testId="si-empty" />
            )}
            {list.status === 'ready' && items.length > 0 && (
                <SalesInvoiceList items={items} selectedId={selectedId} onOpen={open} sort={sort} onSort={toggleSort} onSortChange={setSort} chosen={chosen} onChoose={choose} />
            )}
            {pages > 1 && (
                <nav aria-label={t('si_pages')} className="flex items-center gap-2 text-sm">
                    <button type="button" className={secondaryButtonClass} disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>‹</button>
                    <span>{t('si_page_of', { page: String(page), pages: String(pages), total: String(total) })}</span>
                    <button type="button" className={secondaryButtonClass} disabled={page >= pages} onClick={() => setPage((p) => p + 1)}>›</button>
                </nav>
            )}

            <div ref={detailRef}>
                {detail?.status === 'loading' && <LoadingRows rows={4} label={t('si_loading')} />}
                {detail?.status === 'error' && <ErrorState error={detail.error} onRetry={() => setReloadToken((n) => n + 1)} />}
                {detail?.status === 'ready' && (
                    <SalesInvoiceDetail invoice={detail.data} onChanged={changed}>
                        <InvoiceConceptEditor invoice={detail.data} onChanged={changed} onReload={() => setReloadToken((n) => n + 1)} />
                    </SalesInvoiceDetail>
                )}
            </div>
        </div>
    );
}
