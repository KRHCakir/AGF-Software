'use client';

import Link from 'next/link';
import { useCallback, useEffect, useRef, useState } from 'react';
import { orderLabel } from '../../lib/invoiceOrderLink';
import { round2 } from '../../lib/invoiceMath';
import { useTranslation } from '../../i18n/LanguageContext';
import { formatDateTime, formatMoney } from '../../lib/availability';
import { formatCalendarDate, formatDateOnly } from '../../lib/calendarDate';
import { formatRate, isEuro } from '../../lib/exchangeRate';
import { ApiError, toApiError } from '../../services/apiClient';
import { getInvoiceAllocations, getPurchaseInvoice, refreshExchangeRate } from '../../services/purchaseInvoiceService';
import { getInvoiceAttachment } from '../../services/aiReadingService';
import { type InvoiceAllocations, lineEuro } from '../../types/partijCost';
import type { PurchaseInvoice } from '../../types/purchaseInvoice';
import { ErrorState, LoadState, LoadingRows } from '../availability/StateViews';
import { sectionClass } from '../purchase/sectionClass';
import AllocationPanel from './AllocationPanel';

// Inkoopfactuur bekijken en per regel de kosten verdelen over partijen (bedragen excl. btw, in euro). Een factuur van goederen via een
// inkooporder en een directe factuur van derden werken hier hetzelfde. Bij een factuur in een andere valuta laat het scherm bij elk
// openen de server de ECB-koers opnieuw ophalen, zolang er niets verdeeld is; daarna ligt hij vast. Niemand typt een koers.
// Een antwoord bevat alle regels van de factuur. Komen twee antwoorden in omgekeerde volgorde binnen, dan wint per regel de hoogste versie:
// een ouder antwoord zet een nieuwere stand van een andere regel nooit terug.
export function newestLines(current: InvoiceAllocations, next: InvoiceAllocations): InvoiceAllocations {
    return { ...next, lines: next.lines.map((line) => {
        const known = current.lines.find((candidate) => candidate.lineId === line.lineId);
        return known !== undefined && known.version > line.version ? known : line;
    }) };
}

// Een koersantwoord kan na een opgeslagen verdeling binnenkomen. Dan blijven de nieuwste regels en de vergrendelde koers van het scherm staan.
export function mergeRefreshed(current: InvoiceAllocations, next: InvoiceAllocations): InvoiceAllocations {
    const merged = newestLines(current, next);
    return current.rateLocked
        ? { ...merged, rateLocked: true, exchangeRate: current.exchangeRate, exchangeRateDate: current.exchangeRateDate }
        : merged;
}

export default function PurchaseInvoiceDetail({ invoiceId, onClose }: { invoiceId: number; onClose: () => void }) {
    const { t, locale } = useTranslation();
    const [state, setState] = useState<LoadState<{ invoice: PurchaseInvoice; allocations: InvoiceAllocations }>>({ status: 'loading' });
    const [reloadToken, setReloadToken] = useState(0);
    const [attachmentFailed, setAttachmentFailed] = useState(false);
    // Bijlage openen: het venster gaat direct bij de klik open (anders blokkeert de browser het als pop-up) en krijgt het document zodra
    // het binnen is. Is het venster toch geblokkeerd, dan wordt het bestand gedownload; lukt ophalen niet, dan een melding.
    const openAttachment = async (id: number, attachment: { id: number; fileName: string }) => {
        setAttachmentFailed(false);
        const target = window.open('', '_blank');
        if (target !== null) target.opener = null;
        try {
            const url = URL.createObjectURL(await getInvoiceAttachment(id, attachment.id));
            if (target !== null && !target.closed) {
                target.location.href = url;
            } else {
                const link = document.createElement('a');
                link.href = url;
                link.download = attachment.fileName;
                link.click();
            }
            window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
        } catch {
            target?.close();
            setAttachmentFailed(true);
        }
    };
    const [rate, setRate] = useState<{ status: 'idle' | 'fetching' } | { status: 'unavailable' | 'none'; error: ApiError }>({ status: 'idle' });
    const rateRequest = useRef<AbortController | null>(null);

    // De ECB-koers laten ophalen (alleen een factuur in een andere valuta waarvan nog niets verdeeld is). De nieuwe stand vervangt de verdeling.
    const fetchRate = useCallback((allocations: InvoiceAllocations) => {
        if (isEuro(allocations.currency) || allocations.rateLocked) return;
        rateRequest.current?.abort();
        const controller = new AbortController();
        rateRequest.current = controller;
        setRate({ status: 'fetching' });
        refreshExchangeRate(allocations.invoiceId, controller.signal)
            .then((next) => {
                setState((current) => (current.status === 'ready' && current.data.invoice.id === next.invoiceId ? { status: 'ready', data: { ...current.data, allocations: mergeRefreshed(current.data.allocations, next) } } : current));
                setRate({ status: 'idle' });
            })
            .catch((caught: unknown) => {
                const apiError = toApiError(caught);
                if (apiError.kind === 'aborted') return;
                setRate({ status: apiError.status === 400 ? 'none' : 'unavailable', error: apiError });
            });
    }, []);

    useEffect(() => {
        const controller = new AbortController();
        setState((current) => (current.status === 'ready' && current.data.invoice.id === invoiceId ? current : { status: 'loading' }));
        setRate({ status: 'idle' });
        Promise.all([getPurchaseInvoice(invoiceId, controller.signal), getInvoiceAllocations(invoiceId, controller.signal)])
            .then(([invoice, allocations]) => {
                setState({ status: 'ready', data: { invoice, allocations } });
                fetchRate(allocations);
            })
            .catch((caught: unknown) => { const apiError = toApiError(caught); if (apiError.kind !== 'aborted') setState({ status: 'error', error: apiError }); });
        return () => { controller.abort(); rateRequest.current?.abort(); };
    }, [invoiceId, reloadToken, fetchRate]);

    if (state.status === 'loading') return <LoadingRows label={t('purchase_loading')} />;
    if (state.status === 'error') return <ErrorState error={state.error} onRetry={() => setReloadToken((n) => n + 1)} />;
    const { invoice, allocations } = state.data;
    const money = (value: number) => formatMoney(value, invoice.currency ?? null, locale);
    const euro = (value: number) => formatMoney(value, 'EUR', locale);
    const foreign = !isEuro(allocations.currency);
    const rateKnown = foreign && allocations.exchangeRate != null && allocations.exchangeRateDate != null;
    // Algemene kosten in euro; zonder koers is de hele factuur algemene kosten (in haar eigen valuta).
    const general = allocations.lines.every((line) => line.general !== null)
        ? euro(allocations.lines.reduce((total, line) => total + (line.general ?? 0), 0))
        : money(allocations.lines.reduce((total, line) => total + line.lineTotal, 0));

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
                {/* AI-voorstel (stap 7a): wie keurde het goed, welk model las het, hoeveel velden werden aangepast; het originele document als bijlage. */}
                {invoice.aiReading && (
                    <p className="text-sm text-gray-800" data-testid="invoice-ai-reading" data-tour="invoice-ai-reading">
                        {t('ai_detail_approved', { name: invoice.aiReading.approvedBy, date: formatDateTime(invoice.aiReading.approvedAt, locale), model: invoice.aiReading.modelName ?? '–', version: invoice.aiReading.modelVersion ?? '–' })}
                        {invoice.aiReading.adjustedFields > 0 && <> · {t('ai_detail_adjusted', { count: String(invoice.aiReading.adjustedFields) })}</>}
                    </p>
                )}
                {/* Gekoppeld aan de bon (stap 4b): de orders met link, en per regel de prijs op de bon tegenover de factuur. */}
                {(invoice.purchaseOrders ?? []).length > 0 && (
                    <div className="text-sm text-gray-800" data-testid="invoice-orders" data-tour="invoice-orders">
                        <p>
                            {t('invoice_detail_orders')}{' '}
                            {(invoice.purchaseOrders ?? []).map((order, index) => (
                                <span key={order.id}>{index > 0 && ', '}<Link href={`/purchase-orders?id=${order.id}`} className="font-semibold text-blue-800 underline underline-offset-2">{orderLabel(order)}</Link></span>
                            ))}
                            {invoice.orderPriceDecision && <> · {t(invoice.orderPriceDecision === 'AdjustOrder' ? 'invoice_detail_adjusted' : 'invoice_detail_recorded')}</>}
                        </p>
                        <ul className="text-xs text-gray-700">
                            {invoice.lines.filter((line) => line.supplyCommitmentLineId != null).map((line) => {
                                const difference = line.orderUnitPrice == null ? null : round2(line.lineTotal - round2(line.quantity * line.orderUnitPrice));
                                return (
                                    <li key={line.id} data-testid="invoice-order-line">
                                        {line.description ?? '–'}: {line.orderUnitPrice == null ? t('invoice_link_no_price') : t('invoice_detail_line_prices', { order: money(line.orderUnitPrice), invoice: money(line.unitPrice) })}
                                        {difference !== null && difference !== 0 && <span className="ml-1 font-medium text-red-800">{t('invoice_detail_line_difference', { amount: money(difference) })}</span>}
                                    </li>
                                );
                            })}
                        </ul>
                    </div>
                )}
                {(invoice.attachments ?? []).map((attachment) => (
                    <button key={attachment.id} type="button" className="mr-2 text-sm text-blue-800 underline underline-offset-2" data-testid="invoice-attachment"
                        onClick={() => void openAttachment(invoice.id, attachment)}>
                        📎 {attachment.fileName}
                    </button>
                ))}
                {attachmentFailed && <p role="alert" className="text-sm text-amber-900" data-testid="invoice-attachment-failed">{t('ai_attachment_failed')}</p>}
                {foreign && (
                    <div className="mt-1 rounded border border-sky-200 bg-sky-50 px-2 py-1 text-sm text-sky-950" data-testid="invoice-exchange-rate" data-tour="invoice-exchange-rate">
                        {rateKnown && (
                            <p>
                                <span className="font-medium">{t('exchange_rate_line', { date: formatDateOnly(allocations.exchangeRateDate!, locale), rate: formatRate(allocations.exchangeRate!, locale), currency: allocations.currency })}</span>
                                <span className="block text-xs text-sky-900">{t(allocations.rateLocked ? 'exchange_rate_locked' : 'exchange_rate_live')}</span>
                            </p>
                        )}
                        {rate.status === 'fetching' && <p role="status">{t('exchange_rate_fetching')}</p>}
                        {rate.status === 'unavailable' && (
                            <p role="alert" className="text-amber-900">
                                {t(rateKnown ? 'exchange_rate_unavailable_kept' : 'exchange_rate_unavailable')}{' '}
                                <button type="button" className="underline" onClick={() => fetchRate(allocations)}>{t('exchange_rate_retry')}</button>
                            </p>
                        )}
                        {rate.status === 'none' && <p role="alert" className="text-amber-900">{t('exchange_rate_none', { currency: allocations.currency })}</p>}
                    </div>
                )}
                <p className="text-sm text-gray-700" data-testid="invoice-general">{t('invoice_detail_general', { amount: general })}</p>
            </header>
            {allocations.lines.map((line) => (
                <section key={line.lineId} className={sectionClass} aria-label={line.description ?? ''} data-tour="invoice-line" data-tour-key={line.lineId}>
                    <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2 text-sm">
                        <span className="font-semibold text-gray-900">{line.description ?? '–'} <span className="ml-1 rounded bg-gray-100 px-1 text-xs font-normal text-gray-700">{line.kind}</span></span>
                        <span className="font-mono">
                            {t('invoice_line_amount', { amount: money(line.lineTotal) })}
                            {foreign && lineEuro(line, allocations.currency) !== null && <span className="text-gray-700" data-testid="invoice-line-eur"> = {euro(lineEuro(line, allocations.currency)!)}</span>}
                        </span>
                    </div>
                    <AllocationPanel invoiceId={invoice.id} line={line} canAllocate={allocations.canAllocate} currency={allocations.currency} exchangeRate={allocations.exchangeRate ?? null}
                        onSaved={(next) => setState((current) => (current.status === 'ready' && current.data.invoice.id === invoice.id
                            ? { status: 'ready', data: { invoice: current.data.invoice, allocations: newestLines(current.data.allocations, next) } } : current))}
                        onReload={() => setReloadToken((n) => n + 1)} />
                </section>
            ))}
        </article>
    );
}
