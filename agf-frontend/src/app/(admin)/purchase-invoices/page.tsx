'use client';

import Link from 'next/link';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import PurchaseInvoiceDetail from '../../../components/costs/PurchaseInvoiceDetail';
import { EmptyState, ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import { FirstStepLink } from '../../../components/shared/FirstStep';
import { useTranslation } from '../../../i18n/LanguageContext';
import { formatMoney } from '../../../lib/availability';
import { formatEuro } from '../../../lib/documentCurrency';
import { isEuro } from '../../../lib/exchangeRate';
import { formatCalendarDate } from '../../../lib/calendarDate';
import { toApiError } from '../../../services/apiClient';
import { listPurchaseInvoices } from '../../../services/purchaseInvoiceService';
import type { PurchaseInvoice } from '../../../types/purchaseInvoice';
import { primaryButtonClass } from '../../../components/supply/SuccessPanel';
import { listRowClass, rowClick } from '../../../lib/listRows';
import { type SortColumns, sortRows } from '../../../lib/listSort';
import { SortHeader } from '../../../components/shared/SortHeader';
import { useListSort } from '../../../components/shared/useListSort';

// Sorteren op de kolomkoppen (feedback 22 november). De lijst toont de 200 nieuwste facturen (zoeken vindt oudere); er wordt binnen de
// getoonde facturen gesorteerd. Het bedrag sorteert op het bedrag in euro.
const INVOICE_COLUMNS: SortColumns<PurchaseInvoice> = {
    date: { kind: 'date', value: (invoice) => invoice.invoiceDate },
    number: { kind: 'natural', value: (invoice) => invoice.invoiceNumber },
    vendor: { kind: 'text', value: (invoice) => invoice.vendorName },
    due: { kind: 'date', value: (invoice) => invoice.dueDate },
    total: { kind: 'number', value: (invoice) => invoice.totalAmountEur ?? (isEuro(invoice.currency) ? invoice.totalAmount : null) },
    status: { kind: 'text', value: (invoice) => invoice.status },
};
const INVOICE_SORT_KEYS = Object.keys(INVOICE_COLUMNS);

// Valuta-ronde: het totaal in euro (vastgelegde koers), met het bedrag in de factuurvaluta eronder; nooit valuta door elkaar. Een oudere
// factuur in een andere valuta zonder vastgelegde koers heeft nog geen eurobedrag ("–").
function invoiceAmount(invoice: PurchaseInvoice, locale: string) {
    const euro = invoice.totalAmountEur ?? (isEuro(invoice.currency) ? invoice.totalAmount : null);
    return (
        <>
            {euro == null ? '–' : formatEuro(euro, locale)}
            {!isEuro(invoice.currency) && <span className="block text-xs text-gray-600">{formatMoney(invoice.totalAmount, invoice.currency ?? null, locale)}</span>}
        </>
    );
}

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
    const { sort, toggle } = useListSort('purchase-invoices', INVOICE_SORT_KEYS);
    const rows = useMemo(() => (state.status === 'ready' ? sortRows(state.data, INVOICE_COLUMNS, sort) : []), [state, sort]);


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
                                <SortHeader className="px-3 py-2 align-top" label={t('invoice_date')} sortKey="date" sort={sort} onSort={toggle} />
                                <SortHeader className="px-3 py-2 align-top" label={t('invoice_number')} sortKey="number" sort={sort} onSort={toggle} />
                                <SortHeader className="px-3 py-2 align-top" label={t('purchase_vendor')} sortKey="vendor" sort={sort} onSort={toggle} />
                                <SortHeader className="px-3 py-2 align-top" label={t('purchase_due_date')} sortKey="due" sort={sort} onSort={toggle} />
                                <SortHeader className="px-3 py-2 text-right align-top" label={t('purchase_col_total')} sortKey="total" sort={sort} onSort={toggle}><span className="block font-normal normal-case">{t('po_amount_eur_hint')}</span></SortHeader>
                                <SortHeader className="px-3 py-2 align-top" label={t('purchase_col_status')} sortKey="status" sort={sort} onSort={toggle} />
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-100">
                            {state.data.length === 0 && (
                                <tr><td colSpan={6} className="p-2">
                                    <EmptyState testId="purchase-invoices-empty" title={t('purchase_list_empty')} description={t('purchase_empty_text')}
                                        action={<FirstStepLink href="/purchase-invoices/new" label={t('purchase_new_title')} />} />
                                </td></tr>
                            )}
                            {rows.map((invoice, index) => (
                                <tr key={invoice.id} aria-selected={invoice.id === selected} className={listRowClass(index, { selected: invoice.id === selected, clickable: true })}
                                    onClick={rowClick(() => open(invoice.id))} data-testid={`purchase-invoice-row-${invoice.id}`}>
                                    <td className="px-3 py-2">{invoice.invoiceDate ? formatCalendarDate(invoice.invoiceDate, locale) : '–'}</td>
                                    <td className="px-3 py-2 font-mono">
                                        <button type="button" className="font-semibold text-blue-800 hover:underline" data-tour="invoice-open" data-tour-key={invoice.invoiceNumber ?? invoice.id}
                                            aria-label={t('invoice_open', { number: invoice.invoiceNumber ?? String(invoice.id) })} onClick={() => open(invoice.id)}>{invoice.invoiceNumber ?? '–'}</button>
                                    </td>
                                    <td className="px-3 py-2">{invoice.vendorName ?? '–'}</td>
                                    <td className="px-3 py-2">{invoice.dueDate ? formatCalendarDate(invoice.dueDate, locale) : '–'}</td>
                                    <td className="whitespace-nowrap px-3 py-2 text-right font-mono" data-testid="invoice-list-amount">{invoiceAmount(invoice, locale)}</td>
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
