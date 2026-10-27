'use client';

import Link from 'next/link';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useCallback, useEffect, useRef, useState } from 'react';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import PurchaseOrderDetail from '../../../components/purchase/PurchaseOrderDetail';
import PurchaseOrderList from '../../../components/purchase/PurchaseOrderList';
import { inputClass } from '../../../components/supply/FieldError';
import { primaryButtonClass, secondaryButtonClass } from '../../../components/supply/SuccessPanel';
import { useTranslation } from '../../../i18n/LanguageContext';
import { ORDER_STATUS_LABEL_KEYS, PURCHASE_ORDER_STATUSES } from '../../../lib/purchaseOrderRules';
import { supplyShortcutHref } from '../../../lib/supplyShortcut';
import { toApiError } from '../../../services/apiClient';
import { getPurchaseOrder, listPurchaseOrders } from '../../../services/purchaseOrderService';
import { getVendors } from '../../../services/referenceDataService';
import type { PurchaseOrderDetail as Order, PurchaseOrderList as OrderList } from '../../../types/purchaseOrder';
import type { VendorOption } from '../../../types/referenceData';

const PAGE_SIZE = 50;

// Inkoopopdrachten: zoeken op ordernummer, leveranciersreferentie, leverancier of factuurnummer (de server zoekt en
// pagineert), filters status (standaard open) en leverancier; nieuwste eerst. Klikken opent het detail (?id=).
export default function PurchaseOrdersPage() {
    const { t } = useTranslation();
    return (
        <Suspense fallback={<LoadingRows label={t('po_loading')} />}>
            <PurchaseOrdersWorkspace />
        </Suspense>
    );
}

function parseId(value: string | null): number | null {
    return value !== null && /^[1-9][0-9]{0,9}$/.test(value) ? Number(value) : null;
}

function PurchaseOrdersWorkspace() {
    const { t } = useTranslation();
    const router = useRouter();
    const pathname = usePathname();
    const searchParams = useSearchParams();
    const selectedId = parseId(searchParams.get('id'));
    const [query, setQuery] = useState(searchParams.get('query') ?? '');
    const [debounced, setDebounced] = useState(query.trim());
    const [status, setStatus] = useState('open');
    const [vendorId, setVendorId] = useState('');
    const [page, setPage] = useState(1);
    const [reloadToken, setReloadToken] = useState(0);
    const [list, setList] = useState<LoadState<OrderList>>({ status: 'loading' });
    const [vendors, setVendors] = useState<VendorOption[]>([]);
    const [detail, setDetail] = useState<LoadState<Order> | null>(null);
    const detailRef = useRef<HTMLDivElement | null>(null);

    useEffect(() => {
        const handle = setTimeout(() => setDebounced(query.trim()), 250);
        return () => clearTimeout(handle);
    }, [query]);
    useEffect(() => { setPage(1); }, [debounced, status, vendorId]);

    useEffect(() => {
        const controller = new AbortController();
        getVendors(controller.signal).then(setVendors).catch(() => setVendors([]));
        return () => controller.abort();
    }, []);

    useEffect(() => {
        const controller = new AbortController();
        setList((current) => (current.status === 'ready' ? current : { status: 'loading' }));
        listPurchaseOrders({ query: debounced, status, vendorId: vendorId === '' ? null : Number(vendorId), page, pageSize: PAGE_SIZE }, controller.signal)
            .then((data) => setList({ status: 'ready', data }))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setList({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [debounced, status, vendorId, page, reloadToken]);

    useEffect(() => {
        if (selectedId === null) { setDetail(null); return; }
        const controller = new AbortController();
        setDetail((current) => (current?.status === 'ready' && current.data.id === selectedId ? current : { status: 'loading' }));
        getPurchaseOrder(selectedId, controller.signal)
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

    // Na afkeuren of een factuurnummer: het detail is het antwoord van de server; de lijst haalt de nieuwe status en factuurnummers op.
    const changed = (order: Order) => {
        setDetail({ status: 'ready', data: order });
        setReloadToken((n) => n + 1);
    };

    const total = list.status === 'ready' ? list.data.total : 0;
    const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <h1 className="text-2xl font-bold text-gray-900">{t('po_title')}</h1>
                <div className="flex flex-wrap items-center gap-2">
                    <button type="button" onClick={() => setReloadToken((n) => n + 1)} className={secondaryButtonClass}>↻ {t('availability_refresh')}</button>
                    <Link href={supplyShortcutHref('plan')} className={primaryButtonClass}>+ {t('po_new')}</Link>
                </div>
            </div>

            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-4">
                <div className="sm:col-span-2">
                    <label htmlFor="po-search" className="text-xs font-medium text-gray-700">{t('po_search')}</label>
                    <input id="po-search" type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('po_search_placeholder')} className={inputClass} />
                </div>
                <div>
                    <label htmlFor="po-status" className="text-xs font-medium text-gray-700">{t('po_col_status')}</label>
                    <select id="po-status" value={status} onChange={(event) => setStatus(event.target.value)} className={inputClass}>
                        <option value="open">{t('po_filter_open')}</option>
                        <option value="all">{t('po_filter_all')}</option>
                        {PURCHASE_ORDER_STATUSES.map((value) => <option key={value} value={value}>{t(ORDER_STATUS_LABEL_KEYS[value])}</option>)}
                    </select>
                </div>
                <div>
                    <label htmlFor="po-vendor" className="text-xs font-medium text-gray-700">{t('po_col_vendor')}</label>
                    <select id="po-vendor" value={vendorId} onChange={(event) => setVendorId(event.target.value)} className={inputClass}>
                        <option value="">{t('po_filter_all_vendors')}</option>
                        {vendors.map((vendor) => <option key={vendor.id} value={vendor.id}>{vendor.name}</option>)}
                    </select>
                </div>
            </div>

            {list.status === 'loading' && <LoadingRows label={t('po_loading')} />}
            {list.status === 'error' && <ErrorState error={list.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {list.status === 'ready' && list.data.items.length === 0 && <EmptyState title={t('po_list_empty')} />}
            {list.status === 'ready' && list.data.items.length > 0 && (
                <>
                    <PurchaseOrderList items={list.data.items} selectedId={selectedId} onOpen={open} />
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
                {detail?.status === 'loading' && <LoadingRows rows={4} label={t('po_loading')} />}
                {detail?.status === 'error' && <ErrorState error={detail.error} onRetry={() => setReloadToken((n) => n + 1)} />}
                {detail?.status === 'ready' && <PurchaseOrderDetail key={detail.data.id} order={detail.data} onChanged={changed} />}
            </div>
        </div>
    );
}
