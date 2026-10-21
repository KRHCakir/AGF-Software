'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { ErrorState, LoadState, LoadingRows } from '../../../components/availability/StateViews';
import { useTranslation } from '../../../i18n/LanguageContext';
import { formatCalendarDate } from '../../../lib/calendarDate';
import { toApiError } from '../../../services/apiClient';
import { listPurchaseInvoices } from '../../../services/purchaseInvoiceService';
import type { PurchaseInvoice } from '../../../types/purchaseInvoice';
import { primaryButtonClass } from '../../../components/supply/SuccessPanel';

// Overzicht van inkoopfacturen uit de nieuwe werkplek (dezelfde tabel als de oude pagina "Facturen (oud)").
export default function PurchaseInvoicesPage() {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<PurchaseInvoice[]>>({ status: 'loading' });
    const [reloadToken, setReloadToken] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        listPurchaseInvoices(controller.signal)
            .then((data) => setState({ status: 'ready', data }))
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError });
            });
        return () => controller.abort();
    }, [reloadToken]);

    const money = (value: number) => new Intl.NumberFormat(locale === 'nl' ? 'nl-NL' : 'en-GB', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(value);

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
                            {state.data.length === 0 && <tr><td colSpan={6} className="px-3 py-6 text-center text-gray-600">{t('purchase_list_empty')}</td></tr>}
                            {state.data.map((invoice) => (
                                <tr key={invoice.id}>
                                    <td className="px-3 py-2">{invoice.invoiceDate ? formatCalendarDate(invoice.invoiceDate, locale) : '–'}</td>
                                    <td className="px-3 py-2 font-mono">{invoice.invoiceNumber ?? '–'}</td>
                                    <td className="px-3 py-2">{invoice.vendorName ?? '–'}</td>
                                    <td className="px-3 py-2">{invoice.dueDate ? formatCalendarDate(invoice.dueDate, locale) : '–'}</td>
                                    <td className="px-3 py-2 text-right font-mono">{money(invoice.totalAmount)} {invoice.currency ?? ''}</td>
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
