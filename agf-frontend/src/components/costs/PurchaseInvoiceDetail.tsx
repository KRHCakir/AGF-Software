'use client';

import { useEffect, useState } from 'react';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatMoney } from '../../lib/availability';
import { formatCalendarDate } from '../../lib/calendarDate';
import { toApiError } from '../../services/apiClient';
import { getInvoiceAllocations, getPurchaseInvoice } from '../../services/purchaseInvoiceService';
import type { InvoiceAllocations } from '../../types/partijCost';
import type { PurchaseInvoice } from '../../types/purchaseInvoice';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { sectionClass } from '../purchase/sectionClass';
import AllocationPanel from './AllocationPanel';

// Inkoopfactuur bekijken en per regel de kosten verdelen over partijen (bedragen excl. btw, in euro). Een factuur van goederen via een
// inkooporder en een directe factuur van derden werken hier hetzelfde.
export default function PurchaseInvoiceDetail({ invoiceId, onClose }: { invoiceId: number; onClose: () => void }) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<{ invoice: PurchaseInvoice; allocations: InvoiceAllocations }>>({ status: 'loading' });
    const [reloadToken, setReloadToken] = useState(0);

    useEffect(() => {
        const controller = new AbortController();
        setState((current) => (current.status === 'ready' && current.data.invoice.id === invoiceId ? current : { status: 'loading' }));
        Promise.all([getPurchaseInvoice(invoiceId, controller.signal), getInvoiceAllocations(invoiceId, controller.signal)])
            .then(([invoice, allocations]) => setState({ status: 'ready', data: { invoice, allocations } }))
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError }); });
        return () => controller.abort();
    }, [invoiceId, reloadToken]);

    if (state.status === 'loading') return <LoadingRows label={t('purchase_loading')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setReloadToken((n) => n + 1)} />;
    const { invoice, allocations } = state.data;
    const money = (value: number) => formatMoney(value, invoice.currency ?? null, locale);
    const general = allocations.lines.reduce((total, line) => total + line.general, 0);

    return (
        <article className="space-y-3" aria-labelledby="invoice-detail-title" data-tour="invoice-detail">
            <header className={sectionClass}>
                <div className="flex flex-wrap items-center justify-between gap-2">
                    <h2 id="invoice-detail-title" className="text-xl font-bold text-gray-900">
                        {t('invoice_detail_title', { number: invoice.invoiceNumber ?? `#${invoice.id}` })} · {invoice.vendorName ?? '–'}
                    </h2>
                    <button type="button" className="text-sm text-blue-800 underline underline-offset-2" onClick={onClose}>{t('invoice_detail_close')}</button>
                </div>
                <p className="text-sm text-gray-800">
                    {invoice.invoiceDate ? formatCalendarDate(invoice.invoiceDate, locale) : '–'} · {t('invoice_detail_totals', { net: money(invoice.subTotal), vat: money(invoice.vatAmount), gross: money(invoice.totalAmount) })}
                </p>
                <p className="text-sm text-gray-700" data-testid="invoice-general">{t('invoice_detail_general', { amount: formatMoney(general, 'EUR', locale) })}</p>
            </header>
            {allocations.lines.map((line) => (
                <section key={line.lineId} className={sectionClass} aria-label={line.description ?? ''} data-tour="invoice-line" data-tour-key={line.lineId}>
                    <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2 text-sm">
                        <span className="font-semibold text-gray-900">{line.description ?? '–'} <span className="ml-1 rounded bg-gray-100 px-1 text-xs font-normal text-gray-700">{line.kind}</span></span>
                        <span className="font-mono">{t('invoice_line_amount', { amount: money(line.lineTotal) })}</span>
                    </div>
                    <AllocationPanel invoiceId={invoice.id} line={line} canAllocate={allocations.canAllocate}
                        onSaved={(next) => setState({ status: 'ready', data: { invoice, allocations: next } })} onReload={() => setReloadToken((n) => n + 1)} />
                </section>
            ))}
        </article>
    );
}
