'use client';

import Link from 'next/link';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useCallback, useEffect, useState } from 'react';
import PurchaseInvoiceDetail from '../../../components/costs/PurchaseInvoiceDetail';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import { FirstStepLink } from '../../../components/shared/FirstStep';
import { useTranslation } from '../../../i18n/LanguageContext';
import { formatMoney } from '../../../lib/availability';
import { formatCalendarDate } from '../../../lib/calendarDate';
import { toApiError } from '../../../services/apiClient';
import { listPurchaseInvoices } from '../../../services/purchaseInvoiceService';
import type { PurchaseInvoice } from '../../../types/purchaseInvoice';
import { primaryButtonClass } from '../../../components/supply/SuccessPanel';
import { listRowClass, rowClick } from '../../../lib/listRows';

// Overzicht van inkoopfacturen uit de nieuwe werkplek (dezelfde tabel als de oude pagina "Facturen (oud)"). Klikken opent de factuur
// (?id=) met per regel "Aan welke partij hoort dit?" (kosten verdelen over partijen).
export default function PurchaseInvoicesPage() {
    const { t } = useTranslation();
    return (
        <Suspense fallback={<LoadingRows label={t('purchase_loading')} />}>
            <PurchaseInvoicesWorkspace />
        </Suspense>
    );
}

function PurchaseInvoicesWorkspace() {
    const { t, locale } = useTranslation();
    const router = useRouter();
    const pathname = usePathname();
    const searchParams = useSearchParams();
    const selected = /^[1-9][0-9]{0,9}$/.test(searchParams.get('id') ?? '') ? Number(searchParams.get('id')) : null;
    const open = useCallback((id: number | null) => {
        router.replace(id === null ? pathname : `${pathname}?id=${id}`, { scroll: false });
    }, [router, pathname]);
    const [state, setState] = useState<LoadState<PurchaseInvoice[]>>({ status: 'loading' });
    const [reloadToken, setReloadToken] = useState(0);
    // Zoeken op factuurnummer, leverancier of ordernummer (ook ?query= vanaf de order, stap 4b).
    const [query, setQuery] = useState(() => searchParams.get('query') ?? '');

    useEffect(() => {
        const controller = new AbortController();
        const timer = setTimeout(() => listPurchaseInvoices(controller.signal, query)
            .then((data) => setState({ status: 'ready', data }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            }), query === '' ? 0 : 300);
        return () => { clearTimeout(timer); controller.abort(); };
    }, [reloadToken, query]);


    return (
        <div className="mx-auto max-w-[96rem] space-y-3 p-2">
            <div className="flex flex-wrap items-end justify-between gap-3">
                <div>
                    <h1 className="text-2xl font-bold text-gray-900">{t('purchase_title')}</h1>
                    <p className="text-sm text-gray-600">{t('purchase_subtitle')}</p>
                </div>
                <div className="flex items-center gap-2">
                    <button type="button" onClick={() => setReloadToken((n) => n + 1)} className="rounded border border-gray-300 bg-white px-3 py-1 text-sm font-medium text-gray-800 hover:bg-gray-100">↻ {t('availability_refresh')}</button>
                    <Link href="/purchase-invoices/new" className={primaryButtonClass}>+ {t('purchase_new_title')}</Link>
                </div>
            </div>
            <div className="max-w-md">
                <label htmlFor="purchase-invoice-search" className="text-xs font-medium text-gray-700">{t('purchase_list_search')}</label>
                <input id="purchase-invoice-search" type="search" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('purchase_list_search_placeholder')}
                    data-tour="pinvoice-list-search" className="mt-1 w-full rounded border border-gray-300 px-2 py-1 text-sm" />
            </div>
            {selected !== null && <PurchaseInvoiceDetail invoiceId={selected} onClose={() => open(null)} />}
            {state.status === 'loading' && <LoadingRows label={t('purchase_loading')} />}
            {state.status === 'error' && <ErrorState error={state.error} onRetry={() => setReloadToken((n) => n + 1)} />}
            {state.status === 'ready' && (
                <div className="overflow-x-auto rounded-md border border-gray-200 bg-white">
                    <table className="min-w-full divide-y divide-gray-200 text-sm">
                        <thead className="bg-gray-50 text-left text-xs font-semibold uppercase tracking-wide text-gray-600">
                            <tr>
                                <th scope="col" className="px-3 py-2">{t('invoice_date')}</th>
                                <th scope="col" className="px-3 py-2">{t('invoice_number')}</th>
                                <th scope="col" className="px-3 py-2">{t('purchase_vendor')}</th>
                                <th scope="col" className="px-3 py-2">{t('purchase_due_date')}</th>
                                <th scope="col" className="px-3 py-2 text-right">{t('purchase_col_total')}</th>
                                <th scope="col" className="px-3 py-2">{t('purchase_col_status')}</th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {state.data.length === 0 && (
                                <tr><td colSpan={6} className="p-2">
                                    <EmptyState testId="purchase-invoices-empty" title={t('purchase_list_empty')} description={t('purchase_empty_text')}
                                        action={<FirstStepLink href="/purchase-invoices/new" label={t('purchase_new_title')} />} />
                                </td></tr>
                            )}
                            {state.data.map((invoice, index) => (
                                <tr key={invoice.id} aria-selected={invoice.id === selected} className={listRowClass(index, { selected: invoice.id === selected, clickable: true })}
                                    onClick={rowClick(() => open(invoice.id))} data-testid={`purchase-invoice-row-${invoice.id}`}>
                                    <td className="px-3 py-2">{invoice.invoiceDate ? formatCalendarDate(invoice.invoiceDate, locale) : '–'}</td>
                                    <td className="px-3 py-2 font-mono">
                                        <button type="button" className="font-semibold text-blue-800 hover:underline" data-tour="invoice-open" data-tour-key={invoice.invoiceNumber ?? invoice.id}
                                            aria-label={t('invoice_open', { number: invoice.invoiceNumber ?? String(invoice.id) })} onClick={() => open(invoice.id)}>{invoice.invoiceNumber ?? '–'}</button>
                                    </td>
                                    <td className="px-3 py-2">{invoice.vendorName ?? '–'}</td>
                                    <td className="px-3 py-2">{invoice.dueDate ? formatCalendarDate(invoice.dueDate, locale) : '–'}</td>
                                    <td className="px-3 py-2 text-right font-mono">{formatMoney(invoice.totalAmount, invoice.currency ?? null, locale)}</td>
                                    <td className="px-3 py-2">{invoice.status}</td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}
        </div>
    );
}
